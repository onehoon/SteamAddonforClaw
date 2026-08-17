using SteamInputAddonforClaw.Diagnostics;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

[Collection("AppLog")]
public sealed class AppLogTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

    public AppLogTests()
    {
        // AppLog now defaults to Off; most tests in this file exercise Info-level behavior and would
        // otherwise silently log nothing. Tests that specifically exercise level filtering (Off, Debug,
        // live level changes) set MinimumLevelOverride explicitly within the test body, overriding this.
        AppLog.MinimumLevelOverride = AppLogLevel.Info;
    }

    [Fact]
    public void Info_WritesDailyFileAndRemovesLogsOlderThanYesterday()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(Path.Combine(_directory, "SteamInputAddonforClaw-2000-01-01.log"), "old");
        AppLog.DirectoryOverride = _directory;
        AppLog.Info("diagnostic event");
        AppLog.DrainForTests();
        Assert.Single(Directory.EnumerateFiles(_directory, "SteamInputAddonforClaw-*.log"));
    }

    [Fact]
    public void CurrentFileName_IsLaunchSpecific()
    {
        AppLog.DirectoryOverride = _directory;
        AppLog.Info("filename");
        AppLog.DrainForTests();
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
        AppLog.DrainForTests();
        Assert.Single(Directory.EnumerateFiles(_directory));
        var log = LogFileTestHelper.ReadAllText(AppLog.CurrentLogFilePath);
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
        AppLog.DrainForTests();

        var log = LogFileTestHelper.ReadAllText(Directory.EnumerateFiles(_directory).Single());
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
        AppLog.DrainForTests();

        var log = LogFileTestHelper.ReadAllText(Directory.EnumerateFiles(_directory).Single());
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
        AppLog.DrainForTests();
        var log = LogFileTestHelper.ReadAllText(Directory.EnumerateFiles(_directory).Single());
        Assert.DoesNotContain("Debug A", log); Assert.Contains("Debug B", log); Assert.DoesNotContain("Debug C", log);
    }

    [Fact]
    public void OffLevel_SuppressesEverythingIncludingFatal()
    {
        AppLog.DirectoryOverride = _directory;
        AppLog.MinimumLevelOverride = AppLogLevel.Off;
        AppLog.Debug("Test", "should not appear");
        AppLog.Info("Test", "should not appear");
        AppLog.Warn("Test", "should not appear");
        AppLog.Error("Test", "should not appear", new InvalidOperationException());
        AppLog.Fatal("Test", "should not appear", new InvalidOperationException());
        AppLog.DrainForTests();
        Assert.False(File.Exists(AppLog.CurrentLogFilePath));
    }

    [Fact]
    public void IsEnabled_ReflectsMinimumLevelOverride()
    {
        AppLog.MinimumLevelOverride = AppLogLevel.Off;
        Assert.False(AppLog.IsEnabled(AppLogLevel.Info));
        Assert.False(AppLog.IsEnabled(AppLogLevel.Debug));
        Assert.False(AppLog.IsEnabled(AppLogLevel.Fatal));

        AppLog.MinimumLevelOverride = AppLogLevel.Info;
        Assert.True(AppLog.IsEnabled(AppLogLevel.Info));
        Assert.False(AppLog.IsEnabled(AppLogLevel.Debug));

        AppLog.MinimumLevelOverride = AppLogLevel.Debug;
        Assert.True(AppLog.IsEnabled(AppLogLevel.Info));
        Assert.True(AppLog.IsEnabled(AppLogLevel.Debug));
    }

    [Fact]
    public void ChangeLogLevel_SupportsAllThreeLevelsAndUpdatesAppLogImmediately()
    {
        var store = new SteamInputAddonforClaw.Settings.SettingsStore(Path.Combine(_directory, "settings.json"));
        var coordinator = new SteamInputAddonforClaw.Settings.StartupSettingsCoordinator(
            new SteamInputAddonforClaw.Settings.AppSettings(), store, new NoOpStartupManager());

        coordinator.ChangeLogLevel(SteamInputAddonforClaw.Settings.AppLogPreference.Debug);
        Assert.Equal(AppLogLevel.Debug, AppLog.MinimumLevelOverride);

        coordinator.ChangeLogLevel(SteamInputAddonforClaw.Settings.AppLogPreference.Info);
        Assert.Equal(AppLogLevel.Info, AppLog.MinimumLevelOverride);

        coordinator.ChangeLogLevel(SteamInputAddonforClaw.Settings.AppLogPreference.Off);
        Assert.Equal(AppLogLevel.Off, AppLog.MinimumLevelOverride);
        Assert.Equal(SteamInputAddonforClaw.Settings.AppLogPreference.Off, store.Load().LogLevel);
    }

    private sealed class NoOpStartupManager : SteamInputAddonforClaw.Install.IWindowsStartupManager
    {
        public SteamInputAddonforClaw.Install.StartupRegistrationResult Synchronize(bool enabled) => SteamInputAddonforClaw.Install.StartupRegistrationResult.Enabled();
    }

    [Fact]
    public void Retention_KeepsYesterdayFileAndDeletesTwoDayOldFile()
    {
        Directory.CreateDirectory(_directory);
        AppLog.DirectoryOverride = _directory;
        var today = DateTime.Today;
        File.WriteAllText(Path.Combine(_directory, $"SteamInputAddonforClaw-{today.AddDays(-1):yyyy-MM-dd}.log"), "keep");
        File.WriteAllText(Path.Combine(_directory, $"SteamInputAddonforClaw-{today.AddDays(-2):yyyy-MM-dd}.log"), "delete");

        AppLog.Info("retention");
        AppLog.DrainForTests();

        Assert.True(File.Exists(Path.Combine(_directory, $"SteamInputAddonforClaw-{today.AddDays(-1):yyyy-MM-dd}.log")));
        Assert.False(File.Exists(Path.Combine(_directory, $"SteamInputAddonforClaw-{today.AddDays(-2):yyyy-MM-dd}.log")));
    }

    [Fact]
    public void Pruning_RunsOncePerDirectoryAndDate_AndAgainAfterRollover()
    {
        var day = DateTime.Today;
        AppLog.LocalDateProvider = () => day;
        AppLog.DirectoryOverride = _directory;
        AppLog.Info("one"); AppLog.Info("two");
        AppLog.DrainForTests();
        Assert.Equal(1, AppLog.PruneInvocationCount);
        AppLog.LocalDateProvider = () => day.AddDays(1);
        AppLog.Info("three");
        AppLog.DrainForTests();
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
            AppLog.DirectoryOverride = _directory; AppLog.Info("A again");
            AppLog.DrainForTests();
            Assert.Equal(2, AppLog.PruneInvocationCount);
            Assert.Single(Directory.EnumerateFiles(_directory));
            Assert.Single(Directory.EnumerateFiles(other));
            AppLog.LocalDateProvider = () => DateTime.Today.AddDays(1);
            AppLog.Info("A next day");
            AppLog.DrainForTests();
            Assert.Equal(3, AppLog.PruneInvocationCount);
        }
        finally { if (Directory.Exists(other)) Directory.Delete(other, true); }
    }

    [Fact]
    public void Retention_RecognizesNewAndLegacyFormatsButNotUnrelatedFiles()
    {
        var today = DateTime.Today;
        Directory.CreateDirectory(_directory);
        var recentNew = AppLogFileName.Create(new DateTimeOffset(today.AddDays(-1)), 111, "recent");
        var oldNew = AppLogFileName.Create(new DateTimeOffset(today.AddDays(-2)), 222, "old");
        File.WriteAllText(Path.Combine(_directory, recentNew), "recent");
        File.WriteAllText(Path.Combine(_directory, oldNew), "old");
        File.WriteAllText(Path.Combine(_directory, $"SteamInputAddonforClaw-{today.AddDays(-1):yyyy-MM-dd}.log"), "legacy recent");
        File.WriteAllText(Path.Combine(_directory, $"SteamInputAddonforClaw-{today.AddDays(-2):yyyy-MM-dd}.log"), "legacy old");
        File.WriteAllText(Path.Combine(_directory, "notes.txt"), "keep");
        File.WriteAllText(Path.Combine(_directory, "SteamInputAddonforClaw-not-a-valid-log-name.tmp"), "keep");
        AppLog.DirectoryOverride = _directory; AppLog.Info("retention");
        AppLog.DrainForTests();
        Assert.True(File.Exists(Path.Combine(_directory, recentNew)));
        Assert.False(File.Exists(Path.Combine(_directory, oldNew)));
        Assert.True(File.Exists(Path.Combine(_directory, $"SteamInputAddonforClaw-{today.AddDays(-1):yyyy-MM-dd}.log")));
        Assert.False(File.Exists(Path.Combine(_directory, $"SteamInputAddonforClaw-{today.AddDays(-2):yyyy-MM-dd}.log")));
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
        AppLog.DrainForTests();
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
        AppLog.DrainForTests();

        var lines = LogFileTestHelper.ReadAllLines(Directory.EnumerateFiles(_directory).Single());
        Assert.Equal(entries.Length, lines.Length);
        Assert.All(lines, line => Assert.Contains("[Concurrent] entry Index=", line));
    }

    [Fact]
    public void SequentialWrites_PreserveEnqueueOrder()
    {
        Directory.CreateDirectory(_directory);
        AppLog.DirectoryOverride = _directory;
        for (var index = 0; index < 200; index++)
            AppLog.Info("Order", "entry", ("Index", index));
        AppLog.DrainForTests();

        var lines = LogFileTestHelper.ReadAllLines(Directory.EnumerateFiles(_directory).Single());
        Assert.Equal(200, lines.Length);
        for (var index = 0; index < 200; index++)
            Assert.Contains($"Index={index}", lines[index]);
    }

    [Fact]
    public void MaximumDirectorySize_RemovesOldestRetainedLog()
    {
        Directory.CreateDirectory(_directory);
        AppLog.DirectoryOverride = _directory;
        var oldPath = Path.Combine(_directory, $"SteamInputAddonforClaw-{DateTime.Today.AddDays(-1):yyyy-MM-dd}.log");
        using (var stream = File.Create(oldPath)) stream.SetLength(AppLog.MaximumLogDirectoryBytes + 1);

        AppLog.Info("size cap");
        AppLog.DrainForTests();

        Assert.False(File.Exists(oldPath));
    }

    [Fact]
    public void LaunchId_IsConsistentAcrossEntries()
    {
        Directory.CreateDirectory(_directory);
        AppLog.DirectoryOverride = _directory;
        AppLog.Info("Test", "first");
        AppLog.Info("Test", "second");
        AppLog.DrainForTests();
        var launchIds = LogFileTestHelper.ReadAllLines(Directory.EnumerateFiles(_directory).Single()).Select(line => line.Split(' ').Single(part => part.StartsWith("[L="))).Distinct();
        Assert.Single(launchIds);
    }

    [Fact]
    public void LoggingFailure_IsNonFatal()
    {
        var file = Path.GetTempFileName();
        AppLog.DirectoryOverride = file;
        AppLog.Info("Test", "must not throw");
        // The writer's directory-create/open fails (DirectoryOverride points at a file, not a
        // directory); DrainForTests() itself must still complete, proving the writer loop survived
        // the failure instead of dying or recursing back into AppLog.
        AppLog.DrainForTests();
        Assert.True(File.Exists(file));
        File.Delete(file);
    }

    [Fact]
    public void BufferedEntryWriter_DropsLowPriorityWhenSaturated()
    {
        var gate = new SemaphoreSlim(0);
        using var writer = new BufferedEntryWriter<int>(
            capacity: 1,
            process: _ => gate.Wait(),
            isHighPriority: static _ => false);

        // The writer thread immediately dequeues item 0 and blocks inside process() on the gate, so the
        // queue (capacity 1) fills up on the next enqueue and every one after that must be dropped.
        writer.Enqueue(0);
        Thread.Sleep(50);
        writer.Enqueue(1);
        writer.Enqueue(2);
        writer.Enqueue(3);

        gate.Release(10);
        writer.DrainForTests();

        Assert.True(writer.DroppedLowPriorityCount > 0);
        Assert.Equal(0, writer.DroppedHighPriorityCount);
    }

    [Fact]
    public void BufferedEntryWriter_RetriesThenDropsHighPriorityWhenSaturated()
    {
        var gate = new SemaphoreSlim(0);
        using var writer = new BufferedEntryWriter<int>(
            capacity: 1,
            process: _ => gate.Wait(),
            isHighPriority: static _ => true);

        writer.Enqueue(0);
        Thread.Sleep(50);
        writer.Enqueue(1);
        writer.Enqueue(2);

        gate.Release(10);
        writer.DrainForTests();

        Assert.True(writer.DroppedHighPriorityCount > 0);
    }

    [Fact]
    public void BufferedEntryWriter_ShutdownReturnsTrueWhenWriterStopsWithinTimeout()
    {
        using var writer = new BufferedEntryWriter<int>(capacity: 8, process: static _ => { }, isHighPriority: static _ => false);
        writer.Enqueue(1);
        Assert.True(writer.Shutdown(TimeSpan.FromSeconds(2)));
    }

    [Fact]
    public void BufferedEntryWriter_ShutdownReturnsFalseWhenWriterExceedsTimeout()
    {
        var gate = new SemaphoreSlim(0);
        var writer = new BufferedEntryWriter<int>(capacity: 8, process: _ => gate.Wait(), isHighPriority: static _ => false);
        writer.Enqueue(1);
        try
        {
            Assert.False(writer.Shutdown(TimeSpan.FromMilliseconds(50)));
        }
        finally
        {
            gate.Release(10);
        }
    }

    [Fact]
    public void BufferedEntryWriter_ShutdownDrainsQueuedItemsFirst()
    {
        var processed = new List<int>();
        var sync = new object();
        using var writer = new BufferedEntryWriter<int>(capacity: 64, process: item =>
        {
            lock (sync) processed.Add(item);
        }, isHighPriority: static _ => true);

        for (var i = 0; i < 20; i++) writer.Enqueue(i);
        writer.Shutdown();

        lock (sync) Assert.Equal(Enumerable.Range(0, 20), processed);
    }

    [Fact]
    public void BufferedEntryWriter_EnqueueAfterShutdownDoesNotThrow()
    {
        using var writer = new BufferedEntryWriter<int>(capacity: 8, process: static _ => { }, isHighPriority: static _ => false);
        writer.Shutdown();
        var exception = Record.Exception(() => writer.Enqueue(1));
        Assert.Null(exception);
    }

    [Fact]
    public void BufferedEntryWriter_ShutdownCalledTwiceIsSafeAndStillReturnsTrue()
    {
        // Program.cs's Main() calls AppLog.Shutdown() in a `finally` unconditionally, even on the normal
        // MainWindow-exit path where App.xaml.cs's OnMainWindowClosed already called it once. Shutdown
        // must tolerate that second call without throwing and without misreporting whether the writer
        // stopped cleanly.
        using var writer = new BufferedEntryWriter<int>(capacity: 8, process: static _ => { }, isHighPriority: static _ => false);
        writer.Enqueue(1);
        Assert.True(writer.Shutdown(TimeSpan.FromSeconds(2)));
        var exception = Record.Exception(() => Assert.True(writer.Shutdown(TimeSpan.FromSeconds(2))));
        Assert.Null(exception);
    }

    public void Dispose()
    {
        AppLog.DirectoryOverride = null;
        AppLog.MinimumLevelOverride = AppLogLevel.Off;
        AppLog.ResetMaintenanceStateForTests();
        if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
    }
}

[CollectionDefinition("AppLog", DisableParallelization = true)]
public sealed class AppLogCollection;
