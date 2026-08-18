using Windows.Devices.Enumeration;
using Windows.Devices.HumanInterfaceDevice;
using SteamInputAddonforClaw.Controllers.Detection;
using SteamInputAddonforClaw.Diagnostics;

namespace SteamInputAddonforClaw.Devices.MSI.Claw;

internal sealed class WindowsMsiClawRumbleEndpointCatalog
{
    private readonly IControllerDeviceEnumerator _devices;
    internal WindowsMsiClawRumbleEndpointCatalog(IControllerDeviceEnumerator? devices = null) => _devices = devices ?? new WindowsControllerDeviceEnumerator();

    internal IReadOnlyList<MsiClawRumbleEndpointCandidate> Find(MsiClawPhysicalInputIdentity identity)
    {
        if (string.IsNullOrWhiteSpace(identity.PnpInstanceId)) return [];
        const string hidInterfaceClassGuid = "{4D1E55B2-F16F-11CF-88CB-001111000030}";
        var devices = DeviceInformation.FindAllAsync($"System.Devices.InterfaceClassGuid:=\"{hidInterfaceClassGuid}\"", ["System.Devices.DeviceInstanceId", "System.Devices.Hid.InputReportByteLength", "System.Devices.Hid.OutputReportByteLength", "System.Devices.HardwareIds"])
            .AsTask().GetAwaiter().GetResult();
        var pnpDevices = _devices.EnumeratePresentDevices();
        var candidates = new List<MsiClawRumbleEndpointCandidate>();
        foreach (var device in devices)
        {
            var pnp = device.Properties.TryGetValue("System.Devices.DeviceInstanceId", out var value) ? value as string : null;
            if (!HasHardwareId(device)) continue;
            var topology = pnpDevices.Where(candidate =>
                string.Equals(candidate.InstanceId, pnp, StringComparison.OrdinalIgnoreCase) &&
                candidate.VendorId == MsiClawHardware.VendorId && candidate.ProductId == MsiClawHardware.DirectInputProductId).ToArray();
            if (topology.Length != 1) continue;
            var physicalRoot = topology[0].AncestorInstanceIds.FirstOrDefault(root => root.StartsWith("USB\\VID_0DB0&PID_1902\\", StringComparison.OrdinalIgnoreCase));
            if (string.IsNullOrWhiteSpace(physicalRoot)) continue;
            var inputLength = GetLength(device, "System.Devices.Hid.InputReportByteLength");
            var outputLength = GetLength(device, "System.Devices.Hid.OutputReportByteLength");
            if (inputLength <= 0 || outputLength <= 0) continue;
            AppLog.Debug("Rumble", "Rumble endpoint candidate", ("VID", MsiClawHardware.VendorId), ("PID", MsiClawHardware.DirectInputProductId), ("InputReportLength", inputLength), ("OutputReportLength", outputLength), ("Writable", outputLength == 64), ("PhysicalRootMatch", true));
            candidates.Add(new(device.Id, pnp!, physicalRoot, MsiClawHardware.VendorId,
                MsiClawHardware.DirectInputProductId, inputLength, outputLength, outputLength == 64));
        }
        return candidates;
    }

    private static int GetLength(DeviceInformation device, string key) =>
        device.Properties.TryGetValue(key, out var value) && value is not null ? Convert.ToInt32(value) : 0;

    private static bool HasHardwareId(DeviceInformation device) =>
        device.Properties.TryGetValue("System.Devices.HardwareIds", out var value) &&
        value is string[] ids && ids.Any(id => id.Contains("VID_0DB0&PID_1902", StringComparison.OrdinalIgnoreCase));
}
