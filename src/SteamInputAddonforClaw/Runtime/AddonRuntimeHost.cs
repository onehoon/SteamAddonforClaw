using SteamInputAddonforClaw.Developer;
using SteamInputAddonforClaw.Diagnostics;
using SteamInputAddonforClaw.Lifecycle;
using SteamInputAddonforClaw.Power;
using SteamInputAddonforClaw.Routing;
using SteamInputAddonforClaw.Steam;

namespace SteamInputAddonforClaw.Runtime;

/// <summary>
/// The single UI-independent owner of the running Addon runtime: the Steam/Big Picture
/// session-observation graph (<see cref="SteamSessionRuntime"/>), the optional routing runtime
/// (<see cref="AddonRoutingRuntime"/> -- null is a valid, passive state when no routing
/// composition is available), normal/resume reconcile orchestration
/// (<see cref="ResumeFreshReconcileSuppression"/>), suspend/resume power orchestration
/// (<c>PowerTransitionCoordinator</c>/<c>PowerTransitionWatcher</c>), and user-termination safety
/// (<c>UserTerminationGuard</c>). No dependency on the presentation shell, the main window, the system tray, or any
/// device-specific (MSI/CenterM) type -- UI communication happens only through the generic
/// <see cref="SteamSessionStateChanged"/>/<see cref="StatusRefreshRequested"/> events, and the
/// device-specific stock-mode baseline capability arrives only as a generic
/// <c>Func&lt;CancellationToken, Task&lt;bool&gt;&gt;</c> callback.
/// </summary>
/// <remarks>
/// This is C5c: the last major Runtime Core ownership step. The application shell retains only
/// startup/bootstrap, the presentation window, and the system tray.
///
/// <para>
/// Lifecycle: construct, subscribe to the two events, call <see cref="StartPowerObservation"/>
/// once dependents (e.g. <c>MainWindow</c>) are ready to receive the events it may synchronously
/// raise, then eventually call <see cref="PrepareForShutdown"/> (stops Steam observation; safe to
/// call multiple times) followed by <see cref="DisposeAsync"/> (idempotent runtime shutdown:
/// stops power observation, attempts canonical routing shutdown, and disposes the routing backend
/// only when that shutdown succeeds). Failed canonical rollback preserves residual ownership.
/// </para>
/// </remarks>
internal sealed class AddonRuntimeHost : IAsyncDisposable
{
    internal static bool ShouldDisposeRoutingBackend(bool canonicalShutdownSucceeded) => canonicalShutdownSucceeded;
    private readonly SteamSessionRuntime _steamRuntime;
    private readonly AddonRoutingRuntime? _routingRuntime;
    private readonly ResumeFreshReconcileSuppression _resumeFreshReconcileSuppression = new();
    private readonly PowerMutationGate _powerGate;
    private readonly RecoverySafetyState _recoverySafetyState;
    private readonly PowerTransitionCoordinator _powerCoordinator;
    private readonly PowerTransitionWatcher _powerWatcher;
    private readonly UserTerminationGuard _userTerminationGuard;
    private readonly Func<Task<bool>>? _routingShutdownOverride;
    private readonly Func<ValueTask>? _routingDisposeOverride;

    // Guards against a resume notification that was already queued in PowerTransitionCoordinator
    // before shutdown began running ReconcileFreshAfterResumeAsync's Steam refresh concurrently
    // with (or after) PrepareForShutdown disposing the same SteamSessionRuntime --
    // SteamSessionWatcher.Refresh() throws ObjectDisposedException once disposed. Both
    // PrepareForShutdown and the Steam-touching section of ReconcileFreshAfterResumeAsync take
    // this lock, so whichever runs first either fully disposes before the other's refresh
    // attempt is even considered, or fully refreshes (a fast, synchronous, non-reentrant
    // registry read) before disposal can start.
    private readonly Lock _steamLifecycleLock = new();
    private bool _steamStopped;
    private int _disposed;

