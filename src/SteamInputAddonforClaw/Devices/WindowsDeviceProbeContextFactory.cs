using Microsoft.Win32;
using System.Management;
using SteamInputAddonforClaw.Controllers.Detection;
using SteamInputAddonforClaw.Devices.Abstractions;

namespace SteamInputAddonforClaw.Devices;

internal enum DeviceProbeCaptureStatus { Success, Indeterminate }

internal sealed record DeviceProbeContextCapture(DeviceProbeCaptureStatus Status, DeviceProbeContext Context, string Reason);

internal interface IWindowsDeviceProbeContextFactory
{
    DeviceProbeContextCapture Capture();
}

internal sealed record WindowsDeviceIdentity(string? Manufacturer, string? SystemProductName, string? BaseBoardProduct, bool BaseBoardCaptureSucceeded);

internal interface IWindowsDeviceIdentitySource
{
    WindowsDeviceIdentity Capture();
}

internal sealed class WindowsDeviceIdentitySource : IWindowsDeviceIdentitySource
{
    public WindowsDeviceIdentity Capture()
    {
        string? manufacturer = null;
        string? product = null;
        try
        {
            using var bios = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\BIOS");
            manufacturer = bios?.GetValue("SystemManufacturer") as string;
            product = bios?.GetValue("SystemProductName") as string;
        }
        catch { }

        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT Product FROM Win32_BaseBoard");
            var board = searcher.Get().Cast<ManagementObject>().FirstOrDefault();
            return new(manufacturer, product, board?["Product"]?.ToString(), true);
        }
        catch { return new(manufacturer, product, null, false); }
    }
}

internal sealed class WindowsDeviceProbeContextFactory(
    IWindowsDeviceIdentitySource? identitySource = null,
    IControllerDeviceEnumerator? controllerEnumerator = null) : IWindowsDeviceProbeContextFactory
{
    private readonly IWindowsDeviceIdentitySource _identitySource = identitySource ?? new WindowsDeviceIdentitySource();
    private readonly IControllerDeviceEnumerator _controllerEnumerator = controllerEnumerator ?? new WindowsControllerDeviceEnumerator();

    public DeviceProbeContextCapture Capture()
    {
        WindowsDeviceIdentity identity;
        try { identity = _identitySource.Capture(); }
        catch { identity = new(null, null, null, false); }

        IReadOnlyList<ControllerDeviceInfo> controllers;
        try { controllers = _controllerEnumerator.EnumeratePresentDevices(); }
        catch
        {
            return new(DeviceProbeCaptureStatus.Indeterminate,
                new([], identity.Manufacturer, identity.SystemProductName, identity.BaseBoardProduct),
                "ControllerPnpEnumerationFailed");
        }

        var context = new DeviceProbeContext(
            controllers.Select(device => new DeviceProbePnpDevice(device.InstanceId, device.HardwareIds, device.VendorId, device.ProductId)),
            identity.Manufacturer,
            identity.SystemProductName,
            identity.BaseBoardProduct);
        if (!identity.BaseBoardCaptureSucceeded || string.IsNullOrWhiteSpace(identity.BaseBoardProduct))
            return new(DeviceProbeCaptureStatus.Indeterminate, context, "BaseBoardProductUnavailable");
        return new(DeviceProbeCaptureStatus.Success, context, "DeviceProbeCaptured");
    }
}
