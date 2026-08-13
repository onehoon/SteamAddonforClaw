using SteamInputAddonforClaw.Controllers.Detection;

namespace SteamInputAddonforClaw.VirtualOutput.Viiper;

internal sealed class ViiperVirtualDeviceIdentityPolicy
{
    internal const ushort VendorId = 0x28DE;
    internal const ushort ProductId = 0x1102;

    // Hardware-observed usbip-win2 UDE host identity. Real Gordon PnP nodes do not carry any
    // USBIP/VIIPER text themselves -- the resolved ancestor record does, and only this exact
    // driver identity is trusted as ownership proof.
    private const string UsbIpWin2Service = "usbip2_ude";
    private const string UsbIpWin2HardwareId = @"ROOT\USBIP_WIN2\UDE";

    /// <summary>
    /// A candidate is addon-owned Gordon output only if it is the right VID/PID AND at least one
    /// of its ancestor InstanceIds resolves, in the same current snapshot, to a record that is
    /// positively identified as the usbip-win2 host. `currentByInstanceId` must be built from the
    /// same snapshot `device` came from.
    /// </summary>
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

    /// <summary>
    /// Diagnostic-only broad USBIP/VIIPER substring check. This is NOT production ownership
    /// proof -- it predates the usbip-win2 ancestor rule above and is kept solely so
    /// ViiperVirtualDeviceIdentityDiagnostics can surface raw textual evidence for hardware
    /// investigation.
    /// </summary>
    internal static bool HasBroadUsbIpOrViiperTextEvidence(ControllerDeviceInfo device)
    {
        var topology = string.Join('\n', device.AncestorInstanceIds.Append(device.InstanceId).Append(device.Service ?? string.Empty));
        return topology.Contains("USBIP", StringComparison.OrdinalIgnoreCase) || topology.Contains("VIIPER", StringComparison.OrdinalIgnoreCase);
    }
}
