namespace SteamInputAddonforClaw.Status;

internal static class ControllerSoftwareStatusFormatter
{
    internal static string Format(ControllerSoftwareStatus item) => item.Runtime switch
    {
        SoftwareRuntimeStatus.Running => "Running",
        SoftwareRuntimeStatus.Starting => "Starting",
        SoftwareRuntimeStatus.Indeterminate => "Indeterminate",
        _ when item.Installation == SoftwareInstallationStatus.Installed => "Installed / Not running",
        _ when item.Installation == SoftwareInstallationStatus.NotInstalled => "Not installed",
        _ => "Indeterminate"
    };
}
