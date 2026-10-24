using System.Xml.Linq;
using SteamInputAddonforClaw.Contracts.Frontend;
using SteamInputAddonforClaw.Views;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class SettingsPageLoggingTests
{
    [Fact]
    public void Settings_page_owns_a_compact_logging_card_before_components()
    {
        var root = FindRepositoryRoot();
        var xamlPath = Path.Combine(root, "src", "SteamInputAddonforClaw.UI", "Views", "SettingsPage.xaml");
        var xaml = File.ReadAllText(xamlPath);
        var document = XDocument.Load(xamlPath);
        var loggingCard = document.Descendants()
            .Single(element => element.Name.LocalName == "SettingsCard"
                && (string?)element.Attribute("Header") == "Logging");

        Assert.Equal("Choose log detail.", (string?)loggingCard.Attribute("Description"));

        var controlRow = loggingCard.Descendants().Single(element => element.Name.LocalName == "StackPanel");
        Assert.Equal(
            new[] { "ComboBox", "Button" },
            controlRow.Elements().Select(element => element.Name.LocalName));

        var comboBox = controlRow.Elements().First();
        Assert.Equal(
            new[] { "Off", "Info", "Debug" },
            comboBox.Elements()
                .Where(element => element.Name.LocalName == "ComboBoxItem")
                .Select(element => (string?)element.Attribute("Content")));
        Assert.Equal("120", (string?)comboBox.Attribute("Width"));
        Assert.Equal("Open folder", (string?)controlRow.Elements().Last().Attribute("Content"));
        Assert.Contains("Click=\"OpenLogFolderButton_Click\"", xaml, StringComparison.Ordinal);

        Assert.DoesNotContain("SelectedIndex=", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("QuickSettingsPowerSourceCard", xaml, StringComparison.Ordinal);
        Assert.True(xaml.IndexOf("Header=\"Logging\"", StringComparison.Ordinal)
            < xaml.IndexOf("x:Name=\"RequiredComponentsExpander\"", StringComparison.Ordinal));
        Assert.True(xaml.IndexOf("x:Name=\"RequiredComponentsExpander\"", StringComparison.Ordinal)
            < xaml.IndexOf("x:Name=\"DeveloperMenuCard\"", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(FrontendLogLevel.Off, 0)]
    [InlineData(FrontendLogLevel.Info, 1)]
    [InlineData(FrontendLogLevel.Debug, 2)]
    public void Settings_logging_selection_maps_the_authoritative_frontend_level(
        FrontendLogLevel level,
        int expectedIndex)
    {
        Assert.Equal(expectedIndex, SettingsPage.GetLogLevelSelectionIndex(level));
    }

    [Fact]
    public void Settings_logging_uses_bootstrap_mutation_and_authoritative_rollback()
    {
        var source = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(), "src", "SteamInputAddonforClaw.UI", "Views", "SettingsPage.xaml.cs"));
        var mutationStart = source.IndexOf("private async void LogLevelComboBox_SelectionChanged(", StringComparison.Ordinal);
        var refreshStart = source.IndexOf("private async Task RefreshLoggingStateAsync()", mutationStart, StringComparison.Ordinal);
        var openFolderStart = source.IndexOf("private void OpenLogFolderButton_Click(", refreshStart, StringComparison.Ordinal);
        var mutation = source[mutationStart..refreshStart];
        var refresh = source[refreshStart..openFolderStart];

        var mutationResult = mutation.IndexOf("await _frontend.SetLogLevelAsync(level)", StringComparison.Ordinal);
        var applyAuthoritativeResult = mutation.IndexOf("UiLog.SetMinimumLevel(result.LogLevel)", StringComparison.Ordinal);
        Assert.True(mutationResult >= 0 && mutationResult < applyAuthoritativeResult);
        Assert.Contains("_lastKnownLogLevel = result.LogLevel", mutation, StringComparison.Ordinal);
        Assert.DoesNotContain("UiLog.SetMinimumLevel(level)", mutation, StringComparison.Ordinal);
        Assert.Contains("await RefreshLoggingStateAsync()", mutation, StringComparison.Ordinal);

        var bootstrapRead = refresh.IndexOf("await _frontend!.GetBootstrapAsync()", StringComparison.Ordinal);
        var applyBootstrapLevel = refresh.IndexOf("UiLog.SetMinimumLevel(_lastKnownLogLevel)", StringComparison.Ordinal);
        Assert.True(bootstrapRead >= 0 && bootstrapRead < applyBootstrapLevel);
        Assert.Contains("_lastKnownLogLevel = bootstrap.Settings.LogLevel", refresh, StringComparison.Ordinal);
        Assert.Contains("_logDirectoryPath = bootstrap.LogDirectoryPath", refresh, StringComparison.Ordinal);
        Assert.Contains("SetLogLevel(_lastKnownLogLevel)", refresh, StringComparison.Ordinal);
        var refreshFailure = refresh.IndexOf("catch (Exception refreshException)", StringComparison.Ordinal);
        Assert.True(refreshFailure >= 0);
        Assert.Contains("SetLogLevel(_lastKnownLogLevel)", refresh[refreshFailure..], StringComparison.Ordinal);
        Assert.DoesNotContain("UiLog.SetMinimumLevel(", refresh[refreshFailure..], StringComparison.Ordinal);
        Assert.Contains("ProcessStartInfo(\"explorer.exe\", $\"\\\"{_logDirectoryPath}\\\"\")", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Child_startup_and_authoritative_bootstrap_apply_log_level_before_logging()
    {
        var root = FindRepositoryRoot();
        var uiProgram = File.ReadAllText(Path.Combine(root, "src", "SteamInputAddonforClaw.UI", "Program.cs"));
        var uiApp = File.ReadAllText(Path.Combine(root, "src", "SteamInputAddonforClaw.UI", "App.xaml.cs"));
        var overlayProgram = File.ReadAllText(Path.Combine(root, "src", "SteamInputAddonforClaw.Overlay", "Program.cs"));
        var overlayApp = File.ReadAllText(Path.Combine(root, "src", "SteamInputAddonforClaw.Overlay", "App.xaml.cs"));

        Assert.True(uiProgram.IndexOf("UiLog.SetMinimumLevel(FrontendLaunchArguments.ResolveLogLevel(args))", StringComparison.Ordinal)
            < uiProgram.IndexOf("UiLog.Debug(", StringComparison.Ordinal));
        Assert.True(overlayProgram.IndexOf("OverlayLog.SetMinimumLevel(FrontendLaunchArguments.ResolveLogLevel(args))", StringComparison.Ordinal)
            < overlayProgram.IndexOf("OverlayLog.Info(", StringComparison.Ordinal));
        Assert.True(uiApp.IndexOf("UiLog.SetMinimumLevel(bootstrap.Settings.LogLevel)", StringComparison.Ordinal)
            < uiApp.IndexOf("AppLog.Info(\"Frontend\", \"Bootstrap acquired.\")", StringComparison.Ordinal));

        var settingsHandlerStart = overlayApp.IndexOf("private void OnFrontendSettingsStateReceived(", StringComparison.Ordinal);
        var settingsHandlerEnd = overlayApp.IndexOf("private void OnControllerVibrationStateReceived(", settingsHandlerStart, StringComparison.Ordinal);
        var settingsHandler = overlayApp[settingsHandlerStart..settingsHandlerEnd];
        Assert.Contains("settingsAvailable && Enum.IsDefined(settings.LogLevel)", settingsHandler, StringComparison.Ordinal);
        Assert.True(settingsHandler.IndexOf("OverlayLog.SetMinimumLevel(settings.LogLevel)", StringComparison.Ordinal)
            < settingsHandler.IndexOf("_dispatcherQueue?.TryEnqueue", StringComparison.Ordinal));
    }

    [Fact]
    public void Developer_menu_keeps_folder_access_and_diagnostics_but_no_longer_owns_log_level()
    {
        var root = FindRepositoryRoot();
        var xaml = File.ReadAllText(Path.Combine(root, "src", "SteamInputAddonforClaw.UI", "Views", "DeveloperPage.xaml"));
        var source = File.ReadAllText(Path.Combine(root, "src", "SteamInputAddonforClaw.UI", "Views", "DeveloperPage.xaml.cs"));

        Assert.DoesNotContain("Header=\"Logging\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("LogLevelComboBox", xaml, StringComparison.Ordinal);
        Assert.Contains("Content=\"Log Folder\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Click=\"OpenLogFolderButton_Click\"", xaml, StringComparison.Ordinal);
        Assert.Contains("_logDirectoryPath = bootstrap.LogDirectoryPath", source, StringComparison.Ordinal);
        Assert.DoesNotContain("_isInitializingLogLevel", source, StringComparison.Ordinal);
        Assert.DoesNotContain("_lastKnownLogLevel", source, StringComparison.Ordinal);
        Assert.DoesNotContain("LogLevelComboBox_SelectionChanged", source, StringComparison.Ordinal);
        Assert.DoesNotContain("SetLogLevel(", source, StringComparison.Ordinal);
        Assert.DoesNotContain("RefreshAuthoritativeStateAsync", source, StringComparison.Ordinal);

        foreach (var diagnostic in new[]
        {
            "Text=\"Environment Discovery\"",
            "Header=\"Gyro / Sensor Test\"",
            "Header=\"Fan Hardware Probe\"",
        })
        {
            Assert.Contains(diagnostic, xaml, StringComparison.Ordinal);
        }
        Assert.DoesNotContain("Header=\"Vibration Profile 0/100 Probe\"", xaml, StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "SteamInputAddonforClaw.slnx")))
            directory = directory.Parent;

        Assert.NotNull(directory);
        return directory!.FullName;
    }
}
