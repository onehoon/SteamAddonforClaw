using SteamInputAddonforClaw.Diagnostics;

namespace SteamInputAddonforClaw.Controllers.Detection;

internal sealed class ControllerTopologySnapshot
{
    private readonly IReadOnlyDictionary<string, ControllerDeviceInfo> _devicesByInstanceId;

    internal ControllerTopologySnapshot(IReadOnlyList<ControllerDeviceInfo> devices)
    {
        var devicesByInstanceId = new Dictionary<string, ControllerDeviceInfo>(StringComparer.OrdinalIgnoreCase);
        var duplicates = 0;
        foreach (var device in devices)
        {
            if (!devicesByInstanceId.TryAdd(device.InstanceId, device)) duplicates++;
        }

        _devicesByInstanceId = devicesByInstanceId;
        AppLog.Debug("PnP", "Controller topology snapshot created.", ("DeviceCount", devices.Count), ("IndexedInstanceCount", devicesByInstanceId.Count), ("DuplicateInstanceCount", duplicates));
    }

    internal IReadOnlyList<ControllerDeviceInfo> ResolveAncestors(ControllerDeviceInfo device)
    {
        var ancestors = device.AncestorInstanceIds
            .Where(_devicesByInstanceId.ContainsKey)
            .Select(instanceId => _devicesByInstanceId[instanceId])
            .ToArray();
        AppLog.Debug("PnP", "Controller ancestry resolved.", ("InstanceId", device.InstanceId), ("AncestorCount", device.AncestorInstanceIds.Count), ("ResolvedAncestorCount", ancestors.Length), ("UnresolvedAncestorCount", device.AncestorInstanceIds.Count - ancestors.Length));
        return ancestors;
    }
}
