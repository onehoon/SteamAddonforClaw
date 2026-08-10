using System.Collections.Concurrent;
using SteamInputAddonforClaw.Controllers.Detection;

namespace SteamInputAddonforClaw.VirtualOutput.Viiper;

internal sealed class AddonOwnedVirtualDeviceTracker : IControllerIdentityExclusionSource
{
    private readonly ConcurrentDictionary<string, byte> _instanceIds = new(StringComparer.OrdinalIgnoreCase);
    private int _uncertainOwnership;

    public bool IsExcluded(ControllerDeviceInfo device) => _instanceIds.ContainsKey(device.InstanceId);
    public bool HasUncertainOwnership => Volatile.Read(ref _uncertainOwnership) != 0;

    internal void Publish(ControllerDeviceInfo device)
    {
        if (string.IsNullOrWhiteSpace(device.InstanceId)) throw new ArgumentException("An addon-owned device requires an instance identity.", nameof(device));
        _instanceIds[device.InstanceId] = 0;
    }

    internal void Remove(ControllerDeviceInfo device)
    {
        _instanceIds.TryRemove(device.InstanceId, out _);
        Volatile.Write(ref _uncertainOwnership, 0);
    }

    internal void MarkOwnershipUncertain()
    {
        _instanceIds.Clear();
        Volatile.Write(ref _uncertainOwnership, 1);
    }
}
