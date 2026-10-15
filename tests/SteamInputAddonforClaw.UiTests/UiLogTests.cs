using SteamInputAddonforClaw.FrontendTransport;
using SteamInputAddonforClaw.Contracts.Frontend;
using SteamInputAddonforClaw.UI.Diagnostics;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

[Collection("UiLog")]
public sealed class UiLogTests
{
    [Fact]
    public void Debug_writes_debug_severity_to_the_configured_ui_log()
    {
        var previousDirectory = UiLog.DirectoryPath;
        var directory = Path.Combine(Path.GetTempPath(), "SteamInputAddonforClaw.UiLog.Tests", Guid.NewGuid().ToString("N"));
        try
        {
            UiLog.ConfigureDirectory([FrontendLaunchArguments.LogDirectoryOption, directory]);
            UiLog.SetMinimumLevel(FrontendLogLevel.Debug);
            UiLog.Debug("Frontend", "Runtime transport teardown skipped", ("Reason", "test"));

            var path = Path.Combine(directory, $"ui-{Environment.ProcessId}.log");
            var line = Assert.Single(File.ReadAllLines(path));
            Assert.Contains("[DEBUG] [Frontend] Runtime transport teardown skipped", line);
            Assert.Contains("Reason=test", line);
        }
        finally
        {
            UiLog.SetMinimumLevel(FrontendLogLevel.Off);
            UiLog.ConfigureDirectory([FrontendLaunchArguments.LogDirectoryOption, previousDirectory]);
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Off_suppresses_every_severity_without_creating_a_log_directory()
    {
        var previousDirectory = UiLog.DirectoryPath;
        var directory = NewDirectory();
        try
        {
            UiLog.ConfigureDirectory([FrontendLaunchArguments.LogDirectoryOption, directory]);
            UiLog.SetMinimumLevel(FrontendLogLevel.Off);
            WriteAllSeverities();

            Assert.False(Directory.Exists(directory));
        }
        finally { Restore(directory, previousDirectory); }
    }

    [Fact]
    public void Info_writes_info_warning_and_error_but_not_debug()
    {
        var previousDirectory = UiLog.DirectoryPath;
        var directory = NewDirectory();
        try
        {
            UiLog.ConfigureDirectory([FrontendLaunchArguments.LogDirectoryOption, directory]);
            UiLog.SetMinimumLevel(FrontendLogLevel.Info);
            WriteAllSeverities();

            var text = File.ReadAllText(Path.Combine(directory, $"ui-{Environment.ProcessId}.log"));
            Assert.Contains("[INFO]", text);
            Assert.Contains("[WARN]", text);
            Assert.Contains("[ERROR]", text);
            Assert.DoesNotContain("[DEBUG]", text);
        }
        finally { Restore(directory, previousDirectory); }
    }

    [Fact]
    public void Debug_writes_all_severities()
    {
        var previousDirectory = UiLog.DirectoryPath;
        var directory = NewDirectory();
        try
        {
            UiLog.ConfigureDirectory([FrontendLaunchArguments.LogDirectoryOption, directory]);
            UiLog.SetMinimumLevel(FrontendLogLevel.Debug);
            WriteAllSeverities();

            var text = File.ReadAllText(Path.Combine(directory, $"ui-{Environment.ProcessId}.log"));
            Assert.Contains("[DEBUG]", text);
            Assert.Contains("[INFO]", text);
            Assert.Contains("[WARN]", text);
            Assert.Contains("[ERROR]", text);
        }
        finally { Restore(directory, previousDirectory); }
    }

    private static void WriteAllSeverities()
    {
        UiLog.Debug("Test", "debug");
        UiLog.Info("Test", "info");
        UiLog.Warn("Test", "warning");
        UiLog.Error("Test", "error", new InvalidOperationException("test"));
    }

    private static string NewDirectory() => Path.Combine(Path.GetTempPath(), "SteamInputAddonforClaw.UiLog.Tests", Guid.NewGuid().ToString("N"));

    private static void Restore(string directory, string previousDirectory)
    {
        UiLog.SetMinimumLevel(FrontendLogLevel.Off);
        UiLog.ConfigureDirectory([FrontendLaunchArguments.LogDirectoryOption, previousDirectory]);
        if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
    }
}

[CollectionDefinition("UiLog", DisableParallelization = true)]
public sealed class UiLogCollection;
