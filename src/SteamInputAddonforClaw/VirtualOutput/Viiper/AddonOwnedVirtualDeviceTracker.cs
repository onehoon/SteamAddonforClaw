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

    internal void ResolveOwnership(IEnumerable<ControllerDeviceInfo> devices)
    {
        var resolved = devices.Where(device => !string.IsNullOrWhiteSpace(device.InstanceId))
            .Select(device => device.InstanceId)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (resolved.Length == 0) throw new ArgumentException("At least one verified virtual-device identity is required.", nameof(devices));
        _instanceIds.Clear();
        foreach (var instanceId in resolved) _instanceIds[instanceId] = 0;
        Volatile.Write(ref _uncertainOwnership, 0);
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

    internal bool ClearUncertaintyAfterVerifiedAbsence(IEnumerable<ControllerDeviceInfo> present, ViiperVirtualDeviceIdentityPolicy policy,
        IEnumerable<ControllerDeviceInfo>? before = null, IEnumerable<ControllerDeviceInfo>? owned = null)
    {
        var presentSnapshot = present as IReadOnlyList<ControllerDeviceInfo> ?? present.ToArray();
        var beforeIds = (before ?? []).Select(device => device.InstanceId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var ownedIds = (owned ?? []).Select(device => device.InstanceId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var currentByInstanceId = ViiperVirtualDeviceIdentityPolicy.BuildInstanceIndex(presentSnapshot);
        if (presentSnapshot.Any(device => policy.IsMatchingCandidate(device, currentByInstanceId) && !beforeIds.Contains(device.InstanceId) && !ownedIds.Contains(device.InstanceId))) return false;
        _instanceIds.Clear();
        Volatile.Write(ref _uncertainOwnership, 0);
        return true;
    }
}
