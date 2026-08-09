using System.Diagnostics;
using System.Text.Json;
using SteamInputAddonforClaw.Controllers.Detection;
using SteamInputAddonforClaw.Devices.Abstractions;
using SteamInputAddonforClaw.Diagnostics;

namespace SteamInputAddonforClaw.Devices.MSI.Claw;

internal enum MsiClawNativeMode { XInput, DirectInput, Other }

internal sealed class MsiClawNativeStateManager(IControllerDeviceEnumerator deviceEnumerator) : INativeControllerStateManager
{
    internal const int SnapshotFormatVersion = 1;
    public HandheldDeviceId DeviceId { get; } = new("msi.claw");

    public NativeStateCaptureResult CaptureSnapshot()
    {
        var stopwatch = Stopwatch.StartNew();
        IReadOnlyList<ControllerDeviceInfo> devices;
        try { devices = deviceEnumerator.EnumeratePresentDevices(); }
        catch (Exception exception)
        {
            AppLog.Error("NativeState", "MSI Claw PnP enumeration failed.", exception, ("Action", "Passive"));
            return new(NativeStateCaptureStatus.Failed, null, exception.Message);
        }

        var candidates = devices.Where(device => device.Present && MsiClawHardware.IsKnownController(device.VendorId, device.ProductId)).ToList();
        if (candidates.Count == 0)
            return new(NativeStateCaptureStatus.DeviceNotFound, null, "No known MSI Claw controller identity is present.");

        var logicalCandidates = candidates.GroupBy(LogicalIdentity, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderBy(device => device.InstanceId, StringComparer.OrdinalIgnoreCase).ToList()).ToList();
        if (logicalCandidates.Count != 1)
            return new(NativeStateCaptureStatus.Indeterminate, null, "Multiple logical MSI controller candidates are present.");

        var selectedLogicalCandidate = logicalCandidates.Single();
        var products = selectedLogicalCandidate.Select(device => device.ProductId).Distinct().ToList();
        if (products.Count != 1 || ModeFor(products[0]) is not { } mode)
            return new(NativeStateCaptureStatus.Indeterminate, null, "MSI interfaces do not identify one exact restorable state.");

        var selected = selectedLogicalCandidate.First();
        var payload = new MsiClawNativeStatePayload(mode, selected.InstanceId, selected.ParentInstanceId, selected.ContainerId, selected.ProductId);
        var snapshot = new DeviceNativeStateSnapshot(DeviceId, SnapshotFormatVersion, DateTimeOffset.UtcNow, JsonSerializer.SerializeToElement(payload));
        AppLog.Info("NativeState", "MSI Claw native state snapshot completed.", ("Mode", mode), ("InstanceId", selected.InstanceId), ("ElapsedMs", stopwatch.ElapsedMilliseconds));
        return new(NativeStateCaptureStatus.Success, snapshot, "Snapshot captured.");
    }

    public Task<NativeStateRestoreResult> RestoreSnapshotAsync(DeviceNativeStateSnapshot snapshot, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (snapshot is null || snapshot.DeviceId != DeviceId)
            return Task.FromResult(new NativeStateRestoreResult(NativeStateRestoreStatus.Failed, "SnapshotDeviceIdMismatch"));
        if (snapshot.FormatVersion != SnapshotFormatVersion)
            return Task.FromResult(new NativeStateRestoreResult(NativeStateRestoreStatus.Unsupported, "UnsupportedSnapshotFormatVersion"));
        MsiClawNativeStatePayload? original;
        try { original = snapshot.Payload.Deserialize<MsiClawNativeStatePayload>(); }
        catch (JsonException) { return Task.FromResult(new NativeStateRestoreResult(NativeStateRestoreStatus.Failed, "MalformedSnapshotPayload")); }
        if (original is null || original.ProductId is null)
            return Task.FromResult(new NativeStateRestoreResult(NativeStateRestoreStatus.Failed, "MalformedSnapshotPayload"));

        var current = CaptureSnapshot();
        if (!current.AllowsMutation || current.Snapshot is null)
            return Task.FromResult(new NativeStateRestoreResult(current.Status == NativeStateCaptureStatus.Indeterminate ? NativeStateRestoreStatus.Indeterminate : NativeStateRestoreStatus.Failed, current.Reason));
        var currentPayload = current.Snapshot.Payload.Deserialize<MsiClawNativeStatePayload>();
        if (currentPayload == original)
            return Task.FromResult(new NativeStateRestoreResult(NativeStateRestoreStatus.Success, "AlreadyOriginalState"));
        return Task.FromResult(new NativeStateRestoreResult(NativeStateRestoreStatus.Unsupported, "NativeModeRestoreNotImplemented"));
    }

    private static MsiClawNativeMode? ModeFor(ushort? productId) => productId switch
    {
        MsiClawHardware.XInputProductId => MsiClawNativeMode.XInput,
        MsiClawHardware.DirectInputProductId => MsiClawNativeMode.DirectInput,
        MsiClawHardware.TestingProductId => MsiClawNativeMode.Other,
        _ => null
    };

    private static string LogicalIdentity(ControllerDeviceInfo device)
    {
        if (device.ContainerId is Guid containerId && containerId != Guid.Empty && containerId != new Guid("00000000-0000-0000-ffff-ffffffffffff")) return $"container:{containerId:D}";
        if (!string.IsNullOrWhiteSpace(device.ParentInstanceId)) return $"parent:{device.ParentInstanceId}";
        return $"instance:{device.InstanceId}";
    }
}
