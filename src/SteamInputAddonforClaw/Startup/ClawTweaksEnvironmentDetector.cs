using System.Diagnostics;
using Windows.Management.Deployment;
using SteamInputAddonforClaw.Controllers.Detection;
using SteamInputAddonforClaw.Diagnostics;
using SteamInputAddonforClaw.Status;

namespace SteamInputAddonforClaw.Startup;

internal enum ClawTweaksState
{
    NotInstalled,
    InstalledInactive,
    Starting,
    Active,
    Indeterminate
}

internal enum ControllerEnvironmentMode
{
    Unsupported,
    StockCenterM,
    ClawTweaks,
    HHCManaged,
    Indeterminate
}

internal sealed record ControllerEnvironment(ControllerEnvironmentMode Mode, ClawTweaksState ClawTweaksState);

internal interface IControllerEnvironmentDetector
{
    ControllerEnvironment Detect();
}

internal interface IHandheldCompanionRuntimeDetector
{
    bool IsRunning();
}

internal interface IClawTweaksRuntimeDetector
{
    bool IsRunning();
}

internal sealed record ClawTweaksInstallationInfo(bool Installed, string? PackageFullName, string? InstallLocation, string DetectionSource);

internal interface IClawTweaksInstallationProbe
{
    ClawTweaksInstallationInfo Detect();
}

internal sealed class ClawTweaksInstallationProbe : IClawTweaksInstallationProbe
{
    private const string PackageName = "MSIClaw.ClawTweaks";

    public ClawTweaksInstallationInfo Detect()
    {
        foreach (var package in new PackageManager().FindPackagesForUser(string.Empty))
        {
            if (string.Equals(package.Id.Name, PackageName, StringComparison.OrdinalIgnoreCase))
            {
                return new ClawTweaksInstallationInfo(true, package.Id.FullName, package.InstalledLocation?.Path, "MsixPackage");
            }
        }

        foreach (var path in ClawTweaksEnvironmentDetector.KnownExecutablePaths)
        {
            if (File.Exists(path)) return new ClawTweaksInstallationInfo(true, null, Path.GetDirectoryName(path), "LegacyExecutable");
        }

        return new ClawTweaksInstallationInfo(false, null, null, "None");
    }
}

internal sealed class ClawTweaksRuntimeDetector : IClawTweaksRuntimeDetector
{
    public bool IsRunning()
    {
        if (Process.GetProcessesByName("ClawTweaks").Length > 0) return true;
        var installation = new ClawTweaksInstallationProbe().Detect();
        if (!installation.Installed || string.IsNullOrWhiteSpace(installation.InstallLocation)) return false;

        return Process.GetProcessesByName("XboxGamingBar")
            .Any(process => IsPackageOwned(process, installation.InstallLocation));
    }

    private static bool IsPackageOwned(Process process, string installLocation)
    {
        try
        {
            var path = process.MainModule?.FileName;
            var owned = path?.StartsWith(installLocation, StringComparison.OrdinalIgnoreCase) == true;
            AppLog.Debug("ClawTweaks", "Runtime process candidate.", ("ProcessName", process.ProcessName), ("ProcessId", process.Id), ("ImagePath", path), ("PackageOwned", owned));
            return owned;
        }
        catch (Exception exception)
        {
            AppLog.Debug("ClawTweaks", "Runtime image path unavailable.", ("ProcessName", process.ProcessName), ("ProcessId", process.Id), ("Reason", exception.GetType().Name), ("Action", "Continue"));
            return false;
        }
    }
}

internal sealed class HandheldCompanionRuntimeDetector : IHandheldCompanionRuntimeDetector
{
    public bool IsRunning() => Process.GetProcessesByName("HandheldCompanion").Length > 0;
}

internal sealed class ClawTweaksEnvironmentDetector : IControllerEnvironmentDetector
{
    internal static readonly string[] KnownExecutablePaths =
    [
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "ClawTweaks", "ClawTweaks.exe"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ClawTweaks", "ClawTweaks.exe")
    ];
    private readonly IHandheldCompanionRuntimeDetector _handheldCompanionRuntimeDetector;
    private readonly IClawTweaksRuntimeDetector _clawTweaksRuntimeDetector;
    private readonly IClawTweaksInstallationProbe _installationProbe;

