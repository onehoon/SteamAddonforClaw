using SteamInputAddonforClaw.Routing;
using SteamInputAddonforClaw.Controllers.Detection;

namespace SteamInputAddonforClaw.Status;

internal static class AddonStatusEvaluator
{
    public static AddonStatusSnapshot Map(RoutingDecision decision, ExternalControllerAssessment externalController, ControllerEnvironmentCompatibilityAssessment compatibility) => new(
        decision.Kind switch
        {
            RoutingDecisionKind.Eligible => AddonOperationalStatus.Ready,
            RoutingDecisionKind.WaitingForSteam => AddonOperationalStatus.WaitingForSteam,
            RoutingDecisionKind.SetupRequired => AddonOperationalStatus.SetupRequired,
            RoutingDecisionKind.Passive when decision.Reason is RoutingDecisionReason.ControllerEnvironmentUnsupported or RoutingDecisionReason.UnsupportedDevice => AddonOperationalStatus.Unsupported,
            RoutingDecisionKind.Passive or RoutingDecisionKind.VetoedForSession => AddonOperationalStatus.Passive,
            _ => AddonOperationalStatus.Indeterminate
        },
        decision.Reason switch
        {
            RoutingDecisionReason.ExternalControllerPresent => FormatExternalControllerReason(externalController),
            RoutingDecisionReason.ExternalControllerSessionLatched => "External controller veto remains active for this Steam session.",
            RoutingDecisionReason.ExternalControllerIndeterminate => "External controller state is indeterminate.",
            RoutingDecisionReason.RecoveryUnsafe => "Recovery state is not safe.",
            RoutingDecisionReason.UnsupportedDevice => "This handheld model is not supported by the current version.",
            RoutingDecisionReason.DeviceCompatibilityIndeterminate => "Handheld model compatibility could not be verified.",
            RoutingDecisionReason.ControllerEnvironmentUnsupported or RoutingDecisionReason.ControllerEnvironmentIndeterminate => FormatCompatibilityReason(compatibility),
            RoutingDecisionReason.PrerequisitesNotReady => "Required routing components are not ready.",
            RoutingDecisionReason.SteamInactive => "Waiting for a Steam session.",
            _ => "Routing prerequisites are satisfied."
        });

    private static string FormatCompatibilityReason(ControllerEnvironmentCompatibilityAssessment compatibility) => compatibility.Reason switch
    {
        ControllerEnvironmentCompatibilityReason.ClawTweaksNotSupportedByCurrentVersion => "ClawTweaks is installed. This version supports stock MSI Center M only.",
        ControllerEnvironmentCompatibilityReason.HandheldCompanionNotSupportedByCurrentVersion => "Handheld Companion is installed. This version supports stock MSI Center M only.",
        ControllerEnvironmentCompatibilityReason.MultipleThirdPartyControllerManagersNotSupportedByCurrentVersion => "Multiple third-party controller managers are installed. This version supports stock MSI Center M only.",
        ControllerEnvironmentCompatibilityReason.WinhancedNotSupportedByCurrentVersion => "Winhanced is installed. This version supports stock MSI Center M only.",
        ControllerEnvironmentCompatibilityReason.MsiCenterMRequired => "MSI Center M is required for this version.",
        ControllerEnvironmentCompatibilityReason.MsiCenterMNotOperational => "MSI Center M is not operational.",
        ControllerEnvironmentCompatibilityReason.MsiCenterMStarting => "MSI Center M is starting.",
        _ => "Controller environment state is indeterminate."
    };

    private static string FormatExternalControllerReason(ExternalControllerAssessment assessment)
    {
        var name = ExternalControllerStatusCardFactory.GetFirstControllerName(assessment);
        return assessment.ExternalControllers.Count > 1
            ? $"External physical controllers detected: {name} and {assessment.ExternalControllers.Count - 1} more."
            : $"External physical controller detected: {name}.";
    }
}
