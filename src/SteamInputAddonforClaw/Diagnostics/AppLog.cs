using System.Globalization;

namespace SteamInputAddonforClaw.Diagnostics;

internal static class AppLog
{
    private static readonly Lock Sync = new();
    private static readonly string DefaultDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SteamInputAddonforClaw", "logs");

    public static void Info(string message) => Write("INFO", message, null);
    public static void Error(string message, Exception exception) => Write("ERROR", message, exception);

    internal static string? DirectoryOverride { get; set; }

    private static void Write(string level, string message, Exception? exception)
    {
        try
        {
            lock (Sync)
            {
                var now = DateTimeOffset.Now;
                var directory = DirectoryOverride ?? DefaultDirectory;
                Directory.CreateDirectory(directory);
                foreach (var path in Directory.EnumerateFiles(directory, "SteamInputAddonforClaw-*.log"))
                {
                    var dateText = Path.GetFileNameWithoutExtension(path).Replace("SteamInputAddonforClaw-", string.Empty);
                    if (DateTime.TryParseExact(dateText, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) && date < now.Date.AddDays(-1)) File.Delete(path);
                }

                var details = exception is null ? string.Empty : $"{Environment.NewLine}{exception}";
                File.AppendAllText(Path.Combine(directory, $"SteamInputAddonforClaw-{now:yyyy-MM-dd}.log"), $"{now:O} [{level}] [T{Environment.CurrentManagedThreadId}] {message}{details}{Environment.NewLine}");
            }
        }
        catch { }
    }
}
