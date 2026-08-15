using SteamInputAddonforClaw.Controllers.Detection;

namespace SteamInputAddonforClaw.VirtualOutput.Viiper;

/// <summary>
/// Steam Deck counterpart to <see cref="ViiperVirtualDeviceIdentityPolicy"/>: exact target identity
/// VID <c>28DE</c> PID <c>1205</c>, not a broad "Valve device"/VID-only match. Kept as a small
/// parallel policy (same usbip-win2 ancestor-ownership rule, different VID/PID) rather than
/// generalizing Gordon's policy class, per docs/VIIPER_MIGRATION_TODO.md SD2 step 14 -- Gordon's
/// existing <c>28DE:1102</c> assumptions are untouched.
/// </summary>
internal sealed class SteamDeckVirtualDeviceIdentityPolicy
{
    internal const ushort VendorId = 0x28DE;
    internal const ushort ProductId = 0x1205;

    // Same hardware-observed usbip-win2 UDE host identity Gordon's policy trusts -- see
    // ViiperVirtualDeviceIdentityPolicy for the rationale. Real Steam Deck PnP nodes do not carry
    // any USBIP/VIIPER text themselves either.
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
