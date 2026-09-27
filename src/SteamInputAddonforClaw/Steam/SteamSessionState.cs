namespace SteamInputAddonforClaw.Steam;

public enum SteamSessionSource
{
    Actual,
    BigPicture
}

public sealed record SteamSessionState(bool IsActive, uint RunningAppId, SteamSessionSource Source)
{
    public static SteamSessionState FromRunningAppId(uint runningAppId) => new(runningAppId != 0, runningAppId, SteamSessionSource.Actual);

    internal static SteamSessionState CreateBigPicture() => new(true, 0, SteamSessionSource.BigPicture);

    public bool HasSameIdentity(SteamSessionState other) =>
        IsActive == other.IsActive && RunningAppId == other.RunningAppId && Source == other.Source;
}
