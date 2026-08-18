using SteamInputAddonforClaw.Diagnostics;
using SteamInputAddonforClaw.Power;

namespace SteamInputAddonforClaw.CenterM;

/// <summary>
/// OEM1 (Center M button) suppression/MainUI lifecycle states. Names intentionally mirror the PR2-A
/// task brief; see the coordinator's class doc for the exact semantics of each.
/// </summary>
internal enum CenterMOem1LifecycleState
{
    Disabled,
    NeedsSetup,
    Reconciling,
    Armed,
    NativeMainUiActive,
    HiddenDebounce,
    FaultedNative
}

/// <summary>Immutable status snapshot for tests / a future status surface. Never exposed via
/// frontend/IPC in this PR. Deliberately carries no raw native handles.</summary>
internal readonly record struct CenterMOem1LifecycleSnapshot(
    CenterMOem1LifecycleState State,
    bool DesiredEnabled,
    bool SuppressionReady,
    bool NativeBehaviorGuaranteed,
    int? HelperProcessId,
    int? RealMainUiProcessId,
    bool SeenVisible,
    CenterMAutoRunState AutoRunState,
    bool LauncherReady,
    bool ServerReady,
    string? LastReason);

/// <summary>
/// PR2-A: composes the PR1 (#234) dormant Center M primitives into the OEM1 suppression/real-MainUI
/// lifecycle coordinator described in the OEM1/CenterM research handoff. This type owns ONLY the
/// helper-arming / MainUI-tracking lifecycle -- it has no knowledge of Steam routing, VIIPER, Steam
/// Deck, Xbox360 output, Game Bar, frontend/UI, or which action (if any) a future PR attaches to
/// OEM1. There is no custom OEM1 action in this PR: reaching <see cref="CenterMOem1LifecycleState.Armed"/>
/// only means suppression-lifecycle readiness, not that button remapping is active.
///
/// All facts are captured fresh at the moment of every safety-relevant decision -- OS
/// notifications/timer ticks are only wake-up hints, never trusted as the state itself (research
/// handoff: never infer causally). Every mutating public entry point runs behind a single async
/// gate (<see cref="_gate"/>) so enable/disable, polling ticks, debounce completion, suspend, resume,
/// and shutdown can never race each other's ownership/tracking decisions. A monotonically
/// increasing <see cref="_generation"/> lets a debounce (or any other deferred continuation) that
/// resumes after newer state has already committed recognize it is stale and refuse to act.
///
/// Dormant by design: nothing in this type starts a timer, touches the registry, stages a helper, or
/// terminates any process merely by being constructed. Every transition is driven by an explicit
/// caller (test, or -- in a later PR -- a small production composition seam) calling one of the
/// public methods below. Normal production Runtime composition must not construct or call into this
/// type in this PR.
/// </summary>
internal sealed class CenterMOem1LifecycleCoordinator : IPowerSuspendParticipant, IAsyncDisposable
{
    private readonly Func<string> _publishRootProvider;
    private readonly CenterMBackendProbe _backendProbe;
    private readonly Func<CenterMAutoRunState> _autoRunReader;
    private readonly IProcessSnapshotSource _processSnapshotSource;
    private readonly CenterMHelperOwnership _helperOwnership;
    private readonly MainUiLifecycleObserver _mainUiObserver;
    private readonly SafeMainUiTerminator _terminator;
    private readonly IProcessHandleOpener? _handleOpener;
    private readonly Func<string, string?> _stager;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly TimeSpan _hiddenDebounce;
    private readonly TimeSpan _helperStopTimeout;
    private readonly TimeSpan _mainUiTerminateTimeout;

    // Single-owner serialization boundary. A SemaphoreSlim (not a normal lock) is used deliberately
    // because arming/reconciling/debounce-completion legitimately await across the gate (staging
    // I/O, native calls, and the injectable delay) -- a normal lock must never be held across an
    // arbitrary async wait.
    private readonly SemaphoreSlim _gate = new(1, 1);

