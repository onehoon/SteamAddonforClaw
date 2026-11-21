using System.Reflection;
using System.Xml.Linq;
using SteamInputAddonforClaw.Contracts.Frontend;
using SteamInputAddonforClaw.Views;
using Xunit;

namespace SteamInputAddonforClaw.UiTests;

public sealed class ProfilesPagePolicyTests
{
    [Fact]
    public async Task Fake_frontend_feeds_one_mixed_catalog_with_platform_scoped_identity_and_filters()
    {
        var frontend = DispatchProxy.Create<IAddonFrontendControl, FakeProfilesFrontendProxy>();
        var fake = (FakeProfilesFrontendProxy)(object)frontend;
        fake.SteamGames =
        [
            new FrontendProfileGameCatalogEntry(100, "Shared Game", FrontendProfileGameSource.Steam, Favorite: true),
            new FrontendProfileGameCatalogEntry(480, "Hidden Steam entry", FrontendProfileGameSource.Steam),
            new FrontendProfileGameCatalogEntry(480, "Shared Game", FrontendProfileGameSource.NonSteam),
        ];
        fake.XboxSnapshot = new(FrontendXboxGameCatalogOutcome.Ready,
            [new FrontendXboxGameCatalogEntry("store:0123456789AB", "Shared Game", Favorite: true)], null);

        var steam = ProfilePage.FilterSteamCatalog(await frontend.ScanProfileGamesAsync());
        var xbox = await frontend.ScanXboxGamesAsync();
        var items = new Dictionary<string, ProfilesLibraryItem>(StringComparer.Ordinal);
        Assert.Empty(ProfilesCatalogPolicy.SynchronizeItems(items, steam, xbox.Games));

        Assert.Collection(ProfilesCatalogPolicy.FilterAndSort(items.Values, ProfilesLibraryFilter.All, null),
            item => Assert.Equal(FrontendProfileGameSource.Steam, item.SteamGame?.Source),
            item => Assert.True(item.IsXbox),
            item => Assert.Equal(FrontendProfileGameSource.NonSteam, item.SteamGame?.Source));
        Assert.Collection(ProfilesCatalogPolicy.FilterAndSort(items.Values, ProfilesLibraryFilter.Steam, null),
            item => Assert.Equal(FrontendProfileGameSource.Steam, item.SteamGame?.Source),
            item => Assert.Equal(FrontendProfileGameSource.NonSteam, item.SteamGame?.Source));
        Assert.Single(ProfilesCatalogPolicy.FilterAndSort(items.Values, ProfilesLibraryFilter.Xbox, null));
        Assert.Equal(3, items.Values.Select(item => item.Identity).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(new[] { "Shared Game", "Shared Game" },
            ProfilesCatalogPolicy.FilterAndSort(items.Values, ProfilesLibraryFilter.Steam, "shared").Select(item => item.Name));
        Assert.Single(ProfilesCatalogPolicy.FilterAndSort(items.Values, ProfilesLibraryFilter.Steam, "100"));
        Assert.Equal(1, fake.SteamScanCount);
        Assert.Equal(1, fake.XboxScanCount);

        // Filter/search are local projections: no additional frontend scan is needed.
        _ = ProfilesCatalogPolicy.FilterAndSort(items.Values, ProfilesLibraryFilter.Xbox, "shared");
        Assert.Equal(1, fake.SteamScanCount);
        Assert.Equal(1, fake.XboxScanCount);

        fake.FailSteam = true;
        IReadOnlyList<FrontendProfileGameCatalogEntry> lastValidSteam = steam;
        await Assert.ThrowsAsync<InvalidOperationException>(() => frontend.ScanProfileGamesAsync());
        fake.XboxSnapshot = new(FrontendXboxGameCatalogOutcome.Ready, [], null);
        var successfulEmptyXbox = await frontend.ScanXboxGamesAsync();
        ProfilesCatalogPolicy.SynchronizeItems(items, lastValidSteam, successfulEmptyXbox.Games);
        Assert.Equal(2, ProfilesCatalogPolicy.FilterAndSort(items.Values, ProfilesLibraryFilter.All, null).Count);
        Assert.Equal(2, fake.SteamScanCount);
        Assert.Equal(2, fake.XboxScanCount);
    }

    [Fact]
    public void Known_favorites_sort_first_across_platforms_and_unknown_favorites_remain_visible()
    {
        var steamFavorite = ProfilesLibraryItem.FromSteam(new(10, "Zulu", FrontendProfileGameSource.Steam, Favorite: true));
        var xboxFavorite = ProfilesLibraryItem.FromXbox(new("store:0123456789AB", "Alpha", Favorite: true));
        var unknownFavorite = ProfilesLibraryItem.FromSteam(new(11, "Beta", FrontendProfileGameSource.Steam,
            Favorite: true, FavoriteStateKnown: false));
        var ordinary = ProfilesLibraryItem.FromXbox(new("store:1123456789AB", "Charlie"));

        var sorted = ProfilesCatalogPolicy.FilterAndSort(
            [ordinary, unknownFavorite, steamFavorite, xboxFavorite], ProfilesLibraryFilter.All, null);

        Assert.Equal(new[] { xboxFavorite, steamFavorite, unknownFavorite, ordinary }, sorted);
        Assert.False(unknownFavorite.FavoriteStateKnown);
        Assert.Equal("—", unknownFavorite.FavoriteGlyph);
    }

    [Fact]
    public void Catalog_sync_and_visible_reconciliation_reuse_cards_by_stable_identity()
    {
        var dictionary = new Dictionary<string, ProfilesLibraryItem>(StringComparer.Ordinal);
        var firstSteam = new FrontendProfileGameCatalogEntry(1, "One", FrontendProfileGameSource.Steam);
        var secondSteam = new FrontendProfileGameCatalogEntry(2, "Two", FrontendProfileGameSource.NonSteam);
        var xbox = new FrontendXboxGameCatalogEntry("store:0123456789AB", "Three");
        ProfilesCatalogPolicy.SynchronizeItems(dictionary, [firstSteam, secondSteam], [xbox]);
        var retained = dictionary[ProfilesLibraryItem.GetIdentity(firstSteam)];
        var current = new System.Collections.ObjectModel.ObservableCollection<ProfilesLibraryItem>(
            ProfilesCatalogPolicy.FilterAndSort(dictionary.Values, ProfilesLibraryFilter.All, null));

        var updatedSteam = firstSteam with { Name = "One renamed", Favorite = true };
        var removed = ProfilesCatalogPolicy.SynchronizeItems(dictionary, [updatedSteam], [xbox]);
        var desired = ProfilesCatalogPolicy.FilterAndSort(dictionary.Values, ProfilesLibraryFilter.All, null);
        ProfilesCatalogPolicy.ReconcileVisibleCards(current, desired);

        Assert.Contains(ProfilesLibraryItem.GetIdentity(secondSteam), removed);
        Assert.Same(retained, dictionary[ProfilesLibraryItem.GetIdentity(updatedSteam)]);
        Assert.Same(retained, Assert.Single(current, item => item.Identity == retained.Identity));
        Assert.Equal(desired, current);
    }

    [Theory]
    [InlineData(1.0)]
    [InlineData(1.25)]
    [InlineData(1.5)]
    [InlineData(2.0)]
    public void Unified_grid_uses_the_existing_steam_cover_layout_for_each_raster_scale(double scale)
    {
        foreach (var width in new[] { 384.0, 720.0, 1024.0, 1366.0, 1920.0 })
        {
            var steamCalculated = SteamCoverGridLayout.TryCalculate(width, scale, out var expected);
            var unifiedCalculated = ProfilesPage.TryCalculateCoverGridLayout(width, scale, out var actual);

            Assert.Equal(steamCalculated, unifiedCalculated);
            if (!steamCalculated) continue;
            Assert.Equal(expected.Columns, actual.Columns);
            Assert.Equal(expected.CoverWidthDip, actual.CoverWidthDip);
            Assert.Equal(expected.CoverHeightDip, actual.CoverHeightDip);
            Assert.Equal(expected.ColumnGapDip, actual.ColumnGapDip);
            Assert.Equal(expected.RowGapDip, actual.RowGapDip);
        }
    }

    [Fact]
    public void Unified_gallery_keeps_filter_refresh_and_detail_return_in_one_stable_page()
    {
        var root = FindRepositoryRoot();
        var xamlPath = Path.Combine(root, "src/SteamInputAddonforClaw.UI/Views/ProfilesPage.xaml");
        var xaml = XDocument.Load(xamlPath);
        var code = File.ReadAllText(Path.ChangeExtension(xamlPath, ".xaml.cs"));
        var filterHandler = Method(code, "private void GameFilterSelector_SelectionChanged", "private void GameSearchBox_TextChanged");
        var backHandler = Method(code, "private void OnEditorBackRequested", "private void OnSteamActivityRefreshRequested");
        var refresh = Method(code, "private async Task RefreshSteamCatalogAsync", "private void EnsureXboxArtworkResolver");
        var refreshBoth = Method(code, "private async Task RefreshCatalogsAsync", "private async Task RefreshSteamCatalogAsync");
        var steamScan = Method(code, "private async Task RefreshSteamCatalogAsync", "private async Task RefreshXboxCatalogAsync");
        var xboxScan = Method(code, "private async Task RefreshXboxCatalogAsync", "private void EnsureXboxArtworkResolver");

        Assert.Single(xaml.Descendants(), element => element.Name.LocalName == "GridView"
            && (string?)element.Attribute(XName.Get("Name", "http://schemas.microsoft.com/winfx/2006/xaml")) == "GameGrid");
        Assert.Equal(3, xaml.Descendants().Count(element => element.Name.LocalName == "SelectorBarItem"));
        Assert.Contains("RebuildVisibleItems()", filterHandler, StringComparison.Ordinal);
        Assert.DoesNotContain("RefreshCatalogsAsync", filterHandler, StringComparison.Ordinal);
        Assert.DoesNotContain("GameGrid.ItemsSource =", code.Replace("GameGrid.ItemsSource = _visibleItems;", string.Empty, StringComparison.Ordinal), StringComparison.Ordinal);
        Assert.DoesNotContain("GameSearchBox.Text =", backHandler, StringComparison.Ordinal);
        Assert.DoesNotContain("_filter =", backHandler, StringComparison.Ordinal);
        Assert.Contains("_steamCatalog =", refresh, StringComparison.Ordinal);
        Assert.Contains("_xboxCatalog =", refresh, StringComparison.Ordinal);
        Assert.Contains("Task.WhenAll(steamRefresh, xboxRefresh)", refreshBoth, StringComparison.Ordinal);
        Assert.Contains("RebuildCatalogItems();", steamScan, StringComparison.Ordinal);
        Assert.Contains("RebuildCatalogItems();", xboxScan, StringComparison.Ordinal);
        Assert.True(steamScan.IndexOf("await _frontend!.ScanProfileGamesAsync", StringComparison.Ordinal)
            < steamScan.IndexOf("RebuildCatalogItems();", StringComparison.Ordinal));
        Assert.True(steamScan.IndexOf("RebuildCatalogItems();", StringComparison.Ordinal)
            < steamScan.IndexOf("SteamPortraitArtworkResolver.CreateContext", StringComparison.Ordinal));
        Assert.True(refreshBoth.IndexOf("var steamRefresh =", StringComparison.Ordinal)
            < refreshBoth.IndexOf("await Task.WhenAll(steamRefresh, xboxRefresh)", StringComparison.Ordinal));
        Assert.Contains("private async Task RefreshSteamCatalogAsync", code, StringComparison.Ordinal);
        Assert.Contains("private async Task RefreshXboxCatalogAsync", code, StringComparison.Ordinal);
        Assert.Contains("SetGameProfileFavoriteAsync", code, StringComparison.Ordinal);
        Assert.Contains("SetXboxGameProfileFavoriteAsync", code, StringComparison.Ordinal);
        Assert.DoesNotContain("SetGameProfileEnabledAsync", code, StringComparison.Ordinal);
    }

    [Fact]
    public void Catalog_failures_preserve_the_last_valid_platform_and_hosted_editors_do_not_scan()
    {
        var root = FindRepositoryRoot();
        var profiles = File.ReadAllText(Path.Combine(root, "src/SteamInputAddonforClaw.UI/Views/ProfilesPage.xaml.cs"));
        var steamScan = Method(profiles, "private async Task RefreshSteamCatalogAsync", "private async Task RefreshXboxCatalogAsync");
        var xboxScan = Method(profiles, "private async Task RefreshXboxCatalogAsync", "private void EnsureXboxArtworkResolver");
        var steamEditor = File.ReadAllText(Path.Combine(root, "src/SteamInputAddonforClaw.UI/Views/ProfilePage.xaml.cs"));
        var xboxEditor = File.ReadAllText(Path.Combine(root, "src/SteamInputAddonforClaw.UI/Views/XboxPage.xaml.cs"));
        var steamHosted = Method(steamEditor, "internal void ActivateForUnifiedProfiles", "internal async Task OpenGameFromUnifiedProfilesAsync");
        var xboxHosted = Method(xboxEditor, "internal void ActivateForUnifiedProfiles", "internal async Task OpenGameFromUnifiedProfilesAsync");

        Assert.Contains("keeping the last valid catalog", steamScan, StringComparison.Ordinal);
        Assert.DoesNotContain("_steamCatalog = []", steamScan, StringComparison.Ordinal);
        Assert.Contains("keeping the last valid catalog", xboxScan, StringComparison.Ordinal);
        Assert.DoesNotContain("_xboxCatalog = []", xboxScan, StringComparison.Ordinal);
        Assert.DoesNotContain("ScanProfileGamesAsync", steamHosted, StringComparison.Ordinal);
        Assert.DoesNotContain("ScanXboxGamesAsync", xboxHosted, StringComparison.Ordinal);
        Assert.Contains("ResolveCatalogCardAsync", profiles, StringComparison.Ordinal);
        Assert.Contains("SteamPortraitArtworkResolver.ResolveCandidates", profiles, StringComparison.Ordinal);
        Assert.Contains("card.Item.Identity == identity", profiles, StringComparison.Ordinal);
    }

    private static string Method(string source, string startMarker, string endMarker)
    {
        var start = source.IndexOf(startMarker, StringComparison.Ordinal);
        var end = source.IndexOf(endMarker, start + startMarker.Length, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start, $"Could not find method region: {startMarker}");
        return source[start..end];
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

public class FakeProfilesFrontendProxy : DispatchProxy
{
    public IReadOnlyList<FrontendProfileGameCatalogEntry> SteamGames { get; set; } = [];
    public FrontendXboxGameCatalogSnapshot XboxSnapshot { get; set; } =
        FrontendXboxGameCatalogSnapshot.Unavailable("Unavailable");
    public int SteamScanCount { get; private set; }
    public int XboxScanCount { get; private set; }
    public bool FailSteam { get; set; }

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        if (targetMethod?.Name == nameof(IAddonFrontendControl.ScanProfileGamesAsync))
        {
            SteamScanCount++;
            if (FailSteam) return Task.FromException<IReadOnlyList<FrontendProfileGameCatalogEntry>>(new InvalidOperationException("simulated Steam scan failure"));
            return Task.FromResult(SteamGames);
        }
        if (targetMethod?.Name == nameof(IAddonFrontendControl.ScanXboxGamesAsync))
        {
            XboxScanCount++;
            return Task.FromResult(XboxSnapshot);
        }
        throw new NotSupportedException($"Unexpected frontend call: {targetMethod?.Name}");
    }
}
