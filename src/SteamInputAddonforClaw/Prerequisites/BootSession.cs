using System.Globalization;

namespace SteamInputAddonforClaw.Prerequisites;

internal enum BootSessionAttemptResult
{
    Claimed,
    AlreadyClaimed,
    Unavailable,
}

internal static class BootSession
{
    private const long MaximumA2vmRumbleAttemptMarkerLength = 128;

    internal static bool HasChangedSince(DateTimeOffset startedAtUtc)
    {
        var bootAtUtc = DateTimeOffset.UtcNow - TimeSpan.FromMilliseconds(Environment.TickCount64);
        return bootAtUtc > startedAtUtc;
    }

    internal static BootSessionAttemptResult TryClaimA2vmRumbleAttempt() =>
        TryClaimA2vmRumbleAttempt(GetA2vmRumbleAttemptPath(), DateTimeOffset.UtcNow, Environment.TickCount64);

    internal static BootSessionAttemptResult TryClaimA2vmRumbleAttempt(
        string? markerPath,
        DateTimeOffset nowUtc,
        long tickCount64)
    {
        if (string.IsNullOrWhiteSpace(markerPath) || tickCount64 < 0)
            return BootSessionAttemptResult.Unavailable;

        try
        {
            var directory = Path.GetDirectoryName(markerPath);
            if (string.IsNullOrWhiteSpace(directory))
                return BootSessionAttemptResult.Unavailable;

            Directory.CreateDirectory(directory);
            if (!File.Exists(markerPath))
            {
                using var created = new FileStream(markerPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                WriteAttemptTimestamp(created, nowUtc);
                return BootSessionAttemptResult.Claimed;
            }

            using var stream = new FileStream(markerPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            if (stream.Length is <= 0 or > MaximumA2vmRumbleAttemptMarkerLength)
                return BootSessionAttemptResult.Unavailable;
            using var reader = new StreamReader(stream, leaveOpen: true);
            var contents = reader.ReadToEnd();
            if (!DateTimeOffset.TryParse(
                    contents,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind,
                    out var previousAttemptUtc))
                return BootSessionAttemptResult.Unavailable;

            var bootAtUtc = nowUtc - TimeSpan.FromMilliseconds(tickCount64);
            if (bootAtUtc <= previousAttemptUtc)
                return BootSessionAttemptResult.AlreadyClaimed;

            stream.SetLength(0);
            stream.Position = 0;
            WriteAttemptTimestamp(stream, nowUtc);
            return BootSessionAttemptResult.Claimed;
        }
        catch
        {
            // This marker gates only the optional boot-time A2VM cycle. A local I/O failure must
            // never block ordinary Full1902 ownership or be treated as a fresh boot.
            return BootSessionAttemptResult.Unavailable;
        }
    }

    private static string? GetA2vmRumbleAttemptPath()
    {
        var localData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return string.IsNullOrWhiteSpace(localData)
            ? null
            : Path.Combine(localData, "SteamInputAddonforClaw", "State", "a2vm-rumble-boot-attempt.txt");
    }

    private static void WriteAttemptTimestamp(Stream stream, DateTimeOffset nowUtc)
    {
        using var writer = new StreamWriter(stream, leaveOpen: true);
        writer.Write(nowUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
        writer.Flush();
        if (stream is FileStream fileStream)
            fileStream.Flush(flushToDisk: true);
    }
}
