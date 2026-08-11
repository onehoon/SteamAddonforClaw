using SteamInputAddonforClaw.Controllers.Detection;

namespace SteamInputAddonforClaw.VirtualOutput.Viiper;

internal enum ViiperVirtualDeviceResolutionStatus { Resolved, NoNewCandidate, Ambiguous }

internal sealed record ViiperVirtualDeviceResolution(ViiperVirtualDeviceResolutionStatus Status, IReadOnlyList<ControllerDeviceInfo> Devices, string Reason)
{
    internal bool Succeeded => Status == ViiperVirtualDeviceResolutionStatus.Resolved;
}

internal sealed class ViiperVirtualDeviceIdentityResolver(ViiperVirtualDeviceIdentityPolicy policy)
{
    internal ViiperVirtualDeviceResolution Resolve(IReadOnlyList<ControllerDeviceInfo> before, IReadOnlyList<ControllerDeviceInfo> after)
    {
        var previous = before.Select(device => device.InstanceId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var delta = after.Where(device => policy.IsMatchingCandidate(device) && !previous.Contains(device.InstanceId)).ToArray();
        var groups = delta.GroupBy(GetLogicalKey, StringComparer.OrdinalIgnoreCase).ToArray();
        if (groups.Length == 0) return new(ViiperVirtualDeviceResolutionStatus.NoNewCandidate, [], "VirtualDeviceDidNotAppear");
        if (groups.Length > 1) return new(ViiperVirtualDeviceResolutionStatus.Ambiguous, [], "AmbiguousVirtualDeviceIdentity");
        return new(ViiperVirtualDeviceResolutionStatus.Resolved, groups[0].ToArray(), "VirtualDeviceIdentityResolved");
    }

    private static string GetLogicalKey(ControllerDeviceInfo device) => ControllerLogicalIdentity.GetLogicalKey(device);
}
