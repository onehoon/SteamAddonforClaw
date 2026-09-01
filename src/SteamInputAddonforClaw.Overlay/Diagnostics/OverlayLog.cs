using SteamInputAddonforClaw.FrontendTransport;

namespace SteamInputAddonforClaw.Overlay.Diagnostics;

internal static class OverlayLog
{
    private static readonly object Sync = new();
    private static string _directory = FrontendLaunchArguments.ResolveLogDirectory([]);

    internal static string DirectoryPath => _directory;

    internal static void ConfigureDirectory(string[] args)
    {
        var directory = FrontendLaunchArguments.ResolveLogDirectory(args);
        if (Path.IsPathFullyQualified(directory)) _directory = directory;
    }

    internal static void Info(string category, string message, params (string Key, object? Value)[] fields) =>
        Write("INFO", category, message, null, fields);

    internal static void Debug(string category, string message, params (string Key, object? Value)[] fields) =>
        Write("DEBUG", category, message, null, fields);

    internal static void Warn(string category, string message, Exception? exception = null, params (string Key, object? Value)[] fields) =>
        Write("WARN", category, message, exception, fields);

    internal static void Error(string category, string message, Exception exception, params (string Key, object? Value)[] fields) =>
        Write("ERROR", category, message, exception, fields);

    private static void Write(string level, string category, string message, Exception? exception, IReadOnlyList<(string Key, object? Value)> fields)
    {
        try
        {
            var suffix = string.Join(' ', fields.Select(field => $"{field.Key}={Format(field.Value)}"));
            var line = $"{DateTimeOffset.Now:O} [{level}] [P{Environment.ProcessId}] [{category}] {message}{(suffix.Length == 0 ? string.Empty : " " + suffix)}{(exception is null ? string.Empty : Environment.NewLine + exception)}{Environment.NewLine}";
            lock (Sync)
            {
                Directory.CreateDirectory(DirectoryPath);
                File.AppendAllText(Path.Combine(DirectoryPath, $"overlay-{Environment.ProcessId}.log"), line);
            }
        }
        catch
        {
            // Overlay diagnostics are best effort and must never affect the UI lifecycle.
        }
    }

    private static string Format(object? value)
    {
        if (value is null) return "null";
        var text = value.ToString()!.Replace("\r", " ").Replace("\n", " ");
        return text.IndexOfAny([' ', '=']) >= 0 ? $"\"{text.Replace("\"", "\\\"")}\"" : text;
    }
}
