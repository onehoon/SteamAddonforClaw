namespace SteamInputAddonforClaw.QamHost;

internal static class QamHostRecovery
{
    internal static bool IsOpen(DateTimeOffset now, DateTimeOffset? deadline) =>
        deadline is null || now < deadline.Value;
}
