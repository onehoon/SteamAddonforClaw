using SteamInputAddonforClaw.Controllers.Detection;
using SteamInputAddonforClaw.Prerequisites;

namespace SteamInputAddonforClaw.Status;

internal static class AddonStatusEvaluator
{
    public static AddonStatusSnapshot Evaluate(IReadOnlyList<ControllerSoftwareStatus> software, RuntimePrerequisiteAssessment prerequisites, SteamStatusSnapshot steam, ExternalControllerAssessment externalController, bool recoverySafe)
    {
        if (externalController.Status == ExternalControllerAssessmentStatus.ExternalPresent) return new(AddonOperationalStatus.Passive, "External physical controller detected.");
        if (!recoverySafe) return new(AddonOperationalStatus.RecoveryRequired, "Recovery is required before routing can continue.");
        if (externalController.Status == ExternalControllerAssessmentStatus.Indeterminate) return new(AddonOperationalStatus.Indeterminate, "External controller state is indeterminate.");
        var hhc = software.First(status => status.Kind == ControllerSoftwareKind.HandheldCompanion);
        if (hhc.Runtime == SoftwareRuntimeStatus.Running) return new(AddonOperationalStatus.Passive, "Handheld Companion is running.");
        if (hhc.Runtime is SoftwareRuntimeStatus.Starting or SoftwareRuntimeStatus.Indeterminate)
            return new(AddonOperationalStatus.Indeterminate, "Handheld Companion state is not stable.");
        var clawTweaks = software.First(status => status.Kind == ControllerSoftwareKind.ClawTweaks);
        if (clawTweaks.Runtime == SoftwareRuntimeStatus.Running) return new(AddonOperationalStatus.Passive, "ClawTweaks is running.");
        if (clawTweaks.Runtime is SoftwareRuntimeStatus.Starting or SoftwareRuntimeStatus.Indeterminate) return new(AddonOperationalStatus.Indeterminate, "ClawTweaks state is not stable.");
        if (!prerequisites.IsRoutingReady) return new(AddonOperationalStatus.SetupRequired, "Required routing components are not ready.");
        return steam.IsActive ? new(AddonOperationalStatus.Ready, "Routing prerequisites are ready.") : new(AddonOperationalStatus.WaitingForSteam, "Waiting for a Steam session.");
    }
}
