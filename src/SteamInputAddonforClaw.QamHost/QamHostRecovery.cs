namespace SteamInputAddonforClaw.QamHost;

internal static class QamHostRecovery
{
    internal static DateTimeOffset? BeginAfterSessionFailure(bool managed, DateTimeOffset? currentDeadline, DateTimeOffset now, TimeSpan window) =>
        managed ? currentDeadline ?? now.Add(window) : null;

    internal static bool IsOpen(DateTimeOffset now, DateTimeOffset? deadline) =>
        deadline is null || now < deadline.Value;
}
