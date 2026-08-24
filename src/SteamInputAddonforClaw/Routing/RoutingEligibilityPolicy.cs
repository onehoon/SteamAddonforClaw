using SteamInputAddonforClaw.Prerequisites;
using SteamInputAddonforClaw.Steam;
using SteamInputAddonforClaw.Status;
using SteamInputAddonforClaw.Devices;

namespace SteamInputAddonforClaw.Routing;

internal enum RoutingDecisionKind { Passive, WaitingForSteam, Eligible, SetupRequired, Indeterminate }
internal enum RoutingDecisionReason
{
    SteamInactive,
    RecoveryUnsafe, UnsupportedDevice, DeviceCompatibilityIndeterminate, ControllerEnvironmentUnsupported, ControllerEnvironmentIndeterminate, PrerequisitesNotReady, Eligible
}

internal sealed record RoutingDecision(RoutingDecisionKind Kind, RoutingDecisionReason Reason);
internal sealed record RoutingPolicyInput(
    SteamSessionState Steam,
    HardwareCompatibilityAssessment HardwareCompatibility,
    ControllerEnvironmentCompatibilityAssessment Compatibility,
    RuntimePrerequisiteAssessment Prerequisites,
    bool RecoverySafe);

internal static class RoutingEligibilityPolicy
{
    public static RoutingDecision Evaluate(RoutingPolicyInput input)
    {
        if (!input.RecoverySafe)
            return new(RoutingDecisionKind.Indeterminate, RoutingDecisionReason.RecoveryUnsafe);
        if (input.HardwareCompatibility.Status == HardwareCompatibilityStatus.Unsupported)
            return new(RoutingDecisionKind.Passive, RoutingDecisionReason.UnsupportedDevice);
        if (input.HardwareCompatibility.Status == HardwareCompatibilityStatus.Indeterminate)
            return new(RoutingDecisionKind.Indeterminate, RoutingDecisionReason.DeviceCompatibilityIndeterminate);

        if (input.Compatibility.Status == ControllerEnvironmentCompatibilityStatus.Unsupported)
            return new(RoutingDecisionKind.Passive, RoutingDecisionReason.ControllerEnvironmentUnsupported);
        if (input.Compatibility.Status == ControllerEnvironmentCompatibilityStatus.Indeterminate)
            return new(RoutingDecisionKind.Indeterminate, RoutingDecisionReason.ControllerEnvironmentIndeterminate);
        if (!input.Prerequisites.IsRoutingReady)
            return new(RoutingDecisionKind.SetupRequired, RoutingDecisionReason.PrerequisitesNotReady);
        return input.Steam.IsActive
            ? new(RoutingDecisionKind.Eligible, RoutingDecisionReason.Eligible)
            : new(RoutingDecisionKind.WaitingForSteam, RoutingDecisionReason.SteamInactive);
    }
}
