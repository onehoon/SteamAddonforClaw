using System.Xml.Linq;
using SteamInputAddonforClaw.Contracts.Frontend;
using SteamInputAddonforClaw.Views;
using Xunit;

namespace SteamInputAddonforClaw.UiTests;

public sealed class XboxCatalogPageUiTests
{
    [Fact]
    public void Main_navigation_shows_Steam_then_XBOX_as_separate_top_level_pages()
    {
        var document = XDocument.Load(Source("src", "SteamInputAddonforClaw.UI", "MainWindow.xaml"));
        var menu = document.Descendants().Single(element => element.Name.LocalName == "NavigationView.MenuItems");
        var labels = menu.Elements()
            .Where(element => element.Name.LocalName == "NavigationViewItem")
            .Select(element => (string?)element.Attribute("Content"))
            .ToArray();

        Assert.Equal(new[] { "Device", "Controller", "Steam", "XBOX", "Overlay", "Shortcut", "How to Use" }, labels);
        var steam = menu.Elements().Single(element => (string?)element.Attribute("Content") == "Steam");
        var xbox = menu.Elements().Single(element => (string?)element.Attribute("Content") == "XBOX");
        Assert.Equal("Profile", (string?)steam.Attribute("Tag"));
        Assert.Equal("Xbox", (string?)xbox.Attribute("Tag"));
        Assert.Contains("isSettingsSelected", File.ReadAllText(Source("src", "SteamInputAddonforClaw.UI", "MainNavigationState.cs")), StringComparison.Ordinal);
    }

    [Fact]
    public void Steam_page_keeps_its_catalog_and_profile_editor_under_the_Steam_heading()
    {
        var document = XDocument.Load(Source("src", "SteamInputAddonforClaw.UI", "Views", "ProfilePage.xaml"));
        var code = File.ReadAllText(Source("src", "SteamInputAddonforClaw.UI", "Views", "ProfilePage.xaml.cs"));

        Assert.Contains(document.Descendants().Where(element => element.Name.LocalName == "TextBlock"),
            element => (string?)element.Attribute("Text") == "Steam");
        Assert.Contains("x:Name=\"ProfileEnabledToggle\"", document.ToString(), StringComparison.Ordinal);
        Assert.Contains("FavoriteButton_Click", code, StringComparison.Ordinal);
    }

    [Fact]
    public void Xbox_page_contains_only_display_name_catalog_cards_and_safe_controls()
    {
        var document = XDocument.Load(Source("src", "SteamInputAddonforClaw.UI", "Views", "XboxPage.xaml"));
        var xaml = document.ToString();
        var code = File.ReadAllText(Source("src", "SteamInputAddonforClaw.UI", "Views", "XboxPage.xaml.cs"));

        Assert.Contains("SteamInputAddonforClaw.Views.XboxPage", xaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"XBOX\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"RefreshGamesButton\"", xaml, StringComparison.Ordinal);
        Assert.Contains("PlaceholderText=\"Search games...\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"GameGrid\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding DisplayName}\"", xaml, StringComparison.Ordinal);
        foreach (var internalField in new[] { "StoreId", "TitleId", "PackageFamilyName", "PackageFullName", "ConfigPath", "Executable", "AUMID", "ProcessPath" })
        {
            Assert.DoesNotContain(internalField, xaml, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(internalField, code, StringComparison.OrdinalIgnoreCase);
        }
        Assert.DoesNotContain("Text=\"{Binding Key}\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Favorite", xaml, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Profile", xaml, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Xbox_page_scans_once_on_activation_and_refresh_and_ignores_results_after_leave()
    {
        var pageCode = File.ReadAllText(Source("src", "SteamInputAddonforClaw.UI", "Views", "XboxPage.xaml.cs"));
        var windowCode = File.ReadAllText(Source("src", "SteamInputAddonforClaw.UI", "MainWindow.xaml.cs"));
        var activate = Method(pageCode, "internal void Activate()", "internal void Deactivate()");
        var deactivate = Method(pageCode, "internal void Deactivate()", "private async void RefreshGamesButton_Click");
        var refresh = Method(pageCode, "private async Task RefreshGamesAsync()", "private void Render(");
        var refreshButton = Method(pageCode, "private async void RefreshGamesButton_Click", "private async Task RefreshGamesAsync()");
        var showPage = Method(windowCode, "private void ShowPage(", "private async Task RefreshSystemStatusAsync()");

        Assert.Contains("_active = true", activate, StringComparison.Ordinal);
        Assert.Contains("_ = RefreshGamesAsync()", activate, StringComparison.Ordinal);
        Assert.Contains("scan?.Cancel()", deactivate, StringComparison.Ordinal);
        Assert.Contains("await RefreshGamesAsync()", refreshButton, StringComparison.Ordinal);
        Assert.Contains("previousScan?.Cancel()", refresh, StringComparison.Ordinal);
        var scanCall = refresh.IndexOf("await frontend.ScanXboxGamesAsync(scan.Token)", StringComparison.Ordinal);
        var staleGuard = refresh.IndexOf("if (!IsCurrentScan(_active, _scanCancellation, scan)) return", StringComparison.Ordinal);
        Assert.True(scanCall >= 0 && staleGuard > scanCall);
        Assert.Contains("XboxContent.Activate()", showPage, StringComparison.Ordinal);
        Assert.Contains("else if (wasXbox) XboxContent.Deactivate()", showPage, StringComparison.Ordinal);
        Assert.DoesNotContain("StateInvalidated", pageCode, StringComparison.Ordinal);
        Assert.DoesNotContain("PackageCatalog", pageCode, StringComparison.Ordinal);
        Assert.DoesNotContain("Timer", pageCode, StringComparison.Ordinal);
    }

    [Fact]
    public void Xbox_page_search_uses_display_name_only_and_catalog_is_sorted_deterministically()
    {
        FrontendXboxGameCatalogEntry[] games =
        [
            new("store:z", "Beta"),
            new("store:a", "alpha"),
            new("store:q:internal-only", "Hidden key game"),
            new("pfn:two", "Alpha")
        ];

        Assert.Empty(XboxPage.FilterAndSort(games, "internal-only"));
        var sorted = XboxPage.FilterAndSort(games, null);
        Assert.Equal(new[] { "Alpha", "alpha", "Beta", "Hidden key game" }, sorted.Select(game => game.DisplayName));
        Assert.Equal("pfn:two", sorted[0].Key);
    }

    [Fact]
    public void Xbox_page_latest_request_guard_rejects_cancelled_or_retired_scans()
    {
        using var first = new CancellationTokenSource();
        using var latest = new CancellationTokenSource();

        Assert.True(XboxPage.IsCurrentScan(true, first, first));
        Assert.False(XboxPage.IsCurrentScan(true, latest, first));
        first.Cancel();
        Assert.False(XboxPage.IsCurrentScan(true, first, first));
        Assert.False(XboxPage.IsCurrentScan(false, first, first));
    }

    private static string Method(string source, string startMarker, string endMarker)
    {
        var start = source.IndexOf(startMarker, StringComparison.Ordinal);
        var end = source.IndexOf(endMarker, start, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start, $"Could not isolate method between '{startMarker}' and '{endMarker}'.");
        return source[start..end];
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
