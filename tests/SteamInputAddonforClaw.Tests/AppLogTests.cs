using SteamInputAddonforClaw.Diagnostics;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class AppLogTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

    [Fact]
    public void Info_WritesDailyFileAndRemovesLogsOlderThanYesterday()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(Path.Combine(_directory, "SteamInputAddonforClaw-2000-01-01.log"), "old");
        AppLog.DirectoryOverride = _directory;
        AppLog.Info("diagnostic event");
        Assert.Single(Directory.EnumerateFiles(_directory, "SteamInputAddonforClaw-*.log"));
    }

    public void Dispose()
    {
        AppLog.DirectoryOverride = null;
        if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
    }
}
