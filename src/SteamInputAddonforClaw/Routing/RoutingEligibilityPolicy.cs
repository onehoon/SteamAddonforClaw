using SteamInputAddonforClaw.Prerequisites;
using SteamInputAddonforClaw.Steam;
using SteamInputAddonforClaw.Status;
using SteamInputAddonforClaw.Devices;

namespace SteamInputAddonforClaw.Routing;

internal enum RoutingDecisionKind { Passive, WaitingForSteam, Eligible, SetupRequired, Indeterminate }
internal enum RoutingDecisionReason
{
    SteamInactive, AddonOwnedOutputIdentityUncertain,
    RecoveryUnsafe, UnsupportedDevice, DeviceCompatibilityIndeterminate, ControllerEnvironmentUnsupported, ControllerEnvironmentIndeterminate, PrerequisitesNotReady, Eligible
}

internal sealed record RoutingDecision(RoutingDecisionKind Kind, RoutingDecisionReason Reason);
internal sealed record RoutingPolicyInput(
    SteamSessionState Steam,
    HardwareCompatibilityAssessment HardwareCompatibility,
    ControllerEnvironmentCompatibilityAssessment Compatibility,
    RuntimePrerequisiteAssessment Prerequisites,
    bool RecoverySafe,
    bool AddonOwnedOutputIdentityUncertain = false);

internal static class RoutingEligibilityPolicy
{
    public static RoutingDecision Evaluate(RoutingPolicyInput input)
    {
        if (!input.RecoverySafe)
            return new(RoutingDecisionKind.Indeterminate, RoutingDecisionReason.RecoveryUnsafe);
        // Addon-owned VIIPER output identity safety is independent of external-controller detection:
        // if a previous virtual-output mutation left ownership unverifiable, routing must fail safe
        // regardless of what other physical controllers are connected. See AddonOwnedVirtualDeviceTracker.
        if (input.AddonOwnedOutputIdentityUncertain)
            return new(RoutingDecisionKind.Indeterminate, RoutingDecisionReason.AddonOwnedOutputIdentityUncertain);

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
