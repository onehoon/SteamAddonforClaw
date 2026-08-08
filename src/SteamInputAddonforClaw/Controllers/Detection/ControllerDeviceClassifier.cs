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

internal sealed record ControllerClassificationResult(
    ControllerDeviceClassification Classification,
    string Reason);

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
    private static readonly string[] ClawTweaksRoutingTokens =
    [
        "CLAWTWEAKS",
        "VIIPER",
        "USBIP",
        "USB/IP"
    ];

    private readonly IControllerIdentityExclusionSource _identityExclusionSource;

    public ControllerDeviceClassifier(IControllerIdentityExclusionSource? identityExclusionSource = null)
    {
        _identityExclusionSource = identityExclusionSource ?? EmptyControllerIdentityExclusionSource.Instance;
    }

    public ControllerDeviceClassification Classify(ControllerDeviceInfo device)
        => ClassifyDetailed(device).Classification;

    internal ControllerClassificationResult ClassifyDetailed(ControllerDeviceInfo device)
    {
        if (!device.Present)
        {
            return new ControllerClassificationResult(ControllerDeviceClassification.NotController, "DeviceNotPresent");
        }

        if (!IsGameControllerCandidate(device))
        {
            return new ControllerClassificationResult(ControllerDeviceClassification.NotController, "NotGameControllerCandidate");
        }

        if (IsInternalClaw(device))
        {
            return new ControllerClassificationResult(ControllerDeviceClassification.InternalClaw, "KnownMsiClawVidPid");
        }

        if (_identityExclusionSource.IsExcluded(device))
        {
            return new ControllerClassificationResult(ControllerDeviceClassification.AddonOwnedVirtual, "IdentityExclusionSource");
        }

        var knownVirtualReason = GetKnownVirtualReason(device);
        if (knownVirtualReason is not null)
        {
            return new ControllerClassificationResult(ControllerDeviceClassification.KnownVirtual, knownVirtualReason);
        }

        if (device.InstanceId.StartsWith("ROOT\\", StringComparison.OrdinalIgnoreCase))
        {
            return new ControllerClassificationResult(ControllerDeviceClassification.Indeterminate, "UnverifiedRootVirtualIdentity");
        }

        if (device.InstanceId.Contains("VIRTUAL", StringComparison.OrdinalIgnoreCase))
        {
            return new ControllerClassificationResult(ControllerDeviceClassification.Indeterminate, "UnverifiedVirtualInstanceIdentity");
        }

        return new ControllerClassificationResult(ControllerDeviceClassification.ExternalPhysical, "PhysicalGameControllerWithoutExclusion");
    }

    public bool IsRelevantTopologyDevice(ControllerDeviceInfo device)
    {
        return IsGameControllerCandidate(device)
            || IsInternalClaw(device)
            || ContainsKnownVirtualIdentity(device);
    }

    public bool IsClawTweaksVirtualControllerCandidate(ControllerDeviceInfo device)
    {
        return IsGameControllerCandidate(device) && ContainsClawTweaksRoutingIdentity(device);
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
        return GetKnownVirtualReason(device) is not null;
    }

    private static bool ContainsClawTweaksRoutingIdentity(ControllerDeviceInfo device)
    {
        var identity = GetIdentityText(device);
        return ClawTweaksRoutingTokens.Any(token => identity.Contains(token, StringComparison.OrdinalIgnoreCase));
    }

    private static string? GetKnownVirtualReason(ControllerDeviceInfo device)
    {
        var identity = GetIdentityText(device);

        if (identity.Contains("VIIPER", StringComparison.OrdinalIgnoreCase))
        {
            return "KnownVirtualViiper";
        }

        if (identity.Contains("USBIP", StringComparison.OrdinalIgnoreCase)
            || identity.Contains("USB/IP", StringComparison.OrdinalIgnoreCase))
        {
            return "KnownVirtualUsbIp";
        }

        if (identity.Contains("VIGEM", StringComparison.OrdinalIgnoreCase))
        {
            return "KnownVirtualViGEm";
        }

        if (identity.Contains("HANDHELDCOMPANION", StringComparison.OrdinalIgnoreCase))
        {
            return "KnownVirtualHandheldCompanion";
        }

        return identity.Contains("CLAWTWEAKS", StringComparison.OrdinalIgnoreCase)
            ? "KnownVirtualClawTweaks"
            : null;
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
