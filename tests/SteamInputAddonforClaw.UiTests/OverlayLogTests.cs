using SteamInputAddonforClaw.Overlay.Diagnostics;
using SteamInputAddonforClaw.FrontendTransport;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class OverlayLogTests
{
    [Fact]
    public void Configured_log_directory_writes_a_process_identifiable_overlay_log()
    {
        var directory = Path.Combine(Path.GetTempPath(), "SteamInputAddonforClaw.OverlayLog.Tests", Guid.NewGuid().ToString("N"));
        try
        {
            OverlayLog.ConfigureDirectory([FrontendLaunchArguments.LogDirectoryOption, directory]);
            OverlayLog.Info("Test", "Overlay diagnostic entry", ("Marker", "configured"));

            var files = Directory.GetFiles(directory, "overlay-*.log");
            var file = Assert.Single(files);
            Assert.Contains("Overlay diagnostic entry", File.ReadAllText(file));
            Assert.Contains("Marker=configured", File.ReadAllText(file));
            Assert.EndsWith($"overlay-{Environment.ProcessId}.log", file, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }
}
