using SteamInputAddonforClaw.Controllers.Detection;
using SteamInputAddonforClaw.Diagnostics;
using SteamInputAddonforClaw.Routing;

namespace SteamInputAddonforClaw.VirtualOutput.Viiper;

internal sealed record ViiperVirtualDeviceIdentitySummary(
    string Result,
    int BeforeDeviceCount,
    int CurrentDeviceCount,
    int NewDeviceCount,
    int GordonVidPidCount,
    int NewGordonVidPidCount,
    int ViiperTopologyEvidenceCount,
    int PolicyCandidateCount,
    int LogicalCandidateGroupCount);

internal sealed record ViiperVirtualDeviceIdentityDeviceEvidence(
    ControllerDeviceInfo Device,
    bool IsNewSinceBefore,
    bool VidMatch,
    bool PidMatch,
    bool VidPidMatchesGordon,
    bool HasCurrentViiperTopologyEvidence,
    bool InstanceContainsCurrentPolicyToken,
    bool AncestorContainsCurrentPolicyToken,
    bool ServiceContainsCurrentPolicyToken,
    IReadOnlyList<string> BroaderTopologyEvidenceFields,
    bool CurrentPolicyMatches,
    string LogicalIdentityKey,
    string Rejection,
    IReadOnlyList<string> RelatedInterestingNodes);

internal sealed record ViiperVirtualDeviceIdentityEvidence(
    ViiperVirtualDeviceIdentitySummary Summary,
    IReadOnlyList<ViiperVirtualDeviceIdentityDeviceEvidence> Devices);

/// <summary>
/// Bounded, diagnostic-only evidence collection for VIIPER Gordon PnP identity resolution
/// failures. This does not change and must never influence
/// <see cref="ViiperVirtualDeviceIdentityResolver"/>/<see cref="ViiperVirtualDeviceIdentityPolicy"/>
/// production behavior -- it only re-examines the same before/after snapshots after the
/// resolver has already decided, to explain why.
/// </summary>
internal static class ViiperVirtualDeviceIdentityDiagnostics
{
    internal const string NoneRejection = "None";
    internal const string PreExistingInstanceRejection = "PreExistingInstance";
    internal const string VendorMismatchRejection = "VendorMismatch";
    internal const string ProductMismatchRejection = "ProductMismatch";
    internal const string MissingViiperTopologyEvidenceRejection = "MissingViiperTopologyEvidence";

    internal static ViiperVirtualDeviceIdentityEvidence Build(
        IReadOnlyList<ControllerDeviceInfo> before,
        IReadOnlyList<ControllerDeviceInfo> after,
        ViiperVirtualDeviceIdentityPolicy policy,
        ViiperVirtualDeviceResolution resolution)
    {
        var previousIds = before.Select(device => device.InstanceId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        bool IsNew(ControllerDeviceInfo device) => !previousIds.Contains(device.InstanceId);
        bool VidMatch(ControllerDeviceInfo device) => device.VendorId == ViiperVirtualDeviceIdentityPolicy.VendorId;
        bool PidMatch(ControllerDeviceInfo device) => device.ProductId == ViiperVirtualDeviceIdentityPolicy.ProductId;

        var byInstanceId = new Dictionary<string, ControllerDeviceInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (var device in after) byInstanceId.TryAdd(device.InstanceId, device);

        // A: new since `before`. B: Gordon VID/PID regardless of newness. C: any broader
        // USBIP/VIIPER token evidence, even outside the narrow fields the production policy checks.
        var coreInteresting = new List<ControllerDeviceInfo>();
        var coreSeen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var device in after)
        {
            if (!coreSeen.Add(device.InstanceId)) continue;
            if (IsNew(device) || (VidMatch(device) && PidMatch(device)) || FindBroaderTopologyEvidenceFields(device).Count > 0)
                coreInteresting.Add(device);
        }

        // D: pull in parent/ancestor records for the interesting set from the same current
        // snapshot, since USBIP/VIIPER evidence may live on a related node rather than the leaf.
        var interestingIds = new HashSet<string>(coreInteresting.Select(device => device.InstanceId), StringComparer.OrdinalIgnoreCase);
        var interesting = new List<ControllerDeviceInfo>(coreInteresting);
        foreach (var device in coreInteresting)
        {
            foreach (var relatedId in device.AncestorInstanceIds.Append(device.ParentInstanceId ?? string.Empty))
            {
                if (string.IsNullOrWhiteSpace(relatedId) || !interestingIds.Add(relatedId)) continue;
                if (byInstanceId.TryGetValue(relatedId, out var relatedDevice)) interesting.Add(relatedDevice);
            }
        }

        var deviceEvidence = interesting.Select(device => BuildDeviceEvidence(device, interesting, previousIds)).ToArray();

        var policyDelta = after.Where(device => policy.IsMatchingCandidate(device) && IsNew(device)).ToArray();
        var logicalGroupCount = policyDelta.GroupBy(ControllerLogicalIdentity.GetLogicalKey, StringComparer.OrdinalIgnoreCase).Count();
        var topologyEvidenceCount = after.Count(ViiperVirtualDeviceIdentityPolicy.HasCurrentTopologyEvidence);

        var summary = new ViiperVirtualDeviceIdentitySummary(
            resolution.Reason,
            before.Count,
            after.Count,
            after.Count(IsNew),
            after.Count(device => VidMatch(device) && PidMatch(device)),
            after.Count(device => IsNew(device) && VidMatch(device) && PidMatch(device)),
            topologyEvidenceCount,
            policyDelta.Length,
            logicalGroupCount);

        return new ViiperVirtualDeviceIdentityEvidence(summary, deviceEvidence);
    }

