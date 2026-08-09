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

internal sealed class HandheldCompanionSoftwareStatusProvider(IHandheldCompanionRuntimeDetector runtimeDetector, IApplicationInstallationProbe? installationProbe = null) : IControllerSoftwareStatusProvider
{
    public ControllerSoftwareStatus Capture()
    {
        try
        {
            var running = runtimeDetector.IsRunning();
            var installed = running || (installationProbe ?? new HandheldCompanionInstallationProbe()).Detect().Installed;
            return new(ControllerSoftwareKind.HandheldCompanion, "Handheld Companion", installed ? SoftwareInstallationStatus.Installed : SoftwareInstallationStatus.NotInstalled,
                running ? SoftwareRuntimeStatus.Running : SoftwareRuntimeStatus.NotRunning, running ? "HandheldCompanionRunning" : installed ? "HandheldCompanionInstalled" : "HandheldCompanionNotInstalled");
        }
        catch { return new(ControllerSoftwareKind.HandheldCompanion, "Handheld Companion", SoftwareInstallationStatus.Indeterminate, SoftwareRuntimeStatus.Indeterminate, "HandheldCompanionInspectionFailed"); }
    }
}

internal sealed class MsiCenterMSoftwareStatusProvider : IControllerSoftwareStatusProvider
{
    private readonly IApplicationInstallationProbe _installationProbe;
    private readonly Func<bool> _isRunning;
    public MsiCenterMSoftwareStatusProvider(IApplicationInstallationProbe? installationProbe = null, Func<bool>? isRunning = null)
    {
        _installationProbe = installationProbe ?? new MsiCenterMInstallationProbe();
        _isRunning = isRunning ?? (() => MsiCenterMIdentity.ProcessNames.Any(name => Process.GetProcessesByName(name).Length > 0));
    }
    public ControllerSoftwareStatus Capture()
    {
        try
        {
            var installed = _installationProbe.Detect().Installed;
            var running = _isRunning();
            return new(ControllerSoftwareKind.MsiCenterM, "MSI Center M", (installed || running) ? SoftwareInstallationStatus.Installed : SoftwareInstallationStatus.NotInstalled,
                running ? SoftwareRuntimeStatus.Running : SoftwareRuntimeStatus.NotRunning, running ? "MsiCenterMRunning" : installed ? "MsiCenterMInstalled" : "MsiCenterMNotInstalled");
        }
        catch { return new(ControllerSoftwareKind.MsiCenterM, "MSI Center M", SoftwareInstallationStatus.Indeterminate, SoftwareRuntimeStatus.Indeterminate, "MsiCenterMInspectionFailed"); }
    }
}

internal sealed record ApplicationInstallationInfo(bool Installed, string Reason);
internal interface IApplicationInstallationProbe { ApplicationInstallationInfo Detect(); }

internal abstract class UninstallRegistrationInstallationProbe(string displayName, IReadOnlyList<string> knownExecutablePaths) : IApplicationInstallationProbe
{
    public ApplicationInstallationInfo Detect()
    {
        foreach (var root in new[] { Microsoft.Win32.RegistryKey.OpenBaseKey(Microsoft.Win32.RegistryHive.LocalMachine, Microsoft.Win32.RegistryView.Registry64), Microsoft.Win32.Registry.CurrentUser })
        {
            using (root)
            using (var uninstall = root.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"))
            {
                if (uninstall is not null && uninstall.GetSubKeyNames().Any(key => HasDisplayName(uninstall, key))) return new(true, "UninstallRegistration");
            }
        }
        return knownExecutablePaths.Any(File.Exists) ? new(true, "KnownInstallPath") : new(false, "NotInstalled");
    }

    private bool HasDisplayName(Microsoft.Win32.RegistryKey uninstall, string key)
    {
        using var entry = uninstall.OpenSubKey(key);
        return string.Equals(entry?.GetValue("DisplayName") as string, displayName, StringComparison.OrdinalIgnoreCase);
    }
}

internal sealed class HandheldCompanionInstallationProbe : UninstallRegistrationInstallationProbe
{
    public HandheldCompanionInstallationProbe() : base("Handheld Companion", [Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "HandheldCompanion", "HandheldCompanion.exe"), Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HandheldCompanion", "HandheldCompanion.exe")]) { }
}

internal static class MsiCenterMIdentity
{
    internal const string DisplayName = "MSI Center M";
    internal static readonly string[] ProcessNames = ["MSI Center M", "MSI.CentralServer", "Center_M_Server"];
}

internal sealed class MsiCenterMInstallationProbe : UninstallRegistrationInstallationProbe
{
    public MsiCenterMInstallationProbe() : base(MsiCenterMIdentity.DisplayName, []) { }
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
