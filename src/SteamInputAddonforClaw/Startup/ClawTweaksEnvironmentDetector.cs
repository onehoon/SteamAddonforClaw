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
    Indeterminate
}

internal sealed record ControllerEnvironment(ControllerEnvironmentMode Mode, ClawTweaksState ClawTweaksState);

internal interface IControllerEnvironmentDetector
{
    ControllerEnvironment Detect();
}

internal sealed class ClawTweaksEnvironmentDetector : IControllerEnvironmentDetector
{
    private static readonly string[] KnownExecutablePaths =
    [
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "ClawTweaks", "ClawTweaks.exe"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ClawTweaks", "ClawTweaks.exe")
    ];
    private readonly IControllerDeviceEnumerator _deviceEnumerator;

    public ClawTweaksEnvironmentDetector(IControllerDeviceEnumerator deviceEnumerator)
    {
        _deviceEnumerator = deviceEnumerator;
    }

    public ControllerEnvironment Detect()
    {
        var installed = KnownExecutablePaths.Any(File.Exists);
        var processRunning = Process.GetProcessesByName("ClawTweaks").Length > 0;
        if (!installed && !processRunning)
        {
            return new ControllerEnvironment(ControllerEnvironmentMode.StockCenterM, ClawTweaksState.NotInstalled);
        }

        try
        {
            var virtualTopologyPresent = _deviceEnumerator.EnumeratePresentDevices().Any(device =>
                string.Join('\n', device.HardwareIds
                    .Concat(device.CompatibleIds)
                    .Append(device.InstanceId)
                    .Append(device.ParentInstanceId ?? string.Empty)
                    .Concat(device.AncestorInstanceIds)
                    .Append(device.Service ?? string.Empty))
                .Contains("CLAWTWEAKS", StringComparison.OrdinalIgnoreCase)
                || string.Join('\n', device.AncestorInstanceIds).Contains("USBIP", StringComparison.OrdinalIgnoreCase)
                || string.Join('\n', device.AncestorInstanceIds).Contains("VIIPER", StringComparison.OrdinalIgnoreCase));

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
