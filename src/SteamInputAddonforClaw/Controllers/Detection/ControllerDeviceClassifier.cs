namespace SteamInputAddonforClaw.Controllers.Detection;

using SteamInputAddonforClaw.Devices.Abstractions;

public enum ControllerDeviceClassification
{
    NotController,
    InternalHandheld,
    KnownVirtual,
    Indeterminate
}

internal sealed record ControllerClassificationResult(
    ControllerDeviceClassification Classification,
    string Reason,
    ControllerDeviceInfo? EvidenceDevice = null);

public sealed class ControllerDeviceClassifier
{
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

    private readonly IInternalControllerMatcher _internalControllerMatcher;

    public ControllerDeviceClassifier(IInternalControllerMatcher internalControllerMatcher)
    {
        _internalControllerMatcher = internalControllerMatcher ?? throw new ArgumentNullException(nameof(internalControllerMatcher));
    }

    public ControllerDeviceClassification Classify(ControllerDeviceInfo device)
        => ClassifyDetailed(device).Classification;

    /// <summary>
    /// Narrow predicate for "is this device part of the MSI Claw's own known internal-controller
    /// topology (any of its VID 0x0DB0 / PID 1901-1903 interfaces, including its non-gamepad-usage
    /// vendor/control HID interfaces)?" Deliberately does not go through the general classifier (and
    /// therefore never computes or discards an external-physical-controller verdict for non-Claw
    /// devices) and deliberately does NOT require <see cref="IsGameControllerCandidate"/>: the Claw's
    /// XInput (PID 1901, UsagePage 0xFFA0/Usage 0x0001) and DirectInput (PID 1902, UsagePage
    /// 0xFFF0/Usage 0x0040) control HID interfaces — the ones the mode-switch logic actually depends on
    /// — are vendor-defined usage pages, not generic gamepad-usage interfaces, so requiring
    /// IsGameControllerCandidate here would let startup declare Stable from the gamepad-usage interface
    /// alone while the control HID interface the mode switch needs is still enumerating. Callers that
    /// just need to find/ignore the Claw (e.g. startup topology stabilization) should use this instead
    /// of <see cref="ClassifyDetailed(ControllerDeviceInfo, ControllerTopologySnapshot?)"/>. Still safe
    /// against external controllers: MatchInternalController only matches MSI's own VID/PID family.
    /// </summary>
    internal bool IsInternalHandheld(ControllerDeviceInfo device, ControllerTopologySnapshot? topology = null)
    {
        return device.Present
            && MatchInternalController(device, topology).Status == InternalControllerMatchStatus.Match;
    }

    internal ControllerClassificationResult ClassifyDetailed(ControllerDeviceInfo device)
        => ClassifyDetailed(device, topology: null);

    internal ControllerClassificationResult ClassifyDetailed(ControllerDeviceInfo device, ControllerTopologySnapshot? topology)
    {
        if (!device.Present)
        {
            return new ControllerClassificationResult(ControllerDeviceClassification.NotController, "DeviceNotPresent");
        }

        if (!IsGameControllerCandidate(device))
        {
            return new ControllerClassificationResult(ControllerDeviceClassification.NotController, "NotGameControllerCandidate");
        }

        var internalMatch = MatchInternalController(device, topology);
        if (internalMatch.Status == InternalControllerMatchStatus.Indeterminate)
        {
            return new ControllerClassificationResult(ControllerDeviceClassification.Indeterminate, internalMatch.Reason);
        }

        if (internalMatch.Status == InternalControllerMatchStatus.Match)
        {
            return new ControllerClassificationResult(ControllerDeviceClassification.InternalHandheld, internalMatch.Reason);
        }

        var knownVirtual = GetKnownVirtualEvidence(device, topology);
        if (knownVirtual is not null)
        {
            return new ControllerClassificationResult(ControllerDeviceClassification.KnownVirtual, knownVirtual.Value.Reason, knownVirtual.Value.Device);
        }

        if (device.InstanceId.StartsWith("ROOT\\", StringComparison.OrdinalIgnoreCase))
        {
            return new ControllerClassificationResult(ControllerDeviceClassification.Indeterminate, "UnverifiedRootVirtualIdentity");
        }

        if (device.InstanceId.Contains("VIRTUAL", StringComparison.OrdinalIgnoreCase))
        {
            return new ControllerClassificationResult(ControllerDeviceClassification.Indeterminate, "UnverifiedVirtualInstanceIdentity");
        }

        // A physical game controller that is neither the MSI Claw nor a known/addon-owned virtual device.
        // The addon does not detect, classify, or otherwise take an interest in external physical
        // controllers, so this is deliberately folded into NotController rather than given its own
        // classification: nothing downstream needs to distinguish "external physical controller" from
        // "not a controller we care about".
        return new ControllerClassificationResult(ControllerDeviceClassification.NotController, "NotAddonRelevant");
    }

