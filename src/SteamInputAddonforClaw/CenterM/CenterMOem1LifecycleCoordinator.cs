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
/// resumes after newer state has already committed recognize it is stale and refuse to act. A
/// separate monotonically increasing <see cref="_lifecycleEpoch"/> is bumped by disable/suspend/
/// shutdown BEFORE they ever try to acquire <see cref="_gate"/>, so a debounce continuation that
/// re-enters the gate after such a request can recognize the request became authoritative even if
/// the continuation itself happened to win the race for the gate first.
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
    private readonly IProcessIdentityInspector _identityInspector;
    private readonly Func<string, string?> _stager;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly Func<bool> _environmentEligible;
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
    private PendingDebounce? _pendingDebounce;
    /// <summary>Finding #3 (review 4957507443): tracks the most recently started fire-and-forget
    /// <see cref="RunDebounceAsync"/> task purely so <see cref="DisposeAsync"/> can drain it before
    /// disposing <see cref="_gate"/> -- never awaited/consulted by any safety-relevant logic, which
    /// must continue to rely only on <see cref="_pendingDebounce"/>/epoch/generation.</summary>
    private Task? _debounceTask;
    private long _generation;
    private long _lifecycleEpoch;
    private string? _lastReason;
    private bool _shutdown;

    /// <summary>Suspended mutation barrier (finding #1). While true, no arm/reconcile/destructive
    /// polling path may mutate helper ownership or MainUI tracking -- only
    /// <see cref="ReconcileAfterResumeAsync"/> clears it and performs the fresh resume
    /// reconciliation.</summary>
    private bool _suspended;

    /// <summary>Test-only synchronization seam: awaited (if set) by a debounce continuation
    /// immediately before it acquires <see cref="_gate"/>, letting tests deterministically force a
    /// competing high-priority request (disable/suspend/shutdown) to run to completion first. Never
    /// touched by production code.</summary>
    internal Func<Task>? TestOnly_BeforeDebounceGateAcquire;

    /// <summary>Test-only synchronization seam: raised once a debounce fire-and-forget continuation
    /// has finished running to completion, regardless of whether it canceled early, found itself
    /// stale, or ran <see cref="CompleteDebounceCore"/>. Lets tests replace wall-clock
    /// <c>Task.Delay</c> polling with deterministic awaits. Never touched by production code.</summary>
    internal event Action? TestOnly_DebounceContinuationFinished;

    /// <summary>Test-only synchronization seam (finding #2, review 4957507443): deterministically
    /// simulates a suspend/disable/shutdown request's lifecycle-epoch bump becoming authoritative
    /// while some other operation is already in flight, without any real timing race. Mirrors
    /// exactly what <see cref="QuiesceForSuspendAsync"/>/<see cref="SetDesiredEnabledAsync"/>/
    /// <see cref="ShutdownAsync"/> do before ever waiting on <see cref="_gate"/>. Never touched by
    /// production code.</summary>
    internal void TestOnly_BumpLifecycleEpoch() => BumpLifecycleEpoch();

    internal CenterMOem1LifecycleCoordinator(
        Func<string> publishRootProvider,
        CenterMBackendProbe? backendProbe = null,
        Func<CenterMAutoRunState>? autoRunReader = null,
        IProcessSnapshotSource? processSnapshotSource = null,
        CenterMHelperOwnership? helperOwnership = null,
        MainUiLifecycleObserver? mainUiObserver = null,
        SafeMainUiTerminator? terminator = null,
        IProcessHandleOpener? handleOpener = null,
        IProcessIdentityInspector? identityInspector = null,
        Func<string, string?>? stager = null,
        Func<TimeSpan, CancellationToken, Task>? delay = null,
        Func<bool>? environmentEligibility = null,
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
        _identityInspector = identityInspector ?? new Win32ProcessIdentityInspector();
        _stager = stager ?? CenterMHelperStaging.StageFromPublishRoot;
        _delay = delay ?? ((delay, ct) => Task.Delay(delay, ct));
        // Finding #5: fail-open for THIS feature means "leave MSI's native OEM1 behavior intact",
        // i.e. never start suppression -- so the default (and any probe exception/unknown result)
        // must be treated as unsupported/false, never true. A production composition seam must
        // explicitly inject a device/capability predicate to ever arm suppression at all.
        _environmentEligible = WrapEnvironmentEligibility(environmentEligibility);
        _hiddenDebounce = hiddenDebounce ?? TimeSpan.FromSeconds(1);
        _helperStopTimeout = helperStopTimeout ?? TimeSpan.FromSeconds(5);
        _mainUiTerminateTimeout = mainUiTerminateTimeout ?? TimeSpan.FromSeconds(5);
    }

    public string Name => "CenterMOem1LifecycleCoordinator";

    /// <summary>Finding #5: wraps the injected (or absent) eligibility predicate so neither an
    /// omitted predicate nor an exception thrown by one can ever be conflated with "supported" --
    /// both must be treated as unsupported/fail-open while any already-owned helper remains subject
    /// to normal reconcile-time cleanup.</summary>
    private static Func<bool> WrapEnvironmentEligibility(Func<bool>? predicate)
    {
        if (predicate is null) return static () => false;
        return () =>
        {
            try
            {
                return predicate();
            }
            catch (Exception ex)
            {
                AppLog.Warn("CenterM.Oem1", "Environment eligibility probe threw; treating as unsupported/fail-open.", ex);
                return false;
            }
        };
    }

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
        // Bumped BEFORE acquiring the gate (finding #6): a disable request becomes authoritative to
        // any debounce continuation that re-enters the gate later, even if that continuation happens
        // to win the race for the gate itself.
        if (!enabled) BumpLifecycleEpoch();

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
                    AppLog.Warn("CenterM.Oem1", "Owned helper exited unexpectedly; retiring.", null, ("ProcessId", _helperOwnership.ProcessId));
                    _helperOwnership.RetireConfirmedExited();
                    BumpGeneration();
                    if (_suspended)
                    {
                        // Suspended barrier (finding #1): retiring the now-stale ownership record is
                        // safe bookkeeping, but a fresh reconcile that could stage/start a new helper
                        // must never run until ReconcileAfterResumeAsync explicitly clears the
                        // barrier.
                        _lastReason = "HelperExitedWhileSuspended";
                        AppLog.Warn("CenterM.Oem1", "Helper exited while suspended; deferring reconciliation until resume.", null);
                        return;
                    }
                    AppLog.Warn("CenterM.Oem1", "Reconciling fresh after unexpected helper exit.", null);
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
            if (_suspended)
            {
                // Suspended barrier (finding #1): no new debounce may be started and no termination
                // may be attempted from stale PID/window evidence while suspended.
                return;
            }

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
    /// termination debounce), establishes the suspended mutation barrier so no later poll/reconcile
    /// entry point can stage/start a new helper or attempt MainUI termination while suspended, and
    /// prevents new helper creation while suspended. Never terminates the real MainUI, and never
    /// intentionally terminates the helper solely because suspend occurred.</summary>
    public async Task<bool> QuiesceForSuspendAsync(DateTimeOffset deadline, long cycle, long epoch, CancellationToken cancellationToken)
    {
        // Bumped BEFORE acquiring the gate (finding #6), same reasoning as SetDesiredEnabledAsync.
        BumpLifecycleEpoch();

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            CancelPendingDebounceCore("Suspend");
            _suspended = true;
            _lastReason = "SuspendQuiesced";
            AppLog.Info("CenterM.Oem1", "Suspend quiesce completed; suspended mutation barrier established.", ("State", _state));
            return true;
        }
        finally { _gate.Release(); }
    }

    /// <summary>Fresh, complete OEM1 reconciliation on resume, independent of Steam/VIIPER routing
    /// success. This is the explicit boundary that clears the suspended mutation barrier established
    /// by <see cref="QuiesceForSuspendAsync"/> -- no other entry point clears it. Re-checks helper
    /// liveness, process topology, real MainUI presence/identity, Launcher/Server, and AutoRun before
    /// ever re-arming.</summary>
    internal async Task ReconcileAfterResumeAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_shutdown) return;

            _suspended = false;

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
    /// Addon-owned helper identity. Never terminates a real MainUI. Never reports a clean
    /// <see cref="CenterMOem1LifecycleState.Disabled"/> state while the exact owned helper's exit
    /// could not be confirmed -- an unresolved retained handle stays retained and the coordinator
    /// stays <see cref="CenterMOem1LifecycleState.FaultedNative"/> instead (finding #5).</summary>
    internal async Task ShutdownAsync(CancellationToken cancellationToken = default)
    {
        // Bumped BEFORE acquiring the gate (finding #6), same reasoning as SetDesiredEnabledAsync.
        BumpLifecycleEpoch();

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            CancelPendingDebounceCore("Shutdown");
            _shutdown = true;
            _trackedMainUi?.Dispose();
            _trackedMainUi = null;

            if (_helperOwnership.IsOwned)
            {
                var stopped = _helperOwnership.Stop(_helperStopTimeout);
                AppLog.Info("CenterM.Oem1", "Shutdown helper stop attempted.", ("Confirmed", stopped));
                if (!stopped)
                {
                    _lastReason = "ShutdownCleanupUnconfirmed";
                    SetState(CenterMOem1LifecycleState.FaultedNative);
                    AppLog.Warn("CenterM.Oem1", "Shutdown could not confirm helper cleanup; ownership retained, not reporting clean Disabled state.", null, ("ProcessId", _helperOwnership.ProcessId));
                    return;
                }
            }

            SetState(CenterMOem1LifecycleState.Disabled);
        }
        finally { _gate.Release(); }
    }

    public async ValueTask DisposeAsync()
    {
        await ShutdownAsync().ConfigureAwait(false);

        // Finding #4 (review 4957507443): ShutdownAsync's own Stop attempt may not have confirmed
        // exit for the exact retained helper handle -- disposal is the last point at which this
        // coordinator can still drive that resolution before its serialization primitive goes away.
        // Give it a final bounded number of extra attempts against the SAME exact retained handle
        // (never name/PID rediscovery) rather than silently discarding the retry boundary the moment
        // ShutdownAsync's single attempt didn't resolve it.
        for (var attempt = 1; attempt <= DisposeFinalCleanupAttempts && _helperOwnership.IsOwned; attempt++)
        {
            if (_helperOwnership.Stop(_helperStopTimeout)) break;
            AppLog.Warn("CenterM.Oem1", "Dispose final cleanup attempt could not confirm helper termination.", null, ("Attempt", attempt), ("ProcessId", _helperOwnership.ProcessId));
        }

        if (_helperOwnership.IsOwned)
        {
            // Not discarded: the CenterMHelperOwnership instance (and its retained SafeProcessHandle/
            // job handle) is left exactly as-is -- still the sole authority over the exact process --
            // for whatever caller-held reference can still resolve it later. The coordinator's own
            // gate is still disposed below because DisposeAsync must complete in bounded time; it is
            // the coordinator's serialization ability that ends here, not the retained handle itself.
            AppLog.Warn("CenterM.Oem1", "Dispose completing with the exact retained helper handle still unresolved after bounded final cleanup attempts; ownership remains retained on the CenterMHelperOwnership instance, not discarded.", null, ("ProcessId", _helperOwnership.ProcessId));
        }

        // Finding #3 (review 4957507443): RunDebounceAsync is deliberately fire-and-forget, but a
        // continuation whose delay already completed and is merely waiting to (re-)acquire _gate
        // must be drained to completion BEFORE _gate is disposed -- otherwise it can resume after
        // disposal and fault on the now-disposed semaphore. Awaited here, outside the gate (already
        // released by ShutdownAsync), so this can never deadlock against the continuation's own
        // _gate.WaitAsync.
        var pendingDebounceTask = _debounceTask;
        if (pendingDebounceTask is not null)
        {
            try { await pendingDebounceTask.ConfigureAwait(false); }
            catch (Exception ex)
            {
                AppLog.Warn("CenterM.Oem1", "Debounce continuation drain during dispose observed an exception.", null, ("Exception", ex.Message));
            }
        }

        _gate.Dispose();
    }

    /// <summary>Bounded number of additional exact-handle Stop attempts <see cref="DisposeAsync"/>
    /// makes if <see cref="ShutdownAsync"/>'s own attempt did not confirm termination -- finite so
    /// disposal still completes in bounded time, but strictly more than the zero extra attempts
    /// disposal previously made.</summary>
    private const int DisposeFinalCleanupAttempts = 3;

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

        if (_suspended)
        {
            // Suspended barrier (finding #1): every mutating reconciliation path funnels through
            // here, so guarding at entry blocks re-arm, drift cleanup, and disable-retry alike until
            // ReconcileAfterResumeAsync explicitly clears the barrier.
            _lastReason = $"{reason}:SuspendedBarrier";
            AppLog.Info("CenterM.Oem1", "Reconcile suppressed by active suspend barrier.", ("Reason", reason));
            return;
        }

        if (!_desiredEnabled)
        {
            // Finding #5 (second bullet): a desired-disabled reconcile reached from any caller (not
            // just the direct SetDesiredEnabledAsync(false) -> DisableCore path) must preserve the
            // same "never falsely report clean" semantics -- it must not blindly commit Disabled
            // while the exact owned helper's cleanup remains unconfirmed.
            if (_helperOwnership.IsOwned)
            {
                var stopped = _helperOwnership.Stop(_helperStopTimeout);
                if (!stopped)
                {
                    _lastReason = "DesiredDisabledCleanupUnconfirmed";
                    SetState(CenterMOem1LifecycleState.FaultedNative);
                    AppLog.Warn("CenterM.Oem1", "Desired-disabled reconcile could not confirm helper cleanup; ownership retained, not reporting clean Disabled state.", null, ("ProcessId", _helperOwnership.ProcessId));
                    return;
                }
                BumpGeneration();
            }
            SetState(CenterMOem1LifecycleState.Disabled);
            return;
        }

        if (_trackedMainUi is not null)
        {
            // Finding #4: an existing tracked real MainUI identity is authoritative and must be
            // resolved FIRST, via its exact retained handle, before helper arm/re-adoption is ever
            // considered -- e.g. on resume after the tracked identity exited during sleep, or when
            // it remained alive and would otherwise be rediscovered from the process-name snapshot
            // and re-adopted as a second handle, losing the existing identity-bound SeenVisible
            // history. ObserveTrackedMainUiCore itself retires an exited identity (and then re-enters
            // ReconcileCore fresh with _trackedMainUi cleared) or fails open on an uncertain/mismatched
            // identity without ever considering helper arm.
            await ObserveTrackedMainUiCore(cancellationToken).ConfigureAwait(false);
            return;
        }

        if (!_environmentEligible())
        {
            // Finding #8: an unsupported/uncertain environment must fail open before ever touching
            // Launcher/Server/AutoRun/helper staging.
            var eligibilityClean = await EnsureNoHelperOwned(cancellationToken).ConfigureAwait(false);
            _lastReason = eligibilityClean ? "UnsupportedEnvironment" : "UnsupportedEnvironmentCleanupUnconfirmed";
            SetState(eligibilityClean ? CenterMOem1LifecycleState.NeedsSetup : CenterMOem1LifecycleState.FaultedNative);
            return;
        }

        _lastAutoRun = _autoRunReader();
        if (_lastAutoRun != CenterMAutoRunState.Disabled)
        {
            var autoRunClean = await EnsureNoHelperOwned(cancellationToken).ConfigureAwait(false);
            if (!autoRunClean)
            {
                _lastReason = "AutoRunDriftCleanupUnconfirmed";
                SetState(CenterMOem1LifecycleState.FaultedNative);
                return;
            }
            SetState(CenterMOem1LifecycleState.NeedsSetup);
            return;
        }

        var backend = _backendProbe.Capture();
        _launcherReady = backend.LauncherPresent;
        _serverReady = backend.ServerPresent;
        if (!_launcherReady || !_serverReady)
        {
            var prereqClean = await EnsureNoHelperOwned(cancellationToken).ConfigureAwait(false);
            if (!prereqClean)
            {
                _lastReason = "PrerequisiteDriftCleanupUnconfirmed";
                SetState(CenterMOem1LifecycleState.FaultedNative);
                return;
            }
            SetState(CenterMOem1LifecycleState.NeedsSetup);
            return;
        }

        SetState(CenterMOem1LifecycleState.Reconciling);

        var sameName = _processSnapshotSource.GetProcessesByName(CenterMProcessNames.MainUi);
        if (sameName is null)
        {
            // Finding #1 (review 4957507443): the same certainty-lost class as the existing-helper
            // validity branch below -- an uncertain enumeration reached from a fresh reconcile must
            // also actually disarm any owned helper, not merely report FaultedNative while it stays
            // running.
            var disarmed = DisarmOwnedHelperForFailOpen();
            _lastReason = disarmed ? "ProcessEnumerationUncertain:Disarmed" : "ProcessEnumerationUncertain:DisarmCleanupUnconfirmed";
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
        // valid, OPERATIONAL owned helper -- e.g. a redundant reconcile call) simply confirm Armed.
        if (_helperOwnership.IsOwned)
        {
            var validity = EvaluateArmedHelperValidity(sameName, ownedPid);
            if (validity == ArmedHelperValidity.Valid)
            {
                SetState(CenterMOem1LifecycleState.Armed);
                return;
            }

            // Finding #1/#3: owned does not mean operationally armed -- a job-less
            // PartialCleanupUnconfirmed residue, a liveness that is not confirmed Alive, or a fresh
            // same-name enumeration that no longer confirms the authoritative invariant must all
            // fail open into FaultedNative rather than ever being (re-)committed to Armed. Finding #1
            // (review 4957507443): failing open must actually disarm the exact owned helper too --
            // the helper filename IS the suppression mechanism, so leaving it running while merely
            // reporting a non-Armed state does not restore native OEM1 behavior.
            var disarmed = DisarmOwnedHelperForFailOpen();
            _lastReason = disarmed ? $"ExistingHelperInvalid:{validity}:Disarmed" : $"ExistingHelperInvalid:{validity}:DisarmCleanupUnconfirmed";
            SetState(CenterMOem1LifecycleState.FaultedNative);
            AppLog.Warn("CenterM.Oem1", "Existing-helper reconcile failed the authoritative armed-helper validity check; disarm attempted, never committing Armed.", null, ("Validity", validity), ("Disarmed", disarmed));
            return;
        }

        await AttemptArm(cancellationToken).ConfigureAwait(false);
    }

    private enum ArmedHelperValidity { Valid, NotOwned, ResidualCleanupOnly, LivenessNotAlive, InvariantInvalid }

    /// <summary>Single authoritative check for "is the currently retained helper handle eligible to
    /// be (or remain) Armed", shared by fresh reconciliation (finding #1) and the steady-state Armed
    /// poll path (finding #3) so the two can never drift apart. Never treats <see cref="CenterMHelperOwnership.IsOwned"/>
    /// alone as sufficient -- a job-less <see cref="HelperStartResult.PartialCleanupUnconfirmed"/>
    /// residue never completed the operational arm sequence and must never be promoted to Armed
    /// merely because it is alive and unique in a fresh enumeration.</summary>
    private ArmedHelperValidity EvaluateArmedHelperValidity(IReadOnlyList<ProcessSnapshotEntry> sameName, int? ownedPid)
    {
        if (ownedPid is null || !_helperOwnership.IsOwned) return ArmedHelperValidity.NotOwned;
        if (!_helperOwnership.IsOperationallyOwned) return ArmedHelperValidity.ResidualCleanupOnly;
        if (_helperOwnership.PollLiveness() != HelperLivenessState.Alive) return ArmedHelperValidity.LivenessNotAlive;
        return CenterMHelperInvariant.Evaluate(sameName, ownedPid.Value) == CenterMHelperInvariantState.Valid
            ? ArmedHelperValidity.Valid
            : ArmedHelperValidity.InvariantInvalid;
    }

    /// <summary>Finding #1 (review 4957507443): single shared path for "suppression certainty was
    /// lost, fail open" so every caller (fresh reconciliation, the steady-state Armed poll, an
    /// uncertain same-name enumeration, ...) actually disarms the exact owned helper instead of only
    /// changing <see cref="_state"/> -- the helper filename itself is the suppression mechanism, so
    /// <c>SuppressionReady == false</c> while it remains alive does not restore native OEM1
    /// behavior. A no-op (returns true) when nothing is owned. Never touches a foreign process, and
    /// never falls back to name/PID rediscovery -- only the exact retained handle via
    /// <see cref="CenterMHelperOwnership.Stop"/>.</summary>
    private bool DisarmOwnedHelperForFailOpen()
    {
        if (!_helperOwnership.IsOwned) return true;
        var stopped = _helperOwnership.Stop(_helperStopTimeout);
        if (stopped) BumpGeneration();
        return stopped;
    }

    /// <summary>Attempts to stop any owned helper and reports whether cleanup was actually
    /// confirmed. Finding #5: callers must never commit a "clean" state (NeedsSetup/Disabled) on the
    /// strength of this alone -- they must check the returned value and stay in an explicit
    /// unresolved state (FaultedNative) when it is false.</summary>
    private async Task<bool> EnsureNoHelperOwned(CancellationToken cancellationToken)
    {
        if (!_helperOwnership.IsOwned) return true;
        var stopped = _helperOwnership.Stop(_helperStopTimeout);
        if (!stopped)
            AppLog.Warn("CenterM.Oem1", "Could not confirm helper cleanup while leaving suppression readiness.", null, ("ProcessId", _helperOwnership.ProcessId));
        await Task.CompletedTask;
        return stopped;
    }

    private async Task AttemptArm(CancellationToken cancellationToken)
    {
        // Finding #2 (review 4957507443): the request-time suspend boundary (QuiesceForSuspendAsync
        // bumps _lifecycleEpoch BEFORE it ever waits for _gate) must invalidate an arm already in
        // flight, even though _suspended itself is only set once QuiesceForSuspendAsync finally
        // acquires _gate -- i.e. strictly AFTER this method (which holds the gate throughout) has
        // already returned. Capture the epoch now so it can be re-checked immediately before ever
        // committing Armed below.
        var epochAtStart = Interlocked.Read(ref _lifecycleEpoch);

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

        if (Interlocked.Read(ref _lifecycleEpoch) != epochAtStart)
        {
            // Finding #2: a suspend (or disable/shutdown) request became authoritative while this
            // arm was already in flight, strictly after the helper was created. The newly-created
            // exact owned helper must be cleaned immediately and Armed must never be committed from
            // this now-stale operation -- QuiesceForSuspendAsync is still blocked waiting for this
            // method to release the gate and will establish _suspended right after.
            var cleaned = _helperOwnership.Stop(_helperStopTimeout);
            _lastReason = cleaned ? "SuspendRequestedDuringArm" : "SuspendRequestedDuringArmCleanupUnconfirmed";
            SetState(cleaned ? CenterMOem1LifecycleState.NeedsSetup : CenterMOem1LifecycleState.FaultedNative);
            AppLog.Warn("CenterM.Oem1", "Lifecycle epoch changed (suspend/disable/shutdown requested) while an arm was in flight; newly-created helper cleanup attempted, Armed never committed.", null, ("Cleaned", cleaned));
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
            // The helper itself was the only same-name process; nothing remains to adopt. Finding
            // #6: the helper stop sequence above can involve one or two bounded waits (graceful
            // terminate/wait, and possibly a Job-close fallback wait) -- eligibility, Launcher/
            // Server, and AutoRun evidence captured earlier in this same ReconcileCore call may have
            // drifted during that interval. Never jump directly into AttemptArm from that stale
            // pre-stop snapshot; re-enter a fresh full reconciliation that recaptures every
            // prerequisite before ever creating another helper.
            SetState(CenterMOem1LifecycleState.NeedsSetup);
            await ReconcileCore("PostForeignHelperStopReconcile", cancellationToken).ConfigureAwait(false);
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

        // Finding #2: lifecycle authority must be anchored to the exact retained kernel process
        // object, never to the enumeration-time PID alone. Re-inspect the just-acquired handle
        // directly and require it to still prove Alive, the same PID, the exact process name, and
        // the expected package path before this identity is ever committed -- a candidate that
        // exited or changed identity between enumeration and handle acquisition must never be
        // adopted.
        var freshIdentity = _identityInspector.Inspect(tracked.Handle);
        if (freshIdentity.Status != LiveProcessProbeStatus.Alive
            || freshIdentity.ProcessId != candidate.ProcessId
            || !string.Equals(freshIdentity.ProcessName, CenterMProcessNames.MainUi, StringComparison.Ordinal)
            || !SafeMainUiTerminator.PathMatchesExpectedPackage(freshIdentity.ExecutablePath))
        {
            tracked.Dispose();
            _lastReason = "MainUiRetainedIdentityMismatchAtAdoption";
            SetState(CenterMOem1LifecycleState.FaultedNative);
            AppLog.Warn("CenterM.Oem1", "Freshly retained handle did not confirm the enumeration-time candidate identity; no adoption.", null, ("CandidateProcessId", candidate.ProcessId), ("RetainedStatus", freshIdentity.Status));
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

        // Finding #2: every observation re-anchors to the retained handle first -- never trusts the
        // PID/window snapshot as authority on its own. A PID can be reused by an unrelated process
        // between polls; only the retained kernel object can prove this is still the same identity.
        var identity = _identityInspector.Inspect(tracked.Handle);

        if (identity.Status == LiveProcessProbeStatus.Exited)
        {
            // The retained handle itself is authoritative for "this identity is gone" -- even if a
            // PID/window provider now reports something alive/visible for the same numeric PID
            // (reused by an unrelated process), that evidence must never be treated as a continuation
            // of this identity. SeenVisible must never transfer to a reused PID.
            await HandleTrackedMainUiNaturalExit(cancellationToken).ConfigureAwait(false);
            return;
        }

        if (identity.Status == LiveProcessProbeStatus.Uncertain
            || identity.ProcessId != tracked.ProcessId
            || !string.Equals(identity.ProcessName, CenterMProcessNames.MainUi, StringComparison.Ordinal)
            || !SafeMainUiTerminator.PathMatchesExpectedPackage(identity.ExecutablePath))
        {
            // Uncertain or a mismatched PID/name/path: cancel any destructive work in flight and
            // fail open. Never use stale PID/window evidence as authority to re-arm or terminate.
            CancelPendingDebounceCore("RetainedIdentityUncertainOrMismatch");
            _lastReason = "TrackedMainUiRetainedIdentityUncertain";
            SetState(CenterMOem1LifecycleState.FaultedNative);
            AppLog.Warn("CenterM.Oem1", "Retained MainUI handle inspection was uncertain or no longer matches the tracked identity; canceling any pending debounce and failing open.", null, ("TrackedProcessId", tracked.ProcessId), ("RetainedStatus", identity.Status));
            return;
        }

        // Only an alive, matching retained identity may proceed to PID-based window enumeration.
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
                // Finding #4: the debounce is edge-triggered and identity-bound -- it starts once
                // when first entering HiddenDebounce for this tracked identity. Repeated
                // HiddenAfterVisible observations while a debounce is already running for the same
                // identity must never restart the deadline; only an explicit cancellation condition
                // (visible again, exit, identity/topology change, disable, suspend, shutdown) does.
                if (_pendingDebounce is null)
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
            // Finding #1 (review 4957507443): an uncertain enumeration while Armed must actually
            // disarm the exact owned helper, not just flip the reported state -- the helper filename
            // is the suppression mechanism, and it must never be left running indefinitely just
            // because a later poll tick that could have re-checked it never runs while State !=
            // Armed.
            var disarmed = DisarmOwnedHelperForFailOpen();
            _lastReason = disarmed ? "ProcessEnumerationUncertain:Disarmed" : "ProcessEnumerationUncertain:DisarmCleanupUnconfirmed";
            SetState(CenterMOem1LifecycleState.FaultedNative);
            AppLog.Warn("CenterM.Oem1", "Same-name enumeration uncertain during poll; disarm attempted.", null, ("Disarmed", disarmed));
            return;
        }

        var ownedPid = _helperOwnership.IsOwned ? _helperOwnership.ProcessId : null;
        var foreign = sameName.Where(p => p.ProcessId != ownedPid).ToList();
        if (foreign.Count > 0)
        {
            await HandleSameNamePresentDuringReconcile(foreign, cancellationToken).ConfigureAwait(false);
            return;
        }

        // Finding #3: the steady-state Armed poll path must enforce the same authoritative
        // validity check as reconciliation -- a confidently empty same-name enumeration must never
        // be treated as "nothing changed" just because no foreign process appeared. Fails open into
        // FaultedNative (never re-arms blindly from this poll tick); a later reconcile call is what
        // may re-establish Armed from a freshly captured world.
        if (_state == CenterMOem1LifecycleState.Armed)
        {
            var validity = EvaluateArmedHelperValidity(sameName, ownedPid);
            if (validity != ArmedHelperValidity.Valid)
            {
                // Finding #1: same as the uncertain-enumeration branch above -- invalidating Armed
                // must actually disarm the exact owned helper, not merely relabel the state.
                var disarmed = DisarmOwnedHelperForFailOpen();
                _lastReason = disarmed ? $"ArmedInvariantFailedDuringPoll:{validity}:Disarmed" : $"ArmedInvariantFailedDuringPoll:{validity}:DisarmCleanupUnconfirmed";
                SetState(CenterMOem1LifecycleState.FaultedNative);
                AppLog.Warn("CenterM.Oem1", "Steady-state Armed poll failed the authoritative helper validity check; disarm attempted, invalidating Armed.", null, ("Validity", validity), ("Disarmed", disarmed));
            }
        }
    }

    /// <summary>Finding #2: the pending debounce's identity/ownership -- CTS plus the exact tracked
    /// identity/generation/epoch it started under. Using a single object (rather than a bare CTS
    /// reference) as both the "is a debounce currently active" flag and the cancellation handle lets
    /// <see cref="RunDebounceAsync"/> retire it deterministically, exactly once, and only when it is
    /// still the CURRENT pending debounce -- never clearing a newer replacement started by a later,
    /// different continuation.</summary>
    private sealed class PendingDebounce
    {
        internal required CancellationTokenSource Cts { get; init; }
        internal required TrackedCenterMMainUi Tracked { get; init; }
        internal required long Generation { get; init; }
        internal required long Epoch { get; init; }
    }

    private void StartHiddenDebounce(TrackedCenterMMainUi tracked)
    {
        // Defensive only: callers only ever invoke this while _pendingDebounce is null.
        _pendingDebounce?.Cts.Cancel();
        _pendingDebounce?.Cts.Dispose();

        var cts = new CancellationTokenSource();
        var pending = new PendingDebounce
        {
            Cts = cts,
            Tracked = tracked,
            Generation = _generation,
            Epoch = Interlocked.Read(ref _lifecycleEpoch)
        };
        _pendingDebounce = pending;

        AppLog.Info("CenterM.Oem1", "Hidden-after-visible debounce started.", ("ProcessId", tracked.ProcessId));

        // Fire-and-forget by design: the debounce is a background timer whose only effect is to
        // eventually re-enter the single serialized gate and re-validate everything fresh. It must
        // never run its safety-relevant logic outside that gate. The Task is retained in
        // _debounceTask solely so DisposeAsync can drain it before disposing _gate (finding #3) --
        // it is never awaited or consulted by any safety-relevant logic.
        _debounceTask = RunDebounceAsync(pending, cts.Token);
    }

    private async Task RunDebounceAsync(PendingDebounce mine, CancellationToken token)
    {
        var tracked = mine.Tracked;
        try
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

            // Test-only hook (finding #6/#7): lets a test deterministically park this continuation
            // right before it competes for the gate, so a concurrently-issued disable/suspend/
            // shutdown request can be driven to full completion first.
            if (TestOnly_BeforeDebounceGateAcquire is { } hook)
                await hook().ConfigureAwait(false);

            await _gate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
            try
            {
                if (_shutdown) return;
                if (token.IsCancellationRequested) return;
                // A stale timer completion must never act on newer state: the lifecycle epoch must
                // be exactly what it was when this debounce started (finding #6 -- a disable/
                // suspend/shutdown request bumps this BEFORE it ever tries to acquire the gate, so
                // this check is authoritative even if this continuation won the race for the gate),
                // the generation must be exactly what it was, the tracked identity must be the same
                // reference, and the coordinator must still be in HiddenDebounce.
                if (Interlocked.Read(ref _lifecycleEpoch) != mine.Epoch) return;
                if (_generation != mine.Generation) return;
                if (!ReferenceEquals(_trackedMainUi, tracked)) return;
                if (_state != CenterMOem1LifecycleState.HiddenDebounce) return;

                await CompleteDebounceCore(tracked).ConfigureAwait(false);
            }
            finally
            {
                // Finding #2: retire exactly once and only if this continuation's pending debounce
                // is still the current one. Covers every exit path above (shutdown/cancellation/
                // stale-epoch/stale-generation/stale-identity/stale-state, and the normal
                // CompleteDebounceCore completion) uniformly -- none of them may otherwise leave a
                // dead, non-null _pendingDebounce that blocks the next legitimate hidden transition
                // from ever starting a new debounce. A newer replacement debounce (a different
                // PendingDebounce instance) is never cleared by an older stale continuation.
                if (ReferenceEquals(_pendingDebounce, mine)) _pendingDebounce = null;
                _gate.Release();
            }
        }
        finally
        {
            TestOnly_DebounceContinuationFinished?.Invoke();
        }
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
        if (_pendingDebounce is null) return;
        var pending = _pendingDebounce;
        _pendingDebounce = null;
        pending.Cts.Cancel();
        pending.Cts.Dispose();
        BumpGeneration();
        AppLog.Info("CenterM.Oem1", "Hidden debounce canceled.", ("Reason", reason));
    }

    private void BumpGeneration() => _generation++;

    private void BumpLifecycleEpoch() => Interlocked.Increment(ref _lifecycleEpoch);

    private void SetState(CenterMOem1LifecycleState next)
    {
        if (_state == next) return;
        AppLog.Info("CenterM.Oem1", "State transition.", ("From", _state), ("To", next), ("Reason", _lastReason));
        _state = next;
    }
}
