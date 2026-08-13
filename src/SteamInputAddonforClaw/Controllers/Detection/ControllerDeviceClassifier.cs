namespace SteamInputAddonforClaw.Controllers.Detection;

using SteamInputAddonforClaw.Devices.Abstractions;

public enum ControllerDeviceClassification
{
    NotController,
    InternalHandheld,
    AddonOwnedVirtual,
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

    private readonly IControllerIdentityExclusionSource _identityExclusionSource;
    private readonly IInternalControllerMatcher? _internalControllerMatcher;

    public ControllerDeviceClassifier(IControllerIdentityExclusionSource? identityExclusionSource = null)
    {
        _identityExclusionSource = identityExclusionSource ?? EmptyControllerIdentityExclusionSource.Instance;
    }

    public ControllerDeviceClassifier(IInternalControllerMatcher internalControllerMatcher, IControllerIdentityExclusionSource? identityExclusionSource = null)
    {
        _internalControllerMatcher = internalControllerMatcher ?? throw new ArgumentNullException(nameof(internalControllerMatcher));
        _identityExclusionSource = identityExclusionSource ?? EmptyControllerIdentityExclusionSource.Instance;
    }

    public ControllerDeviceClassification Classify(ControllerDeviceInfo device)
        => ClassifyDetailed(device).Classification;

    internal bool HasUncertainIdentityOwnership => _identityExclusionSource.HasUncertainOwnership;

    /// <summary>
    /// Narrow predicate for "is this the MSI Claw's own internal-controller topology?" Deliberately does
    /// not go through the general classifier (and therefore never computes or discards an
    /// external-physical-controller verdict for non-Claw devices) — it only runs the internal-controller
    /// matcher. Callers that just need to find/ignore the Claw (e.g. startup topology stabilization)
    /// should use this instead of <see cref="ClassifyDetailed(ControllerDeviceInfo, ControllerTopologySnapshot?)"/>.
    /// </summary>
    internal bool IsInternalHandheld(ControllerDeviceInfo device, ControllerTopologySnapshot? topology = null)
    {
        return device.Present
            && IsGameControllerCandidate(device)
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

        if (_identityExclusionSource.IsExcluded(device))
        {
            return new ControllerClassificationResult(ControllerDeviceClassification.AddonOwnedVirtual, "IdentityExclusionSource");
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
        if (_internalControllerMatcher is null)
        {
            return new(InternalControllerMatchStatus.NoMatch, "NoInternalControllerMatcher");
        }

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
