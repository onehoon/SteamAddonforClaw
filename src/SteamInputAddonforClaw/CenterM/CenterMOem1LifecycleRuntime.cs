using SteamInputAddonforClaw.Diagnostics;
using SteamInputAddonforClaw.Power;

namespace SteamInputAddonforClaw.CenterM;

/// <summary>
/// The narrow surface <see cref="CenterMOem1LifecycleRuntime"/> needs from
/// <see cref="CenterMOem1LifecycleCoordinator"/> -- its two already-documented poll methods, plus
/// suspend/resume/disposal. Exists purely so tests can substitute a fake driver target without
/// needing a fully-fake coordinator (the coordinator's own state machine already has its own huge
/// test suite); implemented by <see cref="CenterMOem1LifecycleCoordinator"/> itself in production,
/// never by a second implementation there.
/// </summary>
internal interface ICenterMOem1LifecycleDriverTarget
{
    Task PollHelperLivenessAsync(CancellationToken cancellationToken = default);
    Task PollTickAsync(CancellationToken cancellationToken = default);
    Task<bool> QuiesceForSuspendAsync(DateTimeOffset deadline, long cycle, long epoch, CancellationToken cancellationToken);
    Task ReconcileAfterResumeAsync(CancellationToken cancellationToken = default);
    ValueTask DisposeAsync();
}

/// <summary>
/// PR2: the one small production driver/lifetime owner around the already-implemented
/// <see cref="CenterMOem1LifecycleCoordinator"/>. It duplicates no coordinator state -- the
/// coordinator remains the sole authoritative state machine -- and is glue only: start a single
/// low-rate periodic loop that drives the coordinator's own documented
/// <see cref="CenterMOem1LifecycleCoordinator.PollTickAsync"/>/
/// <see cref="CenterMOem1LifecycleCoordinator.PollHelperLivenessAsync"/> reconciliation contract,
/// forward suspend/resume to the coordinator's existing methods, and stop the loop (cancel + join)
/// before disposing the coordinator so a stale tick can never re-enter it after disposal.
/// </summary>
/// <remarks>
/// Routing-guard coexistence: the driver invokes the liveness seam on every tick, but a cleanly
/// dormant OEM1 coordinator skips its helper liveness policy because the shared helper may belong
/// to the routing guard. Once OEM1 is enabled, exact-handle liveness polling applies normally;
/// only the normal MainUI-yield poll (<see cref="CenterMOem1LifecycleCoordinator.PollTickAsync"/>)
/// is skipped while the guard is Armed, so this driver never fights the guard's transient
/// <c>Local\MSI Center M.exe</c> launch-protection authority during routing.
/// </remarks>
internal sealed class CenterMOem1LifecycleRuntime : IPowerSuspendParticipant, IRuntimeResumeParticipant, IAsyncDisposable
{
    private readonly ICenterMOem1LifecycleDriverTarget _coordinator;
    private readonly Func<bool> _routingGuardIsArmed;
    private readonly Func<CancellationToken, Task> _waitForNextTick;
    private readonly Action? _onReconciled;
    private readonly Action? _onSuspending;
    private readonly CancellationTokenSource _cts = new();
    private readonly object _startSync = new();
    private Task? _loop;

    internal CenterMOem1LifecycleRuntime(
        ICenterMOem1LifecycleDriverTarget coordinator,
        Func<bool> routingGuardIsArmed,
        TimeSpan? pollInterval = null,
        Func<TimeSpan, CancellationToken, Task>? delay = null,
        Action? onReconciled = null,
        Action? onSuspending = null)
    {
        _coordinator = coordinator;
        _routingGuardIsArmed = routingGuardIsArmed;
        // PR3: a single narrow, optional observability seam (work order requirement/Scope 10) --
        // invoked after every tick (and after resume reconciliation) so a production OEM1 action-path
        // owner can refresh custom gesture-bridge authority from the coordinator's own freshly
        // reconciled SuppressionReady snapshot, without this runtime adding a second poller/state
        // machine of its own. Never touched unless a caller supplies it.
        _onReconciled = onReconciled;
        _onSuspending = onSuspending;
        // One simple low-rate constant, per the work order -- no configurable polling setting, no
        // scheduler/timer-service abstraction. The delay function is injectable purely so tests can
        // drive the loop deterministically (same seam style as the coordinator's own `_delay`),
        // never touched by production code.
        var interval = pollInterval ?? TimeSpan.FromSeconds(2);
        var delayFn = delay ?? ((ts, token) => Task.Delay(ts, token));
        _waitForNextTick = token => delayFn(interval, token);
    }

