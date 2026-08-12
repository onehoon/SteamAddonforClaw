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
            var running = runtimeDetector.IsRunning(installation);
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
    private readonly IMsiCenterMRuntimeDetector _runtimeDetector;
    public MsiCenterMSoftwareStatusProvider(IApplicationInstallationProbe? installationProbe = null, IMsiCenterMRuntimeDetector? runtimeDetector = null)
    {
        _installationProbe = installationProbe ?? new MsiCenterMInstallationProbe();
        _runtimeDetector = runtimeDetector ?? new MsiCenterMRuntimeDetector();
    }
    public ControllerSoftwareStatus Capture()
    {
        try
        {
            var installed = _installationProbe.Detect().Installed;
            var runtime = _runtimeDetector.Detect();
            return new(ControllerSoftwareKind.MsiCenterM, "MSI Center M", (installed || runtime.Status == SoftwareRuntimeStatus.Running) ? SoftwareInstallationStatus.Installed : SoftwareInstallationStatus.NotInstalled,
                runtime.Status, runtime.Reason);
        }
        catch { return new(ControllerSoftwareKind.MsiCenterM, "MSI Center M", SoftwareInstallationStatus.Indeterminate, SoftwareRuntimeStatus.Indeterminate, "MsiCenterMInspectionFailed"); }
    }
}

internal sealed record ApplicationInstallationInfo(bool Installed, string Reason);
internal interface IApplicationInstallationProbe { ApplicationInstallationInfo Detect(); }

internal sealed record InstalledApplicationRegistration(string Source, string? DisplayName);

internal interface IUninstallRegistrationSource
{
    IReadOnlyList<InstalledApplicationRegistration> Enumerate();
}

internal sealed class WindowsUninstallRegistrationSource : IUninstallRegistrationSource
{
    public IReadOnlyList<InstalledApplicationRegistration> Enumerate()
    {
        var registrations = new List<InstalledApplicationRegistration>();
        AddRegistrations(registrations, Microsoft.Win32.RegistryHive.LocalMachine, Microsoft.Win32.RegistryView.Registry64, "HKLM64");
        AddRegistrations(registrations, Microsoft.Win32.RegistryHive.LocalMachine, Microsoft.Win32.RegistryView.Registry32, "HKLM32");
        AddRegistrations(registrations, Microsoft.Win32.RegistryHive.CurrentUser, Microsoft.Win32.RegistryView.Default, "HKCU");
        return registrations;
    }

    private static void AddRegistrations(List<InstalledApplicationRegistration> registrations, Microsoft.Win32.RegistryHive hive, Microsoft.Win32.RegistryView view, string source)
    {
        using var root = Microsoft.Win32.RegistryKey.OpenBaseKey(hive, view);
        using var uninstall = root.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");
        if (uninstall is null) return;
        foreach (var keyName in uninstall.GetSubKeyNames())
        {
            using var entry = uninstall.OpenSubKey(keyName);
            registrations.Add(new InstalledApplicationRegistration(source, entry?.GetValue("DisplayName") as string));
        }
    }
}

internal abstract class UninstallRegistrationInstallationProbe : IApplicationInstallationProbe
{
    private readonly IReadOnlyList<string> _displayNames;
    private readonly IReadOnlyList<string> _knownExecutablePaths;
    private readonly IUninstallRegistrationSource _source;

    protected UninstallRegistrationInstallationProbe(IReadOnlyList<string> displayNames, IReadOnlyList<string> knownExecutablePaths, IUninstallRegistrationSource? source = null)
    {
        _displayNames = displayNames ?? throw new ArgumentNullException(nameof(displayNames));
        _knownExecutablePaths = knownExecutablePaths ?? throw new ArgumentNullException(nameof(knownExecutablePaths));
        _source = source ?? new WindowsUninstallRegistrationSource();
    }

    public ApplicationInstallationInfo Detect()
    {
        if (_source.Enumerate().Any(registration => registration.DisplayName is not null && _displayNames.Contains(registration.DisplayName, StringComparer.OrdinalIgnoreCase)))
            return new(true, "UninstallRegistration");
        return _knownExecutablePaths.Any(File.Exists) ? new(true, "KnownInstallPath") : new(false, "NotInstalled");
    }
}

internal sealed class HandheldCompanionInstallationProbe : UninstallRegistrationInstallationProbe
{
    public HandheldCompanionInstallationProbe() : base(["Handheld Companion"], [Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "HandheldCompanion", "HandheldCompanion.exe"), Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HandheldCompanion", "HandheldCompanion.exe")]) { }
}

internal static class MsiCenterMIdentity
{
    internal static readonly string[] InstallationDisplayNames = ["MSI Center M", "MSI Center M SDK"];
    internal const string FoundationServiceName = "MSI Foundation Service";
    internal const string ServerProcessName = "MSI_Center_M_Server";
    internal const string ControlModeProcessName = "MSI_Center_M_Server_ControlMode";
    internal const string DesktopUiProcessName = "MSI Center M";
    internal const string QuickSettingsPackageName = "9426MICRO-STARINTERNATION.MSIQuickSettings";
    internal const string QuickSettingsWidgetProcessName = "Gamebar_Widget";
    internal const string QuickSettingsWidgetFileName = "Gamebar_Widget.exe";
}

internal sealed class MsiCenterMInstallationProbe : UninstallRegistrationInstallationProbe
{
    public MsiCenterMInstallationProbe() : base(MsiCenterMIdentity.InstallationDisplayNames, []) { }
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
