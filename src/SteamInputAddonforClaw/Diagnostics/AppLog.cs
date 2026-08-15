using System.Globalization;
using System.Text.RegularExpressions;

namespace SteamInputAddonforClaw.Diagnostics;

// Off is a threshold sentinel, never a level a message is logged at: it sorts above Fatal so that
// MinimumLevelOverride = Off filters every message, including Fatal.
internal enum AppLogLevel { Debug, Info, Warn, Error, Fatal, Off }

internal static class AppLog
{
    internal const int RetentionDays = 7;
    internal const long MaximumLogDirectoryBytes = 100L * 1024 * 1024;

    // Bounded so a logging burst cannot grow memory without limit. Sized generously relative to the
    // application's actual log volume (startup bursts and occasional diagnostic bursts, not a per-tick
    // firehose) so drops are rare in normal operation.
    private const int QueueCapacity = 2048;

    private static readonly string LaunchId = Guid.NewGuid().ToString("N")[..10];
    private static readonly DateTimeOffset LaunchTimestamp = DateTimeOffset.Now;
    private static readonly string LaunchFileName = AppLogFileName.Create(LaunchTimestamp, Environment.ProcessId, LaunchId);
    private static readonly string DefaultDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SteamInputAddonforClaw", "logs");

    // Only the background writer thread and ResetMaintenanceStateForTests touch this state, never the
    // logging call sites, so a small dedicated lock (instead of the old call-site-wide lock) is enough.
    private static readonly Lock MaintenanceSync = new();
    private static readonly Dictionary<string, DateTime> LastPrunedDates = new(StringComparer.OrdinalIgnoreCase);

    // Single background writer for the active log file. Call sites only ever enqueue; see
    // BufferedEntryWriter for the bounded-queue/drop/drain/shutdown policy.
    private static readonly BufferedEntryWriter<LogEntry> Writer = new(QueueCapacity, ProcessEntry, static entry => entry.Level >= AppLogLevel.Warn);
    private static string? _openDirectory;
    private static StreamWriter? _openWriter;

    internal static string? DirectoryOverride { get; set; }
    // Logging ships disabled by default; the user opts into Info (M5/runtime diagnostics) or Debug
    // (verbose) explicitly. See AppLogPreference/AppSettingsPolicy for the persisted user setting.
    private static int _minimumLevel = (int)AppLogLevel.Off;
    internal static AppLogLevel MinimumLevelOverride { get => (AppLogLevel)Volatile.Read(ref _minimumLevel); set => Volatile.Write(ref _minimumLevel, (int)value); }

    /// <summary>
    /// Cheap pre-check for call sites that would otherwise do non-trivial work (timestamp sampling,
    /// state comparisons, etc.) purely to prepare a log entry that will never be enqueued when disabled.
    /// Ordinary AppLog.Debug/Info/... calls do not need this: they already return before any formatting
    /// work when the level is filtered.
    /// </summary>
    internal static bool IsEnabled(AppLogLevel level) => level >= MinimumLevelOverride;
    internal static string DirectoryPath => DirectoryOverride ?? DefaultDirectory;
    internal static string CurrentLogFileName => LaunchFileName;
    internal static string CurrentLogFilePath => Path.Combine(DirectoryPath, LaunchFileName);
    internal static Func<DateTime> LocalDateProvider { get; set; } = static () => DateTime.Now.Date;
    internal static int PruneInvocationCount { get; private set; }

    /// <summary>Count of Debug/Info entries dropped because the queue was saturated.</summary>
    internal static long DroppedLowPriorityLogEntryCount => Writer.DroppedLowPriorityCount;

    /// <summary>Count of Warn/Error/Fatal entries dropped after retrying because the queue stayed saturated.</summary>
    internal static long DroppedHighPriorityLogEntryCount => Writer.DroppedHighPriorityCount;

