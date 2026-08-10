using SteamInputAddonforClaw.Controllers.Detection;

namespace SteamInputAddonforClaw.Devices.MSI.Claw;

internal enum MsiClawModeTransitionStatus { Succeeded, WriteFailed, OldDeviceDidNotDisappear, TargetDeviceDidNotAppear, IdentityMismatch, AmbiguousDevice, UnsupportedDevice, Cancelled, StaleGeneration, TimedOut, RecoveryUnsafe }
internal enum MsiClawIdentityConfidence { Strong, Weak, Indeterminate }
internal sealed record MsiClawPhysicalIdentity(Guid? ContainerId, string? ParentInstanceId, string InstanceId, ushort? VendorId, ushort? ProductId, MsiClawIdentityConfidence Confidence)
{
    internal static MsiClawPhysicalIdentity From(ControllerDeviceInfo device) => new(device.ContainerId, device.ParentInstanceId, device.InstanceId, device.VendorId, device.ProductId,
        IsUsableContainer(device.ContainerId) && !string.IsNullOrWhiteSpace(device.ParentInstanceId) ? MsiClawIdentityConfidence.Strong : MsiClawIdentityConfidence.Indeterminate);
    private static bool IsUsableContainer(Guid? containerId) => containerId is Guid value && value != Guid.Empty && value != new Guid("00000000-0000-0000-ffff-ffffffffffff");
    internal bool StronglyMatches(MsiClawPhysicalIdentity other) => Confidence == MsiClawIdentityConfidence.Strong && other.Confidence == MsiClawIdentityConfidence.Strong && ContainerId == other.ContainerId && string.Equals(ParentInstanceId, other.ParentInstanceId, StringComparison.OrdinalIgnoreCase) && VendorId == other.VendorId;
}
internal sealed record MsiClawModeTransitionResult(MsiClawModeTransitionStatus Status, MsiClawNativeMode FromMode, MsiClawNativeMode TargetMode, ushort? FromPid, ushort? TargetPid, bool WriteSucceeded, bool OldPidDisappeared, bool TargetPidAppeared, bool IdentityVerified, long TotalMs, string Reason)
{
    internal bool Succeeded => Status == MsiClawModeTransitionStatus.Succeeded;
}
internal interface IMsiClawModeController
{
    Task<MsiClawModeTransitionResult> SwitchModeAsync(MsiClawNativeMode target, MsiClawPhysicalIdentity expectedIdentity, CancellationToken cancellationToken);
}