    private CenterMOem1LifecycleState _state = CenterMOem1LifecycleState.Disabled;
    private bool _desiredEnabled;
    private CenterMAutoRunState _lastAutoRun = CenterMAutoRunState.Unknown;
    private bool _launcherReady;
    private bool _serverReady;
    private TrackedCenterMMainUi? _trackedMainUi;
    private CancellationTokenSource? _debounceCts;
    private long _generation;
    private string? _lastReason;
    private bool _shutdown;

    internal CenterMOem1LifecycleCoordinator(
        Func<string> publishRootProvider,
        CenterMBackendProbe? backendProbe = null,
        Func<CenterMAutoRunState>? autoRunReader = null,
        IProcessSnapshotSource? processSnapshotSource = null,
        CenterMHelperOwnership? helperOwnership = null,
        MainUiLifecycleObserver? mainUiObserver = null,
        SafeMainUiTerminator? terminator = null,
        IProcessHandleOpener? handleOpener = null,
        Func<string, string?>? stager = null,
        Func<TimeSpan, CancellationToken, Task>? delay = null,
        TimeSpan? hiddenDebounce = null,
        TimeSpan? helperStopTimeout = null,
        TimeSpan? mainUiTerminateTimeout = null)
    {
        _publishRootProvider = publishRootProvider;
        var snapshotSource = processSnapshotSource ?? new Win32ProcessSnapshotSource();
        _backendProbe = backendProbe ?? new CenterMBackendProbe(snapshotSource);
        _autoRunReader = autoRunReader ?? CenterMAutoRunReader.Read;
        _processSnapshotSource = snapshotSource;
        _helperOwnership = helperOwnership ?? new CenterMHelperOwnership();
        _mainUiObserver = mainUiObserver ?? new MainUiLifecycleObserver();
        _terminator = terminator ?? new SafeMainUiTerminator();
        _handleOpener = handleOpener;
        _stager = stager ?? CenterMHelperStaging.StageFromPublishRoot;
        _delay = delay ?? ((delay, ct) => Task.Delay(delay, ct));
        _hiddenDebounce = hiddenDebounce ?? TimeSpan.FromSeconds(1);
        _helperStopTimeout = helperStopTimeout ?? TimeSpan.FromSeconds(5);
        _mainUiTerminateTimeout = mainUiTerminateTimeout ?? TimeSpan.FromSeconds(5);
    }

    public string Name => "CenterMOem1LifecycleCoordinator";

    internal CenterMOem1LifecycleSnapshot GetSnapshot()
    {
        _gate.Wait();
        try
        {
            return BuildSnapshotCore();
        }
        finally { _gate.Release(); }
    }

    private CenterMOem1LifecycleSnapshot BuildSnapshotCore()
    {
        var suppressionReady = _state == CenterMOem1LifecycleState.Armed;
        // "Native behavior guaranteed" means no residual helper ownership could be suppressing the
        // real MSI Center M launch -- distinct from "custom suppression inactive", which is true in
        // every non-Armed state including FaultedNative-with-retained-ownership.
        var nativeBehaviorGuaranteed = !_helperOwnership.IsOwned;

        return new CenterMOem1LifecycleSnapshot(
            _state,
            _desiredEnabled,
            suppressionReady,
            nativeBehaviorGuaranteed,
            _helperOwnership.ProcessId,
            _trackedMainUi?.ProcessId,
            _trackedMainUi is not null && _mainUiObserver.SeenVisible,
            _lastAutoRun,
            _launcherReady,
            _serverReady,
            _lastReason);
    }

    /// <summary>Internal runtime control seam -- there is no persisted setting in this PR. Default
    /// is false; production must never call this with true.</summary>
    internal async Task SetDesiredEnabledAsync(bool enabled, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_shutdown) return;
            _desiredEnabled = enabled;
            if (!enabled)
            {
                await DisableCore(cancellationToken).ConfigureAwait(false);
                return;
            }
            await ReconcileCore("SetDesiredEnabled(true)", cancellationToken).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

    /// <summary>General-purpose fresh reconciliation entry point (arm attempt, helper-death
    /// recovery, natural-MainUI-exit recovery, ...). Safe to call at any time; it always starts from
    /// a freshly captured world state.</summary>
    internal async Task ReconcileAsync(string reason, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_shutdown) return;
            await ReconcileCore(reason, cancellationToken).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

