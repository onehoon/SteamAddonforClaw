using System.Xml.Linq;
using SteamInputAddonforClaw.Contracts.Frontend;
using SteamInputAddonforClaw.Views;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class SettingsPageLoggingTests
{
    [Fact]
    public void Settings_page_owns_a_compact_logging_card_between_power_source_and_components()
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
        Assert.True(xaml.IndexOf("x:Name=\"QuickSettingsPowerSourceCard\"", StringComparison.Ordinal)
            < xaml.IndexOf("Header=\"Logging\"", StringComparison.Ordinal));
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

        Assert.Contains("_lastKnownLogLevel = bootstrap.Settings.LogLevel", source, StringComparison.Ordinal);
        Assert.Contains("_logDirectoryPath = bootstrap.LogDirectoryPath", source, StringComparison.Ordinal);
        Assert.Contains("_frontend.SetLogLevelAsync(level)", source, StringComparison.Ordinal);
        Assert.Contains("_lastKnownLogLevel = result.LogLevel", source, StringComparison.Ordinal);
        Assert.Contains("await RefreshLoggingStateAsync()", source, StringComparison.Ordinal);
        Assert.Contains("_frontend!.GetBootstrapAsync()", source, StringComparison.Ordinal);
        Assert.Contains("SetLogLevel(_lastKnownLogLevel)", source, StringComparison.Ordinal);
        Assert.Contains("ProcessStartInfo(\"explorer.exe\", $\"\\\"{_logDirectoryPath}\\\"\")", source, StringComparison.Ordinal);
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
            "Header=\"PID1902 Input Cadence\"",
            "Header=\"Vibration Test\"",
            "Header=\"Gyro / Sensor Test\"",
            "Header=\"Fan Hardware Probe\"",
            "Header=\"Battery Charge Limit Test\"",
            "Header=\"GameInput System Button Probe\"",
        })
        {
            Assert.Contains(diagnostic, xaml, StringComparison.Ordinal);
        }
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
