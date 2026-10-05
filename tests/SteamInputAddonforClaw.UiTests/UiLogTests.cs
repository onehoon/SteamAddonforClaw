using SteamInputAddonforClaw.FrontendTransport;
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
            UiLog.Debug("Frontend", "Runtime transport teardown skipped", ("Reason", "test"));

            var path = Path.Combine(directory, $"ui-{Environment.ProcessId}.log");
            var line = Assert.Single(File.ReadAllLines(path));
            Assert.Contains("[DEBUG] [Frontend] Runtime transport teardown skipped", line);
            Assert.Contains("Reason=test", line);
        }
        finally
        {
            UiLog.ConfigureDirectory([FrontendLaunchArguments.LogDirectoryOption, previousDirectory]);
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }
}

[CollectionDefinition("UiLog", DisableParallelization = true)]
public sealed class UiLogCollection;