    public static void Debug(string category, string message, params (string Key, object? Value)[] fields) => Write(AppLogLevel.Debug, category, message, null, fields);
    public static void Debug(string message) => Debug("App", message);
    public static void Info(string message) => Info("App", message);
    public static void Info(string category, string message, params (string Key, object? Value)[] fields) => Write(AppLogLevel.Info, category, message, null, fields);
    public static void Warn(string category, string message, Exception? exception = null, params (string Key, object? Value)[] fields) => Write(AppLogLevel.Warn, category, message, exception, fields);
    public static void Error(string message, Exception exception) => Error("App", message, exception);
    public static void Error(string category, string message, Exception exception, params (string Key, object? Value)[] fields) => Write(AppLogLevel.Error, category, message, exception, fields);
    public static void Fatal(string category, string message, Exception exception) => Write(AppLogLevel.Fatal, category, message, exception, []);

    private static void Write(AppLogLevel level, string category, string message, Exception? exception, IReadOnlyList<(string Key, object? Value)> fields)
    {
        if (level < MinimumLevelOverride) return;
        try
        {
            // Everything that can vary between "now" and "whenever the background writer gets to this
            // entry" (timestamp, field values, exception text, and which directory is currently active)
            // is resolved into a plain string on the caller's thread. The writer only ever sees an
            // immutable, already-formatted line, so ordering and content are identical to the old
            // synchronous behavior even though the actual disk write happens later.
            var now = DateTimeOffset.Now;
            var structured = string.Join(' ', fields.Select(field => $"{field.Key}={Format(field.Value)}"));
            var details = exception is null ? string.Empty : $"{Environment.NewLine}{exception}";
            var line = $"{now:O} [{level.ToString().ToUpperInvariant()}] [P{Environment.ProcessId}] [T{Environment.CurrentManagedThreadId}] [L={LaunchId}] [{category}] {message}{(structured.Length == 0 ? string.Empty : " " + structured)}{details}{Environment.NewLine}";
            Writer.Enqueue(new LogEntry(level, DirectoryPath, line));
        }
        catch { }
    }

    // Runs on the single background writer thread only.
    private static void ProcessEntry(LogEntry entry)
    {
        if (!string.Equals(_openDirectory, entry.Directory, StringComparison.Ordinal))
        {
            _openWriter?.Dispose();
            _openWriter = null;
            Directory.CreateDirectory(entry.Directory);
            _openWriter = new StreamWriter(Path.Combine(entry.Directory, LaunchFileName), append: true) { AutoFlush = false };
            _openDirectory = entry.Directory;
        }

        MaybePrune(entry.Directory);

        _openWriter!.Write(entry.Line);
        _openWriter.Flush();
    }

    private static void MaybePrune(string directory)
    {
        var today = LocalDateProvider().Date;
        lock (MaintenanceSync)
        {
            if (LastPrunedDates.TryGetValue(directory, out var lastPrunedDate) && lastPrunedDate == today) return;
            Prune(directory, today);
            LastPrunedDates[directory] = today;
        }
    }

    private static void Prune(string directory, DateTime today)
    {
        PruneInvocationCount++;
        var activePath = Path.GetFullPath(Path.Combine(directory, LaunchFileName));
        var files = Directory.EnumerateFiles(directory, "SteamInputAddonforClaw-*.log")
            .Select(path => new FileInfo(path))
            .Where(file => AppLogFileName.TryGetLaunchDate(file.Name, out _))
            .OrderBy(file => file.LastWriteTimeUtc).ToList();
        foreach (var file in files)
        {
            if (string.Equals(Path.GetFullPath(file.FullName), activePath, StringComparison.OrdinalIgnoreCase)) continue;
            if (AppLogFileName.TryGetLaunchDate(file.Name, out var date) && date < today.AddDays(-(RetentionDays - 1))) file.Delete();
        }
        var retained = files.Where(file => file.Exists).OrderBy(file => file.LastWriteTimeUtc).ToList();
        var total = retained.Sum(file => file.Length);
        foreach (var file in retained)
        {
            if (total <= MaximumLogDirectoryBytes) break;
            if (string.Equals(Path.GetFullPath(file.FullName), activePath, StringComparison.OrdinalIgnoreCase)) continue;
            total -= file.Length;
            file.Delete();
        }
    }

