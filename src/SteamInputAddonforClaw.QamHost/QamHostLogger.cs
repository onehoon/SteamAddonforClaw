using System.Text;

namespace SteamInputAddonforClaw.QamHost;

internal sealed class QamHostLogger : IDisposable
{
    private readonly StreamWriter? _writer;
    private readonly object _sync = new();

    internal QamHostLogger(string? directory)
    {
        if (string.IsNullOrWhiteSpace(directory)) return;
        try
        {
            Directory.CreateDirectory(directory);
            var name = $"SteamInputAddonforClaw-QamHost-{DateTimeOffset.Now:yyyyMMdd-HHmmss}-P{Environment.ProcessId}.log";
            _writer = new StreamWriter(Path.Combine(directory, name), false, Encoding.UTF8) { AutoFlush = true };
        }
        catch { }
    }

    internal void Info(string message) => Write("INFO", message);
    internal void Warn(string message) => Write("WARN", message);
    internal void Error(string message) => Write("ERROR", message);

    private void Write(string level, string message)
    {
        var line = $"{DateTimeOffset.Now:O} [{level}] [P{Environment.ProcessId}] {message}";
        Console.WriteLine(line);
        try { lock (_sync) _writer?.WriteLine(line); } catch { }
    }

    public void Dispose() { lock (_sync) _writer?.Dispose(); }
}
