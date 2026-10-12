using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TouchpadPoc;

internal readonly record struct PixelRect(int X, int Y, int Width, int Height)
{
    [JsonIgnore]
    public int Right => checked(X + Width);

    [JsonIgnore]
    public int Bottom => checked(Y + Height);

    public bool Contains(int x, int y) =>
        Width > 0 && Height > 0 && x >= X && y >= Y && x < Right && y < Bottom;

    public static PixelRect BottomRightQuarter(PixelRect primaryBounds)
    {
        if (primaryBounds.Width <= 0 || primaryBounds.Height <= 0)
            throw new ArgumentOutOfRangeException(nameof(primaryBounds), "Primary monitor bounds must be non-empty.");

        var leftWidth = primaryBounds.Width / 2;
        var topHeight = primaryBounds.Height / 2;
        return new PixelRect(
            checked(primaryBounds.X + leftWidth),
            checked(primaryBounds.Y + topHeight),
            primaryBounds.Width - leftWidth,
            primaryBounds.Height - topHeight);
    }

    public PixelPoint ToLocal(int screenX, int screenY) =>
        new(checked(screenX - X), checked(screenY - Y));
}

internal readonly record struct PixelPoint(int X, int Y);

internal sealed class PointerContactTracker
{
    private readonly Dictionary<uint, bool> _contacts = [];

    public int Count => _contacts.Count;
    public int AcquiredCount => _contacts.Count(static contact => contact.Value);

    public bool IsKnown(uint pointerId) => _contacts.ContainsKey(pointerId);

    public bool IsAcquired(uint pointerId) =>
        _contacts.TryGetValue(pointerId, out var acquired) && acquired;

    public bool Begin(uint pointerId, bool startsInside, out bool acquired)
    {
        if (_contacts.TryGetValue(pointerId, out acquired))
            return false;

        acquired = startsInside;
        _contacts.Add(pointerId, acquired);
        return true;
    }

    public bool End(uint pointerId, out bool acquired) => _contacts.Remove(pointerId, out acquired);

    public IReadOnlyList<(uint PointerId, bool Acquired)> Clear()
    {
        var contacts = _contacts.Select(static contact => (contact.Key, contact.Value)).ToArray();
        _contacts.Clear();
        return contacts;
    }
}

internal sealed class PointerEventCounts
{
    public long Down { get; private set; }
    public long Move { get; private set; }
    public long Up { get; private set; }
    public long CaptureLost { get; private set; }
    public long Cancel { get; private set; }

    public void AddDown() => Down++;
    public void AddMove() => Move++;
    public void AddUp() => Up++;
    public void AddCaptureLost() => CaptureLost++;
    public void AddCancel() => Cancel++;

    public string Summary =>
        $"Down={Down}, Move={Move}, Up={Up}, CaptureLost={CaptureLost}, Cancel={Cancel}";
}

internal sealed record PocLogEvent
{
    public long Sequence { get; init; }
    public DateTimeOffset Timestamp { get; init; }
    public double ElapsedMilliseconds { get; init; }
    public required string Event { get; init; }
    public string? Phase { get; init; }
    public int? ProcessId { get; init; }
    public string? ToolVersion { get; init; }
    public string? OsVersion { get; init; }
    public string? DpiAwareness { get; init; }
    public bool? DpiAwarenessSet { get; init; }
    public uint? WindowDpi { get; init; }
    public string? WindowDpiScale { get; init; }
    public int? SessionDurationSeconds { get; init; }
    public PixelRect? PrimaryBounds { get; init; }
    public PixelRect? RegionRect { get; init; }
    public PixelRect? ActualWindowRect { get; init; }
    public string? Hwnd { get; init; }
    public string? WindowStyle { get; init; }
    public string? ActualWindowStyle { get; init; }
    public string? RequestedExtendedStyle { get; init; }
    public string? ActualExtendedStyle { get; init; }
    public uint? PointerId { get; init; }
    public string? PointerType { get; init; }
    public string? Flags { get; init; }
    public int? ScreenX { get; init; }
    public int? ScreenY { get; init; }
    public int? RawScreenX { get; init; }
    public int? RawScreenY { get; init; }
    public int? LocalX { get; init; }
    public int? LocalY { get; init; }
    public int? RawLocalX { get; init; }
    public int? RawLocalY { get; init; }
    public bool? PrimaryPointer { get; init; }
    public bool? TargetWindowIsThisWindow { get; init; }
    public bool? Tracked { get; init; }
    public int? ActivePointers { get; init; }
    public string? Reason { get; init; }
    public string? Message { get; init; }
    public int? Win32Error { get; init; }
    public string? EventCounts { get; init; }
}

internal static class PocLogFormatter
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static string Format(PocLogEvent logEvent) =>
        JsonSerializer.Serialize(logEvent, Options);
}

internal sealed class SessionLog : IDisposable
{
    private readonly StreamWriter _writer;
    private readonly long _startedAt = Stopwatch.GetTimestamp();
    private readonly object _sync = new();
    private long _sequence;
    private bool _disposed;

    private SessionLog(StreamWriter writer, string path)
    {
        _writer = writer;
        Path = path;
    }

    public string Path { get; }

    public static SessionLog Create(out string path)
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(localAppData) || !System.IO.Path.IsPathFullyQualified(localAppData))
            throw new IOException("The current user's local application-data directory is not an absolute path.");

        var directory = System.IO.Path.Combine(
            localAppData,
            "SteamInputAddonforClaw-Data",
            "logs",
            "TouchpadPoc");
        Directory.CreateDirectory(directory);

        path = System.IO.Path.Combine(
            directory,
            $"touchpad-poc-{DateTimeOffset.Now:yyyyMMdd-HHmmss-fff}-P{Environment.ProcessId}.log");

        try
        {
            var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
            var writer = new StreamWriter(
                stream,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                bufferSize: 64 * 1024);
            return new SessionLog(writer, path);
        }
        catch (Exception exception)
        {
            throw new IOException($"Could not create the session log '{path}'.", exception);
        }
    }

    public void Write(PocLogEvent logEvent, bool flush = false)
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            var completedEvent = logEvent with
            {
                Sequence = ++_sequence,
                Timestamp = DateTimeOffset.Now,
                ElapsedMilliseconds = Stopwatch.GetElapsedTime(_startedAt).TotalMilliseconds
            };

            _writer.WriteLine(PocLogFormatter.Format(completedEvent));
            if (flush)
                _writer.Flush();
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed)
                return;

            _disposed = true;
            try
            {
                _writer.Flush();
            }
            finally
            {
                _writer.Dispose();
            }
        }
    }
}
