using SteamInputAddonforClaw.Controllers.Detection;

namespace SteamInputAddonforClaw.Devices.MSI.Claw;

internal static class MsiClawHardware
{
    public const ushort VendorId = 0x0DB0;
    public const ushort XInputProductId = 0x1901;
    public const ushort DirectInputProductId = 0x1902;
    public const ushort TestingProductId = 0x1903;
    public const int M1DirectInputButtonIndex = 15;
    public const int M2DirectInputButtonIndex = 16;
    public const ushort DirectInputUsagePage = 0x0001;
    public const ushort DirectInputUsage = 0x0005;
    public const string DirectInputHidCollectionPrefix = "HID\\VID_0DB0&PID_1902&MI_00&COL01\\";

    public const int RequiredDirectInputButtonCount = M2DirectInputButtonIndex + 1;

    public static bool IsKnownControllerProductId(ushort? productId) =>
        productId is XInputProductId or DirectInputProductId or TestingProductId;

    public static bool IsKnownController(ushort? vendorId, ushort? productId) =>
        vendorId == VendorId && IsKnownControllerProductId(productId);

    public static bool IsDirectInputController(ushort vendorId, ushort productId) =>
        vendorId == VendorId && productId == DirectInputProductId;

    public static bool IsDirectInputHidCollection(ControllerDeviceInfo device) =>
        device.Present && device.VendorId == VendorId && device.ProductId == DirectInputProductId &&
        IsPrimaryDirectInputHidCollectionInstanceId(device.InstanceId);

    public static bool IsPrimaryDirectInputHidCollectionInstanceId(string? instanceId) =>
        !string.IsNullOrWhiteSpace(instanceId) &&
        instanceId.StartsWith(DirectInputHidCollectionPrefix, StringComparison.OrdinalIgnoreCase);

    public static IReadOnlyList<string> ResolveDirectInputHidInstanceIds(IEnumerable<ControllerDeviceInfo> devices) =>
        devices.Where(IsDirectInputHidCollection).Select(device => device.InstanceId).ToArray();

    public static string FormatVendorId() => $"0x{VendorId:X4}";
    public static string FormatDirectInputProductId() => $"0x{DirectInputProductId:X4}";
}
