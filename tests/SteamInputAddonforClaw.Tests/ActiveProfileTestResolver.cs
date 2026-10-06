using SteamInputAddonforClaw.Profiles;

namespace SteamInputAddonforClaw.Tests;

internal static class ActiveProfileTestResolver
{
    internal static Func<ProfileDocument, ResolvedActiveProfile?> ForSteam(Func<uint> appIdSource) =>
        document => ActiveProfileResolver.Resolve(ActiveProfileTarget.ForSteam(appIdSource()), document);

    internal static Func<ProfileDocument, ResolvedActiveProfile?> ForXbox(Func<string?> gameKeySource) =>
        document => ActiveProfileResolver.Resolve(
            gameKeySource() is { } key ? ActiveProfileTarget.ForXbox(key) : ActiveProfileTarget.None,
            document);
}
