using SteamInputAddonforClaw.Controllers.Detection;

namespace SteamInputAddonforClaw.VirtualOutput.Viiper;

/// <summary>Resolves which newly-appeared device(s) are the Addon's own Steam Deck virtual device,
/// using <see cref="SteamDeckVirtualDeviceIdentityPolicy"/> (exact identity <c>28DE:1205</c>) to tell
/// the Addon's own virtual device apart from unrelated hardware changes.</summary>
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
