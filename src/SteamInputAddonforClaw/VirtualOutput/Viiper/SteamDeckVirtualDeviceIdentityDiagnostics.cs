using SteamInputAddonforClaw.Controllers.Detection;
using SteamInputAddonforClaw.Diagnostics;
using SteamInputAddonforClaw.Routing;

namespace SteamInputAddonforClaw.VirtualOutput.Viiper;

internal sealed record SteamDeckVirtualDeviceIdentitySummary(
    string Result,
    int BeforeDeviceCount,
    int CurrentDeviceCount,
    int NewDeviceCount,
    int DeckVidPidCount,
    int NewDeckVidPidCount,
    int UsbIpWin2TopologyEvidenceCount,
    int PolicyCandidateCount,
    int LogicalCandidateGroupCount);

/// <summary>
/// A bounded, diagnostic-only summary of a failed (NoNewCandidate/Ambiguous) PnP identity resolution
/// against <see cref="SteamDeckVirtualDeviceIdentityPolicy"/>. This matters specifically because the
/// Steam Deck (<c>28DE:1205</c>) is a composite USB device (keyboard + mouse + controller interfaces
/// under one container), so a failed resolution on first real hardware can be harder to read from the
/// bare resolver result alone.
/// </summary>
internal static class SteamDeckVirtualDeviceIdentityDiagnostics
{
    /// <summary>
    /// Emits exactly one bounded diagnostic dump for a failed Steam Deck identity resolution. Never
    /// throws -- a formatting failure here must not affect the caller's fail-closed rollback.
    /// </summary>
    internal static void LogOnFailure(
        IReadOnlyList<ControllerDeviceInfo> before,
        IReadOnlyList<ControllerDeviceInfo> after,
        ViiperVirtualDeviceResolution resolution,
        uint? busId,
        uint? deviceId)
    {
        try
        {
            Emit(Build(before, after, resolution), busId, deviceId, after);
        }
        catch (Exception exception)
        {
            AppLog.Warn("ViiperIdentityDiagnostic", "Failed to build or emit Steam Deck identity diagnostic evidence.", exception);
        }
    }

    private static SteamDeckVirtualDeviceIdentitySummary Build(
        IReadOnlyList<ControllerDeviceInfo> before,
        IReadOnlyList<ControllerDeviceInfo> after,
        ViiperVirtualDeviceResolution resolution)
    {
        var previousIds = before.Select(device => device.InstanceId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        bool IsNew(ControllerDeviceInfo device) => !previousIds.Contains(device.InstanceId);
        bool VidMatch(ControllerDeviceInfo device) => device.VendorId == SteamDeckVirtualDeviceIdentityPolicy.VendorId;
        bool PidMatch(ControllerDeviceInfo device) => device.ProductId == SteamDeckVirtualDeviceIdentityPolicy.ProductId;

        var byInstanceId = SteamDeckVirtualDeviceIdentityPolicy.BuildInstanceIndex(after);
        var policy = new SteamDeckVirtualDeviceIdentityPolicy();
        var policyDelta = after.Where(device => policy.IsMatchingCandidate(device, byInstanceId) && IsNew(device)).ToArray();
        var logicalGroupCount = policyDelta.GroupBy(ControllerLogicalIdentity.GetLogicalKey, StringComparer.OrdinalIgnoreCase).Count();
        var topologyEvidenceCount = after.Count(device => SteamDeckVirtualDeviceIdentityPolicy.HasUsbIpWin2Ancestor(device, byInstanceId));

        return new SteamDeckVirtualDeviceIdentitySummary(
            resolution.Reason,
            before.Count,
            after.Count,
            after.Count(IsNew),
            after.Count(device => VidMatch(device) && PidMatch(device)),
            after.Count(device => IsNew(device) && VidMatch(device) && PidMatch(device)),
            topologyEvidenceCount,
            policyDelta.Length,
            logicalGroupCount);
    }

    private static void Emit(SteamDeckVirtualDeviceIdentitySummary summary, uint? busId, uint? deviceId, IReadOnlyList<ControllerDeviceInfo> after)
    {
        AppLog.Debug("ViiperIdentityDiagnostic", "SteamDeckIdentityDiagnosticSummary",
            ("Result", summary.Result),
            ("BeforeDeviceCount", summary.BeforeDeviceCount),
            ("CurrentDeviceCount", summary.CurrentDeviceCount),
            ("NewDeviceCount", summary.NewDeviceCount),
            ("DeckVidPidCount", summary.DeckVidPidCount),
            ("NewDeckVidPidCount", summary.NewDeckVidPidCount),
            ("UsbIpWin2TopologyEvidenceCount", summary.UsbIpWin2TopologyEvidenceCount),
            ("PolicyCandidateCount", summary.PolicyCandidateCount),
            ("LogicalCandidateGroupCount", summary.LogicalCandidateGroupCount),
            ("BusId", busId), ("DeviceId", deviceId), ("RoutingExecution", RoutingTraceContext.Current));

        var byInstanceId = SteamDeckVirtualDeviceIdentityPolicy.BuildInstanceIndex(after);
        foreach (var device in after.Where(device => device.VendorId == SteamDeckVirtualDeviceIdentityPolicy.VendorId && device.ProductId == SteamDeckVirtualDeviceIdentityPolicy.ProductId))
        {
            AppLog.Debug("ViiperIdentityDiagnostic", "SteamDeckIdentityDiagnosticDevice",
                ("InstanceId", device.InstanceId),
                ("Present", device.Present),
                ("ContainerId", device.ContainerId?.ToString() ?? "<null>"),
                ("ParentInstanceId", device.ParentInstanceId ?? "<null>"),
                ("HasUsbIpWin2Ancestor", SteamDeckVirtualDeviceIdentityPolicy.HasUsbIpWin2Ancestor(device, byInstanceId)),
                ("EnumeratorName", device.EnumeratorName ?? "<null>"),
                ("Service", device.Service ?? "<null>"),
                ("FriendlyName", device.FriendlyName ?? "<null>"),
                ("LogicalIdentityKey", ControllerLogicalIdentity.GetLogicalKey(device)));
        }
    }
}
