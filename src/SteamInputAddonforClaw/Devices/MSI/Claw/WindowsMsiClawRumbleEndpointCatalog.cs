using Windows.Devices.Enumeration;
using Windows.Devices.HumanInterfaceDevice;

namespace SteamInputAddonforClaw.Devices.MSI.Claw;

internal sealed class WindowsMsiClawRumbleEndpointCatalog
{
    internal IReadOnlyList<MsiClawRumbleEndpointCandidate> Find(MsiClawPhysicalInputIdentity identity)
    {
        if (string.IsNullOrWhiteSpace(identity.PnpInstanceId)) return [];
        var selector = HidDevice.GetDeviceSelector(0, 0, MsiClawHardware.VendorId, MsiClawHardware.DirectInputProductId);
        var devices = DeviceInformation.FindAllAsync(selector, ["System.Devices.DeviceInstanceId", "System.Devices.Hid.InputReportByteLength", "System.Devices.Hid.OutputReportByteLength"])
            .AsTask().GetAwaiter().GetResult();
        var candidates = new List<MsiClawRumbleEndpointCandidate>();
        foreach (var device in devices)
        {
            var pnp = device.Properties.TryGetValue("System.Devices.DeviceInstanceId", out var value) ? value as string : null;
            if (!string.Equals(pnp, identity.PnpInstanceId, StringComparison.OrdinalIgnoreCase)) continue;
            var inputLength = GetLength(device, "System.Devices.Hid.InputReportByteLength");
            var outputLength = GetLength(device, "System.Devices.Hid.OutputReportByteLength");
            if (inputLength <= 0 || outputLength <= 0) continue;
            candidates.Add(new(device.Id, pnp!, identity.PhysicalIdentity, MsiClawHardware.VendorId,
                MsiClawHardware.DirectInputProductId, inputLength, outputLength, outputLength >= 64));
        }
        return candidates;
    }

    private static int GetLength(DeviceInformation device, string key) =>
        device.Properties.TryGetValue(key, out var value) && value is not null ? Convert.ToInt32(value) : 0;
}
