using System.Diagnostics;
using SteamInputAddonforClaw.Controllers.Detection;

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
            if (_handheldCompanionRuntimeDetector.IsRunning())
            {
                return new ControllerEnvironment(ControllerEnvironmentMode.HHCManaged, ClawTweaksState.NotInstalled);
            }
        }
        catch (Exception)
        {
            return new ControllerEnvironment(ControllerEnvironmentMode.Indeterminate, ClawTweaksState.Indeterminate);
        }

        try
        {
            var installed = KnownExecutablePaths.Any(File.Exists);
            var processRunning = _clawTweaksRuntimeDetector.IsRunning();
            if (!installed && !processRunning)
            {
                return new ControllerEnvironment(ControllerEnvironmentMode.StockCenterM, ClawTweaksState.NotInstalled);
            }

            var virtualTopologyPresent = _deviceEnumerator.EnumeratePresentDevices()
                .Any(new ControllerDeviceClassifier().IsClawTweaksVirtualControllerCandidate);

            if (processRunning && virtualTopologyPresent)
            {
                return new ControllerEnvironment(ControllerEnvironmentMode.ClawTweaks, ClawTweaksState.Active);
            }

            return processRunning
                ? new ControllerEnvironment(ControllerEnvironmentMode.Indeterminate, ClawTweaksState.Starting)
                : new ControllerEnvironment(ControllerEnvironmentMode.StockCenterM, ClawTweaksState.InstalledInactive);
        }
        catch (Exception)
        {
            return new ControllerEnvironment(ControllerEnvironmentMode.Indeterminate, ClawTweaksState.Indeterminate);
        }
    }
}
