using System.Globalization;

namespace SteamInputAddonforClaw.Diagnostics;

internal enum AppLogLevel { Trace, Debug, Info, Warn, Error, Fatal }

internal static class AppLog
{
    internal const int RetentionDays = 7;
    internal const long MaximumLogDirectoryBytes = 100L * 1024 * 1024;
    private static readonly Lock Sync = new();
    private static readonly string LaunchId = Guid.NewGuid().ToString("N")[..10];
    private static readonly string DefaultDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SteamInputAddonforClaw", "logs");
    internal static string? DirectoryOverride { get; set; }
    internal static AppLogLevel MinimumLevelOverride { get; set; } = AppLogLevel.Trace;

    public static void Trace(string category, string message, params (string Key, object? Value)[] fields) => Write(AppLogLevel.Trace, category, message, null, fields);
    public static void Debug(string category, string message, params (string Key, object? Value)[] fields) => Write(AppLogLevel.Debug, category, message, null, fields);
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
                var directory = DirectoryOverride ?? DefaultDirectory;
                Directory.CreateDirectory(directory);
                Prune(directory, now.Date);
                var structured = string.Join(' ', fields.Select(field => $"{field.Key}={Format(field.Value)}"));
                var details = exception is null ? string.Empty : $"{Environment.NewLine}{exception}";
                File.AppendAllText(Path.Combine(directory, $"SteamInputAddonforClaw-{now:yyyy-MM-dd}.log"), $"{now:O} [{level.ToString().ToUpperInvariant()}] [P{Environment.ProcessId}] [T{Environment.CurrentManagedThreadId}] [L={LaunchId}] [{category}] {message}{(structured.Length == 0 ? string.Empty : " " + structured)}{details}{Environment.NewLine}");
            }
        }
        catch { }
    }

    private static void Prune(string directory, DateTime today)
    {
        var files = Directory.EnumerateFiles(directory, "SteamInputAddonforClaw-*.log").Select(path => new FileInfo(path)).OrderBy(file => file.LastWriteTimeUtc).ToList();
        foreach (var file in files)
        {
            var dateText = Path.GetFileNameWithoutExtension(file.Name).Replace("SteamInputAddonforClaw-", string.Empty);
            if (DateTime.TryParseExact(dateText, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) && date < today.AddDays(-(RetentionDays - 1))) file.Delete();
        }
        var retained = files.Where(file => file.Exists).OrderBy(file => file.LastWriteTimeUtc).ToList();
        var total = retained.Sum(file => file.Length);
        foreach (var file in retained)
        {
            if (total <= MaximumLogDirectoryBytes) break;
            total -= file.Length;
            file.Delete();
        }
    }

    private static string Format(object? value) => value is null ? "null" : value.ToString()!.Replace("\r", " ").Replace("\n", " ");
}
