using System.Text.RegularExpressions;
using SteamInputAddonforClaw.Controllers.Detection;

namespace SteamInputAddonforClaw.Input.DirectInput;

internal sealed class DirectInputDeviceTopologyResolver(IControllerDeviceEnumerator controllerDevices)
{
    private const ushort MsiVendorId = 0x0DB0;
    private const ushort DirectInputProductId = 0x1902;
    private const ushort GenericDesktopUsagePage = 0x0001;
    private const ushort GamePadUsage = 0x0005;

    public DirectInputTopologyResolution Resolve(string? devicePath)
    {
        var pnpInstanceId = TryGetPnpInstanceId(devicePath);
        if (pnpInstanceId is null) return DirectInputTopologyResolution.Unresolved(devicePath, "InvalidDevicePath");

        ControllerDeviceInfo? device;
        try { device = controllerDevices.FindPresentDevice(pnpInstanceId); }
        catch (Exception exception) { return DirectInputTopologyResolution.Unresolved(devicePath, $"PnPEnumerationFailed:{exception.GetType().Name}"); }
        if (device is null) return DirectInputTopologyResolution.Unresolved(devicePath, "PnpInstanceNotFound");
        if (device.VendorId != MsiVendorId || device.ProductId != DirectInputProductId)
            return DirectInputTopologyResolution.Unresolved(devicePath, "PnpIdentityIsNotMsiPid1902");

        if (TryGetUsage(device.HardwareIds.Concat(device.CompatibleIds)) is not { } usage || usage != (GenericDesktopUsagePage, GamePadUsage))
            return DirectInputTopologyResolution.Unresolved(devicePath, "PnpInterfaceIsNotGamePad");

        var physicalRoot = device.AncestorInstanceIds.FirstOrDefault(IsMsiPhysicalRoot);
        return physicalRoot is null
            ? DirectInputTopologyResolution.Unresolved(devicePath, "MsiPhysicalRootNotFound")
            : new(devicePath, device.InstanceId, physicalRoot, usage.Page, usage.Usage, "VerifiedMsiPhysicalRoot");
    }

    private static string? TryGetPnpInstanceId(string? devicePath)
    {
        if (string.IsNullOrWhiteSpace(devicePath)) return null;
        var path = devicePath.Trim();
        if (!path.StartsWith("\\\\?\\", StringComparison.OrdinalIgnoreCase)) return null;
        var parts = path[4..].Split('#');
        return parts.Length >= 3 && parts[0].Length > 0 && parts[1].Length > 0 && parts[2].Length > 0
            ? $"{parts[0]}\\{parts[1]}\\{parts[2]}"
            : null;
    }

    private static bool IsMsiPhysicalRoot(string instanceId) => instanceId.StartsWith("USB\\VID_0DB0&PID_1902\\", StringComparison.OrdinalIgnoreCase);

    private static (ushort Page, ushort Usage)? TryGetUsage(IEnumerable<string> identifiers)
    {
        foreach (var identifier in identifiers)
        {
            var match = Regex.Match(identifier, "UP:([0-9A-Fa-f]{4})_U:([0-9A-Fa-f]{4})");
            if (match.Success) return (Convert.ToUInt16(match.Groups[1].Value, 16), Convert.ToUInt16(match.Groups[2].Value, 16));
        }

        return null;
    }
}

internal sealed record DirectInputTopologyResolution(string? DevicePath, string? PnpInstanceId, string? PhysicalIdentity, ushort? UsagePage, ushort? Usage, string SelectionReason)
{
    public bool IsVerified => PnpInstanceId is not null && PhysicalIdentity is not null && UsagePage == 0x0001 && Usage == 0x0005;
    public static DirectInputTopologyResolution Unresolved(string? devicePath, string reason) => new(devicePath, null, null, null, null, reason);
}
