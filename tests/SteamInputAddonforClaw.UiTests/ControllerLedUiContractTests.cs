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
        var ledExpanders = xaml.Descendants().Where(element => element.Name.LocalName == "SettingsExpander"
            && (string?)element.Attribute("Header") == "Joystick LED").ToArray();
        var expanders = xaml.Descendants().Where(element => element.Name.LocalName == "SettingsExpander"
            && (string?)element.Attribute(x + "Name") == "ControllerLedExpander").ToArray();

        Assert.Single(ledExpanders);
        Assert.Single(expanders);
        Assert.Equal("Joystick LED", (string?)expanders[0].Attribute("Header"));
        var directHeaderToggle = expanders[0].Elements().SingleOrDefault(element => element.Name.LocalName == "ToggleSwitch"
            && (string?)element.Attribute(x + "Name") == "ControllerLedEnabledToggle");
        Assert.NotNull(directHeaderToggle);
        var expanderItems = expanders[0].Elements().Single(element => element.Name.LocalName == "SettingsExpander.Items");
        var itemHeaders = expanderItems.Elements().Where(element => element.Name.LocalName == "SettingsCard")
            .Select(element => (string?)element.Attribute("Header")).ToArray();
        Assert.Equal(new[] { "Brightness", "Color" }, itemHeaders);
        Assert.DoesNotContain("Enabled", itemHeaders);
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

    [Fact]
    public void Developer_vibration_test_page_wires_explicit_A2VM_LED_read_only_action()
    {
        var xaml = XDocument.Load(Source("src", "SteamInputAddonforClaw.UI", "Views", "VibrationTestPage.xaml"));
        var code = File.ReadAllText(Source("src", "SteamInputAddonforClaw.UI", "Views", "VibrationTestPage.xaml.cs"));
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var button = xaml.Descendants().Single(element =>
            (string?)element.Attribute(x + "Name") == "ReadA2vmLedProfileProbeButton");

        Assert.Equal("Read 0x024A Profile", (string?)button.Attribute("Content"));
        Assert.Equal("ReadA2vmLedProfileProbe_Click", (string?)button.Attribute("Click"));
        Assert.Contains("private async void ReadA2vmLedProfileProbe_Click", code, StringComparison.Ordinal);
        Assert.Contains("RunControllerLedProfileReadProbeAsync()", code, StringComparison.Ordinal);
        Assert.Contains("Stop the Xbox360 terminal STOP loop before running a controller profile probe.", code, StringComparison.Ordinal);
        Assert.Contains("No LED profile write or SyncToROM command is issued", xaml.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("RunControllerLedProfileReadProbeAsync", code[..code.IndexOf("private async Task RunA2vmLedProfileReadProbeAsync", StringComparison.Ordinal)], StringComparison.Ordinal);
        Assert.Contains("Restore Addon default 50 / 50", xaml.ToString(), StringComparison.Ordinal);
        Assert.Contains("physical effect has not yet been verified on A2VM", File.ReadAllText(Source("src", "SteamInputAddonforClaw", "Frontend", "InProcessAddonFrontendControl.cs")), StringComparison.Ordinal);
        Assert.Contains("does not authorize color mutation", xaml.ToString(), StringComparison.Ordinal);
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
