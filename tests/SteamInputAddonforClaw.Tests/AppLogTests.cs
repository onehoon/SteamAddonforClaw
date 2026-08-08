using SteamInputAddonforClaw.Diagnostics;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

[Collection("AppLog")]
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

    [Fact]
    public void AllLevels_WriteStructuredDiagnosticContext()
    {
        AppLog.DirectoryOverride = _directory;
        Directory.CreateDirectory(_directory);
        AppLog.Trace("Test", "trace", ("Field", "value with spaces"));
        AppLog.Debug("Test", "debug");
        AppLog.Info("Test", "info");
        AppLog.Warn("Test", "warn");
        AppLog.Error("Test", "error", new InvalidOperationException("failure"));
        AppLog.Fatal("Test", "fatal", new InvalidOperationException("fatal failure"));

        var log = File.ReadAllText(Directory.EnumerateFiles(_directory).Single());
        Assert.Contains("[TRACE]", log);
        Assert.Contains("[DEBUG]", log);
        Assert.Contains("[INFO]", log);
        Assert.Contains("[WARN]", log);
        Assert.Contains("[ERROR]", log);
        Assert.Contains("[FATAL]", log);
        Assert.Contains("[P", log);
        Assert.Contains("[T", log);
        Assert.Contains("[L=", log);
        Assert.Contains("[Test]", log);
        Assert.Contains("Field=value with spaces", log);
        Assert.Contains("InvalidOperationException", log);
    }

    [Fact]
    public void MinimumLevel_FiltersLowerSeverityEntries()
    {
        AppLog.DirectoryOverride = _directory;
        AppLog.MinimumLevelOverride = AppLogLevel.Info;
        AppLog.Trace("Test", "hidden");
        AppLog.Info("Test", "visible");

        var log = File.ReadAllText(Directory.EnumerateFiles(_directory).Single());
        Assert.DoesNotContain("hidden", log);
        Assert.Contains("visible", log);
    }

    public void Dispose()
    {
        AppLog.DirectoryOverride = null;
        AppLog.MinimumLevelOverride = AppLogLevel.Trace;
        if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
    }
}

[CollectionDefinition("AppLog", DisableParallelization = true)]
public sealed class AppLogCollection;
