using System.Diagnostics;
using SteamInputAddonforClaw.Controllers.Detection;
using SteamInputAddonforClaw.Diagnostics;

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

internal sealed class ClawTweaksRuntimeDetector : IClawTweaksRuntimeDetector
{
    public bool IsRunning() => Process.GetProcessesByName("ClawTweaks").Length > 0;
}

internal sealed class HandheldCompanionRuntimeDetector : IHandheldCompanionRuntimeDetector
{
    public bool IsRunning() => Process.GetProcessesByName("HandheldCompanion").Length > 0;
}

internal sealed class ClawTweaksEnvironmentDetector : IControllerEnvironmentDetector
{
    private static readonly string[] KnownExecutablePaths =
    [
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "ClawTweaks", "ClawTweaks.exe"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ClawTweaks", "ClawTweaks.exe")
    ];
    private readonly IControllerDeviceEnumerator _deviceEnumerator;
    private readonly IHandheldCompanionRuntimeDetector _handheldCompanionRuntimeDetector;
    private readonly IClawTweaksRuntimeDetector _clawTweaksRuntimeDetector;

    public ClawTweaksEnvironmentDetector(
        IControllerDeviceEnumerator deviceEnumerator,
        IHandheldCompanionRuntimeDetector? handheldCompanionRuntimeDetector = null,
        IClawTweaksRuntimeDetector? clawTweaksRuntimeDetector = null)
    {
        _deviceEnumerator = deviceEnumerator;
        _handheldCompanionRuntimeDetector = handheldCompanionRuntimeDetector ?? new HandheldCompanionRuntimeDetector();
        _clawTweaksRuntimeDetector = clawTweaksRuntimeDetector ?? new ClawTweaksRuntimeDetector();
    }

    public ControllerEnvironment Detect()
    {
        try
        {
            AppLog.Debug("HHC", "HHC process lookup started.", ("ProcessName", "HandheldCompanion"));
            if (_handheldCompanionRuntimeDetector.IsRunning())
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
            var installed = KnownExecutablePaths.Any(File.Exists);
            foreach (var path in KnownExecutablePaths) AppLog.Trace("ClawTweaks", "ClawTweaks installation probe.", ("Path", path), ("Exists", File.Exists(path)));
            var processRunning = _clawTweaksRuntimeDetector.IsRunning();
            AppLog.Debug("ClawTweaks", "ClawTweaks process inspection completed.", ("Installed", installed), ("Running", processRunning));
            if (!installed && !processRunning)
            {
                AppLog.Info("Environment", "Environment decision.", ("Mode", ControllerEnvironmentMode.StockCenterM), ("ClawTweaksState", ClawTweaksState.NotInstalled), ("Reason", "ClawTweaksAbsent"));
                return new ControllerEnvironment(ControllerEnvironmentMode.StockCenterM, ClawTweaksState.NotInstalled);
            }

            var virtualTopologyPresent = _deviceEnumerator.EnumeratePresentDevices()
                .Any(new ControllerDeviceClassifier().IsClawTweaksVirtualControllerCandidate);

            if (processRunning && virtualTopologyPresent)
            {
                AppLog.Info("Environment", "Environment decision.", ("Mode", ControllerEnvironmentMode.ClawTweaks), ("Reason", "ProcessAndVirtualTopologyPresent"));
                return new ControllerEnvironment(ControllerEnvironmentMode.ClawTweaks, ClawTweaksState.Active);
            }

            return processRunning
                ? LogDecision(ControllerEnvironmentMode.Indeterminate, ClawTweaksState.Starting, "ProcessRunningButRoutingTopologyMissing")
                : LogDecision(ControllerEnvironmentMode.StockCenterM, ClawTweaksState.InstalledInactive, "InstalledButInactive");
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
