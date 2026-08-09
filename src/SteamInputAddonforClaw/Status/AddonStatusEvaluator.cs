using SteamInputAddonforClaw.Routing;

namespace SteamInputAddonforClaw.Status;

internal static class AddonStatusEvaluator
{
    public static AddonStatusSnapshot Map(RoutingDecision decision) => new(
        decision.Kind switch
        {
            RoutingDecisionKind.Eligible => AddonOperationalStatus.Ready,
            RoutingDecisionKind.WaitingForSteam => AddonOperationalStatus.WaitingForSteam,
            RoutingDecisionKind.SetupRequired => AddonOperationalStatus.SetupRequired,
            RoutingDecisionKind.Passive or RoutingDecisionKind.VetoedForSession => AddonOperationalStatus.Passive,
            _ => AddonOperationalStatus.Indeterminate
        },
        decision.Reason switch
        {
            RoutingDecisionReason.ExternalControllerPresent => "External physical controller detected.",
            RoutingDecisionReason.ExternalControllerSessionLatched => "External controller veto remains active for this Steam session.",
            RoutingDecisionReason.ExternalControllerIndeterminate => "External controller state is indeterminate.",
            RoutingDecisionReason.RecoveryUnsafe => "Recovery state is not safe.",
            RoutingDecisionReason.HandheldCompanionRunning => "Handheld Companion is running.",
            RoutingDecisionReason.HandheldCompanionIndeterminate => "Handheld Companion state is not stable.",
            RoutingDecisionReason.ClawTweaksRunning => "ClawTweaks is running.",
            RoutingDecisionReason.ClawTweaksIndeterminate => "ClawTweaks state is not stable.",
            RoutingDecisionReason.PrerequisitesNotReady => "Required routing components are not ready.",
            RoutingDecisionReason.SteamInactive => "Waiting for a Steam session.",
            _ => "Routing prerequisites are satisfied."
        });
}
