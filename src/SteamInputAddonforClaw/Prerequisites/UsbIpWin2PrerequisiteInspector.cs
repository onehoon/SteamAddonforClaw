using Microsoft.Win32;
using SteamInputAddonforClaw.Controllers.Detection;

namespace SteamInputAddonforClaw.Prerequisites;

internal sealed record UsbIpWin2ProbeResult(bool ServiceInstalled, bool DevicePresent, bool DriverUsable);

internal interface IUsbIpWin2DeviceProbe
{
    UsbIpWin2ProbeResult Probe();
}

internal sealed class UsbIpWin2PrerequisiteInspector(IUsbIpWin2DeviceProbe deviceProbe)
{
    internal const string RootHardwareId = "ROOT\\USBIP_WIN2\\UDE";
    internal const string ServiceName = "usbip2_ude";

    public PrerequisiteAssessment Inspect()
    {
        try
        {
            var result = deviceProbe.Probe();
            if (!result.ServiceInstalled && !result.DevicePresent)
                return new(PrerequisiteKind.UsbIpWin2, PrerequisiteStatus.Missing, "UsbIpWin2DeviceMissing");
            if (result.ServiceInstalled && result.DevicePresent && result.DriverUsable)
                return new(PrerequisiteKind.UsbIpWin2, PrerequisiteStatus.Ready, "UsbIpWin2DeviceReady");
            return new(PrerequisiteKind.UsbIpWin2, PrerequisiteStatus.Unusable, "UsbIpWin2DeviceUnavailable");
        }
        catch
        {
            return new(PrerequisiteKind.UsbIpWin2, PrerequisiteStatus.Indeterminate, "UsbIpWin2InspectionFailed");
        }
    }
}

internal sealed class WindowsUsbIpWin2DeviceProbe(IControllerDeviceEnumerator controllerDeviceEnumerator) : IUsbIpWin2DeviceProbe
{
    public UsbIpWin2ProbeResult Probe()
    {
        var devices = controllerDeviceEnumerator.EnumeratePresentDevices();
        var device = devices.FirstOrDefault(IsUsbIpWin2UdeDevice);
        using var serviceKey = Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Services\{UsbIpWin2PrerequisiteInspector.ServiceName}");
        var serviceInstalled = serviceKey is not null;
        return new UsbIpWin2ProbeResult(
            serviceInstalled,
            device is not null,
            device?.Present == true && string.Equals(device.Service, UsbIpWin2PrerequisiteInspector.ServiceName, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsUsbIpWin2UdeDevice(ControllerDeviceInfo device) =>
        Matches(device.InstanceId)
        || device.HardwareIds.Any(Matches)
        || device.CompatibleIds.Any(Matches);

    private static bool Matches(string? value) => string.Equals(value, UsbIpWin2PrerequisiteInspector.RootHardwareId, StringComparison.OrdinalIgnoreCase)
        || value?.StartsWith(UsbIpWin2PrerequisiteInspector.RootHardwareId + "\\", StringComparison.OrdinalIgnoreCase) == true;
}
