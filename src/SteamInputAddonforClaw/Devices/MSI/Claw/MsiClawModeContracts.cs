using SteamInputAddonforClaw.Controllers.Detection;

namespace SteamInputAddonforClaw.Devices.MSI.Claw;

internal enum MsiClawModeTransitionStatus { Succeeded, WriteFailed, OldDeviceDidNotDisappear, TargetDeviceDidNotAppear, IdentityMismatch, AmbiguousDevice, UnsupportedDevice, Cancelled, StaleGeneration, TimedOut, RecoveryUnsafe }
internal enum MsiClawIdentityConfidence { Strong, Weak, Indeterminate }
internal sealed record MsiClawPhysicalRootResolution(string RawRootInstanceId, string PhysicalDeviceKey);
internal sealed record MsiClawPhysicalIdentity(Guid? ContainerId, string? ParentInstanceId, string InstanceId, ushort? VendorId, ushort? ProductId, MsiClawIdentityConfidence Confidence, string? PhysicalDeviceKey = null)
{
    internal static MsiClawPhysicalIdentity From(ControllerDeviceInfo device)
    {
        var physicalDeviceKey = ResolvePhysicalDeviceKey(device);
        var strong = IsUsableContainer(device.ContainerId) && !string.IsNullOrWhiteSpace(device.ParentInstanceId)
            || !string.IsNullOrWhiteSpace(physicalDeviceKey) && !string.IsNullOrWhiteSpace(device.ParentInstanceId);
        return new(device.ContainerId, device.ParentInstanceId, device.InstanceId, device.VendorId, device.ProductId,
            strong ? MsiClawIdentityConfidence.Strong : MsiClawIdentityConfidence.Indeterminate, physicalDeviceKey);
    }
    internal static MsiClawPhysicalIdentity FromPayload(MsiClawNativeStatePayload payload) =>
        new(payload.ContainerId, payload.ParentInstanceId, payload.InstanceId ?? string.Empty, MsiClawHardware.VendorId, payload.ProductId, payload.IdentityConfidence, payload.PhysicalDeviceKey);
    internal static bool IsUsableContainer(Guid? containerId) => containerId is Guid value && value != Guid.Empty && value != new Guid("00000000-0000-0000-ffff-ffffffffffff");
    internal bool StronglyMatches(MsiClawPhysicalIdentity other) => Confidence == MsiClawIdentityConfidence.Strong && other.Confidence == MsiClawIdentityConfidence.Strong && VendorId == other.VendorId &&
        (IsUsableContainer(ContainerId) && IsUsableContainer(other.ContainerId)
            ? ContainerId == other.ContainerId && string.Equals(ParentInstanceId, other.ParentInstanceId, StringComparison.OrdinalIgnoreCase)
            : !string.IsNullOrWhiteSpace(PhysicalDeviceKey) && !string.IsNullOrWhiteSpace(other.PhysicalDeviceKey) &&
                string.Equals(PhysicalDeviceKey, other.PhysicalDeviceKey, StringComparison.OrdinalIgnoreCase));
    internal static string? ResolvePhysicalDeviceKey(ControllerDeviceInfo device) => ResolvePhysicalRoot(device)?.PhysicalDeviceKey;
    internal static MsiClawPhysicalRootResolution? ResolvePhysicalRoot(ControllerDeviceInfo device)
    {
        var ids = new[] { device.InstanceId }.Concat(device.AncestorInstanceIds);
        foreach (var id in ids)
        {
            if (!id.StartsWith("USB\\VID_", StringComparison.OrdinalIgnoreCase) || id.Contains("&MI_", StringComparison.OrdinalIgnoreCase) || id.Contains("&IG_", StringComparison.OrdinalIgnoreCase)) continue;
            var parts = id.Split('\\');
            if (parts.Length != 3 || !string.Equals(parts[0], "USB", StringComparison.OrdinalIgnoreCase)) continue;
            var prefix = parts[1].Split('&');
            if (prefix.Length != 2 || !prefix[0].StartsWith("VID_", StringComparison.OrdinalIgnoreCase) || !prefix[1].StartsWith("PID_", StringComparison.OrdinalIgnoreCase) ||
                !ushort.TryParse(prefix[0][4..], System.Globalization.NumberStyles.HexNumber, null, out var vid) || !ushort.TryParse(prefix[1][4..], System.Globalization.NumberStyles.HexNumber, null, out var pid) ||
                !MsiClawHardware.IsKnownController(vid, pid) || string.IsNullOrWhiteSpace(parts[2])) continue;
            return new(id, $"USB\\VID_{vid:X4}\\{parts[2]}");
        }
        return null;
    }
}
internal sealed record MsiClawModeTransitionResult(MsiClawModeTransitionStatus Status, MsiClawNativeMode FromMode, MsiClawNativeMode TargetMode, ushort? FromPid, ushort? TargetPid, bool WriteSucceeded, bool OldPidDisappeared, bool TargetPidAppeared, bool IdentityVerified, long TotalMs, string Reason)
{
    internal bool Succeeded => Status == MsiClawModeTransitionStatus.Succeeded;
}
internal interface IMsiClawModeController
{
    Task<MsiClawModeTransitionResult> SwitchModeAsync(MsiClawNativeMode target, MsiClawPhysicalIdentity expectedIdentity, CancellationToken cancellationToken);
}
