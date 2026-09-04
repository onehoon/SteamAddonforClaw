using SteamInputAddonforClaw.Contracts.Frontend;

namespace SteamInputAddonforClaw.Lifecycle;

internal enum UserTerminationBlockReason
{
    None,
    /// <summary>Ordinary user Runtime termination is blocked because a controller-authority startup
    /// transition is still committing (e.g. the deferred Center M Disabled physical PID1902
    /// acquisition + Win+G suppression arm from the Policy B work order).</summary>
    ControllerAuthorityTransition,
    RuntimeShuttingDown,
    /// <summary>Ordinary user Runtime termination is blocked because MSI Center M is exactly Disabled:
    /// the background Addon Runtime is the selected controller authority and the only supported way
    /// to leave that mode is "Enable MSI Center M and Restart" (PR2.5 work order section 4).</summary>
    ControllerAuthorityMandatory
}

internal readonly record struct UserTerminationDecision(
    bool CanTerminate,
    UserTerminationBlockReason Reason);

/// <summary>Whether the exact current MSI Center M startup state makes the background controller
/// Runtime mandatory (PR2.5). Only an exactly-<see cref="FrontendCenterMStartupState.Disabled"/>
/// configuration is Addon-owned authority -- <see cref="FrontendCenterMStartupState.Partial"/> /
/// <see cref="FrontendCenterMStartupState.Unavailable"/> are never silently treated as such.</summary>
internal static class MandatoryControllerRuntimePolicy
{
    internal static bool IsMandatory(FrontendCenterMStartupState centerMStartupState) =>
        centerMStartupState == FrontendCenterMStartupState.Disabled;
}

/// <summary>Composes the existing lower-level <see cref="UserTerminationGuard"/> decision with the
/// PR2.5 mandatory-controller-authority rule. The existing block reasons stay authoritative: the
/// mandatory reason is only applied when ordinary termination would otherwise be permitted, so
/// established cleanup/recovery safety semantics are never replaced.</summary>
internal static class UserTerminationComposition
{
    internal static UserTerminationDecision Compose(UserTerminationDecision inner, bool controllerRuntimeMandatory) =>
        inner.CanTerminate && controllerRuntimeMandatory
            ? new(false, UserTerminationBlockReason.ControllerAuthorityMandatory)
            : inner;
}

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
