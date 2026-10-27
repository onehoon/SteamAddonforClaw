using SteamInputAddonforClaw.Views;
using SteamInputAddonforClaw.Contracts.Frontend;
using System.Globalization;
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

    [Fact]
    public void Steam_catalog_uses_centered_virtualized_portrait_cards_and_independent_favorite_target()
    {
        var root = FindRepositoryRoot();
        var xaml = File.ReadAllText(Path.Combine(root, "src/SteamInputAddonforClaw.UI/Views/ProfilePage.xaml"));
        var catalog = xaml[..xaml.IndexOf("<Grid x:Name=\"DetailPanel\"", StringComparison.Ordinal)];

        Assert.Contains("<ItemsRepeater x:Name=\"GameGrid\"", catalog, StringComparison.Ordinal);
        Assert.Contains("ItemsJustification=\"Center\"", catalog, StringComparison.Ordinal);
        Assert.Contains("ItemsStretch=\"None\"", catalog, StringComparison.Ordinal);
        Assert.Contains("MinColumnSpacing=\"12\"", catalog, StringComparison.Ordinal);
        Assert.Contains("MinRowSpacing=\"12\"", catalog, StringComparison.Ordinal);
        Assert.DoesNotContain("MaximumRowsOrColumns", catalog, StringComparison.Ordinal);
        Assert.Contains("Width=\"176\" Height=\"264\"", catalog, StringComparison.Ordinal);
        var code = File.ReadAllText(Path.Combine(root, "src/SteamInputAddonforClaw.UI/Views/ProfilePage.xaml.cs"));
        Assert.Contains("Stretch = Stretch.UniformToFill", code, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"FallbackCard\"", catalog, StringComparison.Ordinal);
        Assert.Contains("Glyph=\"&#xE7FC;\"", catalog, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"OpenGameButton\"", catalog, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"FavoriteButton\"", catalog, StringComparison.Ordinal);
        Assert.Contains("Click=\"GameCardButton_Click\"", catalog, StringComparison.Ordinal);
        Assert.Contains("Click=\"FavoriteButton_Click\"", catalog, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.Name=\"{Binding Name}\"", catalog, StringComparison.Ordinal);
        Assert.DoesNotContain("Shadow", catalog, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Gradient", catalog, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Steam_cover_card_DIP_dimensions_preserve_physical_size_at_common_scales()
    {
        Assert.Equal((176, 264), SteamPortraitArtworkResolver.GetCoverSizeDip(1.0));

        var at150Percent = SteamPortraitArtworkResolver.GetCoverSizeDip(1.5);
        Assert.Equal(176.0 / 1.5, at150Percent.Width, 5);
        Assert.Equal(176.0, at150Percent.Height, 5);

        Assert.Equal((88, 132), SteamPortraitArtworkResolver.GetCoverSizeDip(2.0));
        Assert.Equal((176, 264), SteamPortraitArtworkResolver.GetCoverSizeDip(0));
        Assert.Equal((176, 264), SteamPortraitArtworkResolver.GetCoverSizeDip(double.NaN));
    }

    [Fact]
    public void Steam_custom_portrait_precedes_local_cache_and_landscape_art_is_never_a_candidate()
    {
        using var temp = new TemporaryDirectory();
        const uint appId = 480;
        const uint accountId = 123;
        var customPng = temp.PathFor($"userdata/{accountId}/config/grid/{appId}p.png");
        var customJpg = temp.PathFor($"userdata/{accountId}/config/grid/{appId}p.jpg");
        var customJpeg = temp.PathFor($"userdata/{accountId}/config/grid/{appId}p.jpeg");
        var cache = temp.PathFor($"appcache/librarycache/{appId}/library_600x900_2x.jpg");
        temp.CreateFile(customPng);
        temp.CreateFile(customJpg);
        temp.CreateFile(customJpeg);
        temp.CreateFile(cache);
        temp.CreateFile(temp.PathFor($"appcache/librarycache/{appId}/header.jpg"));
        temp.CreateFile(temp.PathFor($"appcache/librarycache/{appId}/library_hero.jpg"));

        var candidates = SteamPortraitArtworkResolver.ResolveCandidates(
            temp.Root,
            appId,
            FrontendProfileGameSource.Steam,
            accountId);

        Assert.Equal([customPng, customJpg, customJpeg, cache], candidates);
        Assert.DoesNotContain(candidates, path => Path.GetFileName(path).Contains("header", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(candidates, path => Path.GetFileName(path).Contains("hero", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Steam_portrait_cache_uses_direct_hashed_and_legacy_candidates_with_two_x_first()
    {
        using var temp = new TemporaryDirectory();
        const uint appId = 730;
        var cache = $"appcache/librarycache/{appId}";
        var directTwoX = temp.PathFor($"{cache}/library_600x900_2x.jpg");
        var direct = temp.PathFor($"{cache}/library_600x900.jpg");
        var hashedTwoX = temp.PathFor($"{cache}/abc/library_600x900_2x.jpg");
        var hashed = temp.PathFor($"{cache}/abc/library_600x900.jpg");
        var flatTwoX = temp.PathFor($"appcache/librarycache/{appId}_library_600x900_2x.jpg");
        var flat = temp.PathFor($"appcache/librarycache/{appId}_library_600x900.jpg");
        foreach (var path in new[] { directTwoX, direct, hashedTwoX, hashed, flatTwoX, flat })
            temp.CreateFile(path);

        var candidates = SteamPortraitArtworkResolver.ResolveCandidates(
            temp.Root,
            appId,
            FrontendProfileGameSource.Steam,
            accountId: null);

        Assert.Equal([directTwoX, hashedTwoX, flatTwoX, direct, hashed, flat], candidates);
    }

    [Fact]
    public void Portrait_candidate_paths_are_cached_for_the_current_catalog_view()
    {
        using var temp = new TemporaryDirectory();
        var context = new SteamPortraitArtworkContext(temp.Root, accountId: null);
        var game = new FrontendProfileGameCatalogEntry(480, "Spacewar", FrontendProfileGameSource.Steam);

        Assert.Empty(context.GetCandidates(game));
        temp.CreateFile(temp.PathFor("appcache/librarycache/480/library_600x900.jpg"));
        Assert.Empty(context.GetCandidates(game));
    }

    [Fact]
    public void Non_steam_shortcuts_and_missing_Steam_art_use_the_named_fallback_without_cross_account_lookup()
    {
        using var temp = new TemporaryDirectory();
        Assert.Empty(SteamPortraitArtworkResolver.ResolveCandidates(
            temp.Root,
            appId: 123456789,
            FrontendProfileGameSource.NonSteam,
            accountId: 123));
        Assert.Empty(SteamPortraitArtworkResolver.ResolveCandidates(
            temp.Root,
            appId: 480,
            FrontendProfileGameSource.Steam,
            accountId: null));

        var code = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "src/SteamInputAddonforClaw.UI/Views/ProfilePage.xaml.cs"));
        var prepare = Method(code, "private void GameGrid_ElementPrepared(", "private void GameGrid_ElementClearing(");
        Assert.Contains("card.Item.Source != FrontendProfileGameSource.Steam", code, StringComparison.Ordinal);
        var reset = Method(code, "public void Reset(GameCardItem item)", "public void CancelLoad()");
        Assert.Contains("ClearArtwork()", reset, StringComparison.Ordinal);
        Assert.Contains("FallbackCard.Visibility = Visibility.Visible", reset, StringComparison.Ordinal);
    }

    [Fact]
    public void Steam_account_resolution_prefers_valid_active_user_and_fails_open_on_ambiguity()
    {
        using var temp = new TemporaryDirectory();
        var userdata = temp.PathFor("userdata");
        Directory.CreateDirectory(Path.Combine(userdata, "123"));
        Directory.CreateDirectory(Path.Combine(userdata, "456"));
        var loginUsers = temp.PathFor("loginusers.vdf");
        var steamId123 = 76561197960265728UL + 123;
        var steamId456 = 76561197960265728UL + 456;
        File.WriteAllText(loginUsers,
            $"\"{steamId123.ToString(CultureInfo.InvariantCulture)}\" {{ \"MostRecent\" \"1\" }}\n" +
            $"\"{steamId456.ToString(CultureInfo.InvariantCulture)}\" {{ \"MostRecent\" \"1\" }}\n");

        Assert.Equal((uint)123, SteamPortraitArtworkResolver.ResolveUserdataAccountId(userdata, loginUsers, 123));
        Assert.Null(SteamPortraitArtworkResolver.ResolveUserdataAccountId(userdata, loginUsers, null));
        Assert.Null(SteamPortraitArtworkResolver.ResolveUserdataAccountId(userdata, loginUsers, 999));
        temp.CreateFile(temp.PathFor("userdata/123/config/grid/480p.png"));
        temp.CreateFile(temp.PathFor("userdata/456/config/grid/480p.png"));
        Assert.Empty(SteamPortraitArtworkResolver.ResolveCandidates(
            temp.Root,
            appId: 480,
            FrontendProfileGameSource.Steam,
            SteamPortraitArtworkResolver.ResolveUserdataAccountId(userdata, loginUsers, null)));

        File.WriteAllText(loginUsers, $"\"{steamId123.ToString(CultureInfo.InvariantCulture)}\" {{ \"MostRecent\" \"1\" }}");
        Assert.Equal((uint)123, SteamPortraitArtworkResolver.ResolveUserdataAccountId(userdata, loginUsers, null));
    }

    [Fact]
    public async Task Unsupported_or_corrupt_portrait_falls_through_to_the_next_candidate()
    {
        var attempted = new List<string>();
        var decoded = new object();

        var result = await ProfilePage.TryLoadFirstAvailableAsync<object>(["corrupt.jpg", "valid.jpg"], path =>
        {
            attempted.Add(path);
            return path == "corrupt.jpg"
                ? Task.FromException<object?>(new InvalidDataException("invalid image bytes"))
                : Task.FromResult<object?>(decoded);
        });

        Assert.Same(decoded, result);
        Assert.Equal(["corrupt.jpg", "valid.jpg"], attempted);
    }

    [Fact]
    public void Realized_cover_loading_is_lazy_cancellable_and_rejects_stale_recycled_cards()
    {
        var root = FindRepositoryRoot();
        var xaml = File.ReadAllText(Path.Combine(root, "src/SteamInputAddonforClaw.UI/Views/ProfilePage.xaml"));
        var code = File.ReadAllText(Path.Combine(root, "src/SteamInputAddonforClaw.UI/Views/ProfilePage.xaml.cs"));
        Assert.Contains("ElementPrepared=\"GameGrid_ElementPrepared\"", xaml, StringComparison.Ordinal);
        Assert.Contains("ElementClearing=\"GameGrid_ElementClearing\"", xaml, StringComparison.Ordinal);
        Assert.Contains("DecodePixelWidth = 352", code, StringComparison.Ordinal);
        Assert.Contains("cancellationToken.ThrowIfCancellationRequested()", code, StringComparison.Ordinal);
        Assert.Contains("card.Root.DataContext is GameCardItem item", code, StringComparison.Ordinal);
        Assert.Contains("card.Matches(item)", code, StringComparison.Ordinal);
        Assert.Contains("card.ClearArtwork()", code, StringComparison.Ordinal);
        var load = Method(code, "private async Task LoadGameCardArtworkAsync(", "private static Task<BitmapImage?> TryLoadProfileArtworkAsync(");
        var identityCheck = load.IndexOf("!IsCurrentGameCard(card, generation)", StringComparison.Ordinal);
        var imageAssignment = load.IndexOf("card.CoverImageFrame.Background = new ImageBrush", StringComparison.Ordinal);
        Assert.True(identityCheck >= 0 && imageAssignment > identityCheck);
        Assert.Contains("_observedXamlRoot.Changed += XamlRoot_Changed", code, StringComparison.Ordinal);
        Assert.Contains("_observedXamlRoot.Changed -= XamlRoot_Changed", code, StringComparison.Ordinal);
        Assert.Contains("Math.Abs(_lastRasterizationScale - rasterizationScale) < 0.0001", code, StringComparison.Ordinal);
        var activate = Method(code, "internal void Activate()", "internal static bool ShouldStartCatalogScan(");
        Assert.DoesNotContain("ApplyCatalogFilter()", activate, StringComparison.Ordinal);
        var invalidation = Method(code, "private void OnStateInvalidated(", "private async void RefreshGamesButton_Click(");
        Assert.DoesNotContain("ApplyCatalogFilter", invalidation, StringComparison.Ordinal);
    }

    [Fact]
    public void Search_favorite_mutation_and_profile_navigation_keep_their_existing_actions()
    {
        var root = FindRepositoryRoot();
        var xaml = File.ReadAllText(Path.Combine(root, "src/SteamInputAddonforClaw.UI/Views/ProfilePage.xaml"));
        var code = File.ReadAllText(Path.Combine(root, "src/SteamInputAddonforClaw.UI/Views/ProfilePage.xaml.cs"));
        var open = Method(code, "private async void GameCardButton_Click(", "private void BackButton_Click(");
        var favorite = Method(code, "private async void FavoriteButton_Click(", "private async Task SelectGameAsync(");

        Assert.Contains("GameSearchBox_TextChanged", xaml, StringComparison.Ordinal);
        Assert.Contains("FilterAndSort(_catalog, query)", code, StringComparison.Ordinal);
        Assert.Contains("await SelectGameAsync(game)", open, StringComparison.Ordinal);
        Assert.Contains("SetGameProfileFavoriteAsync(game.AppId, !game.Favorite, game.Name)", favorite, StringComparison.Ordinal);
        Assert.Contains("if (!result.Succeeded)", favorite, StringComparison.Ordinal);
        Assert.DoesNotContain("SelectGameAsync", favorite, StringComparison.Ordinal);
        Assert.Contains("ApplyCatalogFilter()", favorite, StringComparison.Ordinal);
        Assert.Contains("IsEnabled=\"{Binding FavoriteStateKnown}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("FavoriteAutomationName", xaml, StringComparison.Ordinal);
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

    private sealed class TemporaryDirectory : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), $"SteamPortraitTests-{Guid.NewGuid():N}");

        public string PathFor(string relativePath) => Path.Combine(Root, relativePath.Replace('/', Path.DirectorySeparatorChar));

        public string CreateFile(string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, [1, 2, 3]);
            return path;
        }

        public void Dispose()
        {
            if (Directory.Exists(Root))
                Directory.Delete(Root, recursive: true);
        }
    }
}
