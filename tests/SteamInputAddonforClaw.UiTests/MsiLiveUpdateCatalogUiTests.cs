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

        Assert.Contains("x:Uid=\"MainNav_MsiUpdates\"", mainWindow, StringComparison.Ordinal);
        Assert.Contains("Tag=\"MsiUpdates\"", mainWindow, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"MsiUpdatesPageHost\"", mainWindow, StringComparison.Ordinal);
        Assert.DoesNotContain("MsiUpdatesExpander", devicePage, StringComparison.Ordinal);
        Assert.Contains("x:Uid=\"MsiUpdates_PageTitle\"", msiPage, StringComparison.Ordinal);
        Assert.Contains("MainNavigationPage.MsiUpdates", mainWindowCode, StringComparison.Ordinal);
        Assert.Contains("\"MsiUpdates\" => MainNavigationPage.MsiUpdates", navigationState, StringComparison.Ordinal);
    }


    [Fact]
    public void Catalog_query_is_manual_and_uses_the_existing_cancellable_frontend_request()
    {
        var xaml = ReadSource("src", "SteamInputAddonforClaw.UI", "Views", "MsiUpdatesPage.xaml");
        var code = ReadSource("src", "SteamInputAddonforClaw.UI", "Views", "MsiUpdatesPage.xaml.cs");
        var click = ExtractMethod(code, "private async void MsiUpdatesCheckButton_Click(");

        Assert.Contains("Click=\"MsiUpdatesCheckButton_Click\"", xaml, StringComparison.Ordinal);
        Assert.Contains("IsAddTabButtonVisible=\"False\"", xaml, StringComparison.Ordinal);
        foreach (var category in new[] { "Bios", "Firmware", "Drivers", "Utilities" })
            Assert.Contains($"MsiUpdates_{category}Tab", xaml, StringComparison.Ordinal);
        Assert.Contains("RequestMsiLiveUpdateCatalogAsync(requestCancellation.Token)", click, StringComparison.Ordinal);
        Assert.Contains("_msiUpdatesSnapshot = CreateNotCheckedSnapshot()", click, StringComparison.Ordinal);
        Assert.DoesNotContain("MsiUpdatesInstall", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("InstallCommand", code, StringComparison.Ordinal);
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
    public void Catalog_category_count_is_derived_only_from_successful_or_empty_results()
    {
        var success = new FrontendMsiLiveUpdateCatalogCategorySnapshot(
            FrontendMsiLiveUpdateCatalogCategory.Driver, FrontendMsiLiveUpdateCatalogCategoryState.Success,
            [new FrontendMsiLiveUpdateCatalogEntry("A", "1", null, null), new FrontendMsiLiveUpdateCatalogEntry("B", "2", null, null)]);
        var empty = new FrontendMsiLiveUpdateCatalogCategorySnapshot(
            FrontendMsiLiveUpdateCatalogCategory.Driver, FrontendMsiLiveUpdateCatalogCategoryState.Empty, []);
        var failed = new FrontendMsiLiveUpdateCatalogCategorySnapshot(
            FrontendMsiLiveUpdateCatalogCategory.Driver, FrontendMsiLiveUpdateCatalogCategoryState.Failed, []);
        const string category = "Category";
        Assert.NotEqual(category, MsiUpdatesPage.FormatCategoryHeader(category, success));
        Assert.NotEqual(category, MsiUpdatesPage.FormatCategoryHeader(category, empty));
        Assert.Equal(category, MsiUpdatesPage.FormatCategoryHeader(category, failed));
        Assert.Equal(category, MsiUpdatesPage.FormatCategoryHeader(category, null));
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
    public void Catalog_projection_preserves_version_source_status_and_non_installing_actions()
    {
        var code = ReadSource("src", "SteamInputAddonforClaw.UI", "Views", "MsiUpdatesPage.xaml.cs");
        var render = ExtractMethod(code, "private void RenderMsiLiveUpdateCategory(");
        Assert.Contains("entry.InstalledVersion", code, StringComparison.Ordinal);
        Assert.Contains("entry.LatestVersionSource", code, StringComparison.Ordinal);
        Assert.Contains("FrontendMsiLiveUpdateVersionKind.Package", code, StringComparison.Ordinal);
        Assert.Contains("MsiUpdates_LatestIntelPackage", code, StringComparison.Ordinal);
        Assert.Contains("MsiUpdates_InstalledEstimated", code, StringComparison.Ordinal);
        Assert.Contains("MsiUpdates_IntelSourcePartial", code, StringComparison.Ordinal);
        Assert.Contains("entry.ComparisonStatus switch", render, StringComparison.Ordinal);
        Assert.Contains("FrontendMsiLiveUpdateComparisonStatus.UpdateAvailable", render, StringComparison.Ordinal);
        Assert.Contains("FrontendMsiLiveUpdateComparisonStatus.UpToDate", render, StringComparison.Ordinal);
        Assert.Contains("MsiUpdates_StatusUnknown", render, StringComparison.Ordinal);
        Assert.Contains("MsiUpdates_StatusUnknownAccessible", render, StringComparison.Ordinal);
        Assert.Contains("new Button", render, StringComparison.Ordinal);
        Assert.Contains("IsEnabled = false", render, StringComparison.Ordinal);
        Assert.DoesNotContain("Click =", render, StringComparison.Ordinal);
        Assert.DoesNotContain("Command =", render, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.SetName(statusText, accessibleStatus)", render, StringComparison.Ordinal);
        Assert.Contains("MsiUpdates_CompactSourceMsi", render, StringComparison.Ordinal);
        Assert.Contains("MsiUpdates_CompactSourceIntelDsa", render, StringComparison.Ordinal);
        Assert.Contains("MsiUpdates_ReleaseDate", render, StringComparison.Ordinal);
    }

    [Fact]
    public void Catalog_source_uses_distinct_semantic_version_resource_keys()
    {
        var msiPackage = new FrontendMsiLiveUpdateCatalogEntry("package", "1", null, null,
            LatestVersionSource: FrontendMsiLiveUpdateVersionSource.Msi,
            LatestVersionKind: FrontendMsiLiveUpdateVersionKind.Package);
        var intelPackage = new FrontendMsiLiveUpdateCatalogEntry("package", "2", null, null,
            LatestVersionSource: FrontendMsiLiveUpdateVersionSource.IntelDsa,
            LatestVersionKind: FrontendMsiLiveUpdateVersionKind.Package);
        var msiLabel = MsiUpdatesPage.GetLatestVersionLabel(msiPackage);
        var intelLabel = MsiUpdatesPage.GetLatestVersionLabel(intelPackage);
        Assert.NotEqual(msiLabel.ResourceKey, intelLabel.ResourceKey);
        Assert.False(string.IsNullOrWhiteSpace(msiLabel.ResourceKey));
        Assert.False(string.IsNullOrWhiteSpace(intelLabel.ResourceKey));
        Assert.Equal("MsiUpdates_LatestVersion", msiLabel.ResourceKey);
        Assert.Equal("MsiUpdates_LatestIntelPackage", intelLabel.ResourceKey);
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
