using System.Globalization;

namespace SteamInputAddonforClaw.Profiles;

internal readonly record struct ResolvedActiveProfile(
    string TargetLabel,
    GamePerformanceOverrides Performance,
    GameDisplayOverrides Display);

internal static class ActiveProfileResolver
{
    internal static ResolvedActiveProfile? Resolve(ActiveProfileTarget target, ProfileDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        return target.Kind switch
        {
            ActiveProfileTargetKind.Steam when document.Games.TryGetValue(
                    target.SteamAppId.ToString(CultureInfo.InvariantCulture), out var steamProfile)
                && steamProfile.Enabled
                => new(target.LogLabel, steamProfile.Performance, steamProfile.Display),
            ActiveProfileTargetKind.Xbox when target.XboxGameKey is { Length: > 0 } key
                    && document.XboxGames.TryGetValue(key, out var xboxProfile)
                    && xboxProfile.Enabled
                => new(target.LogLabel, xboxProfile.Performance, xboxProfile.Display),
            _ => null
        };
    }
}
