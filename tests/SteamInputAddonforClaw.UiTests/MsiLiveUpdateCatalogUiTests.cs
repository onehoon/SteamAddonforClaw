using System.Xml.Linq;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class MsiLiveUpdateCatalogUiTests
{
    [Fact]
    public void Catalog_has_a_dedicated_main_navigation_page()
    {
        var mainWindow = ReadSource("src", "SteamInputAddonforClaw.UI", "MainWindow.xaml");
        var devicePage = ReadSource("src", "SteamInputAddonforClaw.UI", "Views", "DevicePage.xaml");
        var msiPage = ReadSource("src", "SteamInputAddonforClaw.UI", "Views", "MsiUpdatesPage.xaml");
        var mainWindowCode = ReadSource("src", "SteamInputAddonforClaw.UI", "MainWindow.xaml.cs");
        var navigationState = ReadSource("src", "SteamInputAddonforClaw.UI", "MainNavigationState.cs");

        Assert.Contains("x:Uid=\"MainNav_MsiUpdates\" Content=\"MSI Update\" Tag=\"MsiUpdates\"", mainWindow, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"MsiUpdatesPageHost\"", mainWindow, StringComparison.Ordinal);
        Assert.DoesNotContain("MsiUpdatesExpander", devicePage, StringComparison.Ordinal);
        Assert.Contains("x:Uid=\"MsiUpdates_PageTitle\"", msiPage, StringComparison.Ordinal);
        Assert.Contains("MainNavigationPage.MsiUpdates", mainWindowCode, StringComparison.Ordinal);
        Assert.Contains("\"MsiUpdates\" => MainNavigationPage.MsiUpdates", navigationState, StringComparison.Ordinal);
    }

    [Fact]
    public void Catalog_query_is_manual_and_tabs_are_fixed()
    {
        var xaml = ReadSource("src", "SteamInputAddonforClaw.UI", "Views", "MsiUpdatesPage.xaml");
        var code = ReadSource("src", "SteamInputAddonforClaw.UI", "Views", "MsiUpdatesPage.xaml.cs");
        var click = ExtractMethod(code, "private async void MsiUpdatesCheckButton_Click(");

        Assert.Contains("Click=\"MsiUpdatesCheckButton_Click\"", xaml, StringComparison.Ordinal);
        Assert.Contains("IsAddTabButtonVisible=\"False\" CanReorderTabs=\"False\" CanDragTabs=\"False\"", xaml, StringComparison.Ordinal);
        Assert.Equal(4, xaml.Split("<TabViewItem ", StringSplitOptions.None).Length - 1);
        Assert.All(new[] { "Bios", "Firmware", "Drivers", "Utilities" }, name =>
            Assert.Contains($"MsiUpdates_{name}Tab", xaml, StringComparison.Ordinal));
        Assert.Contains("RequestMsiLiveUpdateCatalogAsync", click, StringComparison.Ordinal);
        Assert.DoesNotContain("Content=\"Install\"", xaml, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("MsiUpdatesInstall", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void Msi_catalog_strings_exist_in_both_supported_locales()
    {
        var english = ReadResources("en-US");
        var korean = ReadResources("ko-KR");
        var required = new[]
        {
            "MainNav_MsiUpdates.Content", "MsiUpdates_PageTitle.Text",
            "MsiUpdates_CatalogOnly.Text", "MsiUpdates_CheckButton.Content",
            "MsiUpdates_BiosTab.Header", "MsiUpdates_FirmwareTab.Header",
            "MsiUpdates_DriversTab.Header", "MsiUpdates_UtilitiesTab.Header",
            "MsiUpdates_Checking", "MsiUpdates_NotChecked", "MsiUpdates_Completed",
            "MsiUpdates_PartialFailure", "MsiUpdates_Unavailable", "MsiUpdates_Failed",
            "MsiUpdates_LastChecked", "MsiUpdates_CategoryFailed", "MsiUpdates_NoEntries",
            "MsiUpdates_NotCheckedCategory", "MsiUpdates_UnavailableCategory", "MsiUpdates_NoData",
            "MsiUpdates_LatestVersion", "MsiUpdates_ReleaseDate", "MsiUpdates_VersionNotProvided"
        };

        Assert.All(required, key =>
        {
            Assert.Contains(key, english);
            Assert.Contains(key, korean);
        });
    }

    [Fact]
    public void Catalog_description_decodes_entities_strips_markup_and_preserves_breaks()
    {
        var formatted = SteamInputAddonforClaw.Views.MsiUpdatesPage.FormatMsiCatalogDescription(
            "Steps: &lt;b&gt;Install&lt;/b&gt; ISH_SW&gt;OS Components.<br> 1. Run SetupISS.exe.<br/> 2. Restart.");

        Assert.Equal(
            string.Join(Environment.NewLine,
                "Steps: Install ISH_SW>OS Components.",
                "1. Run SetupISS.exe.",
                "2. Restart."),
            formatted);
    }

    [Fact]
    public void Catalog_entries_use_the_app_theme_card_style()
    {
        var xaml = ReadSource("src", "SteamInputAddonforClaw.UI", "Views", "MsiUpdatesPage.xaml");
        var code = ReadSource("src", "SteamInputAddonforClaw.UI", "Views", "MsiUpdatesPage.xaml.cs");

        Assert.Contains("x:Key=\"MsiCatalogEntryCardStyle\"", xaml, StringComparison.Ordinal);
        Assert.Contains("{ThemeResource CardBackgroundFillColorDefaultBrush}", xaml, StringComparison.Ordinal);
        Assert.Contains("{ThemeResource CardStrokeColorDefaultBrush}", xaml, StringComparison.Ordinal);
        Assert.Contains("Style = (Style)Resources[\"MsiCatalogEntryCardStyle\"]", code, StringComparison.Ordinal);
        Assert.Contains("Text = FormatMsiCatalogDescription(entry.Description)", code, StringComparison.Ordinal);
    }

    private static string[] ReadResources(string culture)
    {
        var document = XDocument.Load(Path.Combine(FindRepositoryRoot(), "src", "SteamInputAddonforClaw.UI", "Strings", culture, "Resources.resw"));
        return document.Root!.Elements("data").Select(element => (string)element.Attribute("name")!).ToArray();
    }

    private static string ExtractMethod(string source, string signature)
    {
        var start = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Method signature not found: {signature}");
        var openBrace = source.IndexOf('{', start);
        var depth = 0;
        for (var index = openBrace; index < source.Length; index++)
        {
            if (source[index] == '{') depth++;
            else if (source[index] == '}' && --depth == 0) return source[start..(index + 1)];
        }
        throw new InvalidOperationException($"Method body was not closed: {signature}");
    }

    private static string ReadSource(params string[] pathParts) => File.ReadAllText(Path.Combine(FindRepositoryRoot(), Path.Combine(pathParts)));

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "SteamInputAddonforClaw.slnx"))) return directory.FullName;
        }
        throw new DirectoryNotFoundException("Repository root was not found.");
    }
}
