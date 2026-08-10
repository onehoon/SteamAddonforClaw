using SteamInputAddonforClaw.Controllers.Detection;

namespace SteamInputAddonforClaw.VirtualOutput.Viiper;

internal sealed class ViiperVirtualDeviceIdentityPolicy
{
    internal const ushort VendorId = 0x28DE;
    internal const ushort ProductId = 0x1102;
    internal bool IsMatchingCandidate(ControllerDeviceInfo device) => device.VendorId == VendorId && device.ProductId == ProductId && HasViiperTopology(device);
    private static bool HasViiperTopology(ControllerDeviceInfo device)
    {
        var topology = string.Join('\n', device.AncestorInstanceIds.Append(device.InstanceId).Append(device.Service ?? string.Empty));
        return topology.Contains("USBIP", StringComparison.OrdinalIgnoreCase) || topology.Contains("VIIPER", StringComparison.OrdinalIgnoreCase);
    }
}
