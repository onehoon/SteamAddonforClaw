namespace SteamInputAddonforClaw.Lifecycle;

internal enum UserTerminationBlockReason
{
    None,
    /// <summary>Ordinary user Runtime termination is blocked because a controller-authority startup
    /// transition is still committing (e.g. the deferred Center M Disabled physical PID1902
    /// acquisition + Win+G suppression arm from the Policy B work order), or because the existing
    /// Enable/Disable MSI Center M and Restart authority transition owner is already committing one
    /// (tray restart/overlay cleanup work order section 7).</summary>
    ControllerAuthorityTransition,
    RuntimeShuttingDown,
}

internal readonly record struct UserTerminationDecision(
    bool CanTerminate,
    UserTerminationBlockReason Reason);

internal sealed class UserTerminationGuard
{
    private readonly Func<bool> _runtimeShuttingDown;

    internal UserTerminationGuard(Func<bool> runtimeShuttingDown)
    {
        _runtimeShuttingDown = runtimeShuttingDown ?? throw new ArgumentNullException(nameof(runtimeShuttingDown));
    }

    internal UserTerminationDecision Evaluate()
    {
        if (_runtimeShuttingDown()) return new(false, UserTerminationBlockReason.RuntimeShuttingDown);
        return new(true, UserTerminationBlockReason.None);
    }
}
