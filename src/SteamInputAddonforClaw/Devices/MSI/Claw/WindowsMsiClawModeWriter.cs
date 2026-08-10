using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Devices.Enumeration;
using Windows.Devices.HumanInterfaceDevice;
using Windows.Storage.Streams;
using Windows.Storage;
using SteamInputAddonforClaw.Diagnostics;

namespace SteamInputAddonforClaw.Devices.MSI.Claw;

internal sealed class WindowsMsiClawModeWriter : IMsiClawModeWriter
{
    public async Task<bool> WriteAsync(MsiClawControlHidDevice device, MsiClawNativeMode mode, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var selector = HidDevice.GetDeviceSelector(device.UsagePage, device.Usage, MsiClawHardware.VendorId, device.Device.ProductId ?? 0);
        var infos = await DeviceInformation.FindAllAsync(selector, new[] { "System.Devices.DeviceInstanceId", "System.Devices.ContainerId" }).AsTask(cancellationToken).ConfigureAwait(false);
        var matching = infos.Where(info => MatchesIdentity(info, device.Device)).ToArray();
        if (matching.Length != 1) return false;
        using var hid = await HidDevice.FromIdAsync(matching[0].Id, FileAccessMode.ReadWrite).AsTask(cancellationToken).ConfigureAwait(false);
        if (hid is null) return false;
        var bytes = MsiClawModeCommand.Build(mode);
        var report = hid.CreateOutputReport();
        if (report.Data.Length != bytes.Length) return false;
        var writer = new DataWriter();
        writer.WriteBytes(bytes);
        report.Data = writer.DetachBuffer();
        await hid.SendOutputReportAsync(report).AsTask(cancellationToken).ConfigureAwait(false);
        AppLog.Debug("NativeMode", "MSI Claw mode command written.", ("PID", device.Device.ProductId), ("UsagePage", device.UsagePage), ("Usage", device.Usage), ("ReportLength", bytes.Length), ("Mode", mode));
        return true;
    }

    private static bool MatchesIdentity(DeviceInformation info, Controllers.Detection.ControllerDeviceInfo expected)
    {
        var instance = info.Properties.TryGetValue("System.Devices.DeviceInstanceId", out var value) ? value as string : null;
        var container = info.Properties.TryGetValue("System.Devices.ContainerId", out var containerValue) ? containerValue as Guid? : null;
        return !string.IsNullOrWhiteSpace(instance) && string.Equals(instance, expected.InstanceId, StringComparison.OrdinalIgnoreCase) &&
            container is Guid actualContainer && expected.ContainerId is Guid expectedContainer && actualContainer == expectedContainer;
    }
}
