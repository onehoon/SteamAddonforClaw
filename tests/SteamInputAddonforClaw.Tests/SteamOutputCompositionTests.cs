using SteamInputAddonforClaw.Diagnostics;
using SteamInputAddonforClaw.VirtualOutput.Viiper;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

[Collection("AppLog")]
public sealed class SteamOutputCompositionTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "SteamOutputCompositionTests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void Startup_log_identifies_the_active_Steam_Deck_target()
    {
        Directory.CreateDirectory(_directory);
        AppLog.DirectoryOverride = _directory;
        AppLog.MinimumLevelOverride = AppLogLevel.Info;

        SteamOutputComposition.LogTargetSelected();
        AppLog.DrainForTests();

        var log = LogFileTestHelper.ReadAllText(AppLog.CurrentLogFilePath);
        Assert.Contains("[SteamOutput] Virtual output target selected.", log);
        Assert.Contains("Target=SteamDeck", log);
        Assert.Contains("VID=0x28DE", log);
        Assert.Contains("PID=0x1205", log);
    }

    public void Dispose()
    {
        AppLog.DrainForTests();
        AppLog.DirectoryOverride = null;
        AppLog.MinimumLevelOverride = AppLogLevel.Info;
        if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
    }
}
