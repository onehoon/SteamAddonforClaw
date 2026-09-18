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
    public const ushort DirectInputJoystickUsage = 0x0004;
    public const string DirectInputHidCollectionPrefix = "HID\\VID_0DB0&PID_1902&MI_00&COL01\\";
    public const ushort DirectInputControlUsagePage = 0xFFF0;
    public const ushort DirectInputControlUsage = 0x0040;
    public const ushort ConsumerUsagePage = 0x000C;
    public const ushort ConsumerUsage = 0x0001;
    public const string DirectInputControlHidCollectionPrefix = "HID\\VID_0DB0&PID_1902&MI_00&COL02\\";
    public const string ConsumerHidCollectionPrefix = "HID\\VID_0DB0&PID_1902&MI_01&COL03\\";

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
        instanceId.StartsWith(DirectInputHidCollectionPrefix, StringComparison.OrdinalIgnoreCase) &&
        instanceId.Length > DirectInputHidCollectionPrefix.Length;

    public static IReadOnlyList<string> ResolveDirectInputHidInstanceIds(IEnumerable<ControllerDeviceInfo> devices) =>
        devices.Where(IsDirectInputHidCollection).Select(device => device.InstanceId).ToArray();

    internal static bool TryClassifyOwnedPid1902HidHideTarget(
        string? instanceId,
        out MsiClawHidHideTargetKind kind)
    {
        if (IsPrimaryDirectInputHidCollectionInstanceId(instanceId))
        {
            kind = MsiClawHidHideTargetKind.PrimaryGamepad;
            return true;
        }

        if (!string.IsNullOrWhiteSpace(instanceId)
            && instanceId.StartsWith(DirectInputControlHidCollectionPrefix, StringComparison.OrdinalIgnoreCase))
        {
            kind = MsiClawHidHideTargetKind.Control;
            return true;
        }

        if (!string.IsNullOrWhiteSpace(instanceId)
            && instanceId.StartsWith(ConsumerHidCollectionPrefix, StringComparison.OrdinalIgnoreCase))
        {
            kind = MsiClawHidHideTargetKind.Consumer;
            return true;
        }

        kind = default;
        return false;
    }

    internal static MsiClawHidHideTargetResolution ResolveOwnedPid1902HidHideTargets(
        ControllerDeviceInfo primaryDevice,
        IReadOnlyList<ControllerDeviceInfo> devices)
    {
        ArgumentNullException.ThrowIfNull(primaryDevice);
        ArgumentNullException.ThrowIfNull(devices);

        if (!IsDirectInputHidCollection(primaryDevice))
        {
            return new([], ["PrimaryTargetNotStronglyRooted"]);
        }

        var primaryRoot = ResolvePhysicalRoot(primaryDevice);
        if (primaryRoot is null)
        {
            return new([], ["PrimaryTargetNotStronglyRooted"]);
        }

        var targets = new List<string> { primaryDevice.InstanceId };
        var diagnostics = new List<string>();
        foreach (var kind in new[] { MsiClawHidHideTargetKind.Control, MsiClawHidHideTargetKind.Consumer })
        {
            var matches = devices
                .Where(device => device.Present
                    && device.VendorId == VendorId
                    && device.ProductId == DirectInputProductId
                    && TryClassifyOwnedPid1902HidHideTarget(device.InstanceId, out var candidateKind)
                    && candidateKind == kind
                    && MatchesAuxiliaryUsage(device, kind)
                    && ResolvePhysicalRoot(device) is { } candidateRoot
                    && string.Equals(candidateRoot.PhysicalDeviceKey, primaryRoot.PhysicalDeviceKey, StringComparison.OrdinalIgnoreCase))
                .OrderBy(device => device.InstanceId, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (matches.Length == 1)
            {
                targets.Add(matches[0].InstanceId);
            }
            else if (matches.Length > 1)
            {
                diagnostics.Add($"{kind}Ambiguous:{matches.Length}");
            }
        }

        return new(targets, diagnostics);
    }

    internal static IReadOnlyList<string> SelectPersistedOwnedPid1902HidHideTargets(
        IReadOnlyList<string> hiddenTargets)
    {
        ArgumentNullException.ThrowIfNull(hiddenTargets);

        var candidates = hiddenTargets
            .Where(target => TryClassifyOwnedPid1902HidHideTarget(target, out _))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var primary = candidates
            .Where(target => TryClassifyOwnedPid1902HidHideTarget(target, out var kind)
                && kind == MsiClawHidHideTargetKind.PrimaryGamepad)
            .ToArray();
        if (primary.Length != 1)
        {
            return [];
        }

        var result = new List<string> { primary[0] };
        foreach (var kind in new[] { MsiClawHidHideTargetKind.Control, MsiClawHidHideTargetKind.Consumer })
        {
            var matches = candidates
                .Where(target => TryClassifyOwnedPid1902HidHideTarget(target, out var candidateKind)
                    && candidateKind == kind)
                .OrderBy(target => target, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (matches.Length == 1)
            {
                result.Add(matches[0]);
            }
        }

        return result;
    }

    private static bool MatchesAuxiliaryUsage(ControllerDeviceInfo device, MsiClawHidHideTargetKind kind) =>
        kind switch
        {
            MsiClawHidHideTargetKind.Control => device.UsagePage == DirectInputControlUsagePage && device.Usage == DirectInputControlUsage,
            MsiClawHidHideTargetKind.Consumer => device.UsagePage == ConsumerUsagePage && device.Usage == ConsumerUsage,
            _ => false,
        };

    private static MsiClawPhysicalRootResolution? ResolvePhysicalRoot(ControllerDeviceInfo device) =>
        MsiClawPhysicalIdentity.ResolvePhysicalRoot(device);

    public static string FormatVendorId() => $"0x{VendorId:X4}";
    public static string FormatDirectInputProductId() => $"0x{DirectInputProductId:X4}";
}

internal enum MsiClawHidHideTargetKind
{
    PrimaryGamepad,
    Control,
    Consumer,
}

internal sealed record MsiClawHidHideTargetResolution(
    IReadOnlyList<string> Targets,
    IReadOnlyList<string> Diagnostics);
