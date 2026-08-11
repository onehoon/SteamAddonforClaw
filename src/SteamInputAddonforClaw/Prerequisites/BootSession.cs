namespace SteamInputAddonforClaw.Prerequisites;

internal static class BootSession
{
    internal static bool HasChangedSince(DateTimeOffset startedAtUtc)
    {
        var bootAtUtc = DateTimeOffset.UtcNow - TimeSpan.FromMilliseconds(Environment.TickCount64);
        return bootAtUtc > startedAtUtc;
    }
}
