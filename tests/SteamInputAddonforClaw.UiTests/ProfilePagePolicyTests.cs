using SteamInputAddonforClaw.Views;
using SteamInputAddonforClaw.Contracts.Frontend;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class ProfilePagePolicyTests
{
    [Fact]
    public void Stale_profile_response_is_not_current_after_selection_changes()
    {
        Assert.False(ProfilePage.IsCurrentProfileResponse(200u, 100u));
        Assert.True(ProfilePage.IsCurrentProfileResponse(200u, 200u));
        Assert.False(ProfilePage.IsCurrentProfileResponse(null, 200u));
    }

    [Fact]
    public void Older_tdp_completion_preserves_a_newer_dirty_draft()
    {
        Assert.True(ProfilePage.ShouldPreserveDirtyTdpDraft(true, 4, 5));
        Assert.False(ProfilePage.ShouldPreserveDirtyTdpDraft(false, 4, 5));
        Assert.False(ProfilePage.ShouldPreserveDirtyTdpDraft(true, 5, 5));
    }

    [Fact]
    public void Steam_unknown_favorite_is_neutral_disabled_and_does_not_block_catalog_browsing()
    {
        Assert.Equal("—", ProfilePage.GetFavoriteGlyph(favorite: false, stateKnown: false));
        Assert.Equal("★", ProfilePage.GetFavoriteGlyph(favorite: true, stateKnown: true));
        Assert.Equal("☆", ProfilePage.GetFavoriteGlyph(favorite: false, stateKnown: true));

        var unknown = new FrontendProfileGameCatalogEntry(
            101, "Alpha unknown", FrontendProfileGameSource.Steam, Favorite: true, FavoriteStateKnown: false);
        var favorite = new FrontendProfileGameCatalogEntry(
            102, "Zulu favorite", FrontendProfileGameSource.Steam, Favorite: true, FavoriteStateKnown: true);
        var ordinary = new FrontendProfileGameCatalogEntry(
            103, "Beta ordinary", FrontendProfileGameSource.Steam, FavoriteStateKnown: true);
        var sorted = ProfilePage.FilterAndSort([unknown, favorite, ordinary], null);

        Assert.Equal(new uint[] { 102, 101, 103 }, sorted.Select(game => game.AppId));
        Assert.Equal(new uint[] { 101 }, ProfilePage.FilterAndSort([unknown, favorite, ordinary], "unknown").Select(game => game.AppId));

        var root = FindRepositoryRoot();
        var xaml = File.ReadAllText(Path.Combine(root, "src/SteamInputAddonforClaw.UI/Views/ProfilePage.xaml"));
        var code = File.ReadAllText(Path.Combine(root, "src/SteamInputAddonforClaw.UI/Views/ProfilePage.xaml.cs"));
        Assert.Contains("IsEnabled=\"{Binding FavoriteStateKnown}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("FavoriteStateKnown: true", code, StringComparison.Ordinal);
        Assert.Contains("FavoriteStateInfoBar.IsOpen = _catalog.Any(game => !game.FavoriteStateKnown)", code, StringComparison.Ordinal);
    }

    [Fact]
    public void Steam_catalog_scans_only_until_the_first_success_and_never_overlaps()
    {
        Assert.True(ProfilePage.ShouldStartCatalogScan(catalogLoaded: false, scanInProgress: false));
        Assert.False(ProfilePage.ShouldStartCatalogScan(catalogLoaded: true, scanInProgress: false));
        Assert.False(ProfilePage.ShouldStartCatalogScan(catalogLoaded: false, scanInProgress: true));

        var root = FindRepositoryRoot();
        var code = File.ReadAllText(Path.Combine(root, "src/SteamInputAddonforClaw.UI/Views/ProfilePage.xaml.cs"));
        var refresh = Method(code, "private async Task RefreshGamesAsync(", "private void GameSearchBox_TextChanged(");
        var manualRefresh = Method(code, "private async void RefreshGamesButton_Click(", "private async Task RefreshGamesAsync(");

        var scan = refresh.IndexOf("await _frontend.ScanProfileGamesAsync()", StringComparison.Ordinal);
        var successfulCache = refresh.IndexOf("_catalog = scannedCatalog", StringComparison.Ordinal);
        var loadedFlag = refresh.IndexOf("_catalogLoaded = true", StringComparison.Ordinal);
        var failure = refresh.IndexOf("catch (Exception exception)", StringComparison.Ordinal);
        Assert.True(scan >= 0 && successfulCache > scan && loadedFlag > successfulCache);
        Assert.True(failure > loadedFlag);
        Assert.DoesNotContain("_catalogLoaded = false", refresh, StringComparison.Ordinal);
        Assert.Contains("await RefreshGamesAsync(explicitRefresh: true)", manualRefresh, StringComparison.Ordinal);
        Assert.DoesNotContain("if (_catalogLoaded) return", refresh, StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "SteamInputAddonforClaw.slnx")))
                return directory.FullName;
        }

        throw new DirectoryNotFoundException("Repository root not found.");
    }

    private static string Method(string source, string startMarker, string endMarker)
    {
        var start = source.IndexOf(startMarker, StringComparison.Ordinal);
        var end = source.IndexOf(endMarker, start, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start, $"Could not find method range {startMarker} .. {endMarker}.");
        return source[start..end];
    }
}
