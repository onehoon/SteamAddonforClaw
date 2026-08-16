using SteamInputAddonforClaw.Controllers.Detection;

namespace SteamInputAddonforClaw.VirtualOutput.Viiper;

/// <summary>
/// The exact target Steam Deck identity: VID <c>28DE</c> PID <c>1205</c>, not a broad "Valve
/// device"/VID-only match. Instance identity comparisons are case-insensitive and matching also
/// requires proof of a usbip-win2 UDE ancestor, so a coincidentally identical VID/PID on unrelated
/// hardware cannot be mistaken for the Addon's own virtual device.
/// </summary>
internal sealed class SteamDeckVirtualDeviceIdentityPolicy
{
    internal const ushort VendorId = 0x28DE;
    internal const ushort ProductId = 0x1205;

    // Hardware-observed usbip-win2 UDE host identity. Real Steam Deck PnP nodes do not carry any
    // USBIP/VIIPER text themselves either -- this is how the policy proves the candidate device is
    // one of ours rather than a coincidentally identical VID/PID on unrelated hardware.
    private const string UsbIpWin2Service = "usbip2_ude";
    private const string UsbIpWin2HardwareId = @"ROOT\USBIP_WIN2\UDE";

    internal bool IsMatchingCandidate(ControllerDeviceInfo device, IReadOnlyDictionary<string, ControllerDeviceInfo> currentByInstanceId) =>
        device.VendorId == VendorId && device.ProductId == ProductId && HasUsbIpWin2Ancestor(device, currentByInstanceId);

    internal static bool HasUsbIpWin2Ancestor(ControllerDeviceInfo device, IReadOnlyDictionary<string, ControllerDeviceInfo> currentByInstanceId) =>
        device.AncestorInstanceIds.Any(ancestorId => currentByInstanceId.TryGetValue(ancestorId, out var ancestor) && IsUsbIpWin2HostNode(ancestor));

    internal static bool IsUsbIpWin2HostNode(ControllerDeviceInfo device) =>
        string.Equals(device.Service, UsbIpWin2Service, StringComparison.OrdinalIgnoreCase) &&
        device.HardwareIds.Any(id => string.Equals(id, UsbIpWin2HardwareId, StringComparison.OrdinalIgnoreCase));

    internal static IReadOnlyDictionary<string, ControllerDeviceInfo> BuildInstanceIndex(IEnumerable<ControllerDeviceInfo> devices)
    {
        var index = new Dictionary<string, ControllerDeviceInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (var device in devices) index.TryAdd(device.InstanceId, device);
        return index;
    }
}