    public ClawTweaksEnvironmentDetector(
        IControllerDeviceEnumerator deviceEnumerator,
        IHandheldCompanionRuntimeDetector? handheldCompanionRuntimeDetector = null,
        IClawTweaksRuntimeDetector? clawTweaksRuntimeDetector = null,
        IClawTweaksInstallationProbe? installationProbe = null)
    {
        _ = deviceEnumerator ?? throw new ArgumentNullException(nameof(deviceEnumerator));
        _handheldCompanionRuntimeDetector = handheldCompanionRuntimeDetector ?? new HandheldCompanionRuntimeDetector();
        _clawTweaksRuntimeDetector = clawTweaksRuntimeDetector ?? new ClawTweaksRuntimeDetector();
        _installationProbe = installationProbe ?? new ClawTweaksInstallationProbe();
    }

    public ControllerEnvironment Detect()
    {
        try
        {
            AppLog.Debug("HHC", "HHC process lookup started.", ("ProcessName", "HandheldCompanion"));
            var handheldCompanionInstalled = new HandheldCompanionInstallationProbe().Detect().Installed;
            if (handheldCompanionInstalled || _handheldCompanionRuntimeDetector.IsRunning())
            {
                AppLog.Info("HHC", "Environment owned by Handheld Companion.", ("Action", "Passive"));
                return new ControllerEnvironment(ControllerEnvironmentMode.HHCManaged, ClawTweaksState.NotInstalled);
            }
        }
        catch (Exception exception)
        {
            AppLog.Warn("HHC", "HHC runtime inspection failed.", exception, ("Action", "Passive"), ("Reason", "ProcessEnumerationException"));
            return new ControllerEnvironment(ControllerEnvironmentMode.Indeterminate, ClawTweaksState.Indeterminate);
        }

        try
        {
            var installation = _installationProbe.Detect();
            var installed = installation.Installed;
            AppLog.Info("ClawTweaks", "Installation detection completed.", ("Installed", installed), ("DetectionSource", installation.DetectionSource), ("PackageFullName", installation.PackageFullName), ("InstallLocation", installation.InstallLocation));
            var processRunning = _clawTweaksRuntimeDetector.IsRunning();
            AppLog.Debug("ClawTweaks", "ClawTweaks process inspection completed.", ("Installed", installed), ("Running", processRunning));
            if (!installed && !processRunning)
            {
                AppLog.Info("Environment", "Environment decision.", ("Mode", ControllerEnvironmentMode.StockCenterM), ("ClawTweaksState", ClawTweaksState.NotInstalled), ("Reason", "ClawTweaksAbsent"));
                return new ControllerEnvironment(ControllerEnvironmentMode.StockCenterM, ClawTweaksState.NotInstalled);
            }

            AppLog.Info("Environment", "Unsupported controller manager detected.", ("Manager", "ClawTweaks"), ("Installed", installed), ("Running", processRunning), ("Action", "Passive"), ("Reason", "ClawTweaksNotSupportedByCurrentVersion"));
            return new ControllerEnvironment(ControllerEnvironmentMode.Unsupported, processRunning ? ClawTweaksState.Active : ClawTweaksState.InstalledInactive);
        }
        catch (Exception exception)
        {
            AppLog.Warn("Environment", "ClawTweaks environment detection failed.", exception, ("Action", "Passive"), ("Reason", "ProbeOrTopologyException"));
            return new ControllerEnvironment(ControllerEnvironmentMode.Indeterminate, ClawTweaksState.Indeterminate);
        }
    }

    private static ControllerEnvironment LogDecision(ControllerEnvironmentMode mode, ClawTweaksState state, string reason)
    {
        AppLog.Info("Environment", "Environment decision.", ("Mode", mode), ("ClawTweaksState", state), ("Reason", reason));
        return new ControllerEnvironment(mode, state);
    }
}
