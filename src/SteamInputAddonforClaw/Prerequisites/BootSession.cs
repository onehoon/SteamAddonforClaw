using System.Diagnostics.Eventing.Reader;
using System.Globalization;
using System.Xml.Linq;

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
    private const string A2vmRumbleAttemptMarkerPrefix = "v1:";
    private const string WindowsBootStartEventQuery =
        "*[System[Provider[@Name='Microsoft-Windows-Kernel-Boot'] and EventID=27] and EventData[Data[@Name='BootType']='0' or Data[@Name='BootType']='1' or Data[@Name='BootType']='0x0' or Data[@Name='BootType']='0x1']]";

    internal static bool HasChangedSince(DateTimeOffset startedAtUtc)
    {
        var bootAtUtc = DateTimeOffset.UtcNow - TimeSpan.FromMilliseconds(Environment.TickCount64);
        return bootAtUtc > startedAtUtc;
    }

    internal static BootSessionAttemptResult TryClaimA2vmRumbleAttempt() =>
        TryClaimA2vmRumbleAttempt(GetA2vmRumbleAttemptPath(), GetCurrentWindowsStartId());

    internal static BootSessionAttemptResult TryClaimA2vmRumbleAttempt(
        string? markerPath,
        string? currentWindowsStartId)
    {
        if (string.IsNullOrWhiteSpace(markerPath)
            || !IsValidWindowsStartId(currentWindowsStartId))
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
                WriteAttemptIdentity(created, currentWindowsStartId!);
                return BootSessionAttemptResult.Claimed;
            }

            using var stream = new FileStream(markerPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            if (stream.Length is <= 0 or > MaximumA2vmRumbleAttemptMarkerLength)
                return BootSessionAttemptResult.Unavailable;
            using var reader = new StreamReader(stream, leaveOpen: true);
            var contents = reader.ReadToEnd();
            if (string.Equals(contents, currentWindowsStartId, StringComparison.Ordinal))
                return BootSessionAttemptResult.AlreadyClaimed;

            if (DateTimeOffset.TryParse(
                    contents,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind,
                    out _))
            {
                // A pre-v1 timestamp marker may have been written earlier in this same boot.
                // Conservatively consume this boot once while migrating it to the Windows start ID.
                stream.SetLength(0);
                stream.Position = 0;
                WriteAttemptIdentity(stream, currentWindowsStartId!);
                return BootSessionAttemptResult.AlreadyClaimed;
            }

            if (!IsValidWindowsStartId(contents))
                return BootSessionAttemptResult.Unavailable;

            stream.SetLength(0);
            stream.Position = 0;
            WriteAttemptIdentity(stream, currentWindowsStartId!);
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

    private static string? GetCurrentWindowsStartId()
    {
        try
        {
            var query = new EventLogQuery("System", PathType.LogName, WindowsBootStartEventQuery)
            {
                ReverseDirection = true,
            };
            using var reader = new EventLogReader(query);
            using var record = reader.ReadEvent();
            if (record?.RecordId is not long recordId || record.TimeCreated is not DateTime timeCreated)
                return null;

            var eventXml = XDocument.Parse(record.ToXml());
            var bootType = eventXml
                .Descendants()
                .FirstOrDefault(element =>
                    element.Name.LocalName == "Data"
                    && string.Equals((string?)element.Attribute("Name"), "BootType", StringComparison.Ordinal))
                ?.Value;
            return TryCreateWindowsStartId(recordId, timeCreated, bootType);
        }
        catch
        {
            // Event log access is optional for the A2VM boot-only cycle. Never infer a fresh boot.
            return null;
        }
    }

    internal static string? TryCreateWindowsStartId(long? recordId, DateTime? timeCreated, string? bootTypeValue)
    {
        if (recordId is not > 0 || timeCreated is null)
            return null;

        var bootType = ParseBootType(bootTypeValue);
        if (bootType is not (0 or 1))
            return null;

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{A2vmRumbleAttemptMarkerPrefix}{recordId.Value}:{bootType}:{timeCreated.Value.ToUniversalTime().Ticks}");
    }

    private static int? ParseBootType(string? value)
    {
        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var decimalValue))
            return decimalValue;

        if (value is { Length: > 2 }
            && value.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(value.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var hexValue))
            return hexValue;

        return null;
    }

    private static bool IsValidWindowsStartId(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > MaximumA2vmRumbleAttemptMarkerLength)
            return false;

        var parts = value.Split(':');
        return parts.Length == 4
            && string.Equals(parts[0], A2vmRumbleAttemptMarkerPrefix.TrimEnd(':'), StringComparison.Ordinal)
            && long.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var recordId)
            && recordId > 0
            && int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var bootType)
            && bootType is 0 or 1
            && long.TryParse(parts[3], NumberStyles.None, CultureInfo.InvariantCulture, out var utcTicks)
            && utcTicks > DateTime.MinValue.Ticks
            && utcTicks <= DateTime.MaxValue.Ticks;
    }

    private static void WriteAttemptIdentity(Stream stream, string windowsStartId)
    {
        using var writer = new StreamWriter(stream, leaveOpen: true);
        writer.Write(windowsStartId);
        writer.Flush();
        if (stream is FileStream fileStream)
            fileStream.Flush(flushToDisk: true);
    }
}
