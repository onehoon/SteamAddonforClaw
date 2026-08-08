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

        // A container is the strongest logical-device identity. Parent/root identity prevents
        // multiple interfaces for the same physical controller from being counted twice.
        var logicalCandidates = candidates
            .GroupBy(LogicalIdentity, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderBy(device => device.InstanceId, StringComparer.OrdinalIgnoreCase).ToList())
            .ToList();
        var modes = logicalCandidates.SelectMany(group => group.Select(device => ModeFor(device.ProductId))).Distinct().ToList();
        if (modes.Count != 1)
        {
            AppLog.Warn("ControllerMode", "MSI controller state is ambiguous.", null,
                ("CandidateCount", candidates.Count), ("Modes", string.Join(',', modes)), ("Action", "Passive"), ("ElapsedMs", stopwatch.ElapsedMilliseconds));
            return new(MsiControllerSnapshotStatus.Indeterminate, null, "Conflicting MSI controller mode candidates are present.");
        }

        var distinctProducts = candidates.Select(device => device.ProductId).Distinct().ToList();
        if (distinctProducts.Count != 1)
        {
            return new(MsiControllerSnapshotStatus.Indeterminate, null, "MSI interfaces do not identify one exact restorable state.");
        }

        var selected = candidates.OrderBy(device => device.InstanceId, StringComparer.OrdinalIgnoreCase).First();
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

    private static string LogicalIdentity(ControllerDeviceInfo device) => device.ContainerId is { } containerId
        ? $"container:{containerId:D}"
        : device.AncestorInstanceIds.LastOrDefault() is { Length: > 0 } root
            ? $"root:{root}"
            : device.ParentInstanceId is { Length: > 0 } parent ? $"parent:{parent}" : $"instance:{device.InstanceId}";
}
