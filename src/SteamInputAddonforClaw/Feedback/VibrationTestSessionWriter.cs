namespace SteamInputAddonforClaw.Feedback;

/// <summary>Dedicated diagnostic session log for the Developer Menu's Vibration Test page, separate
/// from the main <c>AppLog</c> stream so a hardware test session can be handed to support as one
/// self-contained file. One instance per open session (page entry to page close).</summary>
internal sealed class VibrationTestSessionWriter : IAsyncDisposable
{
    private readonly StreamWriter _writer;
    internal string FilePath { get; }

    internal VibrationTestSessionWriter(string logRoot)
    {
        var directory = Path.Combine(logRoot, "VibrationTest");
        Directory.CreateDirectory(directory);
        FilePath = Path.Combine(directory, $"vibration-test-{DateTime.UtcNow:yyyyMMdd-HHmmss-fff}.log");
        _writer = new StreamWriter(FilePath, append: false) { AutoFlush = true };
    }

    internal void Write(string message) => _writer.WriteLine($"{DateTime.UtcNow:O} {message}");

    public async ValueTask DisposeAsync() => await _writer.DisposeAsync().ConfigureAwait(false);
}