    private static ViiperVirtualDeviceIdentityDeviceEvidence BuildDeviceEvidence(
        ControllerDeviceInfo device,
        IReadOnlyList<ControllerDeviceInfo> interesting,
        IReadOnlySet<string> previousIds)
    {
        var isNew = !previousIds.Contains(device.InstanceId);
        var vidMatch = device.VendorId == ViiperVirtualDeviceIdentityPolicy.VendorId;
        var pidMatch = device.ProductId == ViiperVirtualDeviceIdentityPolicy.ProductId;
        var hasCurrentTopologyEvidence = ViiperVirtualDeviceIdentityPolicy.HasCurrentTopologyEvidence(device);
        var instanceContains = ContainsToken(device.InstanceId);
        var ancestorContains = device.AncestorInstanceIds.Any(ContainsToken);
        var serviceContains = ContainsToken(device.Service);
        var broaderFields = FindBroaderTopologyEvidenceFields(device);
        var policyMatches = vidMatch && pidMatch && hasCurrentTopologyEvidence;
        var rejection =
            !isNew ? PreExistingInstanceRejection :
            !vidMatch ? VendorMismatchRejection :
            !pidMatch ? ProductMismatchRejection :
            !hasCurrentTopologyEvidence ? MissingViiperTopologyEvidenceRejection :
            NoneRejection;

        var related = new List<string>();
        foreach (var other in interesting)
        {
            if (string.Equals(other.InstanceId, device.InstanceId, StringComparison.OrdinalIgnoreCase)) continue;
            if (!string.IsNullOrWhiteSpace(device.ParentInstanceId) && string.Equals(device.ParentInstanceId, other.InstanceId, StringComparison.OrdinalIgnoreCase))
                related.Add($"ChildOf:{other.InstanceId}");
            if (!string.IsNullOrWhiteSpace(other.ParentInstanceId) && string.Equals(other.ParentInstanceId, device.InstanceId, StringComparison.OrdinalIgnoreCase))
                related.Add($"ParentOf:{other.InstanceId}");
            if (device.AncestorInstanceIds.Any(id => string.Equals(id, other.InstanceId, StringComparison.OrdinalIgnoreCase)))
                related.Add($"AncestorOf:{other.InstanceId}");
            if (ControllerLogicalIdentity.IsUsableContainerId(device.ContainerId) && device.ContainerId == other.ContainerId)
                related.Add($"SameContainerAs:{other.InstanceId}");
        }

        return new ViiperVirtualDeviceIdentityDeviceEvidence(
            device,
            isNew,
            vidMatch,
            pidMatch,
            vidMatch && pidMatch,
            hasCurrentTopologyEvidence,
            instanceContains,
            ancestorContains,
            serviceContains,
            broaderFields,
            policyMatches,
            ControllerLogicalIdentity.GetLogicalKey(device),
            rejection,
            related);
    }

    private static IReadOnlyList<string> FindBroaderTopologyEvidenceFields(ControllerDeviceInfo device)
    {
        var fields = new List<string>();
        if (ContainsToken(device.ParentInstanceId)) fields.Add(nameof(device.ParentInstanceId));
        if (ContainsToken(device.EnumeratorName)) fields.Add(nameof(device.EnumeratorName));
        if (device.HardwareIds.Any(ContainsToken)) fields.Add(nameof(device.HardwareIds));
        if (device.CompatibleIds.Any(ContainsToken)) fields.Add(nameof(device.CompatibleIds));
        if (ContainsToken(device.FriendlyName)) fields.Add(nameof(device.FriendlyName));
        if (ContainsToken(device.ClassName)) fields.Add(nameof(device.ClassName));
        return fields;
    }