    internal AddonRuntimeHost(
        SteamSessionRuntime steamRuntime,
        AddonRoutingRuntime? routingRuntime,
        PowerMutationGate powerGate,
        RecoverySafetyState recoverySafetyState,
        bool recoverySafe,
        Func<bool> hasIncompleteRecovery,
        Func<CancellationToken, Task<bool>> establishBaseline,
        IPowerSuspendResumeNotificationSource? notificationSource = null,
        Func<Task<bool>>? routingShutdownOverride = null,
        Func<ValueTask>? routingDisposeOverride = null)
    {
        _steamRuntime = steamRuntime;
        _routingRuntime = routingRuntime;
        _powerGate = powerGate;
        _recoverySafetyState = recoverySafetyState;
        _steamRuntime.StateChanged += OnSteamSessionStateChanged;
        _routingShutdownOverride = routingShutdownOverride;
        _routingDisposeOverride = routingDisposeOverride;

        var powerParticipants = new List<IPowerSuspendParticipant>();
        // PR2: an optional generic auxiliary power participant the routing composition supplies
        // (e.g. the MSI Center M OEM1 lifecycle driver) -- this host never learns it is MSI/CenterM
        // specific, only that the capability may be present (work order requirement 13).
        //
        // Review fix (BLOCKER): PowerTransitionCoordinator gives every participant one SHARED
        // suspend deadline and stops once no time remains, so this must run BEFORE the routing
        // participant, not after -- a slow/timed-out routing quiesce must never be able to consume
        // the whole budget and starve the auxiliary lifecycle's own (intentionally lightweight)
        // suspend mutation barrier of ever being established.
        if (_routingRuntime?.AuxiliaryPowerParticipant is { } auxiliaryPowerParticipant)
            powerParticipants.Add(auxiliaryPowerParticipant);
        if (_routingRuntime is not null) powerParticipants.Add(_routingRuntime);
        _powerCoordinator = new PowerTransitionCoordinator(powerGate, recoverySafetyState, powerParticipants,
            token => ReconcileFreshAfterResumeAsync(token),
            recoveryEnabled: recoverySafe,
            hasIncompleteRecovery: hasIncompleteRecovery,
            // Review fix (BLOCKER): PowerTransitionCoordinator opens PowerMutationGate as soon as
            // establishBaseline succeeds -- BEFORE the afterRecovery callback (ReconcileFreshAfterResumeAsync)
            // ever runs. Normal routing (e.g. a Steam state-change callback on another thread) could
            // therefore race through the already-open gate and have the routing guard start the
            // shared helper before the OEM1 auxiliary resume reconcile has even begun, which could
            // then misclassify that guard-owned helper as its own stale ownership to clean up. Run
            // the auxiliary resume reconcile here, inside establishBaseline, while the gate is still
            // closed -- after the stock baseline succeeds, before PowerTransitionCoordinator can ever
            // open the gate for this resume.
            establishBaseline: token => EstablishResumeBaselineAsync(establishBaseline, token),
            hasResidualRoutingCleanup: () => _routingRuntime?.HasResidualSessionState == true,
            retryResidualRoutingCleanup: async token =>
                _routingRuntime is null || await _routingRuntime.RetryResidualCleanupForResumeAsync(token).ConfigureAwait(false));
        _powerWatcher = new PowerTransitionWatcher(notificationSource ?? new WindowsSuspendResumeNotificationSource(), powerGate, _powerCoordinator,
            () => _routingRuntime?.CancelInFlightTransition());

        _userTerminationGuard = new UserTerminationGuard(
            () => _routingRuntime?.CaptureTerminationSnapshot() ?? default,
            () => _routingRuntime?.IsSafetySessionActive == true,
            () => _routingRuntime?.HasOwnedRecoveryBoundary == true,
            () => recoverySafetyState.Current == RecoverySafety.Safe && hasIncompleteRecovery());
    }

    internal DeveloperTestModeState DeveloperTestModeState => _steamRuntime.DeveloperTestModeState;

