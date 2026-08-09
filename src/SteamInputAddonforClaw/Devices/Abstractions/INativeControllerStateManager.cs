namespace SteamInputAddonforClaw.Devices.Abstractions;

public enum NativeStateCaptureStatus { Success, DeviceNotFound, Indeterminate, Failed }
public sealed record NativeStateCaptureResult(NativeStateCaptureStatus Status, DeviceNativeStateSnapshot? Snapshot, string Reason)
{
    public bool AllowsMutation => Status == NativeStateCaptureStatus.Success && Snapshot is not null;
}
public enum NativeStateRestoreStatus { Success, Unsupported, Indeterminate, Failed }
public sealed record NativeStateRestoreResult(NativeStateRestoreStatus Status, string Reason)
{
    public bool Restored => Status == NativeStateRestoreStatus.Success;
}
public interface INativeControllerStateManager
{
    HandheldDeviceId DeviceId { get; }
    NativeStateCaptureResult CaptureSnapshot();
    Task<NativeStateRestoreResult> RestoreSnapshotAsync(DeviceNativeStateSnapshot snapshot, CancellationToken cancellationToken);
}
