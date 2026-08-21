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
/// caller (test, or the production composition seam) calling one of the public methods below. A
/// lifecycle/composition PR production-composes this type into the real MSI Claw runtime lifetime
/// (see <see cref="Devices.MSI.Claw.MsiClawRoutingComposition"/> and
/// <see cref="CenterMOem1LifecycleRuntime"/>) and drives its poll contract. As of the OEM1 production
/// action path (<see cref="Devices.MSI.Claw.MsiClawRoutingComposition.ConfigureOem1ActionPath"/>),
/// production DOES call <see cref="SetDesiredEnabledAsync"/> with true once WMI observation has
/// actually started, and with false again on action-failure fail-open or shutdown.
/// </summary>
internal sealed class CenterMOem1LifecycleCoordinator : IPowerSuspendParticipant, IAsyncDisposable, ICenterMOem1LifecycleDriverTarget
{
    private readonly Func<string> _publishRootProvider;
    private readonly CenterMBackendProbe _backendProbe;
    private readonly IProcessSnapshotSource _processSnapshotSource;
    private readonly CenterMHelperOwnership _helperOwnership;
    private readonly Func<bool>? _externalHelperDemand;
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
    private bool _launcherReady;
    private bool _serverReady;
    private TrackedCenterMMainUi? _trackedMainUi;
    private PendingDebounce? _pendingDebounce;
    /// <summary>Review 4957630432 finding #4: tracks EVERY still-active fire-and-forget
    /// <see cref="RunDebounceAsync"/> task -- not only the most recently started one -- purely so
    /// <see cref="DisposeAsync"/> can drain all of them before disposing <see cref="_gate"/>. An
    /// older continuation whose delay already completed can still be alive (parked immediately
    /// before re-acquiring <see cref="_gate"/>) even after <see cref="CancelPendingDebounceCore"/>
    /// has cleared <see cref="_pendingDebounce"/> and a newer replacement debounce has started, so a
    /// single "latest task" field is not enough to guarantee every continuation is drained. Guarded
    /// by <see cref="_debounceTasksLock"/> (not <see cref="_gate"/>) because entries are added under
    /// the gate but removed from a continuation callback that must never itself try to reacquire the
    /// gate. Never awaited/consulted by any safety-relevant logic, which must continue to rely only
    /// on <see cref="_pendingDebounce"/>/epoch/generation.</summary>
    private readonly List<Task> _debounceTasks = [];
    private readonly object _debounceTasksLock = new();
    private long _generation;
    private long _lifecycleEpoch;
    private string? _lastReason;
    private bool _shutdown;

    /// <summary>Review 4957791980 finding #1 (BLOCKER): an explicit, request-time-scoped barrier
    /// distinct from <see cref="_lifecycleEpoch"/>. <see cref="_lifecycleEpoch"/> only records "the
    /// world changed since some earlier snapshot" -- it cannot distinguish "this arm attempt started
    /// cleanly" from "this arm attempt started AFTER a higher-priority request had already bumped
    /// the epoch and is still waiting for <see cref="_gate"/>", because a later waiter is not
    /// guaranteed to lose the race for a <see cref="SemaphoreSlim"/> against an earlier one. This
    /// counter is incremented by <see cref="SetDesiredEnabledAsync"/> (disable only),
    /// <see cref="QuiesceForSuspendAsync"/>, and <see cref="ShutdownAsync"/> BEFORE they ever wait on
    /// <see cref="_gate"/>, and decremented only after that same call has finished running under the
    /// gate (win or lose the race). Any arm-committing path must check
    /// <see cref="IsHighPriorityRequestPending"/> -- not just the epoch delta -- immediately before
    /// every destructive/arming commit point, so a later mutation can never adopt an
    /// already-bumped epoch as a "clean" baseline while a disable/suspend/shutdown request is still
    /// pending. Review 4958332345: mutated and read exclusively under <see cref="_requestBoundarySync"/>
    /// now (a plain <c>object</c>+<c>lock</c>, deliberately not the C# 13 <c>Lock</c> type -- the
    /// existing concurrency history on this branch, CI flakiness from a contended <c>Lock</c>, argues
    /// for the simplest primitive here too) rather than bare <c>Interlocked</c> operations, so the
    /// increment/decrement/check and the "operation start" linearization decision at the three
    /// remaining commit points can never interleave.</summary>
    private int _pendingHighPriorityRequests;

    /// <summary>Review 4958332345 (BLOCKER): a genuine linearization primitive shared by
    /// <see cref="BeginHighPriorityRequest"/>/<see cref="EndHighPriorityRequest"/> and the three
    /// remaining "operation start" points (helper Start, final Armed publication, real-MainUI
    /// termination). The prior round's fix added <see cref="IsHighPriorityRequestPending"/> as an
    /// extra check-then-act read at each site -- still a TOCTOU race, because the read was never
    /// synchronized with the atomic increment in <see cref="BeginHighPriorityRequest"/> via any
    /// shared ordering primitive, so the scheduler could preempt between the check and the mutation
    /// it was meant to gate. A plain <c>object</c>+<c>lock</c> (never the C# 13 <c>Lock</c> type --
    /// this branch already hit CI flakiness from a contended <c>Lock</c>) makes "is a high-priority
    /// request pending" and "the ordinary/destructive operation is now beginning" a single atomic
    /// decision: whichever side wins the lock first is unambiguously ordered before the other. Only
    /// ever held briefly around that decision -- never across an <c>await</c>, native Start call, or
    /// the terminator/WaitForExit sequence.</summary>
    private readonly object _requestBoundarySync = new();

    /// <summary>Suspended mutation barrier (finding #1). While true, no arm/reconcile/destructive
    /// polling path may mutate helper ownership or MainUI tracking -- only
    /// <see cref="ReconcileAfterResumeAsync"/> clears it and performs the fresh resume
    /// reconciliation.</summary>
    private bool _suspended;

    // Request-time suspend intent survives a participant timeout. Unlike _suspended, this is set
    // before waiting for _gate and is cleared only by the explicit resume boundary.
    private bool _suspendBarrierRequested;

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

    /// <summary>Test-only synchronization seam (review 4957791980, finding #1): deterministically
    /// simulates a disable/suspend/shutdown request having already marked itself pending via the
    /// request-time barrier -- mirroring exactly what <see cref="SetDesiredEnabledAsync"/>(false)/
    /// <see cref="QuiesceForSuspendAsync"/>/<see cref="ShutdownAsync"/> do before ever waiting on
    /// <see cref="_gate"/> -- without any real timing race. Never touched by production code. Must be
    /// paired with <see cref="TestOnly_EndHighPriorityRequest"/> once the test no longer needs the
    /// marker set.</summary>
    internal void TestOnly_BeginHighPriorityRequest() => BeginHighPriorityRequest();

    /// <summary>Test-only synchronization seam (review 4957791980, finding #1): retires a marker set
    /// by <see cref="TestOnly_BeginHighPriorityRequest"/>. Never touched by production code.</summary>
    internal void TestOnly_EndHighPriorityRequest() => EndHighPriorityRequest();

