using SteamInputAddonforClaw.Status;

namespace SteamInputAddonforClaw.Startup;

internal static class StartupControllerEnvironmentMapper
{
    internal static ControllerEnvironment Map(ControllerEnvironmentAssessmentSnapshot assessment) =>
        assessment.Manager.Kind switch
        {
            ControllerManagerKind.None => new(ControllerEnvironmentMode.StockCenterM, ClawTweaksStateFor(assessment.Software)),
            ControllerManagerKind.HandheldCompanion => new(ControllerEnvironmentMode.HHCManaged, ClawTweaksStateFor(assessment.Software)),
            ControllerManagerKind.ClawTweaks or ControllerManagerKind.Winhanced or ControllerManagerKind.Multiple => new(ControllerEnvironmentMode.Unsupported, ClawTweaksStateFor(assessment.Software)),
            _ => new(ControllerEnvironmentMode.Indeterminate, ClawTweaksState.Indeterminate)
        };

    private static ClawTweaksState ClawTweaksStateFor(IReadOnlyList<ControllerSoftwareStatus> software)
    {
        var status = software.SingleOrDefault(item => item.Kind == ControllerSoftwareKind.ClawTweaks);
        if (status is null || status.Installation == SoftwareInstallationStatus.Indeterminate || status.Runtime == SoftwareRuntimeStatus.Indeterminate)
            return ClawTweaksState.Indeterminate;
        if (status.Runtime is SoftwareRuntimeStatus.Running or SoftwareRuntimeStatus.Starting) return ClawTweaksState.Active;
        return status.Installation == SoftwareInstallationStatus.Installed ? ClawTweaksState.InstalledInactive : ClawTweaksState.NotInstalled;
    }
}
