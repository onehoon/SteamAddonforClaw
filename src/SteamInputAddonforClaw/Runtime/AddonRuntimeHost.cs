using SteamInputAddonforClaw.Developer;
using SteamInputAddonforClaw.Power;
using SteamInputAddonforClaw.Routing;
using SteamInputAddonforClaw.Steam;

namespace SteamInputAddonforClaw.Runtime;

/// <summary>
/// The first UI-independent application-facing runtime boundary: owns the Steam/Big Picture
/// session-observation graph (<see cref="SteamSessionRuntime"/>), the optional routing runtime
/// (<see cref="AddonRoutingRuntime"/> -- null is a valid, passive state when no routing
/// composition is available), and the normal-reconcile resume-suppression policy that
/// previously lived directly in <c>App.xaml.cs</c>. No dependency on WinUI, the main window, or
/// the system tray -- UI communication happens only through the generic
/// <see cref="SteamSessionStateChanged"/>/<see cref="StatusRefreshRequested"/> events.
/// </summary>
/// <remarks>
/// This is C5b2 scope: Steam/BPM ownership and normal/resume reconcile orchestration move here.
/// Power ownership (<c>PowerMutationGate</c>, <c>RecoverySafetyState</c>,
/// <c>PowerTransitionCoordinator</c>, <c>PowerTransitionWatcher</c>) and top-level application
/// shutdown ordering remain App-owned -- that is C5c.
///
/// <para>
/// <see cref="DisposeAsync"/> stops Steam observation (idempotently -- safe even if
/// <see cref="StopSteamObservation"/> was already called) and disposes the owned routing runtime's
/// backend resources. It deliberately does <b>not</b> perform routing shutdown itself: the caller
/// must call <see cref="ShutdownRoutingAsync"/>, and stop any other external orchestration
/// referencing this host (e.g. the power coordinator), before disposing it -- the same
/// shutdown-before-dispose precondition <see cref="AddonRoutingRuntime"/> already documents.
/// </para>
/// </remarks>
internal sealed class AddonRuntimeHost : IAsyncDisposable, IPowerSuspendParticipant
{
    private readonly SteamSessionRuntime _steamRuntime;
    private readonly AddonRoutingRuntime? _routingRuntime;
    private readonly ResumeFreshReconcileSuppression _resumeFreshReconcileSuppression = new();
    private bool _steamStopped;

    internal AddonRuntimeHost(SteamSessionRuntime steamRuntime, AddonRoutingRuntime? routingRuntime)
    {
        _steamRuntime = steamRuntime;
        _routingRuntime = routingRuntime;
        _steamRuntime.StateChanged += OnSteamSessionStateChanged;
    }

    internal DeveloperTestModeState DeveloperTestModeState => _steamRuntime.DeveloperTestModeState;
    internal bool IsRoutingAvailable => _routingRuntime is not null;

    internal event EventHandler<SteamSessionStateChangedEventArgs>? SteamSessionStateChanged;
    internal event EventHandler? StatusRefreshRequested;

    internal RoutingRuntimeStatusSnapshot CaptureRoutingStatus() => _routingRuntime?.CaptureStatus() ?? RoutingRuntimeStatusSnapshot.Unavailable;
    internal RoutingRuntimeTerminationSnapshot CaptureTerminationSnapshot() => _routingRuntime?.CaptureTerminationSnapshot() ?? default;
    internal bool IsSafetySessionActive => _routingRuntime?.IsSafetySessionActive == true;
    internal bool HasOwnedRecoveryBoundary => _routingRuntime?.HasOwnedRecoveryBoundary == true;
    internal bool HasResidualSessionState => _routingRuntime?.HasResidualSessionState == true;

    internal void CancelInFlightRoutingTransition() => _routingRuntime?.CancelInFlightTransition();

    internal async Task<bool> RetryResidualCleanupForResumeAsync(CancellationToken cancellationToken) =>
        _routingRuntime is null || await _routingRuntime.RetryResidualCleanupForResumeAsync(cancellationToken).ConfigureAwait(false);

    /// <summary>Normal (non-resume) reconcile, via the safe C5b1 path. No-op when routing is unavailable.</summary>
    internal Task ReconcileAsync(CancellationToken cancellationToken = default) =>
        _routingRuntime is null ? Task.CompletedTask : _routingRuntime.ReconcileSafelyAsync(RequestStatusRefresh, cancellationToken);

    /// <summary>
    /// Resume reconciliation: explicit Steam refresh (suppressing the state-change notification
    /// it produces from also triggering a normal reconcile), then fresh routing reconciliation --
    /// exactly the flow previously inlined in App's power-resume callback. Returns
    /// <see langword="true"/> immediately when routing is unavailable.
    /// </summary>
    internal async Task<bool> ReconcileFreshAfterResumeAsync(CancellationToken cancellationToken)
    {
        if (_routingRuntime is null) return true;
        var routingRuntime = _routingRuntime;
        _resumeFreshReconcileSuppression.Begin();
        _resumeFreshReconcileSuppression.ExecuteExplicitRefresh(() => _steamRuntime.Refresh());
        return await RoutingReconcileStatusRefresh.RunResumeFreshAsync(
            freshReconcile: token => routingRuntime.ReconcileFreshAfterResumeAsync(token),
            completeSuppression: _resumeFreshReconcileSuppression.Complete,
            deferredReconcile: () => ReconcileAsync(),
            requestStatusRefresh: RequestStatusRefresh,
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    internal Task ShutdownRoutingAsync() => _routingRuntime is null ? Task.CompletedTask : _routingRuntime.ShutdownAsync();

    /// <summary>Stops Steam/Big Picture session observation. Idempotent.</summary>
    internal void StopSteamObservation()
    {
        if (_steamStopped) return;
        _steamStopped = true;
        _steamRuntime.StateChanged -= OnSteamSessionStateChanged;
        _steamRuntime.Dispose();
    }

    private void OnSteamSessionStateChanged(object? sender, SteamSessionStateChangedEventArgs args)
    {
        SteamSessionStateChanged?.Invoke(this, args);
        if (!_resumeFreshReconcileSuppression.TrySuppressStateChange()) _ = ReconcileAsync();
    }

    private void RequestStatusRefresh() => StatusRefreshRequested?.Invoke(this, EventArgs.Empty);

    public string Name => _routingRuntime?.Name ?? "AddonRuntimeHost";

    public Task<bool> QuiesceForSuspendAsync(DateTimeOffset deadline, long cycle, long epoch, CancellationToken cancellationToken) =>
        _routingRuntime?.QuiesceForSuspendAsync(deadline, cycle, epoch, cancellationToken) ?? Task.FromResult(true);

    public async ValueTask DisposeAsync()
    {
        StopSteamObservation();
        if (_routingRuntime is not null) await _routingRuntime.DisposeAsync().ConfigureAwait(false);
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