    /// <summary>Test-only synchronization seam (review 4958332345, site 1): invoked immediately
    /// before <see cref="AttemptArm"/> calls <see cref="TryLinearizeOrdinaryMutationStart"/> for
    /// helper Start, letting a test deterministically start a REAL competing high-priority request
    /// (e.g. <see cref="QuiesceForSuspendAsync"/>) and let its synchronous
    /// <see cref="BeginHighPriorityRequest"/> prefix run to completion before the linearization
    /// decision is made -- without any real timing race, since the calling method still holds
    /// <see cref="_gate"/> throughout. Never touched by production code.</summary>
    internal Func<Task>? TestOnly_BeforeHelperStartLinearization;

    /// <summary>Test-only synchronization seam (review 4958332345, site 2): invoked immediately
    /// before <see cref="AttemptArm"/> calls <see cref="TryCommitArmedState"/> for the final Armed
    /// publication. Same purpose and guarantees as <see cref="TestOnly_BeforeHelperStartLinearization"/>.
    /// Never touched by production code.</summary>
    internal Func<Task>? TestOnly_BeforeArmedCommitLinearization;

    /// <summary>Test-only synchronization seam (review 4958332345, site 3): invoked immediately
    /// before <see cref="RunDebounceAsync"/> calls <see cref="TryLinearizeOrdinaryMutationStart"/>
    /// for real-MainUI termination, after <see cref="RefreshSuppressionPrerequisites"/> has already
    /// succeeded. Same purpose and guarantees as <see cref="TestOnly_BeforeHelperStartLinearization"/>.
    /// Never touched by production code.</summary>
    internal Func<Task>? TestOnly_BeforeTerminationLinearization;

    /// <summary>Test-only accessor (review 4957630432 finding #3): exposes the coordinator's own
    /// <see cref="CenterMHelperOwnership"/> instance -- whether caller-injected or constructed
    /// internally by the default constructor path -- so a test can prove the terminal ownership
    /// policy in <see cref="DisposeAsync"/> applies to the true default/internal construction path,
    /// not only a test-harness-injected reference held independently of the coordinator. Never
    /// touched by production code.</summary>
    internal CenterMHelperOwnership TestOnly_HelperOwnership => _helperOwnership;

