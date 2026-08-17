namespace SteamInputAddonforClaw.UI.Diagnostics;

internal static class UiLog
{
    private static readonly object Sync = new();
    private static readonly string Directory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SteamInputAddonforClaw",
        "logs");
    private static readonly string FilePath = Path.Combine(Directory, $"ui-{Environment.ProcessId}.log");

    internal static string DirectoryPath => Directory;

    internal static void Info(string message) => Write("INFO", "App", message, null, []);

    internal static void Info(string category, string message, params (string Key, object? Value)[] fields) =>
        Write("INFO", category, message, null, fields);

    internal static void Warn(string category, string message, Exception? exception = null, params (string Key, object? Value)[] fields) =>
        Write("WARN", category, message, exception, fields);

    internal static void Error(string category, string message, Exception exception, params (string Key, object? Value)[] fields) =>
        Write("ERROR", category, message, exception, fields);

    private static void Write(string level, string category, string message, Exception? exception, IReadOnlyList<(string Key, object? Value)> fields)
    {
        try
        {
            var suffix = string.Join(' ', fields.Select(field => $"{field.Key}={field.Value}"));
            var line = $"{DateTimeOffset.Now:O} [{level}] [{category}] {message}{(suffix.Length == 0 ? string.Empty : " " + suffix)}{(exception is null ? string.Empty : Environment.NewLine + exception)}{Environment.NewLine}";
            lock (Sync)
            {
                System.IO.Directory.CreateDirectory(Directory);
                File.AppendAllText(FilePath, line);
            }
        }
        catch
        {
        }
    }
}