    /// <summary>
    /// Blocks the calling thread until every entry enqueued before this call has been written and
    /// flushed by the background writer, then closes the writer's file handle. Intended for tests only,
    /// so they can assert on file content (or delete the log directory) deterministically without an
    /// arbitrary delay or a lingering open handle. The next logged entry reopens the file lazily, exactly
    /// like a directory switch does.
    /// </summary>
    internal static void DrainForTests(TimeSpan? timeout = null) => Writer.RunOnWriterThreadForTests(() =>
    {
        _openWriter?.Dispose();
        _openWriter = null;
        _openDirectory = null;
    }, timeout);

    /// <summary>
    /// Reads a test log on the single writer thread after flushing the active writer. This avoids a
    /// test opening the same file concurrently with a late background log entry after DrainForTests.
    /// </summary>
    internal static string ReadAllTextForTests(string path) => ReadFileForTests(path, File.ReadAllText);

    internal static string[] ReadAllLinesForTests(string path) => ReadFileForTests(path, File.ReadAllLines);

    private static T ReadFileForTests<T>(string path, Func<string, T> read)
    {
        T? result = default;
        Writer.RunOnWriterThreadForTests(() =>
        {
            _openWriter?.Flush();
            result = read(path);
        });
        return result!;
    }

    /// <summary>
    /// Stops accepting new entries and waits (bounded by <paramref name="timeout"/>) for queued entries
    /// to be written before returning. Intended to be called exactly once, from the application's real
    /// shutdown path (see App.xaml.cs OnMainWindowClosed). Never blocks application exit indefinitely.
    /// </summary>
    internal static void Shutdown(TimeSpan? timeout = null)
    {
        var writerStopped = Writer.Shutdown(timeout);
        // Only safe to touch writer-owned state once the writer loop is confirmed to have exited; if the
        // timeout elapsed first the writer may still be mid-write, and closing out from under it would
        // race. Leaking the handle in that rare case is harmless: the process is exiting anyway, and
        // every entry was already flushed to disk individually as it was written.
        if (writerStopped)
        {
            _openWriter?.Dispose();
            _openWriter = null;
            _openDirectory = null;
        }
    }

    internal static void ResetMaintenanceStateForTests()
    {
        lock (MaintenanceSync)
        {
            LastPrunedDates.Clear();
            PruneInvocationCount = 0;
            LocalDateProvider = static () => DateTime.Now.Date;
        }
    }

    private static string Format(object? value)
    {
        if (value is null) return "null";
        var text = value.ToString()!.Replace("\r", " ").Replace("\n", " ");
        return text.IndexOfAny([' ', '=']) >= 0 ? $"\"{text.Replace("\"", "\\\"")}\"" : text;
    }

    private readonly record struct LogEntry(AppLogLevel Level, string Directory, string Line);
}

internal static class AppLogFileName
{
    private const string Prefix = "SteamInputAddonforClaw-";
    private static readonly Regex Pattern = new($"^{Regex.Escape(Prefix)}(?<date>\\d{{4}}-\\d{{2}}-\\d{{2}})(?:-\\d{{6}}\\.\\d{{3}}-P\\d+-L[A-Za-z0-9]+)?\\.log$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    internal static string Create(DateTimeOffset timestamp, int processId, string launchId) => $"{Prefix}{timestamp:yyyy-MM-dd-HHmmss.fff}-P{processId}-L{launchId}.log";

    internal static bool TryGetLaunchDate(string fileName, out DateTime date)
    {
        date = default;
        var match = Pattern.Match(fileName);
        return match.Success && DateTime.TryParseExact(match.Groups["date"].Value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
    }
}
