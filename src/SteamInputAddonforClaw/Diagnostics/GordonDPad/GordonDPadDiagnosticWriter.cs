namespace SteamInputAddonforClaw.Diagnostics.GordonDPad;

/// <summary>
/// A dedicated, single-capture-session log file under
/// <c>%LOCALAPPDATA%\SteamInputAddonforClaw\diagnostics\</c> -- separate from the main <see cref="AppLog"/>
/// file so a Gordon D-pad capture session is self-contained and easy to hand off for analysis. Reuses
/// <see cref="BufferedEntryWriter{T}"/> (the same bounded-queue/single-background-writer infrastructure
/// <see cref="AppLog"/> itself uses) so writes never block a realtime input, native-callback, or HID-read
/// thread. The file is created immediately on construction -- the caller does not need to wait for the
/// first write for it to appear on disk.
/// </summary>
/// <remarks>
/// Unlike <see cref="AppLog"/> (which keeps one <see cref="StreamWriter"/> open across many entries and
/// only ever closes it via its own <c>DrainForTests</c>/shutdown path), this writer opens the file fresh,
/// appends, and closes it again for every single line. A diagnostic capture's write volume is
/// transition-gated (a handful of lines per physical button press, not a per-tick stream), so the
/// per-line open/close cost is negligible, and the payoff is that the file is never left held open
/// between writes -- guaranteeing another process (a text editor with auto-reload, or this test suite)
/// can always read it while a capture is active, on every filesystem, without depending on how faithfully
/// a given environment honors a long-lived <c>FileShare.ReadWrite</c> handle.
/// </remarks>
internal sealed class GordonDPadDiagnosticWriter : IDisposable
{
    private const int QueueCapacity = 4096;

    private readonly BufferedEntryWriter<string> _writer;

    internal string FilePath { get; }

    internal GordonDPadDiagnosticWriter(string directory, string fileName)
    {
        Directory.CreateDirectory(directory);
        FilePath = Path.Combine(directory, fileName);
        // Created immediately (an empty file, but present on disk) rather than lazily on the first queued
        // write, matching the "file must exist immediately when Start Capture succeeds" requirement.
        using (File.Create(FilePath)) { }
        _writer = new BufferedEntryWriter<string>(QueueCapacity, ProcessLine, static _ => true);
    }

    internal void WriteHeader(IReadOnlyList<(string Key, string Value)> fields)
    {
        WriteLine("=== Gordon D-pad diagnostic capture ===");
        foreach (var (key, value) in fields) WriteLine($"{key}={value}");
        WriteLine("=== capture begins ===");
    }

    internal void WriteLine(string line) => _writer.Enqueue(line);

    private void ProcessLine(string line)
    {
        using var stream = new FileStream(FilePath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
        using var fileWriter = new StreamWriter(stream);
        fileWriter.WriteLine(line);
    }

    /// <summary>Test-only: blocks until every line enqueued before this call has been written.</summary>
    internal void DrainForTests(TimeSpan? timeout = null) => _writer.DrainForTests(timeout);

    /// <summary>Stops accepting new lines and waits (bounded) for queued lines to be written. Safe to
    /// call multiple times. There is no lingering open file handle to close -- see remarks above.</summary>
    internal void Stop(TimeSpan? timeout = null) => _writer.Shutdown(timeout);

    public void Dispose() => Stop();
}
