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
        // Full1902 0903 cleanup (section 5): no per-call log here. This generic helper runs once per
        // captured Windows device during classification (audio, ACPI, USB4, ...), so a large startup
        // emitted ~880 unrelated "Controller ancestry resolved" lines. Resolution is unchanged; the
        // "Controller topology snapshot created." summary above stays as the one bounded per-snapshot log.
        return device.AncestorInstanceIds
            .Where(_devicesByInstanceId.ContainsKey)
            .Select(instanceId => _devicesByInstanceId[instanceId])
            .ToArray();
    }
}