    public string Name => "CenterMOem1LifecycleRuntime";

    /// <summary>Test-only observability: whether the periodic loop task has been started.</summary>
    internal bool IsRunning { get { lock (_startSync) return _loop is not null; } }

    /// <summary>Starts the single periodic driver loop. Idempotent -- production composition calls
    /// this once; a duplicate call is a no-op rather than a second competing loop.</summary>
    internal void Start()
    {
        lock (_startSync)
        {
            if (_loop is not null) return;
            AppLog.Info("CenterM.Oem1", "OEM1 lifecycle driver started.");
            _loop = Task.Run(() => RunAsync(_cts.Token));
        }
    }

    private async Task RunAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                await _waitForNextTick(token).ConfigureAwait(false);
                if (token.IsCancellationRequested) break;

                // Invoke the liveness seam on every tick. A cleanly dormant coordinator
                // intentionally returns without polling the shared helper because it may currently
                // belong to the routing guard. Once OEM1 authority is enabled/non-dormant,
                // exact-handle liveness policy applies normally.
                await _coordinator.PollHelperLivenessAsync(token).ConfigureAwait(false);

                // Skipped silently while Armed -- the guard owns transient launch-protection
                // authority for the whole routing session, so this branch is expected to fire on
                // every tick throughout routing and must not become continuous per-tick noise.
                if (!_routingGuardIsArmed())
                    await _coordinator.PollTickAsync(token).ConfigureAwait(false);

                _onReconciled?.Invoke();
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                // Review fix (MAJOR): a single unexpected failure from either poll call (or the
                // routing-guard predicate) must not permanently kill the production monitor -- the
                // coordinator's own state machine already handles known uncertainty fail-open;
                // this only guards against something it didn't anticipate. Contain and retry on the
                // next low-rate tick rather than letting the loop task fault.
                AppLog.Error("CenterM.Oem1", "OEM1 lifecycle reconciliation tick failed; the driver will retry on the next tick.", exception);
            }
        }
    }

    public async Task<bool> QuiesceForSuspendAsync(DateTimeOffset deadline, long cycle, long epoch, CancellationToken cancellationToken)
    {
        // Review fix (BLOCKER): the coordinator's own QuiesceForSuspendAsync establishes its
        // request-time suspend barrier synchronously, before its first await -- so starting that
        // call (without yet awaiting it) is itself the linearization point at which the barrier
        // becomes authoritative. Starting it BEFORE _onSuspending closes the small window where a
        // racing driver tick could otherwise finish (and republish SuppressionReady == true) between
        // the bridge revocation and the barrier actually taking effect. The task is only awaited
        // after _onSuspending runs, matching this method's original ordering guarantee (bridge OFF
        // completes synchronously before suspend quiesce is reported done).
        var quiesce = _coordinator.QuiesceForSuspendAsync(deadline, cycle, epoch, cancellationToken);
        _onSuspending?.Invoke();
        return await quiesce.ConfigureAwait(false);
    }

    /// <summary>Runs the coordinator's existing fresh resume reconciliation exactly once. Never
    /// gated on Steam/VIIPER routing's own resume outcome -- <see cref="Runtime.AddonRuntimeHost"/>
    /// invokes this unconditionally alongside (not nested inside) routing's own resume path.</summary>
    public async Task ReconcileAfterResumeAsync(CancellationToken cancellationToken)
    {
        await _coordinator.ReconcileAfterResumeAsync(cancellationToken).ConfigureAwait(false);
        AppLog.Info("CenterM.Oem1", "OEM1 lifecycle resume reconcile completed.");
        _onReconciled?.Invoke();
    }

    /// <summary>Shutdown ordering (work order requirement 14): stops the periodic loop first --
    /// cancel then join, so a timer callback can never still be capable of entering coordinator
    /// methods once this completes -- then disposes the coordinator itself. No callback-drain
    /// framework: the coordinator already contains its own necessary drain mechanics
    /// (<see cref="CenterMOem1LifecycleCoordinator.DisposeAsync"/>).</summary>
    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        Task? loop;
        lock (_startSync) loop = _loop;
        if (loop is not null)
        {
            try { await loop.ConfigureAwait(false); }
            catch (OperationCanceledException) { }
        }
        _cts.Dispose();
        AppLog.Info("CenterM.Oem1", "OEM1 lifecycle driver stopped.");

        await _coordinator.DisposeAsync().ConfigureAwait(false);
    }
}
