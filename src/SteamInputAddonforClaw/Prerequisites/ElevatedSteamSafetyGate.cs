using SteamInputAddonforClaw.Settings;
using SteamInputAddonforClaw.Steam;

namespace SteamInputAddonforClaw.Prerequisites;

internal static class ElevatedSteamSafetyGate
{
    internal static (bool Allowed, string Reason) Evaluate(
        Func<uint> runningAppIdReader,
        Func<SettingsLoadResult> settingsReader,
        Func<SteamBigPictureProbeResult> bigPictureProbe)
    {
        uint runningAppId;
        try { runningAppId = runningAppIdReader(); }
        catch { return (false, "RunningAppIdUnavailable"); }
        if (runningAppId != 0) return (false, "SteamSessionActive");

        SettingsLoadResult settings;
        try { settings = settingsReader(); }
        catch { return (false, "SettingsUnreliable"); }
        if (!settings.IsReliable) return (false, "SettingsUnreliable");
        if (!settings.Settings.RouteInSteamBigPicture) return (true, "Allowed");

        SteamBigPictureProbeResult probe;
        try { probe = bigPictureProbe(); }
        catch { return (false, "BigPictureProbeFailed"); }
        if (!probe.IsReliable) return (false, "BigPictureProbeFailed");
        return probe.IsActive ? (false, "SteamSessionActive") : (true, "Allowed");
    }
}