    internal CenterMOem1LifecycleCoordinator(
        Func<string> publishRootProvider,
        CenterMBackendProbe? backendProbe = null,
        IProcessSnapshotSource? processSnapshotSource = null,
        CenterMHelperOwnership? helperOwnership = null,
        MainUiLifecycleObserver? mainUiObserver = null,
        SafeMainUiTerminator? terminator = null,
        IProcessHandleOpener? handleOpener = null,
        IProcessIdentityInspector? identityInspector = null,
        Func<string, string?>? stager = null,
        Func<TimeSpan, CancellationToken, Task>? delay = null,
        Func<bool>? environmentEligibility = null,
        Func<bool>? externalHelperDemand = null,
        TimeSpan? hiddenDebounce = null,
        TimeSpan? helperStopTimeout = null,
        TimeSpan? mainUiTerminateTimeout = null,
        IHelperProcessNativeApi? testOnlyDefaultHelperNativeApi = null)
    {
        _publishRootProvider = publishRootProvider;
        var snapshotSource = processSnapshotSource ?? new Win32ProcessSnapshotSource();
        _backendProbe = backendProbe ?? new CenterMBackendProbe(snapshotSource);
        _processSnapshotSource = snapshotSource;
        // testOnlyDefaultHelperNativeApi (review 4957630432 finding #3) only ever substitutes the
        // native API used by the coordinator's OWN internally-constructed CenterMHelperOwnership --
        // never touched by production code (always null there) -- so a test can deterministically
        // exercise the true default/internal ownership construction path (as opposed to the
        // caller-injected `helperOwnership` parameter) without making any real Win32 calls.
        _helperOwnership = helperOwnership ?? new CenterMHelperOwnership(testOnlyDefaultHelperNativeApi);
        _externalHelperDemand = externalHelperDemand;
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

    // Explicit ICenterMOem1LifecycleDriverTarget forwarding: the coordinator's own methods stay
    // `internal` (unchanged -- this is not a rewrite of the coordinator), this just gives
    // CenterMOem1LifecycleRuntime a narrow, fakeable seam onto them.
    Task ICenterMOem1LifecycleDriverTarget.PollHelperLivenessAsync(CancellationToken cancellationToken) => PollHelperLivenessAsync(cancellationToken);
    Task ICenterMOem1LifecycleDriverTarget.PollTickAsync(CancellationToken cancellationToken) => PollTickAsync(cancellationToken);
    Task ICenterMOem1LifecycleDriverTarget.ReconcileAfterResumeAsync(CancellationToken cancellationToken) => ReconcileAfterResumeAsync(cancellationToken);

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
        // Review fix (BLOCKER): QuiesceForSuspendAsync deliberately keeps an already-armed helper
        // alive and does not leave the Armed state, so _state == Armed alone is not sufficient once
        // suspend has revoked custom-action admission (see CenterMOem1LifecycleRuntime.onSuspending).
        // SuppressionReady must also require the suspend barrier to be clear, or a racing runtime
        // tick's onReconciled could re-observe SuppressionReady == true and re-enable the bridge
        // while suspend is still in effect.
        var suppressionReady = _state == CenterMOem1LifecycleState.Armed && !IsSuspendBarrierActive;
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
            _launcherReady,
            _serverReady,
            _lastReason);
    }

    /// <summary>Internal runtime control seam. Called with true by the production OEM1 action path
    /// (<see cref="Devices.MSI.Claw.MsiClawRoutingComposition.ConfigureOem1ActionPath"/>) once WMI
    /// observation has actually started, and with false again on action-failure fail-open or
    /// shutdown.</summary>
    internal async Task SetDesiredEnabledAsync(bool enabled, CancellationToken cancellationToken = default)
    {
        // Bumped BEFORE acquiring the gate (finding #6): a disable request becomes authoritative to
        // any debounce continuation that re-enters the gate later, even if that continuation happens
        // to win the race for the gate itself.
        //
        // Review 4957791980 finding #1: also marks the request pending via the request-time barrier
        // BEFORE ever waiting on the gate -- a later waiter that wins the race for the gate first
        // must see this, not just a same-valued epoch snapshot, so it can never adopt the
        // already-bumped epoch as a clean baseline for a new arm.
        if (!enabled) { BeginHighPriorityRequest(); BumpLifecycleEpoch(); }

        try
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
                await ReconcileCore("SetDesiredEnabled(true)", Interlocked.Read(ref _lifecycleEpoch), cancellationToken).ConfigureAwait(false);
            }
            finally { _gate.Release(); }
        }
        finally
        {
            if (!enabled) EndHighPriorityRequest();
        }
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
            await ReconcileCore(reason, Interlocked.Read(ref _lifecycleEpoch), cancellationToken).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

    /// <summary>Polls the exact retained helper handle for unexpected death while Armed. A no-op
    /// (returns immediately) when shutdown is active or this coordinator is cleanly dormant;
    /// the latter prevents it from applying OEM1 policy to a helper borrowed by the routing guard.</summary>
    internal async Task PollHelperLivenessAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_shutdown) return;
            if (!_desiredEnabled && _state == CenterMOem1LifecycleState.Disabled) return;
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
                    if (IsSuspendBarrierActive)
                    {
                        // Suspended barrier (finding #1): retiring the now-stale ownership record is
                        // safe bookkeeping, but a fresh reconcile that could stage/start a new helper
                        // must never run until ReconcileAfterResumeAsync explicitly clears the
                        // barrier. Review 4957630432 finding #1 (state-consistency gap): the exact
                        // owned helper is now confirmed gone, so Armed is no longer semantically true
                        // even though re-arm is deferred until resume -- invalidate it here so the
                        // snapshot can never report SuppressionReady == true with
                        // HelperProcessId == null in the meantime.
                        _lastReason = "HelperExitedWhileSuspended";
                        if (_state == CenterMOem1LifecycleState.Armed)
                            SetState(CenterMOem1LifecycleState.NeedsSetup);
                        AppLog.Warn("CenterM.Oem1", "Helper exited while suspended; deferring reconciliation until resume.", null);
                        return;
                    }
                    AppLog.Warn("CenterM.Oem1", "Reconciling fresh after unexpected helper exit.", null);
                    await ReconcileCore("HelperUnexpectedExit", Interlocked.Read(ref _lifecycleEpoch), cancellationToken).ConfigureAwait(false);
                    return;
                case HelperLivenessState.Uncertain:
                    // Review 4957630432 finding #1 (BLOCKER): WAIT_FAILED means suppression
                    // certainty is lost -- the helper filename/process IS the suppression mechanism,
                    // so this must use the same shared fail-open disarm contract as the topology/
                    // invariant paths (DisarmOwnedHelperForFailOpen) instead of merely relabeling
                    // _state while the exact owned helper may still be running. Never falls back to
                    // process-name/PID rediscovery; never respawns from this same tick.
                    {
                        var disarmed = DisarmOwnedHelperForFailOpen();
                        _lastReason = disarmed ? "HelperLivenessUncertain:Disarmed" : "HelperLivenessUncertain:DisarmCleanupUnconfirmed";
                        SetState(CenterMOem1LifecycleState.FaultedNative);
                        AppLog.Warn("CenterM.Oem1", "Helper liveness poll returned Uncertain (WAIT_FAILED); exact-handle disarm attempted, never blindly respawning.", null, ("ProcessId", _helperOwnership.ProcessId), ("Disarmed", disarmed));
                    }
                    return;
            }
        }
        finally { _gate.Release(); }
    }

    /// <summary>Polls for a foreign same-name "MSI Center M" process while Armed, and for the
    /// tracked real MainUI's visibility/exit while a real identity is being tracked. This is the
    /// single low-rate reconciliation tick <see cref="CenterMOem1LifecycleRuntime"/> drives whenever
    /// the routing guard is not Armed.</summary>
    internal async Task PollTickAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_shutdown) return;
            if (IsSuspendBarrierActive)
            {
                // Suspended barrier (finding #1): no new debounce may be started and no termination
                // may be attempted from stale PID/window evidence while suspended.
                return;
            }

            var pollEpoch = Interlocked.Read(ref _lifecycleEpoch);

            if (_trackedMainUi is not null)
            {
                // Review 4957791980 finding #2: this poll tick calls ObserveTrackedMainUiCore
                // directly (not through ReconcileCore), so it must independently refresh the same
                // prerequisite facts before permitting hidden-debounce termination -- otherwise the
                // exact same bypass the finding describes for resume/ReconcileCore would still be
                // reachable from this entry point.
                var prerequisitesValid = RefreshSuppressionPrerequisites();
                await ObserveTrackedMainUiCore(cancellationToken, prerequisitesValid, pollEpoch).ConfigureAwait(false);
                return;
            }

            if (_state == CenterMOem1LifecycleState.Armed)
            {
                // Review (re-review of a8c8658): steady-state Armed must keep revalidating the same
                // environment/Launcher/Server prerequisites that gated the original arm -- helper
                // liveness/same-name topology alone (CheckForForeignMainUiCore) never catches
                // Launcher/Server disappearing while already Armed. Reuses the existing
                // RefreshSuppressionPrerequisites/DisarmOwnedHelperForFailOpen primitives (same ones
                // ReconcileCore and the tracked-MainUI poll branch above already use) rather than
                // adding a new validator.
                var prerequisitesValid = RefreshSuppressionPrerequisites();
                if (!prerequisitesValid)
                {
                    var disarmed = DisarmOwnedHelperForFailOpen();
                    _lastReason = disarmed
                        ? $"ArmedPrerequisiteDrift:Launcher={_launcherReady}:Server={_serverReady}:Disarmed"
                        : "ArmedPrerequisiteDrift:DisarmCleanupUnconfirmed";
                    SetState(disarmed ? CenterMOem1LifecycleState.NeedsSetup : CenterMOem1LifecycleState.FaultedNative);
                    AppLog.Warn("CenterM.Oem1", "Armed prerequisite drift detected; custom suppression is failing open.", null,
                        ("LauncherPresent", _launcherReady), ("ServerPresent", _serverReady), ("Disarmed", disarmed));
                    return;
                }

                await CheckForForeignMainUiCore(pollEpoch, cancellationToken).ConfigureAwait(false);
            }
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
        // Review 4957791980 finding #1: also marks the request pending via the request-time barrier
        // for the same reason -- see SetDesiredEnabledAsync.
        BeginSuspendBarrier();

        try
        {
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
        finally { EndHighPriorityRequest(); }
    }

    /// <summary>Fresh, complete OEM1 reconciliation on resume, independent of Steam/VIIPER routing
    /// success. This is the explicit boundary that clears the suspended mutation barrier established
    /// by <see cref="QuiesceForSuspendAsync"/> -- no other entry point clears it. Re-checks helper
    /// liveness, process topology, real MainUI presence/identity, and Launcher/Server before ever
    /// re-arming.</summary>
    internal async Task ReconcileAfterResumeAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_shutdown) return;

            lock (_requestBoundarySync) _suspendBarrierRequested = false;
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
                    // Review 4957630432 finding #1: same shared fail-open disarm contract as the
                    // normal poll path -- an uncertain retained helper must not survive
                    // ReconcileAfterResumeAsync merely because this method returns before
                    // ReconcileCore's own invalid-helper cleanup would otherwise apply.
                    var disarmed = DisarmOwnedHelperForFailOpen();
                    _lastReason = disarmed ? "ResumeHelperLivenessUncertain:Disarmed" : "ResumeHelperLivenessUncertain:DisarmCleanupUnconfirmed";
                    SetState(CenterMOem1LifecycleState.FaultedNative);
                    AppLog.Warn("CenterM.Oem1", "Resume helper liveness poll returned Uncertain; exact-handle disarm attempted.", null, ("Disarmed", disarmed));
                    return;
                }
            }

            await ReconcileCore("ResumeReconcile", Interlocked.Read(ref _lifecycleEpoch), cancellationToken).ConfigureAwait(false);
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
        // Review 4957791980 finding #1: also marks the request pending via the request-time barrier
        // for the same reason -- see SetDesiredEnabledAsync.
        BeginHighPriorityRequest();
        BumpLifecycleEpoch();

        try
        {
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
        finally { EndHighPriorityRequest(); }
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
            // Review 4957630432 finding #3 (MAJOR): a private field with no caller-visible or
            // process-level owner does not actually satisfy the PR1 retention contract once
            // DisposeAsync has made the coordinator itself unusable -- registering the SAME instance
            // (never discarded, never a copy) with the process-wide CenterMOrphanedHelperRegistry
            // gives it a real, testable owner that a future process-shutdown/recovery seam can still
            // resolve later, regardless of whether this CenterMHelperOwnership was caller-injected or
            // constructed internally by the coordinator's default constructor path. The coordinator's
            // own gate is still disposed below because DisposeAsync must complete in bounded time; it
            // is the coordinator's serialization ability that ends here, not the retained handle
            // itself.
            CenterMOrphanedHelperRegistry.Register(_helperOwnership);
            AppLog.Warn("CenterM.Oem1", "Dispose completing with the exact retained helper handle still unresolved after bounded final cleanup attempts; ownership registered with the process-level orphan retry owner, not discarded.", null, ("ProcessId", _helperOwnership.ProcessId));
        }

        // Review 4957630432 finding #4 (MAJOR): RunDebounceAsync is deliberately fire-and-forget,
        // and CancelPendingDebounceCore clears _pendingDebounce immediately while an older
        // continuation whose delay already completed may still be alive, parked immediately before
        // (re-)acquiring _gate. A later hidden cycle can legitimately start a REPLACEMENT debounce
        // once _pendingDebounce is null, so tracking only the single most-recently-started
        // continuation is not enough -- every still-active continuation must be drained before
        // _gate is disposed, or an older stale one can resume after disposal and fault on the
        // now-disposed semaphore. Drained in a loop (rather than a single snapshot) so a
        // continuation that is itself in the process of being added/removed can never be missed.
        // Awaited here, outside the gate (already released by ShutdownAsync), so this can never
        // deadlock against any continuation's own _gate.WaitAsync -- and once _shutdown is true, no
        // further debounce can ever be newly started (every gate-guarded entry point checks
        // _shutdown before it could reach StartHiddenDebounce), so this loop is guaranteed to
        // terminate.
        while (true)
        {
            Task[] snapshot;
            lock (_debounceTasksLock) snapshot = _debounceTasks.Count == 0 ? [] : [.. _debounceTasks];
            if (snapshot.Length == 0) break;

            foreach (var pendingDebounceTask in snapshot)
            {
                try { await pendingDebounceTask.ConfigureAwait(false); }
                catch (Exception ex)
                {
                    AppLog.Warn("CenterM.Oem1", "Debounce continuation drain during dispose observed an exception.", null, ("Exception", ex.Message));
                }
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

        if (_helperOwnership.IsOwned && !HasExternalHelperDemand())
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

    private async Task ReconcileCore(string reason, long expectedEpoch, CancellationToken cancellationToken)
    {
        _lastReason = reason;

        if (IsSuspendBarrierActive)
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
            if (_helperOwnership.IsOwned && !HasExternalHelperDemand())
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

        // Capture the reconciliation epoch once. A suspend participant may time out while this
        // gate-held reconciliation is in flight; its pending marker can then be cleared, but this
        // epoch must still prevent stale Armed publication.
        var reconciliationEpoch = expectedEpoch;

        if (_trackedMainUi is not null)
        {
            // Finding #4 (review 4957507443): an existing tracked real MainUI identity is
            // authoritative and must be RESOLVED first, via its exact retained handle, before helper
            // arm/re-adoption is ever considered -- e.g. on resume after the tracked identity exited
            // during sleep, or when it remained alive and would otherwise be rediscovered from the
            // process-name snapshot and re-adopted as a second handle, losing the existing
            // identity-bound SeenVisible history. ObserveTrackedMainUiCore itself retires an exited
            // identity (and then re-enters ReconcileCore fresh with _trackedMainUi cleared) or fails
            // open on an uncertain/mismatched identity without ever considering helper arm.
            //
            // Review 4957791980 finding #2 (MAJOR): "identity resolved first" is not the same as
            // "termination is still permitted". Refresh the same prerequisite facts (environment
            // eligibility, Launcher/Server) that gate helper arm/suppression BEFORE ever
            // starting or continuing hidden-debounce termination -- resume must re-check the complete
            // current-world prerequisite set, and prerequisite drift during sleep must leave native
            // Center M behavior intact rather than continue a destructive custom-lifecycle action
            // (debounce -> terminate) from pre-suspend assumptions. This never disturbs the retained
            // identity/SeenVisible resolution order above; it only additionally gates whether
            // ObserveTrackedMainUiCore may treat a HiddenAfterVisible observation as a termination
            // candidate.
            var prerequisitesValid = RefreshSuppressionPrerequisites();
            await ObserveTrackedMainUiCore(cancellationToken, prerequisitesValid, expectedEpoch).ConfigureAwait(false);
            return;
        }

        var environmentEligible = _environmentEligible();
        if (!environmentEligible)
        {
            // Finding #8: an unsupported/uncertain environment must fail open before ever touching
            // Launcher/Server/helper staging.
            var eligibilityClean = await EnsureNoHelperOwned(cancellationToken).ConfigureAwait(false);
            _lastReason = eligibilityClean ? "HardwareEnvironmentIneligible" : "HardwareEnvironmentIneligibleCleanupUnconfirmed";
            SetState(eligibilityClean ? CenterMOem1LifecycleState.NeedsSetup : CenterMOem1LifecycleState.FaultedNative);
            LogPrerequisiteSnapshot("NeedsSetup", environmentEligible, null, null);
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
            _lastReason = !_launcherReady ? "LauncherMissing" : "ServerMissing";
            SetState(CenterMOem1LifecycleState.NeedsSetup);
            LogPrerequisiteSnapshot("NeedsSetup", environmentEligible, _launcherReady, _serverReady);
            return;
        }

        LogPrerequisiteSnapshot("ReadyToArm", environmentEligible, _launcherReady, _serverReady);
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
            await HandleSameNamePresentDuringReconcile(foreign, reconciliationEpoch, cancellationToken).ConfigureAwait(false);
            return;
        }

        // No foreign same-name process: either arm a fresh helper, or (if we already have a live,
        // valid, OPERATIONAL owned helper -- e.g. a redundant reconcile call) simply confirm Armed.
        if (_helperOwnership.IsOwned)
        {
            var validity = EvaluateArmedHelperValidity(sameName, ownedPid);
            if (validity == ArmedHelperValidity.Valid)
            {
                // Review 4958332345: re-confirming Armed for an already-owned, already-valid helper
                // is still a commit point, sharing the SAME atomic linearization primitive as the
                // fresh-arm commit in AttemptArm -- a disable/suspend/shutdown request that wins
                // _requestBoundarySync first must never be papered over by simply re-confirming the
                // old Armed state.
                if (!TryCommitArmedState(reconciliationEpoch))
                {
                    var disarmedPending = DisarmOwnedHelperForFailOpen();
                    _lastReason = disarmedPending ? "HighPriorityRequestPendingDuringArmedConfirm:Disarmed" : "HighPriorityRequestPendingDuringArmedConfirm:DisarmCleanupUnconfirmed";
                    SetState(CenterMOem1LifecycleState.FaultedNative);
                    AppLog.Warn("CenterM.Oem1", "A high-priority request won the request boundary while re-confirming an already-valid Armed helper; disarm attempted, Armed never re-committed.", null, ("Disarmed", disarmedPending));
                    return;
                }
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

        await AttemptArm(reconciliationEpoch, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Review 4957791980 finding #2: captures the same fresh prerequisite facts
    /// (environment eligibility, Launcher/Server) that gate helper arm/suppression readiness
    /// elsewhere in <see cref="ReconcileCore"/>, WITHOUT performing any helper cleanup -- used purely
    /// to decide whether hidden-debounce termination of an already-tracked real MainUI may proceed.
    /// Short-circuits (never touches Launcher/Server/<see cref="_launcherReady"/>/
    /// <see cref="_serverReady"/> beyond resetting them to false) once an earlier prerequisite is
    /// already known invalid, matching the same fail-fast order used by the no-tracked-MainUI path
    /// below.</summary>
    private bool RefreshSuppressionPrerequisites()
    {
        if (!_environmentEligible())
        {
            _launcherReady = false;
            _serverReady = false;
            return false;
        }

        var backend = _backendProbe.Capture();
        _launcherReady = backend.LauncherPresent;
        _serverReady = backend.ServerPresent;
        return _launcherReady && _serverReady;
    }

    private void LogPrerequisiteSnapshot(string decision, bool? environmentEligible, bool? launcherPresent, bool? serverPresent)
    {
        AppLog.Info("CenterM.Oem1", "Fresh prerequisite snapshot evaluated.",
            ("EnvironmentEligible", environmentEligible),
            ("RemappingEnabled", _desiredEnabled),
            ("LauncherPresent", launcherPresent),
            ("ServerPresent", serverPresent),
            ("HelperOwned", _helperOwnership.IsOwned),
            ("HelperProcessId", _helperOwnership.ProcessId),
            ("Decision", decision),
            ("Reason", _lastReason));
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
        if (HasExternalHelperDemand())
        {
            AppLog.Warn("CenterM.Oem1", "OEM1 fail-open deferred shared-helper teardown because Routing still requires it.", null,
                ("ProcessId", _helperOwnership.ProcessId));
            return false;
        }
        var stopped = _helperOwnership.Stop(_helperStopTimeout);
        if (stopped) BumpGeneration();
        return stopped;
    }

    private bool HasExternalHelperDemand()
    {
        try { return _externalHelperDemand?.Invoke() == true; }
        catch (Exception exception)
        {
            AppLog.Warn("CenterM.Oem1", "External helper demand could not be confirmed; OEM1 retains fail-open cleanup authority.", exception);
            return false;
        }
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

    private async Task AttemptArm(long expectedEpoch, CancellationToken cancellationToken)
    {
        // Finding #2 (review 4957507443): the request-time suspend boundary (QuiesceForSuspendAsync
        // bumps _lifecycleEpoch BEFORE it ever waits for _gate) must invalidate an arm already in
        // flight, even though _suspended itself is only set once QuiesceForSuspendAsync finally
        // acquires _gate -- i.e. strictly AFTER this method (which holds the gate throughout) has
        // already returned. Capture the epoch now so it can be re-checked immediately before ever
        // committing Armed below.
        // Keep the epoch captured by ReconcileCore. A timed-out suspend may have bumped the
        // lifecycle epoch and cleared its pending marker before this child operation is entered;
        // that newer value is not a valid baseline for the prerequisite/topology evidence that
        // caused this arm.

        var publishRoot = _publishRootProvider();
        var stagedPath = _stager(publishRoot);
        if (stagedPath is null)
        {
            _lastReason = "StagingFailed";
            SetState(CenterMOem1LifecycleState.FaultedNative);
            return;
        }

        // Review 4957630432 finding #2 (BLOCKER): re-check the captured epoch immediately before
        // ever calling Start -- the prior fix only checked AFTER Start succeeded, leaving a window
        // where a suspend/disable/shutdown request became authoritative during staging (before
        // Start was even entered) and a helper could still be newly created. If it already changed,
        // nothing has been created yet, so there is nothing to clean up.
        //
        // Review 4958332345 (BLOCKER): replaces the prior bare IsHighPriorityRequestPending
        // check-then-act read with a genuine linearization decision under _requestBoundarySync.
        // TryLinearizeOrdinaryMutationStart() makes "no high-priority request currently holds the
        // boundary" and "helper Start is now beginning" a single atomic step, closing the window
        // where BeginHighPriorityRequest() could win the race immediately after a separate check
        // passed but strictly before Start was ever called. The epoch-delta check stays alongside it
        // (never replaced by it) -- it still catches a distinct case the boundary lock alone cannot:
        // an epoch bump with no lingering pending marker, e.g. a caller-driven epoch bump (test-only
        // seam) or, in principle, a request whose entire begin/end lifecycle happened not to overlap
        // this exact instant.
        if (TestOnly_BeforeHelperStartLinearization is { } beforeStartHook)
            await beforeStartHook().ConfigureAwait(false);

                if (!TryLinearizeOrdinaryMutationStart(expectedEpoch))
        {
            _lastReason = "SuspendRequestedDuringArmBeforeStart";
            SetState(CenterMOem1LifecycleState.NeedsSetup);
            AppLog.Warn("CenterM.Oem1", "Lifecycle epoch changed or a high-priority request (suspend/disable/shutdown) won the request boundary before helper Start was ever entered; no helper created, Armed never committed.", null);
            return;
        }

        var startResult = _helperOwnership.Start(stagedPath);
        AppLog.Info("CenterM.Oem1", "Helper create/start result.", ("Result", startResult), ("ProcessId", _helperOwnership.ProcessId));
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

        // Review 4957791980 finding #1: same independent-of-epoch-delta reasoning as the pre-Start
        // check above.
        if (Interlocked.Read(ref _lifecycleEpoch) != expectedEpoch || IsHighPriorityRequestPending)
        {
            // Finding #2: a suspend (or disable/shutdown) request became authoritative while this
            // arm was already in flight, strictly after the helper was created. The newly-created
            // exact owned helper must be cleaned immediately and Armed must never be committed from
            // this now-stale operation -- QuiesceForSuspendAsync is still blocked waiting for this
            // method to release the gate and will establish _suspended right after.
            var cleaned = _helperOwnership.Stop(_helperStopTimeout);
            _lastReason = cleaned ? "SuspendRequestedDuringArm" : "SuspendRequestedDuringArmCleanupUnconfirmed";
            SetState(cleaned ? CenterMOem1LifecycleState.NeedsSetup : CenterMOem1LifecycleState.FaultedNative);
            AppLog.Warn("CenterM.Oem1", "Lifecycle epoch changed or a high-priority request is pending (suspend/disable/shutdown) while an arm was in flight; newly-created helper cleanup attempted, Armed never committed.", null, ("Cleaned", cleaned));
            return;
        }

        // Never publish Armed before this post-start invariant check.
        var fresh = _processSnapshotSource.GetProcessesByName(CenterMProcessNames.MainUi);
        var invariant = CenterMHelperInvariant.Evaluate(fresh, _helperOwnership.ProcessId!.Value);
        if (invariant == CenterMHelperInvariantState.Valid)
        {
            // Review 4958332345/4958548607 (BLOCKER): the final linearization point, replacing the prior
            // epoch-delta + IsHighPriorityRequestPending check-then-act read. TryCommitArmedState(epoch)
            // performs the "no high-priority request holds the boundary" check and the Armed
            // publication itself under the SAME _requestBoundarySync lock, so a disable/suspend/
            // shutdown request can never win the boundary in the gap between the check and
            // SetState(Armed) -- there is no such gap anymore. State publication is cheap (no I/O,
            // no native calls), so doing it inside the lock is safe; only the (possibly slow) cleanup
            // below runs outside it. The epoch is checked inside the same lock because a timed-out
            // suspend participant may already have cleared its pending marker while its epoch bump
            // must still invalidate this in-flight arm.
            if (TestOnly_BeforeArmedCommitLinearization is { } beforeCommitHook)
                await beforeCommitHook().ConfigureAwait(false);

            if (!TryCommitArmedState(expectedEpoch))
            {
                var cleanedAtCommit = _helperOwnership.Stop(_helperStopTimeout);
                _lastReason = cleanedAtCommit ? "SuspendRequestedDuringArmCommit" : "SuspendRequestedDuringArmCommitCleanupUnconfirmed";
                SetState(cleanedAtCommit ? CenterMOem1LifecycleState.NeedsSetup : CenterMOem1LifecycleState.FaultedNative);
                AppLog.Warn("CenterM.Oem1", "A high-priority request won the request boundary at the final Armed commit point; newly-created helper cleanup attempted, Armed never committed.", null, ("Cleaned", cleanedAtCommit));
                return;
            }

            AppLog.Info("CenterM.Oem1", "OEM1 suppression lifecycle armed.", ("State", CenterMOem1LifecycleState.Armed), ("SuppressionReady", true), ("ProcessId", _helperOwnership.ProcessId));

            return;
        }

        var stopped = _helperOwnership.Stop(_helperStopTimeout);
        _lastReason = stopped ? $"PostStartInvariantFailed:{invariant}" : $"PostStartInvariantFailed:{invariant}:CleanupUnconfirmed";
        SetState(CenterMOem1LifecycleState.FaultedNative);
        AppLog.Warn("CenterM.Oem1", "Post-start invariant check failed; helper stop attempted, never committed to Armed.", null, ("Invariant", invariant), ("CleanupConfirmed", stopped));
        await Task.CompletedTask;
    }

    private async Task HandleSameNamePresentDuringReconcile(IReadOnlyList<ProcessSnapshotEntry> sameName, long expectedEpoch, CancellationToken cancellationToken)
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
            // terminate/wait, and possibly a Job-close fallback wait) -- eligibility and Launcher/
            // Server evidence captured earlier in this same ReconcileCore call may have
            // drifted during that interval. Never jump directly into AttemptArm from that stale
            // pre-stop snapshot; re-enter a fresh full reconciliation that recaptures every
            // prerequisite before ever creating another helper.
            SetState(CenterMOem1LifecycleState.NeedsSetup);
            await ReconcileCore("PostForeignHelperStopReconcile", expectedEpoch, cancellationToken).ConfigureAwait(false);
            return;
        }

        if (sameName.Count > 1)
        {
            _lastReason = "MultipleForeignMainUiCandidates";
            SetState(CenterMOem1LifecycleState.FaultedNative);
            return;
        }

        await TryAdoptRealMainUi(sameName[0], expectedEpoch, cancellationToken).ConfigureAwait(false);
    }

    private async Task TryAdoptRealMainUi(ProcessSnapshotEntry candidate, long expectedEpoch, CancellationToken cancellationToken)
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

        // This adoption is reached only after the same ReconcileCore call already confirmed
        // environment eligibility and Launcher/Server readiness earlier in
        // this exact invocation (the foreign-same-name branch runs strictly after those checks), and
        // _mainUiObserver.Reset() was just called above, so the very first observation below can
        // never itself be a HiddenAfterVisible termination candidate. Passing true here is therefore
        // consistent with -- not a bypass of -- review 4957791980 finding #2's prerequisite gate.
        await ObserveTrackedMainUiCore(cancellationToken, prerequisitesValid: true, expectedEpoch).ConfigureAwait(false);
    }

    /// <summary>Fresh visibility/exit observation for the currently tracked real MainUI. Drives
    /// Visible / HiddenAfterVisible-debounce / natural-exit reconciliation. Assumes the gate is
    /// already held and <see cref="_trackedMainUi"/> is not null.</summary>
    /// <param name="prerequisitesValid">Review 4957791980 finding #2: whether environment
    /// eligibility and Launcher/Server were all freshly re-confirmed valid THIS call. When
    /// false, a HiddenAfterVisible observation must never start or continue hidden-debounce
    /// termination -- prerequisite drift must leave native Center M behavior intact rather than
    /// continue a destructive custom-lifecycle action from pre-suspend/stale assumptions. Does not
    /// affect natural-exit/uncertain/mismatch handling, which already fail open unconditionally.</param>
    private async Task ObserveTrackedMainUiCore(CancellationToken cancellationToken, bool prerequisitesValid, long expectedEpoch)
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
            await HandleTrackedMainUiNaturalExit(expectedEpoch, cancellationToken).ConfigureAwait(false);
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
                // Review 4958040630 finding #1 (BLOCKER): the request-time high-priority marker
                // (round 6, IsHighPriorityRequestPending) protected the helper-arm linearization
                // points, but a hidden debounce that is started/continued from this observation is
                // equally a "later mutation that could adopt an already-bumped epoch as a clean
                // baseline" -- a suspend/disable/shutdown request can have already marked itself
                // pending (and bumped the epoch) while still waiting for _gate, only for THIS poll to
                // win the race and observe HiddenAfterVisible on the old, not-yet-committed world
                // state. Refuse to start or continue the debounce in that window; cancel any
                // in-flight one and stay passive/native, exactly like the prerequisites-invalid case.
                if (!prerequisitesValid || !TryLinearizeOrdinaryMutationStart(expectedEpoch))
                {
                    // Review 4957791980 finding #2 (MAJOR): prerequisite drift (environment
                    // eligibility or Launcher/Server) must never be bypassed just because a
                    // real MainUI identity is already tracked. Cancel any debounce already in flight,
                    // never start a new one, and never terminate -- stay passive/native until a later
                    // fresh reconciliation confirms the prerequisite set is valid again. The retained
                    // identity/SeenVisible history is preserved untouched; only termination is gated.
                    CancelPendingDebounceCore(prerequisitesValid ? "LifecycleInvalidatedAtObservation" : "PrerequisitesInvalidAtObservation");
                    _lastReason = prerequisitesValid ? "TrackedMainUiHiddenHighPriorityRequestPending" : "TrackedMainUiHiddenPrerequisitesInvalid";
                    SetState(CenterMOem1LifecycleState.NativeMainUiActive);
                    AppLog.Warn("CenterM.Oem1", "Tracked real MainUI observed hidden-after-visible, but prerequisites are invalid or a high-priority disable/suspend/shutdown request is pending; no debounce, no termination, staying passive.", null, ("ProcessId", tracked.ProcessId), ("PrerequisitesValid", prerequisitesValid), ("HighPriorityRequestPending", IsHighPriorityRequestPending));
                    return;
                }

                SetState(CenterMOem1LifecycleState.HiddenDebounce);
                // Finding #4: the debounce is edge-triggered and identity-bound -- it starts once
                // when first entering HiddenDebounce for this tracked identity. Repeated
                // HiddenAfterVisible observations while a debounce is already running for the same
                // identity must never restart the deadline; only an explicit cancellation condition
                // (visible again, exit, identity/topology change, disable, suspend, shutdown) does.
                if (_pendingDebounce is null)
                    StartHiddenDebounce(tracked, expectedEpoch);
                return;

            case MainUiLifecycleState.Exited:
            case MainUiLifecycleState.Absent:
                await HandleTrackedMainUiNaturalExit(expectedEpoch, cancellationToken).ConfigureAwait(false);
                return;

            case MainUiLifecycleState.Uncertain:
                _lastReason = "MainUiWindowSnapshotUncertain";
                SetState(CenterMOem1LifecycleState.FaultedNative);
                return;
        }
    }

    private async Task HandleTrackedMainUiNaturalExit(long expectedEpoch, CancellationToken cancellationToken)
    {
        CancelPendingDebounceCore("NaturalExit");
        _trackedMainUi?.Dispose();
        _trackedMainUi = null;
        _mainUiObserver.Reset();
        BumpGeneration();
        _lastReason = "RealMainUiNaturalExit";
        AppLog.Info("CenterM.Oem1", "Tracked real MainUI exited naturally; reconciling fresh.");
        await ReconcileCore("NaturalMainUiExitReconcile", expectedEpoch, cancellationToken).ConfigureAwait(false);
    }

    private async Task CheckForForeignMainUiCore(long expectedEpoch, CancellationToken cancellationToken)
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
            await HandleSameNamePresentDuringReconcile(foreign, expectedEpoch, cancellationToken).ConfigureAwait(false);
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

    private void StartHiddenDebounce(TrackedCenterMMainUi tracked, long expectedEpoch)
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
            Epoch = expectedEpoch
        };
        _pendingDebounce = pending;

        AppLog.Info("CenterM.Oem1", "Hidden-after-visible debounce started.", ("ProcessId", tracked.ProcessId));

        // Fire-and-forget by design: the debounce is a background timer whose only effect is to
        // eventually re-enter the single serialized gate and re-validate everything fresh. It must
        // never run its safety-relevant logic outside that gate. The Task is tracked in
        // _debounceTasks solely so DisposeAsync can drain EVERY still-active continuation before
        // disposing _gate (review 4957630432 finding #4) -- it is never awaited or consulted by any
        // safety-relevant logic. Removed from the tracking list via a continuation (not from inside
        // RunDebounceAsync itself) so the removal never depends on the gate being held.
        var task = RunDebounceAsync(pending, cts.Token);
        lock (_debounceTasksLock) _debounceTasks.Add(task);
        task.ContinueWith(
            static (_, state) =>
            {
                var (coordinator, completed) = ((CenterMOem1LifecycleCoordinator, Task))state!;
                lock (coordinator._debounceTasksLock) coordinator._debounceTasks.Remove(completed);
            },
            (this, task),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
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

                // Review 4958040630 finding #1 (BLOCKER): re-check the request-time high-priority
                // marker AGAIN here, immediately after this continuation has won the race for _gate
                // and immediately before any termination attempt -- not just at the observation point
                // that started this debounce. A suspend/disable/shutdown request can have marked
                // itself pending and bumped the epoch AFTER this debounce's own epoch snapshot was
                // captured (StartHiddenDebounce runs strictly before that later request), so the
                // epoch/generation/identity/state checks above can all legitimately still pass while
                // the marker is set. The epoch check stays as-is; this is a genuinely independent gate
                // for the "request became pending after this debounce already started" case.
                if (IsHighPriorityRequestPending)
                {
                    _lastReason = "HighPriorityRequestPendingAtDebounceExpiry";
                    AppLog.Warn("CenterM.Oem1", "A disable/suspend/shutdown request is pending as this debounce reached the gate; termination refused.", null, ("ProcessId", tracked.ProcessId));
                    SetState(CenterMOem1LifecycleState.NativeMainUiActive);
                    return;
                }

                // Review 4958040630 finding #2 (MAJOR): the debounce is intentionally ~1 second long,
                // and OEM1 feature prerequisites (environment eligibility, Launcher/Server)
                // are not something SafeMainUiTerminator itself knows about -- it only performs final
                // retained-handle/process/window/same-name safety checks. Re-capture them fresh here,
                // immediately before ever invoking the terminator, so drift that happened entirely
                // during the debounce window (with no intervening poll) cannot lead to a termination
                // decided on now-stale evidence captured back at the original HiddenAfterVisible
                // observation.
                if (!RefreshSuppressionPrerequisites())
                {
                    _lastReason = "TrackedMainUiHiddenPrerequisitesInvalidAtDebounceExpiry";
                    AppLog.Warn("CenterM.Oem1", "OEM1 suppression prerequisites are no longer valid at debounce expiry; termination refused, tracked real MainUI left intact.", null, ("ProcessId", tracked.ProcessId));
                    SetState(CenterMOem1LifecycleState.NativeMainUiActive);
                    return;
                }

                // Review 4958332345 (BLOCKER): RefreshSuppressionPrerequisites() itself performs
                // several externally-observed operations (environment eligibility, Launcher/
                // Server capture) and can legitimately take real time. A disable/suspend/shutdown
                // request can become request-time-authoritative (BeginHighPriorityRequest) WHILE that
                // refresh is in flight and then sit waiting for _gate -- invisible to the earlier
                // check in this method (which ran strictly before the refresh started) and invisible
                // to the refresh's own return value (which only reflects OEM1 environment validity,
                // not lifecycle authority). This is the final destructive linearization point for this
                // debounce: TryLinearizeOrdinaryMutationStart() makes the "no high-priority request
                // holds the boundary" decision and "destructive termination is now beginning" decision
                // atomic under the same _requestBoundarySync lock used by the helper-arm sites, so a
                // request can never win the boundary in the gap between a separate check and entering
                // CompleteDebounceCore. The lock itself is held only for the brief decision -- never
                // across CompleteDebounceCore/SafeMainUiTerminator.TryTerminate/TerminateProcess/
                // WaitForExit, which can legitimately take real time. This check is additive to, never
                // a replacement for, SafeMainUiTerminator's own fresh retained-handle/window/topology
                // checks.
                if (TestOnly_BeforeTerminationLinearization is { } beforeTerminationHook)
                    await beforeTerminationHook().ConfigureAwait(false);

                if (!TryLinearizeOrdinaryMutationStart(mine.Epoch))
                {
                    _lastReason = "HighPriorityRequestWonTerminationBoundary";
                    AppLog.Warn("CenterM.Oem1", "A high-priority request won the request boundary immediately before destructive MainUI termination; termination refused, tracked real MainUI left intact.", null, ("ProcessId", tracked.ProcessId));
                    SetState(CenterMOem1LifecycleState.NativeMainUiActive);
                    return;
                }

                await CompleteDebounceCore(tracked, mine.Epoch).ConfigureAwait(false);
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

    private async Task CompleteDebounceCore(TrackedCenterMMainUi tracked, long expectedEpoch)
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
                await ReconcileCore("PostTerminationReconcile", expectedEpoch, CancellationToken.None).ConfigureAwait(false);
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

    /// <summary>Review 4957791980 finding #1 (extended by review 4958332345): marks a disable/
    /// suspend/shutdown request as pending, BEFORE that request ever waits on <see cref="_gate"/>.
    /// Must be paired with exactly one <see cref="EndHighPriorityRequest"/> regardless of outcome
    /// (success, early return, or an exception/cancellation while waiting for the gate). Now
    /// synchronized via <see cref="_requestBoundarySync"/> (not a bare Interlocked increment) so it
    /// participates in the same atomic ordering as <see cref="TryLinearizeOrdinaryMutationStart"/>
    /// and <see cref="TryCommitArmedState"/> -- the increment and a concurrent linearization decision
    /// can never interleave.</summary>
    private void BeginHighPriorityRequest()
    {
        lock (_requestBoundarySync) { _pendingHighPriorityRequests++; }
    }

    /// <summary>Review 4957791980 finding #1: retires a request marked pending by
    /// <see cref="BeginHighPriorityRequest"/>. Deliberately not tied to the exact moment the
    /// authoritative flag (<c>_desiredEnabled</c>/<c>_suspended</c>/<c>_shutdown</c>) is committed --
    /// holding the marker slightly longer (through the rest of that call's own gate-held work) is
    /// always safe, only ever making a concurrent arm-committing path more conservative, never less.
    /// Held only briefly under <see cref="_requestBoundarySync"/>, never across an await.</summary>
    private void EndHighPriorityRequest()
    {
        lock (_requestBoundarySync) { _pendingHighPriorityRequests--; }
    }

    /// <summary>Review 4957791980 finding #1: true while at least one disable/suspend/shutdown
    /// request has bumped its request-time marker but has not yet finished running under
    /// <see cref="_gate"/>. Retained as a plain observational read for the sites that were not
    /// converted to a linearization call in review 4958332345 (the post-Start / post-invariant-
    /// capture checks inside <see cref="AttemptArm"/>, and the debounce/observation checks that run
    /// well before the final destructive decision) -- those sites still have a genuine follow-up
    /// checkpoint later in the same method that performs the actual linearized decision, so an
    /// ordinary synchronized read here is sufficient and keeps those call sites unchanged.</summary>
    private bool IsHighPriorityRequestPending
    {
        get { lock (_requestBoundarySync) { return _pendingHighPriorityRequests != 0; } }
    }

    private bool IsSuspendBarrierActive
    {
        get
        {
            lock (_requestBoundarySync)
                return _suspendBarrierRequested || _suspended;
        }
    }

    private void BeginSuspendBarrier()
    {
        lock (_requestBoundarySync)
        {
            _suspendBarrierRequested = true;
            _pendingHighPriorityRequests++;
            _lifecycleEpoch++;
        }
    }

    /// <summary>Review 4958332345 (BLOCKER): the shared linearization point for an ordinary/
    /// destructive mutation's "operation start" -- helper Start (site 1) and real-MainUI termination
    /// (site 3). Returns <see langword="true"/> only if no high-priority request currently holds
    /// <see cref="_requestBoundarySync"/> with a pending count, and this successful return IS the
    /// documented linearization point at which the caller's operation has begun: a high-priority
    /// request that wins the lock first (via <see cref="BeginHighPriorityRequest"/>) makes this
    /// return <see langword="false"/>; a request that only acquires the lock after this method
    /// already returned <see langword="true"/> is ordered strictly after an already-started
    /// operation. Only ever held for this single comparison -- never across an await or a native
    /// call.</summary>
    private bool TryLinearizeOrdinaryMutationStart(long expectedEpoch)
    {
        lock (_requestBoundarySync)
        {
            return !_suspendBarrierRequested
                && _pendingHighPriorityRequests == 0
                && Interlocked.Read(ref _lifecycleEpoch) == expectedEpoch;
        }
    }

    /// <summary>Review 4958332345 (BLOCKER): the shared linearization point for the final Armed
    /// state publication (site 2), used by both a fresh arm (<see cref="AttemptArm"/>) and the
    /// steady-state "re-confirm an already-valid Armed helper" branch in <see cref="ReconcileCore"/>.
    /// State publication itself is cheap (no I/O, no native calls), so the check and the
    /// <see cref="SetState"/> call happen atomically under the SAME lock instead of as two separate
    /// steps -- there is no gap in which a high-priority request could win the boundary between them.
    /// Returns <see langword="false"/> without publishing Armed when a high-priority request already
    /// holds the boundary; the caller is then responsible for the (possibly slow) helper cleanup,
    /// which must run outside this lock.</summary>
    private bool TryCommitArmedState(long expectedEpoch)
    {
        lock (_requestBoundarySync)
        {
            if (_suspendBarrierRequested
                || _pendingHighPriorityRequests != 0
                || Interlocked.Read(ref _lifecycleEpoch) != expectedEpoch)
                return false;
            _lastReason = "Armed";
            SetState(CenterMOem1LifecycleState.Armed);
            return true;
        }
    }

    private void SetState(CenterMOem1LifecycleState next)
    {
        if (_state == next) return;
        AppLog.Info("CenterM.Oem1", "State transition.", ("From", _state), ("To", next), ("Reason", _lastReason));
        _state = next;
    }
}
