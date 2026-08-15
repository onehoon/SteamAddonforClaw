using SteamInputAddonforClaw.Controllers.Detection;

namespace SteamInputAddonforClaw.VirtualOutput.Viiper;

/// <summary>Steam Deck counterpart to <see cref="ViiperVirtualDeviceIdentityResolver"/>, using
/// <see cref="SteamDeckVirtualDeviceIdentityPolicy"/> (28DE:1205) instead of Gordon's policy.</summary>
internal sealed class SteamDeckVirtualDeviceIdentityResolver(SteamDeckVirtualDeviceIdentityPolicy policy)
{
    internal SteamDeckVirtualDeviceIdentityPolicy Policy => policy;

    internal ViiperVirtualDeviceResolution Resolve(IReadOnlyList<ControllerDeviceInfo> before, IReadOnlyList<ControllerDeviceInfo> after)
    {
        var previous = before.Select(device => device.InstanceId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var currentByInstanceId = SteamDeckVirtualDeviceIdentityPolicy.BuildInstanceIndex(after);
        var delta = after.Where(device => policy.IsMatchingCandidate(device, currentByInstanceId) && !previous.Contains(device.InstanceId)).ToArray();
        var groups = delta.GroupBy(GetLogicalKey, StringComparer.OrdinalIgnoreCase).ToArray();
        if (groups.Length == 0) return new(ViiperVirtualDeviceResolutionStatus.NoNewCandidate, [], "VirtualDeviceDidNotAppear");
        if (groups.Length > 1) return new(ViiperVirtualDeviceResolutionStatus.Ambiguous, [], "AmbiguousVirtualDeviceIdentity");
        return new(ViiperVirtualDeviceResolutionStatus.Resolved, groups[0].ToArray(), "VirtualDeviceIdentityResolved");
    }

    private static string GetLogicalKey(ControllerDeviceInfo device) => ControllerLogicalIdentity.GetLogicalKey(device);
}