    private static bool ContainsToken(string? value) =>
        !string.IsNullOrEmpty(value) &&
        (value.Contains("USBIP", StringComparison.OrdinalIgnoreCase) || value.Contains("VIIPER", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Emits exactly one bounded diagnostic dump for a failed (NoNewCandidate/Ambiguous)
    /// resolution. Never throws -- a formatting failure here must not affect the caller's
    /// fail-closed rollback.
    /// </summary>
    internal static void LogOnFailure(
        IReadOnlyList<ControllerDeviceInfo> before,
        IReadOnlyList<ControllerDeviceInfo> after,
        ViiperVirtualDeviceIdentityPolicy policy,
        ViiperVirtualDeviceResolution resolution,
        uint? busId,
        uint? deviceId)
    {
        try
        {
            Emit(Build(before, after, policy, resolution), busId, deviceId);
        }
        catch (Exception exception)
        {
            AppLog.Warn("ViiperIdentityDiagnostic", "Failed to build or emit VIIPER identity diagnostic evidence.", exception);
        }
    }

    private static void Emit(ViiperVirtualDeviceIdentityEvidence evidence, uint? busId, uint? deviceId)
    {
        var summary = evidence.Summary;
        AppLog.Debug("ViiperIdentityDiagnostic", "ViiperIdentityDiagnosticSummary",
            ("Result", summary.Result),
            ("BeforeDeviceCount", summary.BeforeDeviceCount),
            ("CurrentDeviceCount", summary.CurrentDeviceCount),
            ("NewDeviceCount", summary.NewDeviceCount),
            ("GordonVidPidCount", summary.GordonVidPidCount),
            ("NewGordonVidPidCount", summary.NewGordonVidPidCount),
            ("ViiperTopologyEvidenceCount", summary.ViiperTopologyEvidenceCount),
            ("PolicyCandidateCount", summary.PolicyCandidateCount),
            ("LogicalCandidateGroupCount", summary.LogicalCandidateGroupCount),
            ("BusId", busId), ("DeviceId", deviceId), ("RoutingExecution", RoutingTraceContext.Current));

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var deviceEvidence in evidence.Devices)
        {
            if (!seen.Add(deviceEvidence.Device.InstanceId)) continue;
            var device = deviceEvidence.Device;
            AppLog.Debug("ViiperIdentityDiagnostic", "ViiperIdentityDiagnosticDevice",
                ("InstanceId", device.InstanceId),
                ("Present", device.Present),
                ("VendorId", FormatHex(device.VendorId)),
                ("ProductId", FormatHex(device.ProductId)),
                ("ContainerId", device.ContainerId?.ToString() ?? "<null>"),
                ("ParentInstanceId", device.ParentInstanceId ?? "<null>"),
                ("AncestorInstanceIds", Join(device.AncestorInstanceIds)),
                ("EnumeratorName", device.EnumeratorName ?? "<null>"),
                ("Service", device.Service ?? "<null>"),
                ("HardwareIds", Join(device.HardwareIds)),
                ("CompatibleIds", Join(device.CompatibleIds)),
                ("ClassName", device.ClassName ?? "<null>"),
                ("ClassGuid", device.ClassGuid ?? "<null>"),
                ("FriendlyName", device.FriendlyName ?? "<null>"),
                ("UsagePage", device.UsagePage?.ToString() ?? "<null>"),
                ("Usage", device.Usage?.ToString() ?? "<null>"),
                ("IsNewSinceBefore", deviceEvidence.IsNewSinceBefore),
                ("VidMatch", deviceEvidence.VidMatch),
                ("PidMatch", deviceEvidence.PidMatch),
                ("VidPidMatchesGordon", deviceEvidence.VidPidMatchesGordon),
                ("HasCurrentViiperTopologyEvidence", deviceEvidence.HasCurrentViiperTopologyEvidence),
                ("InstanceContainsCurrentPolicyToken", deviceEvidence.InstanceContainsCurrentPolicyToken),
                ("AncestorContainsCurrentPolicyToken", deviceEvidence.AncestorContainsCurrentPolicyToken),
                ("ServiceContainsCurrentPolicyToken", deviceEvidence.ServiceContainsCurrentPolicyToken),
                ("BroaderTopologyEvidenceFields", Join(deviceEvidence.BroaderTopologyEvidenceFields)),
                ("CurrentPolicyMatches", deviceEvidence.CurrentPolicyMatches),
                ("LogicalIdentityKey", deviceEvidence.LogicalIdentityKey),
                ("Rejection", deviceEvidence.Rejection),
                ("RelatedInterestingNodes", Join(deviceEvidence.RelatedInterestingNodes)));
        }
    }

    private static string FormatHex(ushort? value) => value is { } v ? $"{v:X4}" : "<null>";
    private static string Join(IReadOnlyList<string> values) => values.Count == 0 ? "<empty>" : string.Join(';', values);
}
