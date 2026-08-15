using SteamInputAddonforClaw.Diagnostics.GordonDPad;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class GordonDPadDiagnosticWriterTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "GordonDPadDiagnosticWriterTests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { if (Directory.Exists(_directory)) Directory.Delete(_directory, true); } catch (IOException) { }
    }

    [Fact]
    public void Constructor_CreatesTheFileImmediately()
    {
        using var writer = new GordonDPadDiagnosticWriter(_directory, "capture.log");

        Assert.True(File.Exists(writer.FilePath));
    }

    [Fact]
    public void WriteHeader_IsReadableWhileCaptureIsStillActive()
    {
        using var writer = new GordonDPadDiagnosticWriter(_directory, "capture.log");

        writer.WriteHeader([("AddonVersion", "1.2.3"), ("ProcessId", "4242")]);
        writer.DrainForTests();

        var content = File.ReadAllText(writer.FilePath);
        Assert.Contains("AddonVersion=1.2.3", content);
        Assert.Contains("ProcessId=4242", content);
    }

    [Fact]
    public void WriteLine_BecomesReadableBeforeStop()
    {
        using var writer = new GordonDPadDiagnosticWriter(_directory, "capture.log");

        writer.WriteLine("Stage=Physical Up=1 Right=0 Left=0 Down=0 Mask=0x01");
        writer.DrainForTests();

        Assert.Contains("Stage=Physical", File.ReadAllText(writer.FilePath));
    }

    [Fact]
    public void Stop_DrainsQueuedLinesBeforeClosing()
    {
        var writer = new GordonDPadDiagnosticWriter(_directory, "capture.log");
        for (var i = 0; i < 50; i++) writer.WriteLine($"Stage=Test Index={i}");

        writer.Stop();

        var content = File.ReadAllText(writer.FilePath);
        Assert.Contains("Index=0", content);
        Assert.Contains("Index=49", content);
    }

    [Fact]
    public void Stop_CalledTwiceDoesNotThrow()
    {
        var writer = new GordonDPadDiagnosticWriter(_directory, "capture.log");
        writer.Stop();
        var exception = Record.Exception(() => writer.Stop());
        Assert.Null(exception);
    }
}
