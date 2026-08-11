using System.Globalization;
using System.Text.RegularExpressions;

namespace SteamInputAddonforClaw.Diagnostics;

internal enum AppLogLevel { Debug, Info, Warn, Error, Fatal }

internal static class AppLog
{
    internal const int RetentionDays = 7;
    internal const long MaximumLogDirectoryBytes = 100L * 1024 * 1024;
    private static readonly Lock Sync = new();
    private static readonly string LaunchId = Guid.NewGuid().ToString("N")[..10];
    private static readonly DateTimeOffset LaunchTimestamp = DateTimeOffset.Now;
    private static readonly string LaunchFileName = AppLogFileName.Create(LaunchTimestamp, Environment.ProcessId, LaunchId);
    private static readonly string DefaultDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SteamInputAddonforClaw", "logs");
    private static readonly Dictionary<string, DateTime> LastPrunedDates = new(StringComparer.OrdinalIgnoreCase);
    internal static string? DirectoryOverride { get; set; }
    private static int _minimumLevel = (int)AppLogLevel.Info;
    internal static AppLogLevel MinimumLevelOverride { get => (AppLogLevel)Volatile.Read(ref _minimumLevel); set => Volatile.Write(ref _minimumLevel, (int)value); }
    internal static string DirectoryPath => DirectoryOverride ?? DefaultDirectory;
    internal static string CurrentLogFileName => LaunchFileName;
    internal static string CurrentLogFilePath => Path.Combine(DirectoryPath, LaunchFileName);
    internal static Func<DateTime> LocalDateProvider { get; set; } = static () => DateTime.Now.Date;
    internal static int PruneInvocationCount { get; private set; }

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
            lock (Sync)
            {
                var now = DateTimeOffset.Now;
                var directory = DirectoryPath;
                Directory.CreateDirectory(directory);
                var today = LocalDateProvider().Date;
                if (!LastPrunedDates.TryGetValue(directory, out var lastPrunedDate) || lastPrunedDate != today)
                {
                    Prune(directory, today);
                    LastPrunedDates[directory] = today;
                }
                var structured = string.Join(' ', fields.Select(field => $"{field.Key}={Format(field.Value)}"));
                var details = exception is null ? string.Empty : $"{Environment.NewLine}{exception}";
                File.AppendAllText(Path.Combine(directory, LaunchFileName), $"{now:O} [{level.ToString().ToUpperInvariant()}] [P{Environment.ProcessId}] [T{Environment.CurrentManagedThreadId}] [L={LaunchId}] [{category}] {message}{(structured.Length == 0 ? string.Empty : " " + structured)}{details}{Environment.NewLine}");
            }
        }
        catch { }
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

    internal static void ResetMaintenanceStateForTests()
    {
        lock (Sync)
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
