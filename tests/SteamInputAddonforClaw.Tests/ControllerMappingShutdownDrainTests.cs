using Xunit;

namespace SteamInputAddonforClaw.Tests;

/// <summary>Source-level coverage for draining existing Controller mapping saves before UI transport disposal.</summary>
public sealed class ControllerMappingShutdownDrainTests
{
    private static string Read(string relative)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "SteamInputAddonforClaw.slnx"))) dir = dir.Parent;
        return File.ReadAllText(Path.Combine(dir!.FullName, relative));
    }

    [Fact]
    public void MainWindow_drain_captures_and_joins_both_existing_mapping_chains()
    {
        var mainWindow = Read("src/SteamInputAddonforClaw.UI/MainWindow.xaml.cs");
        var start = mainWindow.IndexOf("internal Task DrainPendingControllerMappingSavesAsync", StringComparison.Ordinal);
        Assert.True(start >= 0);
        var method = mainWindow[start..mainWindow.IndexOf("private void ReturnToSettings", start, StringComparison.Ordinal)];

        Assert.Contains("var front = _frontButtonSaveChain", method, StringComparison.Ordinal);
        Assert.Contains("var back = _backButtonSaveChain", method, StringComparison.Ordinal);
        Assert.Contains("Task.WhenAll(front, back)", method, StringComparison.Ordinal);
        Assert.DoesNotContain("Task.Delay", method, StringComparison.Ordinal);
        Assert.DoesNotContain("WaitAsync", method, StringComparison.Ordinal);
    }

    [Fact]
    public void App_drains_mapping_saves_before_disposing_the_frontend_client()
    {
        var app = Read("src/SteamInputAddonforClaw.UI/App.xaml.cs");
        var drain = app.IndexOf("DrainPendingControllerMappingSavesAsync", StringComparison.Ordinal);
        var dispose = app.IndexOf("_frontendClient.DisposeAsync", StringComparison.Ordinal);

        Assert.True(drain >= 0 && dispose > drain);
        Assert.DoesNotContain("Task.Delay", app, StringComparison.Ordinal);
        Assert.DoesNotContain("Reconnect", app, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Existing_shutdown_coordinator_remains_the_outer_bounded_policy()
    {
        var app = Read("src/SteamInputAddonforClaw.UI/App.xaml.cs");
        var coordinator = Read("src/SteamInputAddonforClaw.UI/Lifecycle/UiShutdownCoordinator.cs");

        Assert.Contains("new UiShutdownCoordinator(DisposeFrontendAsync, RequestExitOnUiThread, TimeSpan.FromSeconds(5))", app, StringComparison.Ordinal);
        Assert.Contains("cleanup.WaitAsync(_cleanupTimeout)", coordinator, StringComparison.Ordinal);
        Assert.DoesNotContain("Mapping", coordinator, StringComparison.OrdinalIgnoreCase);
    }
}
