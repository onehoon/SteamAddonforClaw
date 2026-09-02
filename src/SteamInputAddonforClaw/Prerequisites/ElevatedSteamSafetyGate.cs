using SteamInputAddonforClaw.Steam;

namespace SteamInputAddonforClaw.Prerequisites;

internal static class ElevatedSteamSafetyGate
{
    internal static (bool Allowed, string Reason) Evaluate(
        Func<uint> runningAppIdReader,
        Func<SteamBigPictureProbeResult> bigPictureProbe)
    {
        uint runningAppId;
        try { runningAppId = runningAppIdReader(); }
        catch { return (false, "RunningAppIdUnavailable"); }
        if (runningAppId != 0) return (false, "SteamSessionActive");

        // Full1902 removed the user-configurable Steam Input routing switch: an active Big Picture
        // session is always a real Steam session and prerequisite mutation must never run through it.
        SteamBigPictureProbeResult probe;
        try { probe = bigPictureProbe(); }
        catch { return (false, "BigPictureProbeFailed"); }
        if (!probe.IsReliable) return (false, "BigPictureProbeFailed");
        return probe.IsActive ? (false, "SteamSessionActive") : (true, "Allowed");
    }
}