    public bool IsClawTweaksVirtualControllerCandidate(ControllerDeviceInfo device)
    {
        return IsGameControllerCandidate(device) && ContainsClawTweaksRoutingIdentity(device);
    }

    internal bool IsClawTweaksVirtualControllerCandidate(ControllerDeviceInfo device, ControllerTopologySnapshot topology)
    {
        var result = ClassifyDetailed(device, topology);
        return IsGameControllerCandidate(device)
            && result.Classification == ControllerDeviceClassification.KnownVirtual
            && result.Reason is "KnownVirtualUsbIpAncestor" or "KnownVirtualUsbIp" or "KnownVirtualViiper" or "KnownVirtualClawTweaks";
    }

    private static bool IsGameControllerCandidate(ControllerDeviceInfo device)
    {
        var evidence = string.Join('\n', device.HardwareIds.Concat(device.CompatibleIds).Append(device.EnumeratorName ?? string.Empty));
        return GameControllerTokens.Any(token => evidence.Contains(token, StringComparison.OrdinalIgnoreCase));
    }

    private InternalControllerMatchResult MatchInternalController(ControllerDeviceInfo device, ControllerTopologySnapshot? topology)
    {
        try
        {
            return _internalControllerMatcher.Match(new InternalControllerMatchContext(device, topology?.ResolveAncestors(device) ?? []));
        }
        catch (Exception exception)
        {
            return new(InternalControllerMatchStatus.Indeterminate, $"InternalControllerMatcherFailed:{exception.GetType().Name}");
        }
    }

    private static bool ContainsClawTweaksRoutingIdentity(ControllerDeviceInfo device)
    {
        var identity = GetIdentityText(device);
        return ClawTweaksRoutingTokens.Any(token => identity.Contains(token, StringComparison.OrdinalIgnoreCase));
    }

    private static (string Reason, ControllerDeviceInfo Device)? GetKnownVirtualEvidence(ControllerDeviceInfo device, ControllerTopologySnapshot? topology)
    {
        var devices = new[] { device }.Concat(topology?.ResolveAncestors(device) ?? []);
        foreach (var identityDevice in devices)
        {
            var identity = GetIdentityText(identityDevice);

            if (identity.Contains("VIIPER", StringComparison.OrdinalIgnoreCase)) return (ReferenceEquals(identityDevice, device) ? "KnownVirtualViiper" : "KnownVirtualViiperAncestor", identityDevice);
            if (identity.Contains("USBIP", StringComparison.OrdinalIgnoreCase) || identity.Contains("USB/IP", StringComparison.OrdinalIgnoreCase) || identityDevice.Service?.Contains("usbip", StringComparison.OrdinalIgnoreCase) == true) return (ReferenceEquals(identityDevice, device) ? "KnownVirtualUsbIp" : "KnownVirtualUsbIpAncestor", identityDevice);
            if (identity.Contains("VIGEM", StringComparison.OrdinalIgnoreCase)) return (ReferenceEquals(identityDevice, device) ? "KnownVirtualViGEm" : "KnownVirtualViGEmAncestor", identityDevice);
            if (identity.Contains("HANDHELDCOMPANION", StringComparison.OrdinalIgnoreCase)) return (ReferenceEquals(identityDevice, device) ? "KnownVirtualHandheldCompanion" : "KnownVirtualHandheldCompanionAncestor", identityDevice);
            if (identity.Contains("CLAWTWEAKS", StringComparison.OrdinalIgnoreCase)) return (ReferenceEquals(identityDevice, device) ? "KnownVirtualClawTweaks" : "KnownVirtualClawTweaksAncestor", identityDevice);
        }

        return null;
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
