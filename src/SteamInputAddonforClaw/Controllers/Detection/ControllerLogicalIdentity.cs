namespace SteamInputAddonforClaw.Controllers.Detection;

internal static class ControllerLogicalIdentity
{
    internal static bool IsUsableContainerId(Guid? containerId) => containerId is { } value &&
        value != Guid.Empty && value != new Guid("00000000-0000-0000-ffff-ffffffffffff");

    internal static string GetLogicalKey(ControllerDeviceInfo device) =>
        IsUsableContainerId(device.ContainerId) ? $"container:{device.ContainerId!.Value:D}" :
        (!string.IsNullOrWhiteSpace(device.ParentInstanceId) ? $"parent:{device.ParentInstanceId}" : $"instance:{device.InstanceId}");
}
