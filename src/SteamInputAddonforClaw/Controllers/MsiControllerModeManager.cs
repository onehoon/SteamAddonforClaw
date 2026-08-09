using System.Diagnostics;
using SteamInputAddonforClaw.Controllers.Detection;
using SteamInputAddonforClaw.Diagnostics;

namespace SteamInputAddonforClaw.Controllers;

internal enum MsiControllerNativeMode { XInput, DirectInput, Other, Indeterminate }
internal enum MsiControllerSnapshotStatus { Success, DeviceNotFound, Indeterminate, EnumerationFailed }

internal sealed record MsiControllerSnapshot(
    MsiControllerNativeMode Mode,
    string? InstanceId,
    string? ParentInstanceId,
    Guid? ContainerId,
    ushort? ProductId,
    DateTimeOffset CapturedAt);

internal sealed record MsiControllerSnapshotResult(
    MsiControllerSnapshotStatus Status,
    MsiControllerSnapshot? Snapshot,
    string Reason)
{
    public bool AllowsMutation => Status == MsiControllerSnapshotStatus.Success && Snapshot is not null;
}

internal sealed class MsiControllerModeManager(IControllerDeviceEnumerator deviceEnumerator)
{
    private const ushort MsiVendorId = 0x0DB0;

    public MsiControllerSnapshotResult CaptureSnapshot()
    {
        var stopwatch = Stopwatch.StartNew();
        AppLog.Debug("ControllerMode", "MSI controller snapshot started.");
        IReadOnlyList<ControllerDeviceInfo> devices;
        try
        {
            devices = deviceEnumerator.EnumeratePresentDevices();
            AppLog.Debug("ControllerMode", "PnP enumeration completed.", ("DeviceCount", devices.Count));
        }
        catch (Exception exception)
        {
            AppLog.Error("ControllerMode", "MSI controller PnP enumeration failed.", exception, ("Action", "Passive"), ("ElapsedMs", stopwatch.ElapsedMilliseconds));
            return new(MsiControllerSnapshotStatus.EnumerationFailed, null, exception.Message);
        }

        var candidates = devices
            .Where(device => device.Present && device.VendorId == MsiVendorId && ModeFor(device.ProductId) is not MsiControllerNativeMode.Indeterminate)
            .ToList();
        foreach (var candidate in candidates)
        {
            AppLog.Trace("ControllerMode", "MSI controller candidate.",
                ("InstanceId", candidate.InstanceId), ("ParentInstanceId", candidate.ParentInstanceId),
                ("ContainerId", candidate.ContainerId), ("VID", "0x0DB0"),
                ("PID", $"0x{candidate.ProductId:X4}"), ("Mode", ModeFor(candidate.ProductId)));
        }

        if (candidates.Count == 0)
        {
            AppLog.Warn("ControllerMode", "MSI controller was not found.", null, ("Action", "Passive"), ("ElapsedMs", stopwatch.ElapsedMilliseconds));
            return new(MsiControllerSnapshotStatus.DeviceNotFound, null, "No known MSI Claw controller identity is present.");
        }

        // A container is the strongest logical-device identity. Parent identity prevents
        // multiple interfaces for the same physical controller from being counted twice.
        var logicalCandidates = candidates
            .GroupBy(LogicalIdentity, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderBy(device => device.InstanceId, StringComparer.OrdinalIgnoreCase).ToList())
            .ToList();
        if (logicalCandidates.Count != 1)
        {
            AppLog.Warn("ControllerMode", "MSI controller state is ambiguous.", null,
                ("CandidateCount", candidates.Count), ("LogicalCandidateCount", logicalCandidates.Count), ("Action", "Passive"), ("ElapsedMs", stopwatch.ElapsedMilliseconds));
            return new(MsiControllerSnapshotStatus.Indeterminate, null, "Multiple logical MSI controller candidates are present.");
        }

        var selectedLogicalCandidate = logicalCandidates.Single();
        var modes = selectedLogicalCandidate.Select(device => ModeFor(device.ProductId)).Distinct().ToList();
        var distinctProducts = selectedLogicalCandidate.Select(device => device.ProductId).Distinct().ToList();
        if (modes.Count != 1 || distinctProducts.Count != 1)
        {
            AppLog.Warn("ControllerMode", "MSI controller state is ambiguous.", null,
                ("CandidateCount", candidates.Count), ("Modes", string.Join(',', modes)), ("Action", "Passive"), ("ElapsedMs", stopwatch.ElapsedMilliseconds));
            return new(MsiControllerSnapshotStatus.Indeterminate, null, "MSI interfaces do not identify one exact restorable state.");
        }

        var selected = selectedLogicalCandidate.First();
        var snapshot = new MsiControllerSnapshot(modes[0], selected.InstanceId, selected.ParentInstanceId, selected.ContainerId, selected.ProductId, DateTimeOffset.UtcNow);
        AppLog.Info("ControllerMode", "MSI controller snapshot completed.", ("Status", MsiControllerSnapshotStatus.Success),
            ("Mode", snapshot.Mode), ("InstanceId", snapshot.InstanceId), ("ElapsedMs", stopwatch.ElapsedMilliseconds));
        return new(MsiControllerSnapshotStatus.Success, snapshot, "Snapshot captured.");
    }

    private static MsiControllerNativeMode ModeFor(ushort? productId) => productId switch
    {
        0x1901 => MsiControllerNativeMode.XInput,
        0x1902 => MsiControllerNativeMode.DirectInput,
        0x1903 => MsiControllerNativeMode.Other,
        _ => MsiControllerNativeMode.Indeterminate
    };

    private static string LogicalIdentity(ControllerDeviceInfo device)
    {
        if (device.ContainerId is Guid containerId && IsUsableContainerId(containerId))
            return $"container:{containerId:D}";

        if (!string.IsNullOrWhiteSpace(device.ParentInstanceId))
            return $"parent:{device.ParentInstanceId}";

        return $"instance:{device.InstanceId}";
    }

    private static bool IsUsableContainerId(Guid containerId)
        => containerId != Guid.Empty && containerId != new Guid("00000000-0000-0000-ffff-ffffffffffff");
}
