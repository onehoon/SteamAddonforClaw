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
    public void CurrentFileName_IsLaunchSpecific()
    {
        AppLog.DirectoryOverride = _directory;
        AppLog.Info("filename");
        var file = Directory.EnumerateFiles(_directory).Single();
        var name = Path.GetFileName(file);
        Assert.StartsWith("SteamInputAddonforClaw-", name);
        Assert.Contains($"-P{Environment.ProcessId}-L", name);
        Assert.DoesNotMatch("^SteamInputAddonforClaw-\\d{4}-\\d{2}-\\d{2}\\.log$", name);
        Assert.Equal(AppLog.CurrentLogFileName, name);
    }

    [Fact]
    public void FileNameFormatter_DifferentLaunchIdentitiesProduceDifferentNames()
    {
        var timestamp = new DateTimeOffset(2026, 8, 12, 8, 32, 15, 123, TimeSpan.FromHours(9));
        var first = AppLogFileName.Create(timestamp, 1111, "AAAA");
        var second = AppLogFileName.Create(timestamp.AddMilliseconds(1), 2222, "BBBB");
        Assert.NotEqual(first, second);
    }

    [Fact]
    public void MultipleWrites_UseOneLaunchFile()
    {
        AppLog.DirectoryOverride = _directory;
        AppLog.Info("first"); AppLog.Info("second"); AppLog.Info("third");
        Assert.Single(Directory.EnumerateFiles(_directory));
        var log = File.ReadAllText(AppLog.CurrentLogFilePath);
        Assert.Contains("first", log); Assert.Contains("second", log); Assert.Contains("third", log);
    }

    [Fact]
    public void AllLevels_WriteStructuredDiagnosticContext()
    {
        AppLog.DirectoryOverride = _directory;
        Directory.CreateDirectory(_directory);
        AppLog.MinimumLevelOverride = AppLogLevel.Debug;
        AppLog.Debug("Test", "debug", ("Field", "value with spaces"));
        AppLog.Info("Test", "info");
        AppLog.Warn("Test", "warn");
        AppLog.Error("Test", "error", new InvalidOperationException("failure"));
        AppLog.Fatal("Test", "fatal", new InvalidOperationException("fatal failure"));

        var log = File.ReadAllText(Directory.EnumerateFiles(_directory).Single());
        Assert.Contains("[DEBUG]", log);
        Assert.Contains("[INFO]", log);
        Assert.Contains("[WARN]", log);
        Assert.Contains("[ERROR]", log);
        Assert.Contains("[FATAL]", log);
        Assert.Contains("[P", log);
        Assert.Contains("[T", log);
        Assert.Contains("[L=", log);
        Assert.Contains("[Test]", log);
        Assert.Contains("Field=\"value with spaces\"", log);
        Assert.Contains("InvalidOperationException", log);
    }

    [Fact]
    public void MinimumLevel_FiltersLowerSeverityEntries()
    {
        AppLog.DirectoryOverride = _directory;
        AppLog.MinimumLevelOverride = AppLogLevel.Info;
        AppLog.Debug("Test", "hidden");
        AppLog.Info("Test", "visible");

        var log = File.ReadAllText(Directory.EnumerateFiles(_directory).Single());
        Assert.DoesNotContain("hidden", log);
        Assert.Contains("visible", log);
    }

    [Fact]
    public void MinimumLevel_CanChangeLive()
    {
        AppLog.DirectoryOverride = _directory;
        AppLog.MinimumLevelOverride = AppLogLevel.Info;
        AppLog.Debug("Test", "Debug A");
        AppLog.MinimumLevelOverride = AppLogLevel.Debug;
        AppLog.Debug("Test", "Debug B");
        AppLog.MinimumLevelOverride = AppLogLevel.Info;
        AppLog.Debug("Test", "Debug C");
        var log = File.ReadAllText(Directory.EnumerateFiles(_directory).Single());
        Assert.DoesNotContain("Debug A", log); Assert.Contains("Debug B", log); Assert.DoesNotContain("Debug C", log);
    }

    [Fact]
    public void Retention_KeepsSixDayOldFileAndDeletesSevenDayOldFile()
    {
        Directory.CreateDirectory(_directory);
        AppLog.DirectoryOverride = _directory;
        var today = DateTime.Today;
        File.WriteAllText(Path.Combine(_directory, $"SteamInputAddonforClaw-{today.AddDays(-6):yyyy-MM-dd}.log"), "keep");
        File.WriteAllText(Path.Combine(_directory, $"SteamInputAddonforClaw-{today.AddDays(-7):yyyy-MM-dd}.log"), "delete");

        AppLog.Info("retention");

        Assert.True(File.Exists(Path.Combine(_directory, $"SteamInputAddonforClaw-{today.AddDays(-6):yyyy-MM-dd}.log")));
        Assert.False(File.Exists(Path.Combine(_directory, $"SteamInputAddonforClaw-{today.AddDays(-7):yyyy-MM-dd}.log")));
    }

    [Fact]
    public void Pruning_RunsOncePerDirectoryAndDate_AndAgainAfterRollover()
    {
        var day = DateTime.Today;
        AppLog.LocalDateProvider = () => day;
        AppLog.DirectoryOverride = _directory;
        AppLog.Info("one"); AppLog.Info("two");
        Assert.Equal(1, AppLog.PruneInvocationCount);
        AppLog.LocalDateProvider = () => day.AddDays(1);
        AppLog.Info("three");
        Assert.Equal(2, AppLog.PruneInvocationCount);
    }

    [Fact]
    public void Pruning_RunsForEachDirectory()
    {
        var other = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            AppLog.DirectoryOverride = _directory; AppLog.Info("A");
            AppLog.DirectoryOverride = other; AppLog.Info("B");
            Assert.Equal(2, AppLog.PruneInvocationCount);
            Assert.Single(Directory.EnumerateFiles(_directory));
            Assert.Single(Directory.EnumerateFiles(other));
        }
        finally { if (Directory.Exists(other)) Directory.Delete(other, true); }
    }

    [Fact]
    public void Retention_RecognizesNewAndLegacyFormatsButNotUnrelatedFiles()
    {
        var today = DateTime.Today;
        Directory.CreateDirectory(_directory);
        var recentNew = AppLogFileName.Create(new DateTimeOffset(today.AddDays(-1)), 111, "recent");
        var oldNew = AppLogFileName.Create(new DateTimeOffset(today.AddDays(-7)), 222, "old");
        File.WriteAllText(Path.Combine(_directory, recentNew), "recent");
        File.WriteAllText(Path.Combine(_directory, oldNew), "old");
        File.WriteAllText(Path.Combine(_directory, $"SteamInputAddonforClaw-{today.AddDays(-1):yyyy-MM-dd}.log"), "legacy recent");
        File.WriteAllText(Path.Combine(_directory, $"SteamInputAddonforClaw-{today.AddDays(-7):yyyy-MM-dd}.log"), "legacy old");
        File.WriteAllText(Path.Combine(_directory, "notes.txt"), "keep");
        File.WriteAllText(Path.Combine(_directory, "SteamInputAddonforClaw-not-a-valid-log-name.tmp"), "keep");
        AppLog.DirectoryOverride = _directory; AppLog.Info("retention");
        Assert.True(File.Exists(Path.Combine(_directory, recentNew)));
        Assert.False(File.Exists(Path.Combine(_directory, oldNew)));
        Assert.True(File.Exists(Path.Combine(_directory, $"SteamInputAddonforClaw-{today.AddDays(-1):yyyy-MM-dd}.log")));
        Assert.False(File.Exists(Path.Combine(_directory, $"SteamInputAddonforClaw-{today.AddDays(-7):yyyy-MM-dd}.log")));
        Assert.True(File.Exists(Path.Combine(_directory, "notes.txt")));
        Assert.True(File.Exists(Path.Combine(_directory, "SteamInputAddonforClaw-not-a-valid-log-name.tmp")));
    }

    [Fact]
    public void Pruning_NeverDeletesActiveFileEvenWhenItExceedsSizeCap()
    {
        AppLog.DirectoryOverride = _directory;
        Directory.CreateDirectory(_directory);
        using (var stream = File.Create(AppLog.CurrentLogFilePath)) stream.SetLength(AppLog.MaximumLogDirectoryBytes + 1);
        var old = Path.Combine(_directory, $"SteamInputAddonforClaw-{DateTime.Today.AddDays(-1):yyyy-MM-dd}.log");
        using (var stream = File.Create(old)) stream.SetLength(1024);
        AppLog.Info("active");
        Assert.True(File.Exists(AppLog.CurrentLogFilePath));
        Assert.False(File.Exists(old));
    }

    [Fact]
    public async Task ConcurrentWrites_KeepOneCompleteLinePerEntry()
    {
        Directory.CreateDirectory(_directory);
        AppLog.DirectoryOverride = _directory;
        var entries = Enumerable.Range(0, 100).ToArray();
        await Task.WhenAll(entries.Select(index => Task.Run(() => AppLog.Info("Concurrent", "entry", ("Index", index)))));

        var lines = File.ReadAllLines(Directory.EnumerateFiles(_directory).Single());
        Assert.Equal(entries.Length, lines.Length);
        Assert.All(lines, line => Assert.Contains("[Concurrent] entry Index=", line));
    }

    [Fact]
    public void MaximumDirectorySize_RemovesOldestRetainedLog()
    {
        Directory.CreateDirectory(_directory);
        AppLog.DirectoryOverride = _directory;
        var oldPath = Path.Combine(_directory, $"SteamInputAddonforClaw-{DateTime.Today.AddDays(-1):yyyy-MM-dd}.log");
        using (var stream = File.Create(oldPath)) stream.SetLength(AppLog.MaximumLogDirectoryBytes + 1);

        AppLog.Info("size cap");

        Assert.False(File.Exists(oldPath));
    }

    [Fact]
    public void LaunchId_IsConsistentAcrossEntries()
    {
        Directory.CreateDirectory(_directory);
        AppLog.DirectoryOverride = _directory;
        AppLog.Info("Test", "first");
        AppLog.Info("Test", "second");
        var launchIds = File.ReadAllLines(Directory.EnumerateFiles(_directory).Single()).Select(line => line.Split(' ').Single(part => part.StartsWith("[L="))).Distinct();
        Assert.Single(launchIds);
    }

    [Fact]
    public void LoggingFailure_IsNonFatal()
    {
        var file = Path.GetTempFileName();
        AppLog.DirectoryOverride = file;
        AppLog.Info("Test", "must not throw");
        Assert.True(File.Exists(file));
        File.Delete(file);
    }

    public void Dispose()
    {
        AppLog.DirectoryOverride = null;
        AppLog.MinimumLevelOverride = AppLogLevel.Info;
        AppLog.ResetMaintenanceStateForTests();
        if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
    }
}

[CollectionDefinition("AppLog", DisableParallelization = true)]
public sealed class AppLogCollection;