    /// <summary>Polls the exact retained helper handle for unexpected death while Armed. A no-op
    /// (returns immediately) when no helper is owned.</summary>
    internal async Task PollHelperLivenessAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_shutdown) return;
            var liveness = _helperOwnership.PollLiveness();
            switch (liveness)
            {
                case HelperLivenessState.NotOwned:
                case HelperLivenessState.Alive:
                    return;
                case HelperLivenessState.Exited:
                    AppLog.Warn("CenterM.Oem1", "Owned helper exited unexpectedly; retiring and reconciling fresh.", null, ("ProcessId", _helperOwnership.ProcessId));
                    _helperOwnership.RetireConfirmedExited();
                    BumpGeneration();
                    await ReconcileCore("HelperUnexpectedExit", cancellationToken).ConfigureAwait(false);
                    return;
                case HelperLivenessState.Uncertain:
                    // Do not blindly respawn, do not discard the retained handle.
                    _lastReason = "HelperLivenessUncertain";
                    SetState(CenterMOem1LifecycleState.FaultedNative);
                    AppLog.Warn("CenterM.Oem1", "Helper liveness poll returned Uncertain (WAIT_FAILED); entering faulted reconciliation without discarding the retained handle.", null, ("ProcessId", _helperOwnership.ProcessId));
                    return;
            }
        }
        finally { _gate.Release(); }
    }

    /// <summary>Polls for a foreign same-name "MSI Center M" process while Armed, and for the
    /// tracked real MainUI's visibility/exit while a real identity is being tracked. This is the
    /// single low-rate reconciliation tick a future PR's timer would call; it is never invoked by
    /// production in this PR.</summary>
    internal async Task PollTickAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_shutdown) return;

            if (_trackedMainUi is not null)
            {
                await ObserveTrackedMainUiCore(cancellationToken).ConfigureAwait(false);
                return;
            }

            if (_state == CenterMOem1LifecycleState.Armed)
                await CheckForForeignMainUiCore(cancellationToken).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

    /// <summary>IPowerSuspendParticipant: cancels in-flight destructive work (hidden-MainUI
    /// termination debounce), and prevents new helper creation while suspended. Never terminates the
    /// real MainUI, and never intentionally terminates the helper solely because suspend
    /// occurred.</summary>
    public async Task<bool> QuiesceForSuspendAsync(DateTimeOffset deadline, long cycle, long epoch, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            CancelPendingDebounceCore("Suspend");
            _lastReason = "SuspendQuiesced";
            AppLog.Info("CenterM.Oem1", "Suspend quiesce completed.", ("State", _state));
            return true;
        }
        finally { _gate.Release(); }
    }

    /// <summary>Fresh, complete OEM1 reconciliation on resume, independent of Steam/VIIPER routing
    /// success. Re-checks helper liveness, process topology, real MainUI presence/identity,
    /// Launcher/Server, and AutoRun before ever re-arming.</summary>
    internal async Task ReconcileAfterResumeAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_shutdown) return;

            if (_helperOwnership.IsOwned)
            {
                var liveness = _helperOwnership.PollLiveness();
                if (liveness == HelperLivenessState.Exited)
                {
                    _helperOwnership.RetireConfirmedExited();
                    BumpGeneration();
                }
                else if (liveness == HelperLivenessState.Uncertain)
                {
                    _lastReason = "ResumeHelperLivenessUncertain";
                    SetState(CenterMOem1LifecycleState.FaultedNative);
                    return;
                }
            }

            await ReconcileCore("ResumeReconcile", cancellationToken).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

    /// <summary>Cancels any pending debounce, prevents further mutation, and cleans only the exact
    /// Addon-owned helper identity. Never terminates a real MainUI.</summary>
    internal async Task ShutdownAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            CancelPendingDebounceCore("Shutdown");
            _shutdown = true;
            if (_helperOwnership.IsOwned)
            {
                var stopped = _helperOwnership.Stop(_helperStopTimeout);
                AppLog.Info("CenterM.Oem1", "Shutdown helper stop attempted.", ("Confirmed", stopped));
            }
            _trackedMainUi?.Dispose();
            _trackedMainUi = null;
            SetState(CenterMOem1LifecycleState.Disabled);
        }
        finally { _gate.Release(); }
    }

    public async ValueTask DisposeAsync()
    {
        await ShutdownAsync().ConfigureAwait(false);
        _gate.Dispose();
    }

    // ---- Core (gate already held) ----

    private async Task DisableCore(CancellationToken cancellationToken)
    {
        CancelPendingDebounceCore("Disable");
        _trackedMainUi?.Dispose();
        _trackedMainUi = null;
        _mainUiObserver.Reset();

        if (_helperOwnership.IsOwned)
        {
            var stopped = _helperOwnership.Stop(_helperStopTimeout);
            if (!stopped)
            {
                _lastReason = "DisableCleanupUnconfirmed";
                SetState(CenterMOem1LifecycleState.FaultedNative);
                AppLog.Warn("CenterM.Oem1", "Disable could not confirm helper cleanup; ownership retained, not reporting clean Disabled state.", null, ("ProcessId", _helperOwnership.ProcessId));
                await Task.CompletedTask;
                return;
            }
        }

        BumpGeneration();
        _lastReason = "Disabled";
        SetState(CenterMOem1LifecycleState.Disabled);
        await Task.CompletedTask;
    }

    private async Task ReconcileCore(string reason, CancellationToken cancellationToken)
    {
        _lastReason = reason;

        if (!_desiredEnabled)
        {
            SetState(CenterMOem1LifecycleState.Disabled);
            return;
        }

        _lastAutoRun = _autoRunReader();
        if (_lastAutoRun != CenterMAutoRunState.Disabled)
        {
            await EnsureNoHelperOwned(cancellationToken).ConfigureAwait(false);
            SetState(CenterMOem1LifecycleState.NeedsSetup);
            return;
        }

        var backend = _backendProbe.Capture();
        _launcherReady = backend.LauncherPresent;
        _serverReady = backend.ServerPresent;
        if (!_launcherReady || !_serverReady)
        {
            await EnsureNoHelperOwned(cancellationToken).ConfigureAwait(false);
            SetState(CenterMOem1LifecycleState.NeedsSetup);
            return;
        }

        SetState(CenterMOem1LifecycleState.Reconciling);

        var sameName = _processSnapshotSource.GetProcessesByName(CenterMProcessNames.MainUi);
        if (sameName is null)
        {
            _lastReason = "ProcessEnumerationUncertain";
            SetState(CenterMOem1LifecycleState.FaultedNative);
            return;
        }

        // The owned helper is itself a same-name process -- it must never be mistaken for a foreign
        // process appearing. Only entries that are NOT the exact owned PID count as foreign.
        var ownedPid = _helperOwnership.IsOwned ? _helperOwnership.ProcessId : null;
        var foreign = sameName.Where(p => p.ProcessId != ownedPid).ToList();

        if (foreign.Count > 0)
        {
            await HandleSameNamePresentDuringReconcile(foreign, cancellationToken).ConfigureAwait(false);
            return;
        }

        // No foreign same-name process: either arm a fresh helper, or (if we already have a live,
        // valid, owned helper -- e.g. a redundant reconcile call) simply confirm Armed.
        if (_helperOwnership.IsOwned)
        {
            var liveness = _helperOwnership.PollLiveness();
            if (liveness != HelperLivenessState.Alive)
            {
                // Owned but not confirmed alive with zero same-name processes present is impossible
                // in a consistent world (the owned helper IS the same-name process) -- fail open
                // rather than assume anything.
                _lastReason = "OwnedHelperMissingFromEnumeration";
                SetState(CenterMOem1LifecycleState.FaultedNative);
                return;
            }
            SetState(CenterMOem1LifecycleState.Armed);
            return;
        }

        await AttemptArm(cancellationToken).ConfigureAwait(false);
    }

    private async Task EnsureNoHelperOwned(CancellationToken cancellationToken)
    {
        if (!_helperOwnership.IsOwned) return;
        var stopped = _helperOwnership.Stop(_helperStopTimeout);
        if (!stopped)
            AppLog.Warn("CenterM.Oem1", "Could not confirm helper cleanup while leaving suppression readiness.", null, ("ProcessId", _helperOwnership.ProcessId));
        await Task.CompletedTask;
    }

    private async Task AttemptArm(CancellationToken cancellationToken)
    {
        var publishRoot = _publishRootProvider();
        var stagedPath = _stager(publishRoot);
        if (stagedPath is null)
        {
            _lastReason = "StagingFailed";
            SetState(CenterMOem1LifecycleState.FaultedNative);
            return;
        }

        var startResult = _helperOwnership.Start(stagedPath);
        switch (startResult)
        {
            case HelperStartResult.Started:
                break;
            case HelperStartResult.PartialCleanupUnconfirmed:
                // IsOwned is true here -- no second helper may ever be created while this remains
                // retained.
                _lastReason = "PartialCleanupUnconfirmed";
                SetState(CenterMOem1LifecycleState.FaultedNative);
                AppLog.Warn("CenterM.Oem1", "Helper start left partial cleanup unconfirmed; ownership retained, no second helper will be created.", null, ("ProcessId", _helperOwnership.ProcessId));
                return;
            default:
                _lastReason = $"HelperStartFailed:{startResult}";
                SetState(CenterMOem1LifecycleState.FaultedNative);
                return;
        }

        // Never publish Armed before this post-start invariant check.
        var fresh = _processSnapshotSource.GetProcessesByName(CenterMProcessNames.MainUi);
        var invariant = CenterMHelperInvariant.Evaluate(fresh, _helperOwnership.ProcessId!.Value);
        if (invariant == CenterMHelperInvariantState.Valid)
        {
            _lastReason = "Armed";
            SetState(CenterMOem1LifecycleState.Armed);
            return;
        }

        var stopped = _helperOwnership.Stop(_helperStopTimeout);
        _lastReason = stopped ? $"PostStartInvariantFailed:{invariant}" : $"PostStartInvariantFailed:{invariant}:CleanupUnconfirmed";
        SetState(CenterMOem1LifecycleState.FaultedNative);
        AppLog.Warn("CenterM.Oem1", "Post-start invariant check failed; helper stop attempted, never committed to Armed.", null, ("Invariant", invariant), ("CleanupConfirmed", stopped));
        await Task.CompletedTask;
    }

    private async Task HandleSameNamePresentDuringReconcile(IReadOnlyList<ProcessSnapshotEntry> sameName, CancellationToken cancellationToken)
    {
        if (_helperOwnership.IsOwned)
        {
            // A foreign same-name process appeared: suppression is no longer Armed regardless of
            // outcome below. Stop the exact owned helper before ever considering adoption of the
            // remaining process.
            var ownedPid = _helperOwnership.ProcessId;
            var stopped = _helperOwnership.Stop(_helperStopTimeout);
            if (!stopped)
            {
                _lastReason = "ForeignMainUiHelperStopUnconfirmed";
                SetState(CenterMOem1LifecycleState.FaultedNative);
                AppLog.Warn("CenterM.Oem1", "Foreign same-name process observed while Armed but helper stop could not be confirmed; blocking adoption and re-arm.", null, ("HelperProcessId", ownedPid));
                return;
            }
            BumpGeneration();

            var freshAfterStop = _processSnapshotSource.GetProcessesByName(CenterMProcessNames.MainUi);
            if (freshAfterStop is null)
            {
                _lastReason = "ProcessEnumerationUncertainAfterHelperStop";
                SetState(CenterMOem1LifecycleState.FaultedNative);
                return;
            }
            sameName = freshAfterStop;
        }

        if (sameName.Count == 0)
        {
            // The helper itself was the only same-name process; nothing remains to adopt.
            SetState(CenterMOem1LifecycleState.NeedsSetup);
            await AttemptArm(cancellationToken).ConfigureAwait(false);
            return;
        }

        if (sameName.Count > 1)
        {
            _lastReason = "MultipleForeignMainUiCandidates";
            SetState(CenterMOem1LifecycleState.FaultedNative);
            return;
        }

        await TryAdoptRealMainUi(sameName[0], cancellationToken).ConfigureAwait(false);
    }

    private async Task TryAdoptRealMainUi(ProcessSnapshotEntry candidate, CancellationToken cancellationToken)
    {
        if (!string.Equals(candidate.ProcessName, CenterMProcessNames.MainUi, StringComparison.Ordinal)
            || !SafeMainUiTerminator.PathMatchesExpectedPackage(candidate.ExecutablePath))
        {
            _lastReason = "MainUiCandidatePathMismatch";
            SetState(CenterMOem1LifecycleState.FaultedNative);
            AppLog.Warn("CenterM.Oem1", "Same-name candidate did not match the expected MSI Center M WindowsApps package path; no adoption, no kill.", null, ("ExecutablePath", candidate.ExecutablePath));
            return;
        }

        var tracked = _handleOpener is null
            ? TrackedCenterMMainUi.Create(candidate.ProcessId, candidate.ExecutablePath)
            : TrackedCenterMMainUi.Create(candidate.ProcessId, candidate.ExecutablePath, _handleOpener);

        if (!tracked.HasRetainedHandle)
        {
            tracked.Dispose();
            _lastReason = "MainUiIdentityHandleUnavailable";
            SetState(CenterMOem1LifecycleState.FaultedNative);
            return;
        }

        _trackedMainUi?.Dispose();
        _trackedMainUi = tracked;
        _mainUiObserver.Reset();
        BumpGeneration();
        _lastReason = "RealMainUiAdopted";
        AppLog.Info("CenterM.Oem1", "Real MainUI adopted; yielding to native Center M.", ("ProcessId", tracked.ProcessId));

        await ObserveTrackedMainUiCore(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Fresh visibility/exit observation for the currently tracked real MainUI. Drives
    /// Visible / HiddenAfterVisible-debounce / natural-exit reconciliation. Assumes the gate is
    /// already held and <see cref="_trackedMainUi"/> is not null.</summary>
    private async Task ObserveTrackedMainUiCore(CancellationToken cancellationToken)
    {
        var tracked = _trackedMainUi!;
        var lifecycle = _mainUiObserver.Observe(tracked.ProcessId);

        switch (lifecycle)
        {
            case MainUiLifecycleState.Visible:
                CancelPendingDebounceCore("VisibleAgain");
                SetState(CenterMOem1LifecycleState.NativeMainUiActive);
                return;

            case MainUiLifecycleState.StartingOrHiddenNeverVisible:
                // Never a kill candidate; never start a debounce.
                SetState(CenterMOem1LifecycleState.NativeMainUiActive);
                return;

            case MainUiLifecycleState.HiddenAfterVisible:
                SetState(CenterMOem1LifecycleState.HiddenDebounce);
                StartHiddenDebounce(tracked);
                return;

            case MainUiLifecycleState.Exited:
            case MainUiLifecycleState.Absent:
                await HandleTrackedMainUiNaturalExit(cancellationToken).ConfigureAwait(false);
                return;

            case MainUiLifecycleState.Uncertain:
                _lastReason = "MainUiWindowSnapshotUncertain";
                SetState(CenterMOem1LifecycleState.FaultedNative);
                return;
        }
    }

    private async Task HandleTrackedMainUiNaturalExit(CancellationToken cancellationToken)
    {
        CancelPendingDebounceCore("NaturalExit");
        _trackedMainUi?.Dispose();
        _trackedMainUi = null;
        _mainUiObserver.Reset();
        BumpGeneration();
        _lastReason = "RealMainUiNaturalExit";
        AppLog.Info("CenterM.Oem1", "Tracked real MainUI exited naturally; reconciling fresh.");
        await ReconcileCore("NaturalMainUiExitReconcile", cancellationToken).ConfigureAwait(false);
    }

    private async Task CheckForForeignMainUiCore(CancellationToken cancellationToken)
    {
        var sameName = _processSnapshotSource.GetProcessesByName(CenterMProcessNames.MainUi);
        if (sameName is null)
        {
            _lastReason = "ProcessEnumerationUncertain";
            SetState(CenterMOem1LifecycleState.FaultedNative);
            return;
        }

        var ownedPid = _helperOwnership.ProcessId;
        var foreign = sameName.Where(p => p.ProcessId != ownedPid).ToList();
        if (foreign.Count == 0) return;

        await HandleSameNamePresentDuringReconcile(foreign, cancellationToken).ConfigureAwait(false);
    }

    private void StartHiddenDebounce(TrackedCenterMMainUi tracked)
    {
        _debounceCts?.Cancel();
        _debounceCts?.Dispose();
        var cts = new CancellationTokenSource();
        _debounceCts = cts;
        var generationAtStart = _generation;
        var token = cts.Token;

        AppLog.Info("CenterM.Oem1", "Hidden-after-visible debounce started.", ("ProcessId", tracked.ProcessId));

        // Fire-and-forget by design: the debounce is a background timer whose only effect is to
        // eventually re-enter the single serialized gate and re-validate everything fresh. It must
        // never run its safety-relevant logic outside that gate.
        _ = RunDebounceAsync(tracked, generationAtStart, token);
    }

    private async Task RunDebounceAsync(TrackedCenterMMainUi tracked, long generationAtStart, CancellationToken token)
    {
        try
        {
            await _delay(_hiddenDebounce, token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            AppLog.Info("CenterM.Oem1", "Hidden debounce canceled before expiry.", ("ProcessId", tracked.ProcessId));
            return;
        }

        await _gate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        try
        {
            if (_shutdown) return;
            if (token.IsCancellationRequested) return;
            // A stale timer completion must never act on newer state: the generation must be
            // exactly what it was when this debounce started, the tracked identity must be the same
            // reference, and the coordinator must still be in HiddenDebounce.
            if (_generation != generationAtStart) return;
            if (!ReferenceEquals(_trackedMainUi, tracked)) return;
            if (_state != CenterMOem1LifecycleState.HiddenDebounce) return;

            await CompleteDebounceCore(tracked).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

    private async Task CompleteDebounceCore(TrackedCenterMMainUi tracked)
    {
        var result = _terminator.TryTerminate(tracked, _mainUiObserver.SeenVisible, _mainUiTerminateTimeout);
        AppLog.Info("CenterM.Oem1", "Debounce expired; safe termination evaluated.", ("Result", result), ("ProcessId", tracked.ProcessId));

        switch (result)
        {
            case SafeMainUiTerminationResult.Terminated:
            case SafeMainUiTerminationResult.AlreadyExited:
                _trackedMainUi?.Dispose();
                _trackedMainUi = null;
                _mainUiObserver.Reset();
                BumpGeneration();
                _lastReason = $"SafeTerminationCompleted:{result}";
                await ReconcileCore("PostTerminationReconcile", CancellationToken.None).ConfigureAwait(false);
                return;

            case SafeMainUiTerminationResult.VisibleAgain:
                _lastReason = "SafeTerminationVisibleAgain";
                SetState(CenterMOem1LifecycleState.NativeMainUiActive);
                return;

            default:
                // IdentityMismatch / AdditionalMainUiDetected / IdentityUncertain / AccessDenied /
                // WaitTimedOut / Failed -- all fail open. No helper re-arm from stale assumptions;
                // preserve exact evidence for later clean reconciliation.
                _lastReason = $"SafeTerminationFailedOpen:{result}";
                SetState(CenterMOem1LifecycleState.FaultedNative);
                return;
        }
    }

    private void CancelPendingDebounceCore(string reason)
    {
        if (_debounceCts is null) return;
        _debounceCts.Cancel();
        _debounceCts.Dispose();
        _debounceCts = null;
        BumpGeneration();
        AppLog.Info("CenterM.Oem1", "Hidden debounce canceled.", ("Reason", reason));
    }

    private void BumpGeneration() => _generation++;

    private void SetState(CenterMOem1LifecycleState next)
    {
        if (_state == next) return;
        AppLog.Info("CenterM.Oem1", "State transition.", ("From", _state), ("To", next), ("Reason", _lastReason));
        _state = next;
    }
}
