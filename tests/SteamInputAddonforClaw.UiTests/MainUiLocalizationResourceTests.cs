using System.Xml.Linq;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class MainUiLocalizationResourceTests
{
    private static readonly XNamespace XamlNamespace = "http://schemas.microsoft.com/winfx/2006/xaml";

    [Fact]
    public void Main_ui_resw_files_share_keys_and_include_nonempty_pilot_resources()
    {
        var stringsDirectory = Path.Combine(FindRepositoryRoot(), "src", "SteamInputAddonforClaw.UI", "Strings");
        var languages = Directory.EnumerateDirectories(stringsDirectory)
            .Select(Path.GetFileName)
            .OrderBy(language => language, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(new[] { "en-US", "ko-KR" }, languages);

        var english = ReadResources(Path.Combine(stringsDirectory, "en-US", "Resources.resw"));
        var korean = ReadResources(Path.Combine(stringsDirectory, "ko-KR", "Resources.resw"));
        var requiredPilotKeys = new[]
        {
            "MainNav_Device.Content",
            "HowToUse_Loading.Text",
            "Settings_AppTheme_Description"
        };

        Assert.Equal(english.Keys.OrderBy(key => key, StringComparer.Ordinal), korean.Keys.OrderBy(key => key, StringComparer.Ordinal));
        foreach (var key in requiredPilotKeys)
        {
            Assert.Contains(key, english.Keys);
        }
        Assert.All(english.Values, value => Assert.False(string.IsNullOrWhiteSpace(value)));
        Assert.All(korean.Values, value => Assert.False(string.IsNullOrWhiteSpace(value)));
    }

    [Fact]
    public void Device_navigation_localizes_only_content_and_keeps_its_stable_identity()
    {
        var root = FindRepositoryRoot();
        var document = XDocument.Load(Path.Combine(root, "src", "SteamInputAddonforClaw.UI", "MainWindow.xaml"));
        var device = Assert.Single(document.Descendants(), element =>
            (string?)element.Attribute(XamlNamespace + "Name") == "DeviceNavigationItem");

        Assert.Equal("MainNav_Device", (string?)device.Attribute(XamlNamespace + "Uid"));
        Assert.Equal("Device", (string?)device.Attribute("Content"));
        Assert.Equal("Device", (string?)device.Attribute("Tag"));
    }

    [Fact]
    public void How_to_use_loading_localizes_only_the_existing_fallback_text()
    {
        var root = FindRepositoryRoot();
        var document = XDocument.Load(Path.Combine(root, "src", "SteamInputAddonforClaw.UI", "Views", "HowToUsePage.xaml"));
        var loading = Assert.Single(document.Descendants(), element =>
            element.Name.LocalName == "TextBlock" && (string?)element.Attribute("Text") == "Loading documentation…");

        Assert.Equal("HowToUse_Loading", (string?)loading.Attribute(XamlNamespace + "Uid"));
        Assert.Equal("Loading documentation…", (string?)loading.Attribute("Text"));
    }

    [Fact]
    public void App_theme_description_lookup_keeps_the_existing_failure_priority_and_English_fallback()
    {
        var source = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "src", "SteamInputAddonforClaw.UI", "Views", "SettingsPage.xaml.cs"));

        Assert.Contains("private const string DefaultAppThemeDescription = \"Choose the main app appearance.\";", source, StringComparison.Ordinal);
        Assert.Contains("AppThemeCard.Description = failureDescription ?? GetDefaultAppThemeDescription();", source, StringComparison.Ordinal);
        Assert.Contains("new ResourceManager(priPath)", source, StringComparison.Ordinal);
        Assert.Contains("GetValue(\"Resources/Settings_AppTheme_Description\")", source, StringComparison.Ordinal);
        Assert.Contains("catch (Exception)", source, StringComparison.Ordinal);
        Assert.Contains("return DefaultAppThemeDescription;", source, StringComparison.Ordinal);
    }

    private static Dictionary<string, string> ReadResources(string path)
    {
        var document = XDocument.Load(path);
        return document.Root!
            .Elements("data")
            .ToDictionary(
                element => (string)element.Attribute("name")!,
                element => element.Element("value")!.Value,
                StringComparer.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "SteamInputAddonforClaw.slnx")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root was not found.");
    }
}
