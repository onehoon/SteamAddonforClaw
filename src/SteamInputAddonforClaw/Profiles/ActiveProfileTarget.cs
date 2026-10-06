using System.Globalization;

namespace SteamInputAddonforClaw.Profiles;

internal enum ActiveProfileTargetKind
{
    None,
    Steam,
    Xbox
}

internal readonly record struct ActiveProfileTarget(
    ActiveProfileTargetKind Kind,
    uint SteamAppId,
    string? XboxGameKey)
{
    internal static ActiveProfileTarget None => new(ActiveProfileTargetKind.None, 0, null);

    internal static ActiveProfileTarget ForSteam(uint appId) => appId == 0
        ? None
        : new(ActiveProfileTargetKind.Steam, appId, null);

    internal static ActiveProfileTarget ForXbox(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        return new(ActiveProfileTargetKind.Xbox, 0, key);
    }

    internal string LogLabel => Kind switch
    {
        ActiveProfileTargetKind.Steam => $"Steam:{SteamAppId.ToString(CultureInfo.InvariantCulture)}",
        ActiveProfileTargetKind.Xbox => $"Xbox:{XboxGameKey}",
        _ => "None"
    };
}
