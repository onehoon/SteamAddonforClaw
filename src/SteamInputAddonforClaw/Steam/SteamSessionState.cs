namespace SteamInputAddonforClaw.Steam;

public sealed record SteamSessionState(bool IsActive, uint RunningAppId)
{
    public static SteamSessionState FromRunningAppId(uint runningAppId) => new(runningAppId != 0, runningAppId);
}
