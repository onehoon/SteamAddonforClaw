using System.Xml.Linq;
using Xunit;

namespace SteamInputAddonforClaw.UiTests;

public sealed class ControllerLedUiContractTests
{
    [Fact]
    public void Controller_page_exposes_only_one_global_static_led_editor()
    {
        var xaml = XDocument.Load(Source("src", "SteamInputAddonforClaw.UI", "Views", "ControllerPage.xaml"));
        var code = File.ReadAllText(Source("src", "SteamInputAddonforClaw.UI", "Views", "ControllerPage.xaml.cs"));
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var expanders = xaml.Descendants().Where(element => element.Name.LocalName == "SettingsExpander"
            && (string?)element.Attribute(x + "Name") == "ControllerLedExpander").ToArray();

        Assert.Single(expanders);
        Assert.Equal("Joystick LED", (string?)expanders[0].Attribute("Header"));
        Assert.Contains("Header=\"Enabled\"", xaml.ToString(), StringComparison.Ordinal);
        Assert.Contains("Header=\"Brightness\"", xaml.ToString(), StringComparison.Ordinal);
        Assert.Contains("Header=\"Color\"", xaml.ToString(), StringComparison.Ordinal);
        Assert.Contains("IsAlphaEnabled=\"False\"", xaml.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("Battery", xaml.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Breathing", xaml.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Wave", xaml.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("NavigationViewItem.*LED", xaml.ToString(), StringComparison.OrdinalIgnoreCase);
        var mainWindow = File.ReadAllText(Source("src", "SteamInputAddonforClaw.UI", "MainWindow.xaml"));
        Assert.DoesNotContain("ControllerLed", mainWindow, StringComparison.Ordinal);
        Assert.Contains("ApplyControllerLedSettings(bootstrap.Settings.ControllerLed)", code, StringComparison.Ordinal);
        Assert.Contains("ControllerLedEnabledToggle.IsOn = settings.Enabled", code, StringComparison.Ordinal);
        Assert.Contains("ControllerLedBrightnessSlider.IsEnabled = settings.Enabled", code, StringComparison.Ordinal);
        Assert.Contains("ControllerLedColorButton.IsEnabled = settings.Enabled", code, StringComparison.Ordinal);
        Assert.Contains("if (_isLoading || !_controllerLedAvailable", code, StringComparison.Ordinal);
        Assert.DoesNotContain("SetControllerLedSettingsAsync", code, StringComparison.Ordinal);
        Assert.DoesNotContain("HidDevice", code, StringComparison.Ordinal);
    }

    [Fact]
    public void Main_window_coalesces_whole_led_records_and_drains_pending_edits()
    {
        var source = File.ReadAllText(Source("src", "SteamInputAddonforClaw.UI", "MainWindow.xaml.cs"));
        Assert.Contains("Interval = TimeSpan.FromMilliseconds(200)", source, StringComparison.Ordinal);
        Assert.Contains("_controllerLedMutationTimer.Tick += ControllerLedMutationTimer_Tick", source, StringComparison.Ordinal);
        Assert.Contains("_controllerLedSaveChain = SaveControllerLedAfterAsync(_controllerLedSaveChain, _controllerLedUiSettings, version)", source, StringComparison.Ordinal);
        Assert.Contains("version != _controllerLedEditVersion", source, StringComparison.Ordinal);
        Assert.Contains("SetControllerLedSettingsAsync(next)", source, StringComparison.Ordinal);
        Assert.Contains("_controllerLedMutationTimer?.Stop()", source, StringComparison.Ordinal);
        Assert.Contains("var led = _controllerLedSaveChain", source, StringComparison.Ordinal);
        Assert.Contains("Task.WhenAll(front, back, led)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Window_close_leaves_led_timer_for_shutdown_drain_to_stop_and_flush()
    {
        var source = File.ReadAllText(Source("src", "SteamInputAddonforClaw.UI", "MainWindow.xaml.cs"));
        var closed = source[source.IndexOf("private void OnWindowClosed(", StringComparison.Ordinal)..];
        closed = closed[..closed.IndexOf("internal async Task CloseVibrationTestForUiShutdownAsync(", StringComparison.Ordinal)];
        var drain = source[source.IndexOf("internal Task DrainPendingControllerMappingSavesAsync(", StringComparison.Ordinal)..];
        drain = drain[..drain.IndexOf("private void ReturnToSettings(", StringComparison.Ordinal)];

        Assert.DoesNotContain("_controllerLedMutationTimer?.Stop()", closed, StringComparison.Ordinal);
        Assert.Contains("if (_controllerLedMutationTimer?.IsRunning == true)", drain, StringComparison.Ordinal);
        Assert.Contains("_controllerLedMutationTimer.Stop();", drain, StringComparison.Ordinal);
        Assert.Contains("_controllerLedSaveChain = SaveControllerLedAfterAsync", drain, StringComparison.Ordinal);
    }

    private static string Source(params string[] parts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "SteamInputAddonforClaw.slnx")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        return Path.Combine([directory!.FullName, .. parts]);
    }
}
