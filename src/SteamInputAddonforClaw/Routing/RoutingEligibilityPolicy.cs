using SteamInputAddonforClaw.Controllers.Detection;
using SteamInputAddonforClaw.Prerequisites;
using SteamInputAddonforClaw.Steam;
using SteamInputAddonforClaw.Status;

namespace SteamInputAddonforClaw.Routing;

internal enum RoutingDecisionKind { Passive, WaitingForSteam, Eligible, SetupRequired, VetoedForSession, Indeterminate }
internal enum RoutingDecisionReason
{
    SteamInactive, ExternalControllerPresent, ExternalControllerSessionLatched, ExternalControllerIndeterminate,
    RecoveryUnsafe, HandheldCompanionRunning, HandheldCompanionIndeterminate,
    ClawTweaksRunning, ClawTweaksIndeterminate, PrerequisitesNotReady, Eligible
}

internal sealed record RoutingDecision(RoutingDecisionKind Kind, RoutingDecisionReason Reason);
internal sealed record RoutingPolicyInput(
    SteamSessionState Steam,
    ExternalControllerAssessment ExternalController,
    IReadOnlyList<ControllerSoftwareStatus> ControllerSoftware,
    RuntimePrerequisiteAssessment Prerequisites,
    bool RecoverySafe);

internal static class RoutingEligibilityPolicy
{
    public static RoutingDecision Evaluate(RoutingPolicyInput input, bool externalControllerVetoLatched)
    {
        if (input.ExternalController.Status == ExternalControllerAssessmentStatus.ExternalPresent)
            return new(input.Steam.IsActive ? RoutingDecisionKind.VetoedForSession : RoutingDecisionKind.Passive, RoutingDecisionReason.ExternalControllerPresent);
        if (externalControllerVetoLatched)
            return new(RoutingDecisionKind.VetoedForSession, RoutingDecisionReason.ExternalControllerSessionLatched);
        if (!input.RecoverySafe)
            return new(RoutingDecisionKind.Indeterminate, RoutingDecisionReason.RecoveryUnsafe);
        if (input.ExternalController.Status == ExternalControllerAssessmentStatus.Indeterminate)
            return new(RoutingDecisionKind.Indeterminate, RoutingDecisionReason.ExternalControllerIndeterminate);

        var hhc = input.ControllerSoftware.First(item => item.Kind == ControllerSoftwareKind.HandheldCompanion);
        if (hhc.Runtime == SoftwareRuntimeStatus.Running)
            return new(RoutingDecisionKind.Passive, RoutingDecisionReason.HandheldCompanionRunning);
        if (hhc.Runtime is SoftwareRuntimeStatus.Starting or SoftwareRuntimeStatus.Indeterminate)
            return new(RoutingDecisionKind.Indeterminate, RoutingDecisionReason.HandheldCompanionIndeterminate);

        var clawTweaks = input.ControllerSoftware.First(item => item.Kind == ControllerSoftwareKind.ClawTweaks);
        if (clawTweaks.Runtime == SoftwareRuntimeStatus.Running)
            return new(RoutingDecisionKind.Passive, RoutingDecisionReason.ClawTweaksRunning);
        if (clawTweaks.Runtime is SoftwareRuntimeStatus.Starting or SoftwareRuntimeStatus.Indeterminate)
            return new(RoutingDecisionKind.Indeterminate, RoutingDecisionReason.ClawTweaksIndeterminate);
        if (!input.Prerequisites.IsRoutingReady)
            return new(RoutingDecisionKind.SetupRequired, RoutingDecisionReason.PrerequisitesNotReady);
        return input.Steam.IsActive
            ? new(RoutingDecisionKind.Eligible, RoutingDecisionReason.Eligible)
            : new(RoutingDecisionKind.WaitingForSteam, RoutingDecisionReason.SteamInactive);
    }
}
