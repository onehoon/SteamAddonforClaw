using System.Collections.Concurrent;
using SteamInputAddonforClaw.Controllers.Detection;

namespace SteamInputAddonforClaw.VirtualOutput.Viiper;

internal sealed class AddonOwnedVirtualDeviceTracker : IControllerIdentityExclusionSource
{
    private readonly ConcurrentDictionary<string, byte> _instanceIds = new(StringComparer.OrdinalIgnoreCase);

    public bool IsExcluded(ControllerDeviceInfo device) => _instanceIds.ContainsKey(device.InstanceId);

    internal void Publish(ControllerDeviceInfo device)
    {
        if (string.IsNullOrWhiteSpace(device.InstanceId)) throw new ArgumentException("An addon-owned device requires an instance identity.", nameof(device));
        _instanceIds[device.InstanceId] = 0;
    }

    internal void Remove(ControllerDeviceInfo device) => _instanceIds.TryRemove(device.InstanceId, out _);

    internal void InvalidateAll() => _instanceIds.Clear();
}
