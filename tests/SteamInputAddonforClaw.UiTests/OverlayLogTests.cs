using SteamInputAddonforClaw.Overlay.Diagnostics;
using SteamInputAddonforClaw.FrontendTransport;
using SteamInputAddonforClaw.Contracts.Frontend;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

[Collection("OverlayLog")]
public sealed class OverlayLogTests
{
    [Fact]
    public void Configured_log_directory_writes_a_process_identifiable_overlay_log()
    {
        var previousDirectory = OverlayLog.DirectoryPath;
        var directory = Path.Combine(Path.GetTempPath(), "SteamInputAddonforClaw.OverlayLog.Tests", Guid.NewGuid().ToString("N"));
        try
        {
            OverlayLog.ConfigureDirectory([FrontendLaunchArguments.LogDirectoryOption, directory]);
            OverlayLog.SetMinimumLevel(FrontendLogLevel.Info);
            OverlayLog.Info("Test", "Overlay diagnostic entry", ("Marker", "configured"));

            var files = Directory.GetFiles(directory, "overlay-*.log");
            var file = Assert.Single(files);
            Assert.Contains("Overlay diagnostic entry", File.ReadAllText(file));
            Assert.Contains("Marker=configured", File.ReadAllText(file));
            Assert.EndsWith($"overlay-{Environment.ProcessId}.log", file, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Restore(directory, previousDirectory);
        }
    }

    [Fact]
    public void Off_suppresses_every_severity_without_creating_a_log_directory()
    {
        var previousDirectory = OverlayLog.DirectoryPath;
        var directory = NewDirectory();
        try
        {
            OverlayLog.ConfigureDirectory([FrontendLaunchArguments.LogDirectoryOption, directory]);
            OverlayLog.SetMinimumLevel(FrontendLogLevel.Off);
            WriteAllSeverities();

            Assert.False(Directory.Exists(directory));
        }
        finally { Restore(directory, previousDirectory); }
    }

    [Fact]
    public void Info_writes_info_warning_and_error_but_not_debug()
    {
        var previousDirectory = OverlayLog.DirectoryPath;
        var directory = NewDirectory();
        try
        {
            OverlayLog.ConfigureDirectory([FrontendLaunchArguments.LogDirectoryOption, directory]);
            OverlayLog.SetMinimumLevel(FrontendLogLevel.Info);
            WriteAllSeverities();

            var file = Assert.Single(Directory.GetFiles(directory, "overlay-*.log"));
            var text = File.ReadAllText(file);
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
        var previousDirectory = OverlayLog.DirectoryPath;
        var directory = NewDirectory();
        try
        {
            OverlayLog.ConfigureDirectory([FrontendLaunchArguments.LogDirectoryOption, directory]);
            OverlayLog.SetMinimumLevel(FrontendLogLevel.Debug);
            WriteAllSeverities();

            var text = File.ReadAllText(Assert.Single(Directory.GetFiles(directory, "overlay-*.log")));
            Assert.Contains("[DEBUG]", text);
            Assert.Contains("[INFO]", text);
            Assert.Contains("[WARN]", text);
            Assert.Contains("[ERROR]", text);
        }
        finally { Restore(directory, previousDirectory); }
    }

    private static void WriteAllSeverities()
    {
        OverlayLog.Debug("Test", "debug");
        OverlayLog.Info("Test", "info");
        OverlayLog.Warn("Test", "warning");
        OverlayLog.Error("Test", "error", new InvalidOperationException("test"));
    }

    private static string NewDirectory() => Path.Combine(Path.GetTempPath(), "SteamInputAddonforClaw.OverlayLog.Tests", Guid.NewGuid().ToString("N"));

    private static void Restore(string directory, string previousDirectory)
    {
        OverlayLog.SetMinimumLevel(FrontendLogLevel.Off);
        OverlayLog.ConfigureDirectory([FrontendLaunchArguments.LogDirectoryOption, previousDirectory]);
        if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
    }
}

[CollectionDefinition("OverlayLog", DisableParallelization = true)]
public sealed class OverlayLogCollection;
