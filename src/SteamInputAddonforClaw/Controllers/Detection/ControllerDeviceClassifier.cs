namespace SteamInputAddonforClaw.Controllers.Detection;

public enum ControllerDeviceClassification
{
    NotController,
    InternalClaw,
    AddonOwnedVirtual,
    KnownVirtual,
    ExternalPhysical,
    Indeterminate
}

public sealed class ControllerDeviceClassifier
{
    private static readonly ushort[] ClawProductIds = [0x1901, 0x1902, 0x1903];
    private static readonly string[] GameControllerTokens =
    [
        "HID_DEVICE_SYSTEM_GAME",
        "HID_DEVICE_UP:0001_U:0004",
        "HID_DEVICE_UP:0001_U:0005",
        "XUSB",
        "XINPUT"
    ];
    private static readonly string[] KnownVirtualTokens =
    [
        "VIIPER",
        "USBIP",
        "USB/IP",
        "VIGEM",
        "HANDHELDCOMPANION",
        "CLAWTWEAKS"
    ];

    private readonly IControllerIdentityExclusionSource _identityExclusionSource;

    public ControllerDeviceClassifier(IControllerIdentityExclusionSource? identityExclusionSource = null)
    {
        _identityExclusionSource = identityExclusionSource ?? EmptyControllerIdentityExclusionSource.Instance;
    }

    public ControllerDeviceClassification Classify(ControllerDeviceInfo device)
    {
        if (!device.Present || !IsGameControllerCandidate(device))
        {
            return ControllerDeviceClassification.NotController;
        }

        if (IsInternalClaw(device))
        {
            return ControllerDeviceClassification.InternalClaw;
        }

        if (_identityExclusionSource.IsExcluded(device))
        {
            return ControllerDeviceClassification.AddonOwnedVirtual;
        }

        if (ContainsKnownVirtualIdentity(device))
        {
            return ControllerDeviceClassification.KnownVirtual;
        }

        if (ContainsUnverifiedVirtualIdentity(device))
        {
            return ControllerDeviceClassification.Indeterminate;
        }

        return ControllerDeviceClassification.ExternalPhysical;
    }

    private static bool IsGameControllerCandidate(ControllerDeviceInfo device)
    {
        var evidence = string.Join('\n', device.HardwareIds.Concat(device.CompatibleIds).Append(device.EnumeratorName ?? string.Empty));
        return GameControllerTokens.Any(token => evidence.Contains(token, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsInternalClaw(ControllerDeviceInfo device)
    {
        return device.VendorId == 0x0DB0 && device.ProductId is ushort productId && ClawProductIds.Contains(productId);
    }

    private static bool ContainsKnownVirtualIdentity(ControllerDeviceInfo device)
    {
        var identity = GetIdentityText(device);
        return KnownVirtualTokens.Any(token => identity.Contains(token, StringComparison.OrdinalIgnoreCase));
    }

    private static bool ContainsUnverifiedVirtualIdentity(ControllerDeviceInfo device)
    {
        var identity = GetIdentityText(device);
        return identity.Contains("ROOT\\", StringComparison.OrdinalIgnoreCase) || identity.Contains("VIRTUAL", StringComparison.OrdinalIgnoreCase);
    }

    private static string GetIdentityText(ControllerDeviceInfo device)
    {
        return string.Join('\n', device.HardwareIds
            .Concat(device.CompatibleIds)
            .Append(device.InstanceId)
            .Append(device.ParentInstanceId ?? string.Empty)
            .Concat(device.AncestorInstanceIds)
            .Append(device.EnumeratorName ?? string.Empty)
            .Append(device.Service ?? string.Empty));
    }
}
