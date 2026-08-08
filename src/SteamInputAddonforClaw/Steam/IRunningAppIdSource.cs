namespace SteamInputAddonforClaw.Steam;

public interface IRunningAppIdSource
{
    uint GetRunningAppId();

    event EventHandler? Changed;
}