    internal event EventHandler<SteamSessionStateChangedEventArgs>? SteamSessionStateChanged;
    internal event EventHandler? StatusRefreshRequested;

    internal RoutingRuntimeStatusSnapshot CaptureRoutingStatus() => _routingRuntime?.CaptureStatus() ?? RoutingRuntimeStatusSnapshot.Unavailable;

    internal UserTerminationDecision EvaluateUserTermination() => _userTerminationGuard.Evaluate();

    /// <summary>Normal (non-resume) reconcile, via the safe C5b1 path. No-op when routing is unavailable.</summary>
    internal Task ReconcileAsync(CancellationToken cancellationToken = default) =>
        _routingRuntime is null ? Task.CompletedTask : _routingRuntime.ReconcileSafelyAsync(RequestStatusRefresh, cancellationToken);

    internal Task ReconcileOem1PrerequisitesAsync(CancellationToken cancellationToken = default) =>
        _routingRuntime?.ReconcileOem1PrerequisitesAsync(cancellationToken) ?? Task.CompletedTask;

    /// <summary>
    /// Registers for suspend/resume notifications and opens the mutation gate if registration
    /// succeeded and recovery is safe -- exactly the startup sequence previously inlined in App.
    /// Registration failure is fail-closed: the gate stays closed. Call once, after subscribing to
    /// this host's events, before anything else may depend on power observation being active.
    /// </summary>
    internal void StartPowerObservation()
    {
        if (!_powerWatcher.Start())
            AppLog.Error("Power.Notify", "Suspend/resume notification registration failed.", new InvalidOperationException("PowerRegisterSuspendResumeNotification failed."));
        else if (_recoverySafetyState.Current == RecoverySafety.Safe)
            _powerGate.OpenAfterRecovery();
    }

