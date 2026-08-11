namespace SteamInputAddonforClaw.Lifecycle;

internal enum UserTerminationBlockReason
{
    None,
    RoutingTransition,
    PendingRoutingCleanup,
    NativeModeActive,
    NativeRecoveryOwned,
    RecoveryMutationOwned,
    RuntimeShuttingDown
}

internal readonly record struct UserTerminationDecision(
    bool CanTerminate,
    UserTerminationBlockReason Reason);

internal sealed class UserTerminationGuard
{
    private readonly Func<Routing.RoutingRuntimeTerminationSnapshot> _routingSnapshot;
    private readonly Func<bool> _nativeModeActive;
    private readonly Func<bool> _nativeRecoveryOwned;
    private readonly Func<bool> _liveRecoveryMutationOwned;

    internal UserTerminationGuard(
        Func<Routing.RoutingRuntimeTerminationSnapshot> routingSnapshot,
        Func<bool> nativeModeActive,
        Func<bool> nativeRecoveryOwned,
        Func<bool> liveRecoveryMutationOwned)
    {
        _routingSnapshot = routingSnapshot ?? throw new ArgumentNullException(nameof(routingSnapshot));
        _nativeModeActive = nativeModeActive ?? throw new ArgumentNullException(nameof(nativeModeActive));
        _nativeRecoveryOwned = nativeRecoveryOwned ?? throw new ArgumentNullException(nameof(nativeRecoveryOwned));
        _liveRecoveryMutationOwned = liveRecoveryMutationOwned ?? throw new ArgumentNullException(nameof(liveRecoveryMutationOwned));
    }

    internal UserTerminationDecision Evaluate()
    {
        var snapshot = _routingSnapshot();
        if (snapshot.ShutdownRequested) return new(false, UserTerminationBlockReason.RuntimeShuttingDown);
        if (snapshot.TransitionInProgress) return new(false, UserTerminationBlockReason.RoutingTransition);
        if (snapshot.HasPendingCleanup) return new(false, UserTerminationBlockReason.PendingRoutingCleanup);
        if (_nativeModeActive()) return new(false, UserTerminationBlockReason.NativeModeActive);
        if (_nativeRecoveryOwned()) return new(false, UserTerminationBlockReason.NativeRecoveryOwned);
        if (_liveRecoveryMutationOwned()) return new(false, UserTerminationBlockReason.RecoveryMutationOwned);
        return new(true, UserTerminationBlockReason.None);
    }
}
