using System.Diagnostics;
using Windows.Management.Deployment;
using SteamInputAddonforClaw.Startup;

namespace SteamInputAddonforClaw.Status;

internal sealed class ClawTweaksSoftwareStatusProvider(IClawTweaksInstallationProbe installationProbe, IClawTweaksRuntimeDetector runtimeDetector) : IControllerSoftwareStatusProvider
{
    public ControllerSoftwareStatus Capture()
    {
        try
        {
            var installation = installationProbe.Detect();
            var running = runtimeDetector.IsRunning();
            return new(ControllerSoftwareKind.ClawTweaks, "ClawTweaks", installation.Installed ? SoftwareInstallationStatus.Installed : SoftwareInstallationStatus.NotInstalled,
                running ? SoftwareRuntimeStatus.Running : SoftwareRuntimeStatus.NotRunning, running ? "ClawTweaksRunning" : installation.Installed ? "ClawTweaksInstalled" : "ClawTweaksNotInstalled");
        }
        catch { return new(ControllerSoftwareKind.ClawTweaks, "ClawTweaks", SoftwareInstallationStatus.Indeterminate, SoftwareRuntimeStatus.Indeterminate, "ClawTweaksInspectionFailed"); }
    }
}

internal sealed class HandheldCompanionSoftwareStatusProvider(IHandheldCompanionRuntimeDetector runtimeDetector) : IControllerSoftwareStatusProvider
{
    public ControllerSoftwareStatus Capture()
    {
        try
        {
            var running = runtimeDetector.IsRunning();
            var installed = running || new PackageManager().FindPackagesForUser(string.Empty).Any(package => package.Id.Name.Contains("HandheldCompanion", StringComparison.OrdinalIgnoreCase));
            return new(ControllerSoftwareKind.HandheldCompanion, "Handheld Companion", installed ? SoftwareInstallationStatus.Installed : SoftwareInstallationStatus.NotInstalled,
                running ? SoftwareRuntimeStatus.Running : SoftwareRuntimeStatus.NotRunning, running ? "HandheldCompanionRunning" : installed ? "HandheldCompanionInstalled" : "HandheldCompanionNotInstalled");
        }
        catch { return new(ControllerSoftwareKind.HandheldCompanion, "Handheld Companion", SoftwareInstallationStatus.Indeterminate, SoftwareRuntimeStatus.Indeterminate, "HandheldCompanionInspectionFailed"); }
    }
}

internal sealed class MsiCenterMSoftwareStatusProvider : IControllerSoftwareStatusProvider
{
    internal static readonly string[] ProcessNames = ["MSI Center M", "MSI.CentralServer", "Center_M_Server"];
    public ControllerSoftwareStatus Capture()
    {
        try
        {
            var packages = new PackageManager().FindPackagesForUser(string.Empty).ToArray();
            var installed = packages.Any(package => package.Id.Name.Contains("MSI", StringComparison.OrdinalIgnoreCase) && package.Id.Name.Contains("Center", StringComparison.OrdinalIgnoreCase));
            var running = ProcessNames.Any(name => Process.GetProcessesByName(name).Length > 0);
            return new(ControllerSoftwareKind.MsiCenterM, "MSI Center M", (installed || running) ? SoftwareInstallationStatus.Installed : SoftwareInstallationStatus.NotInstalled,
                running ? SoftwareRuntimeStatus.Running : SoftwareRuntimeStatus.NotRunning, running ? "MsiCenterMRunning" : installed ? "MsiCenterMInstalled" : "MsiCenterMNotInstalled");
        }
        catch { return new(ControllerSoftwareKind.MsiCenterM, "MSI Center M", SoftwareInstallationStatus.Indeterminate, SoftwareRuntimeStatus.Indeterminate, "MsiCenterMInspectionFailed"); }
    }
}

internal static class ControllerSoftwareStatusSorter
{
    public static IReadOnlyList<ControllerSoftwareStatus> Sort(IEnumerable<ControllerSoftwareStatus> statuses) => statuses
        .OrderBy(StatusRank)
        .ThenBy(status => status.Kind)
        .ToArray();

    private static int StatusRank(ControllerSoftwareStatus status) => status.Runtime == SoftwareRuntimeStatus.Running ? 0
        : status.Installation == SoftwareInstallationStatus.Installed && status.Runtime == SoftwareRuntimeStatus.NotRunning ? 1
        : status.Installation == SoftwareInstallationStatus.Indeterminate || status.Runtime is SoftwareRuntimeStatus.Indeterminate or SoftwareRuntimeStatus.Starting ? 2 : 3;
}