    /// <summary>
    /// Resume reconciliation: explicit Steam refresh (suppressing the state-change notification
    /// it produces from also triggering a normal reconcile), then fresh routing reconciliation --
    /// exactly the flow previously inlined in App's power-resume callback. Returns
    /// <see langword="true"/> immediately when routing is unavailable. Safe to call after
    /// <see cref="PrepareForShutdown"/> -- e.g. a resume notification queued in
    /// <c>PowerTransitionCoordinator</c> before shutdown began, running afterward -- the Steam
    /// refresh is skipped in that case rather than touching the disposed
    /// <see cref="SteamSessionRuntime"/>; fresh routing reconciliation still proceeds.
    /// </summary>
    private async Task<bool> ReconcileFreshAfterResumeAsync(CancellationToken cancellationToken)
    {
        if (_routingRuntime is null) return true;
        var routingRuntime = _routingRuntime;
        _resumeFreshReconcileSuppression.Begin();
        _resumeFreshReconcileSuppression.ExecuteExplicitRefresh(() =>
        {
            lock (_steamLifecycleLock)
            {
                if (!_steamStopped) _steamRuntime.Refresh();
            }
        });
        // The OEM1 auxiliary resume reconcile now runs earlier, inside EstablishResumeBaselineAsync
        // (before PowerTransitionCoordinator ever opens PowerMutationGate for this resume) -- see the
        // review-fix comment there. This callback owns only Steam refresh/suppression plus routing's
        // own fresh reconcile.
        return await RoutingReconcileStatusRefresh.RunResumeFreshAsync(
            freshReconcile: token => routingRuntime.ReconcileFreshAfterResumeAsync(token),
            completeSuppression: _resumeFreshReconcileSuppression.Complete,
            deferredReconcile: () => ReconcileAsync(),
            requestStatusRefresh: RequestStatusRefresh,
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Review fix (BLOCKER): runs after residual-cleanup/incomplete-recovery checks have
    /// already passed, while <see cref="PowerMutationGate"/> is still closed for this resume. Calls
    /// the caller-supplied stock-mode baseline first; if that fails, returns false immediately
    /// (matching the previous <paramref name="establishBaseline"/> contract exactly) without ever
    /// touching the OEM1 auxiliary participant. Only once the baseline itself succeeds does this run
    /// the OEM1 lifecycle's own fresh resume reconciliation -- still before
    /// <see cref="PowerTransitionCoordinator"/> can commit recovery and open the gate -- so normal
    /// routing can never race through an already-open gate and start the shared helper before OEM1
    /// has restored/validated its own long-lived ownership of it. OEM1 failure here remains
    /// feature-local/fail-open (matches the coordinator's own fail-open design): it is logged but
    /// never turns an otherwise-successful baseline into a failed one.</summary>
    private async Task<bool> EstablishResumeBaselineAsync(Func<CancellationToken, Task<bool>> establishBaseline, CancellationToken cancellationToken)
    {
        if (!await establishBaseline(cancellationToken).ConfigureAwait(false))
            return false;

        if (_routingRuntime?.AuxiliaryResumeParticipant is { } auxiliaryResumeParticipant)
        {
            try
            {
                await auxiliaryResumeParticipant.ReconcileAfterResumeAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                AppLog.Error("Power.Recovery", "Auxiliary resume reconciliation failed.", exception);
            }
        }

        return true;
    }

    /// <summary>
    /// Stops Steam/Big Picture session observation -- the first phase of runtime shutdown.
    /// Idempotent. Does not stop power observation, shut down routing, or dispose power objects;
    /// see <see cref="DisposeAsync"/> for the remaining sequence.
    /// </summary>
    internal void PrepareForShutdown()
    {
        lock (_steamLifecycleLock)
        {
            if (_steamStopped) return;
            _steamStopped = true;
            _steamRuntime.StateChanged -= OnSteamSessionStateChanged;
            _steamRuntime.Dispose();
        }
    }

    private void OnSteamSessionStateChanged(object? sender, SteamSessionStateChangedEventArgs args)
    {
        SteamSessionStateChanged?.Invoke(this, args);
        if (!_resumeFreshReconcileSuppression.TrySuppressStateChange()) _ = ReconcileAsync();
    }

    private void RequestStatusRefresh() => StatusRefreshRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>
    /// Full runtime shutdown, idempotent: stops Steam observation (if not already stopped),
    /// disposes power observation, shuts down routing, disposes the power coordinator, then
    /// disposes the routing backend -- preserving the exact effective ordering App previously
    /// performed itself.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        PrepareForShutdown();
        _powerWatcher.Dispose();
        var routingShutdownSucceeded = _routingShutdownOverride is not null
            ? await _routingShutdownOverride().ConfigureAwait(false)
            : _routingRuntime is null || await _routingRuntime.ShutdownAsync().ConfigureAwait(false);
        await _powerCoordinator.DisposeAsync().ConfigureAwait(false);
        if (ShouldDisposeRoutingBackend(routingShutdownSucceeded))
        {
            if (_routingDisposeOverride is not null) await _routingDisposeOverride().ConfigureAwait(false);
            else if (_routingRuntime is not null) await _routingRuntime.DisposeAsync().ConfigureAwait(false);
        }
    }
}

internal sealed class ResumeFreshReconcileSuppression
{
    private readonly Lock _sync = new();
    private int _owned;
    private int _pending;

    public void Begin()
    {
        lock (_sync)
        {
            _pending = 0;
            _owned = 1;
        }
    }

    public bool TrySuppressStateChange()
    {
        lock (_sync)
        {
            if (_owned == 0) return false;
            _pending = 1;
            return true;
        }
    }

    public void ExecuteExplicitRefresh(Action refresh)
    {
        lock (_sync)
        {
            refresh();
            _pending = 0;
        }
    }

    public bool Complete(bool freshSucceeded)
    {
        lock (_sync)
        {
            _owned = 0;
            var pending = _pending != 0;
            _pending = 0;
            return freshSucceeded && pending;
        }
    }
}
