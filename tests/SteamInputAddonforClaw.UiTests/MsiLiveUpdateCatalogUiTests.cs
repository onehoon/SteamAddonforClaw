using System.Xml.Linq;
using SteamInputAddonforClaw.Contracts.Frontend;
using SteamInputAddonforClaw.Views;
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
    public void Catalog_layout_uses_one_page_scroll_and_equal_fixed_tabs()
    {
        var xaml = ReadMsiPageXaml();
        XNamespace xNamespace = "http://schemas.microsoft.com/winfx/2006/xaml";
        var scrollViewers = xaml.Descendants().Where(element => element.Name.LocalName == "ScrollViewer").ToArray();
        var tabView = xaml.Descendants().Single(element => element.Name.LocalName == "TabView");
        var tabItems = tabView.Elements().Where(element => element.Name.LocalName == "TabViewItem").ToArray();
        var header = xaml.Descendants().Single(element => (string?)element.Attribute(xNamespace + "Name") == "MsiUpdatesHeader");
        var headerColumns = header.Elements().Single(element => element.Name.LocalName == "Grid.ColumnDefinitions")
            .Elements().Select(element => (string?)element.Attribute("Width")).ToArray();
        var headerTemplate = xaml.Descendants().Single(element => (string?)element.Attribute(xNamespace + "Key") == "MsiUpdatesTabHeaderTemplate");
        var templateText = headerTemplate.Descendants().Single(element => element.Name.LocalName == "TextBlock");

        Assert.Single(scrollViewers);
        Assert.Equal("MsiUpdatesScrollViewer", (string?)scrollViewers[0].Attribute(xNamespace + "Name"));
        Assert.DoesNotContain(tabView.Descendants(), element => element.Name.LocalName == "ScrollViewer");
        Assert.DoesNotContain(xaml.Descendants().Attributes(), attribute => attribute.Name.LocalName == "MaxHeight");
        Assert.DoesNotContain(xaml.Descendants(), element => element.Name.LocalName == "SettingsCard");
        Assert.Equal("Equal", (string?)tabView.Attribute("TabWidthMode"));
        Assert.Equal("Stretch", (string?)tabView.Attribute("HorizontalAlignment"));
        Assert.Equal(new[] { "*", "Auto" }, headerColumns);
        Assert.Equal("0", (string?)header.Descendants().Single(element => (string?)element.Attribute(xNamespace + "Name") == "MsiUpdatesPageTitle").Attribute("Grid.Column"));
        Assert.Equal("1", (string?)header.Descendants().Single(element => (string?)element.Attribute(xNamespace + "Name") == "MsiUpdatesCheckButton").Attribute("Grid.Column"));
        Assert.Equal(4, tabItems.Length);
        Assert.All(tabItems, item =>
        {
            Assert.Equal("{StaticResource MsiUpdatesTabHeaderTemplate}", (string?)item.Attribute("HeaderTemplate"));
            var content = item.Elements().Single(element => element.Name.LocalName == "StackPanel");
            Assert.Equal("16", (string?)content.Attribute("Margin"));
            Assert.Equal("8", (string?)content.Attribute("Spacing"));
            Assert.Equal("Stretch", (string?)content.Attribute("HorizontalAlignment"));
        });
        Assert.Equal("Center", (string?)templateText.Attribute("TextAlignment"));
        Assert.Equal("Stretch", (string?)templateText.Attribute("HorizontalAlignment"));
        Assert.Equal("8", (string?)templateText.Attribute("Padding"));
        Assert.Contains("IsAddTabButtonVisible=\"False\" CanReorderTabs=\"False\" CanDragTabs=\"False\"", xaml.ToString(SaveOptions.DisableFormatting), StringComparison.Ordinal);
    }

    [Fact]
    public void Catalog_query_is_manual_and_uses_the_existing_cancellable_frontend_request()
    {
        var xaml = ReadSource("src", "SteamInputAddonforClaw.UI", "Views", "MsiUpdatesPage.xaml");
        var code = ReadSource("src", "SteamInputAddonforClaw.UI", "Views", "MsiUpdatesPage.xaml.cs");
        var click = ExtractMethod(code, "private async void MsiUpdatesCheckButton_Click(");

        Assert.Contains("Click=\"MsiUpdatesCheckButton_Click\"", xaml, StringComparison.Ordinal);
        Assert.Contains("IsAddTabButtonVisible=\"False\"", xaml, StringComparison.Ordinal);
        Assert.Equal(4, xaml.Split("<TabViewItem ", StringSplitOptions.None).Length - 1);
        Assert.All(new[] { "Bios", "Firmware", "Drivers", "Utilities" }, name =>
            Assert.Contains($"MsiUpdates_{name}Tab", xaml, StringComparison.Ordinal));
        Assert.Contains("RequestMsiLiveUpdateCatalogAsync(requestCancellation.Token)", click, StringComparison.Ordinal);
        Assert.Contains("_msiUpdatesSnapshot = CreateNotCheckedSnapshot()", click, StringComparison.Ordinal);
        Assert.DoesNotContain("Content=\"Install\"", xaml, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("MsiUpdatesInstall", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void Catalog_fresh_entry_and_in_flight_navigation_reset_and_discard_stale_results()
    {
        var pageCode = ReadSource("src", "SteamInputAddonforClaw.UI", "Views", "MsiUpdatesPage.xaml.cs");
        var mainWindowCode = ReadSource("src", "SteamInputAddonforClaw.UI", "MainWindow.xaml.cs");
        var activate = ExtractMethod(pageCode, "internal void Activate()");
        var deactivate = ExtractMethod(pageCode, "internal void Deactivate()");
        var click = ExtractMethod(pageCode, "private async void MsiUpdatesCheckButton_Click(");
        var isCurrentRequest = ExtractMethod(pageCode, "private bool IsCurrentRequest(");
        var showPage = ExtractMethod(mainWindowCode, "private void ShowPage(");

        Assert.Contains("var wasMsiUpdates = MsiUpdatesPageHost.Visibility == Visibility.Visible", showPage, StringComparison.Ordinal);
        Assert.Contains("page == MainNavigationPage.MsiUpdates && !wasMsiUpdates", showPage, StringComparison.Ordinal);
        Assert.Contains("_msiUpdatesContent!.Activate()", showPage, StringComparison.Ordinal);
        Assert.Contains("wasMsiUpdates && page != MainNavigationPage.MsiUpdates", showPage, StringComparison.Ordinal);
        Assert.Contains("_msiUpdatesContent?.Deactivate()", showPage, StringComparison.Ordinal);
        Assert.Contains("_msiUpdatesSnapshot = CreateNotCheckedSnapshot()", activate, StringComparison.Ordinal);
        Assert.Contains("MsiUpdatesTabView.SelectedIndex = 0", activate, StringComparison.Ordinal);
        Assert.DoesNotContain("RequestMsiLiveUpdateCatalogAsync", activate, StringComparison.Ordinal);
        Assert.Contains("requestCancellation?.Cancel()", deactivate, StringComparison.Ordinal);
        Assert.Contains("_requestCancellation = null", deactivate, StringComparison.Ordinal);
        Assert.Contains("ReferenceEquals(_requestCancellation, requestCancellation)", isCurrentRequest, StringComparison.Ordinal);
        Assert.Contains("catch (OperationCanceledException) when (requestCancellation.IsCancellationRequested)", click, StringComparison.Ordinal);
        Assert.DoesNotContain("MsiUpdates_LastChecked", pageCode, StringComparison.Ordinal);
        Assert.DoesNotContain("DispatcherTimer", pageCode, StringComparison.Ordinal);
        Assert.DoesNotContain("System.Threading.Timer", pageCode, StringComparison.Ordinal);
    }

    [Fact]
    public void Catalog_category_counts_only_appear_for_success_or_empty_results()
    {
        var success = new FrontendMsiLiveUpdateCatalogCategorySnapshot(
            FrontendMsiLiveUpdateCatalogCategory.Driver,
            FrontendMsiLiveUpdateCatalogCategoryState.Success,
            [
                new FrontendMsiLiveUpdateCatalogEntry("A", "1", null, null),
                new FrontendMsiLiveUpdateCatalogEntry("B", "2", null, null)
            ]);
        var empty = new FrontendMsiLiveUpdateCatalogCategorySnapshot(
            FrontendMsiLiveUpdateCatalogCategory.Driver,
            FrontendMsiLiveUpdateCatalogCategoryState.Empty,
            []);
        var failed = new FrontendMsiLiveUpdateCatalogCategorySnapshot(
            FrontendMsiLiveUpdateCatalogCategory.Driver,
            FrontendMsiLiveUpdateCatalogCategoryState.Failed,
            []);

        Assert.Equal("드라이버 (2)", MsiUpdatesPage.FormatCategoryHeader("드라이버", success));
        Assert.Equal("드라이버 (0)", MsiUpdatesPage.FormatCategoryHeader("드라이버", empty));
        Assert.Equal("드라이버", MsiUpdatesPage.FormatCategoryHeader("드라이버", failed));
        Assert.Equal("드라이버", MsiUpdatesPage.FormatCategoryHeader("드라이버", null));
    }

    [Fact]
    public void Catalog_release_date_trims_only_a_valid_iso_date_time_suffix()
    {
        Assert.Equal("2026-10-18", MsiUpdatesPage.FormatCatalogReleaseDate(" 2026-10-18T09:16:00Z "));
        Assert.Equal("2026-10-18", MsiUpdatesPage.FormatCatalogReleaseDate("2026-10-18"));
        Assert.Equal("2026-02-30T09:16:00Z", MsiUpdatesPage.FormatCatalogReleaseDate("2026-02-30T09:16:00Z"));
        Assert.Equal("18/10/2026 09:16", MsiUpdatesPage.FormatCatalogReleaseDate("18/10/2026 09:16"));
    }

    [Fact]
    public void Catalog_resources_are_localized_and_obsolete_visible_copy_is_removed()
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
            "MsiUpdates_CategoryFailed", "MsiUpdates_NoEntries",
            "MsiUpdates_NotCheckedCategory", "MsiUpdates_UnavailableCategory", "MsiUpdates_NoData",
            "MsiUpdates_LatestVersion", "MsiUpdates_ReleaseDate"
        };

        Assert.Equal(english.OrderBy(key => key, StringComparer.Ordinal), korean.OrderBy(key => key, StringComparer.Ordinal));
        Assert.Equal(english.Length, english.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(korean.Length, korean.Distinct(StringComparer.Ordinal).Count());
        Assert.All(required, key =>
        {
            Assert.Contains(key, english);
            Assert.Contains(key, korean);
        });
        Assert.DoesNotContain("MsiUpdates_LastChecked", english);
        Assert.DoesNotContain("MsiUpdates_LastChecked", korean);
        Assert.DoesNotContain("MsiUpdates_VersionNotProvided", english);
        Assert.DoesNotContain("MsiUpdates_VersionNotProvided", korean);

        var englishValues = ReadResourceMap("en-US");
        var koreanValues = ReadResourceMap("ko-KR");
        Assert.All(englishValues, item => Assert.False(string.IsNullOrWhiteSpace(item.Value), item.Key));
        Assert.All(koreanValues, item => Assert.False(string.IsNullOrWhiteSpace(item.Value), item.Key));
    }

    [Fact]
    public void Catalog_cards_are_compact_theme_aware_and_do_not_render_descriptions()
    {
        var xaml = ReadSource("src", "SteamInputAddonforClaw.UI", "Views", "MsiUpdatesPage.xaml");
        var code = ReadSource("src", "SteamInputAddonforClaw.UI", "Views", "MsiUpdatesPage.xaml.cs");

        Assert.Contains("x:Key=\"MsiCatalogEntryCardStyle\"", xaml, StringComparison.Ordinal);
        Assert.Contains("{ThemeResource CardBackgroundFillColorDefaultBrush}", xaml, StringComparison.Ordinal);
        Assert.Contains("{ThemeResource CardStrokeColorDefaultBrush}", xaml, StringComparison.Ordinal);
        Assert.Contains("Style = (Style)Resources[\"MsiCatalogEntryCardStyle\"]", code, StringComparison.Ordinal);
        Assert.Contains("ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) })", code, StringComparison.Ordinal);
        Assert.Contains("ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto })", code, StringComparison.Ordinal);
        Assert.Contains("TextWrapping = TextWrapping.Wrap", code, StringComparison.Ordinal);
        Assert.Contains("if (!string.IsNullOrWhiteSpace(entry.Version))", code, StringComparison.Ordinal);
        Assert.Contains("if (!string.IsNullOrWhiteSpace(entry.ReleaseDate))", code, StringComparison.Ordinal);
        Assert.Contains("string.Join(\" · \", metadata)", code, StringComparison.Ordinal);
        Assert.DoesNotContain("FormatMsiCatalogDescription", code, StringComparison.Ordinal);
        Assert.DoesNotContain("entry.Description", code, StringComparison.Ordinal);
        Assert.DoesNotContain("MsiUpdatesLastCheckedText", xaml + code, StringComparison.Ordinal);
    }

    private static XDocument ReadMsiPageXaml() =>
        XDocument.Load(Path.Combine(FindRepositoryRoot(), "src", "SteamInputAddonforClaw.UI", "Views", "MsiUpdatesPage.xaml"));

    private static string[] ReadResources(string culture)
    {
        var document = XDocument.Load(Path.Combine(FindRepositoryRoot(), "src", "SteamInputAddonforClaw.UI", "Strings", culture, "Resources.resw"));
        return document.Root!.Elements("data").Select(element => (string)element.Attribute("name")!).ToArray();
    }

    private static Dictionary<string, string> ReadResourceMap(string culture)
    {
        var document = XDocument.Load(Path.Combine(FindRepositoryRoot(), "src", "SteamInputAddonforClaw.UI", "Strings", culture, "Resources.resw"));
        return document.Root!.Elements("data").ToDictionary(
            element => (string)element.Attribute("name")!,
            element => element.Element("value")?.Value ?? string.Empty,
            StringComparer.Ordinal);
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
