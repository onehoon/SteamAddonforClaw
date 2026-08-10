namespace SteamInputAddonforClaw.Steam;

public enum SteamSessionSource
{
    Actual,
    DeveloperTest
}

public sealed record SteamSessionState(bool IsActive, uint RunningAppId, SteamSessionSource Source)
{
    public static SteamSessionState FromRunningAppId(uint runningAppId) => new(runningAppId != 0, runningAppId, SteamSessionSource.Actual);

    internal static SteamSessionState CreateDeveloperTest() => new(true, uint.MaxValue, SteamSessionSource.DeveloperTest);

    public bool HasSameIdentity(SteamSessionState other) =>
        IsActive == other.IsActive && RunningAppId == other.RunningAppId && Source == other.Source;
}
