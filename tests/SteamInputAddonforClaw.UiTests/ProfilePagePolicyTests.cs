using SteamInputAddonforClaw.Views;
using SteamInputAddonforClaw.Contracts.Frontend;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Globalization;
using System.Xml.Linq;
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

    [Theory]
    [InlineData(true, "_acFpsDebounce")]
    [InlineData(false, "_dcFpsDebounce")]
    public void Failed_fps_commit_retires_failed_side_before_readback_and_preserves_opposite_pending_draft(
        bool failedAc,
        string failedDebounce)
    {
        var root = FindRepositoryRoot();
        var code = File.ReadAllText(Path.Combine(root, "src/SteamInputAddonforClaw.UI/Views/ProfilePage.xaml.cs"));
        var submit = Method(code, "private async Task SubmitFpsAfterDelayAsync", "private void CancelFpsDebounce");
        var catchIndex = submit.IndexOf("catch (Exception exception)", StringComparison.Ordinal);
        var generationGuard = submit.IndexOf(
            "(ac ? generation == _acFpsGeneration : generation == _dcFpsGeneration)",
            catchIndex,
            StringComparison.Ordinal);
        var acReset = submit.IndexOf("if (ac) _acFpsDebounce = null;", catchIndex, StringComparison.Ordinal);
        var dcReset = submit.IndexOf("else _dcFpsDebounce = null;", acReset, StringComparison.Ordinal);
        var recovery = submit.IndexOf("await RestoreSelectedAfterMutationFailureAsync(", catchIndex, StringComparison.Ordinal);

        Assert.Equal(failedAc ? "_acFpsDebounce" : "_dcFpsDebounce", failedDebounce);
        Assert.True(catchIndex >= 0 && generationGuard > catchIndex && acReset > generationGuard && dcReset > acReset && recovery > dcReset);
        Assert.Contains("var acFpsPending = _acFpsDebounce is { IsCancellationRequested: false } && acFpsDraft.HasValue", code, StringComparison.Ordinal);
        Assert.Contains("var dcFpsPending = _dcFpsDebounce is { IsCancellationRequested: false } && dcFpsDraft.HasValue", code, StringComparison.Ordinal);
    }

    [Fact]
    public void Unknown_favorite_does_not_block_catalog_browsing_and_actions_are_state_gated()
    {
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

    [Theory]
    [InlineData(480u)]
    [InlineData(228980u)]
    [InlineData(993090u)]
    public void Steam_catalog_hides_only_the_configured_native_app_ids(uint appId)
    {
        var hidden = new FrontendProfileGameCatalogEntry(
            appId,
            "Renamed or localized title",
            FrontendProfileGameSource.Steam,
            Favorite: true,
            FavoriteStateKnown: false);

        Assert.False(ProfilePage.ShouldShowInSteamCatalog(hidden));
        Assert.True(ProfilePage.ShouldShowInSteamCatalog(
            new FrontendProfileGameCatalogEntry(570u, "Ordinary game", FrontendProfileGameSource.Steam)));
    }

    [Fact]
    public void Profile_capability_visibility_uses_static_support_and_keeps_runtime_failure_as_status()
    {
        var root = FindRepositoryRoot();
        var code = File.ReadAllText(Path.Combine(root, "src/SteamInputAddonforClaw.UI/Views/ProfilePage.xaml.cs"));
        var initialize = Method(code, "internal void Initialize(IAddonFrontendControl frontend,", "internal void Activate()");
        var render = Method(code, "private void Render(FrontendGameProfileSnapshot snapshot", "private void SetEditorsEnabled(");
        var status = Method(code, "private void RenderProfileStatus(", "private void SetEditorsEnabled(");

        Assert.Contains("TdpCard.Visibility = _featureSupport.Tdp ? Visibility.Visible : Visibility.Collapsed", initialize);
        Assert.Contains("IntelFpsExpander.Visibility = _featureSupport.IntelFpsLimit ? Visibility.Visible : Visibility.Collapsed", initialize);
        Assert.Contains("GpuMinimumClockExpander.Visibility = _featureSupport.IntelGpuMinimumClock ? Visibility.Visible : Visibility.Collapsed", initialize);
        Assert.Contains("TdpEnabledToggle.IsEnabled = snapshot.Exists && snapshot.Enabled && snapshot.PersistenceWritable && _featureSupport.Tdp && snapshot.Limits is not null", render);
        Assert.Contains("snapshot.FpsLimit?.Available == true", render);
        Assert.Contains("if (!snapshot.Exists)", status);
        Assert.Contains("_featureSupport.Tdp && snapshot.Enabled && snapshot.Limits is null", status);
        Assert.Contains("preservePendingFpsDraft: true", code);
        Assert.Contains("DisableEditorsAfterCaptureFailure();", code);
        Assert.Contains("preserveUnavailableDraft = preserveDraft && _gpuDraftDirty && gpu?.Available != true", code);
    }

    [Theory]
    [InlineData(480u)]
    [InlineData(228980u)]
    [InlineData(993090u)]
    public void Steam_catalog_keeps_non_steam_shortcuts_with_matching_app_ids(uint appId)
    {
        var shortcut = new FrontendProfileGameCatalogEntry(
            appId,
            "User shortcut",
            FrontendProfileGameSource.NonSteam);

        Assert.True(ProfilePage.ShouldShowInSteamCatalog(shortcut));
    }

    [Fact]
    public void Steam_catalog_filters_by_source_and_app_id_before_search_and_favorite_sorting()
    {
        var games = new[]
        {
            new FrontendProfileGameCatalogEntry(480u, "Localized title", FrontendProfileGameSource.Steam, Favorite: true, FavoriteStateKnown: false),
            new FrontendProfileGameCatalogEntry(228980u, "Renamed dependency", FrontendProfileGameSource.Steam, Favorite: true),
            new FrontendProfileGameCatalogEntry(993090u, "Different displayed name", FrontendProfileGameSource.Steam),
            new FrontendProfileGameCatalogEntry(570u, "Zulu ordinary", FrontendProfileGameSource.Steam),
            new FrontendProfileGameCatalogEntry(571u, "Localized title", FrontendProfileGameSource.Steam, Favorite: true),
            new FrontendProfileGameCatalogEntry(993090u, "Non-Steam shortcut", FrontendProfileGameSource.NonSteam)
        };

        var visible = ProfilePage.FilterSteamCatalog(games);

        Assert.Equal(
            new (uint AppId, FrontendProfileGameSource Source)[]
            {
                (570u, FrontendProfileGameSource.Steam),
                (571u, FrontendProfileGameSource.Steam),
                (993090u, FrontendProfileGameSource.NonSteam)
            },
            visible.Select(game => (game.AppId, game.Source)));
        Assert.Equal(new uint[] { 571u, 993090u, 570u }, ProfilePage.FilterAndSort(visible, null).Select(game => game.AppId));
        Assert.Empty(ProfilePage.FilterAndSort(visible, "480"));
        Assert.Equal(new uint[] { 571u }, ProfilePage.FilterAndSort(visible, "Localized title").Select(game => game.AppId));

        var hiddenOnly = games.Where(game => game.Source == FrontendProfileGameSource.Steam && game.AppId != 570u && game.AppId != 571u);
        Assert.Empty(ProfilePage.FilterSteamCatalog(hiddenOnly));
    }

    [Fact]
    public void Steam_catalog_empty_state_distinguishes_no_scan_results_from_hidden_only_results()
    {
        Assert.Equal("Steam_Catalog_NoGames", ProfilePage.GetEmptyCatalogMessageKey(scannedGameCount: 0));
        Assert.Equal("Steam_Catalog_NoVisibleGames", ProfilePage.GetEmptyCatalogMessageKey(scannedGameCount: 3));

        var root = FindRepositoryRoot();
        var code = File.ReadAllText(Path.Combine(root, "src/SteamInputAddonforClaw.UI/Views/ProfilePage.xaml.cs"));
        var refresh = Method(code, "private async Task RefreshGamesAsync(", "private void GameSearchBox_TextChanged(");
        var restoreSelection = refresh.IndexOf("_selectedGame = selected is { } id ? _catalog.FirstOrDefault(x => x.AppId == id) : null", StringComparison.Ordinal);
        Assert.True(restoreSelection >= 0);
        var clearSelection = refresh.IndexOf("ClearSelection()", restoreSelection, StringComparison.Ordinal);
        var emptyCatalog = refresh.IndexOf("if (_catalog.Count == 0)", clearSelection, StringComparison.Ordinal);
        var emptyMessage = refresh.IndexOf("GetEmptyCatalogMessageKey(scannedCatalog.Count)", emptyCatalog, StringComparison.Ordinal);
        Assert.True(restoreSelection >= 0 && clearSelection > restoreSelection && emptyCatalog > clearSelection && emptyMessage > emptyCatalog);
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
        var successfulCache = refresh.IndexOf("_catalog = FilterSteamCatalog(scannedCatalog)", StringComparison.Ordinal);
        var loadedFlag = refresh.IndexOf("_catalogLoaded = true", StringComparison.Ordinal);
        var favoriteWarning = refresh.IndexOf("FavoriteStateInfoBar.IsOpen = _catalog.Any(game => !game.FavoriteStateKnown)", StringComparison.Ordinal);
        var applyFilter = refresh.IndexOf("ApplyCatalogFilter()", StringComparison.Ordinal);
        var failure = refresh.IndexOf("catch (Exception exception)", loadedFlag, StringComparison.Ordinal);
        Assert.True(scan >= 0 && successfulCache > scan && loadedFlag > successfulCache);
        Assert.True(favoriteWarning > loadedFlag && applyFilter > favoriteWarning);
        Assert.True(failure > loadedFlag);
        Assert.DoesNotContain("_catalogLoaded = false", refresh, StringComparison.Ordinal);
        Assert.Contains("await RefreshGamesAsync(explicitRefresh: true)", manualRefresh, StringComparison.Ordinal);
        Assert.DoesNotContain("if (_catalogLoaded) return", refresh, StringComparison.Ordinal);
    }

    [Fact]
    public void Steam_catalog_keeps_virtualized_game_and_favorite_actions_independent()
    {
        var root = FindRepositoryRoot();
        var xamlPath = Path.Combine(root, "src/SteamInputAddonforClaw.UI/Views/ProfilePage.xaml");
        var xaml = File.ReadAllText(xamlPath);
        var catalog = xaml[..xaml.IndexOf("<Grid x:Name=\"DetailPanel\"", StringComparison.Ordinal)];
        var document = XDocument.Parse(xaml);
        var grid = Assert.Single(document.Descendants(), element =>
            element.Name.LocalName == "GridView" && (string?)element.Attribute(XName.Get("Name", "http://schemas.microsoft.com/winfx/2006/xaml")) == "GameGrid");
        Assert.Equal("None", (string?)grid.Attribute("SelectionMode"));
        Assert.Equal("False", (string?)grid.Attribute("IsItemClickEnabled"));
        Assert.Equal("False", (string?)grid.Attribute("CanDragItems"));
        Assert.Equal("False", (string?)grid.Attribute("CanReorderItems"));
        Assert.Contains("ContainerContentChanging=\"GameGrid_ContainerContentChanging\"", catalog, StringComparison.Ordinal);
        Assert.Contains("ScrollViewer.VerticalScrollMode=\"Enabled\"", catalog, StringComparison.Ordinal);
        Assert.Contains("ScrollViewer.HorizontalScrollMode=\"Disabled\"", catalog, StringComparison.Ordinal);
        Assert.DoesNotContain("<ScrollViewer", catalog, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"OpenGameButton\"", catalog, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"FavoriteButton\"", catalog, StringComparison.Ordinal);
        Assert.Contains("Click=\"GameCardButton_Click\"", catalog, StringComparison.Ordinal);
        Assert.Contains("Click=\"FavoriteButton_Click\"", catalog, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.Name=\"{Binding Name}\"", catalog, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.Name=\"{Binding FavoriteAutomationName}\"", catalog, StringComparison.Ordinal);
        Assert.Contains("IsEnabled=\"{Binding FavoriteStateKnown}\"", catalog, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(1.0)]
    [InlineData(1.25)]
    [InlineData(1.5)]
    [InlineData(2.0)]
    public void Steam_cover_grid_layout_stays_inside_the_measured_viewport_at_supported_DPI_scales(double scale)
    {
        foreach (var viewportDip in new[] { 240.0, 380.0, 709.0, 980.0, 1200.0 })
        {
            Assert.True(SteamCoverGridLayout.TryCalculate(viewportDip, scale, out var layout));
            Assert.True(double.IsFinite(layout.SlotWidthDip) && layout.SlotWidthDip > 0);
            Assert.True(double.IsFinite(layout.CoverWidthDip) && layout.CoverWidthDip > 0);
            Assert.InRange(layout.Columns, 1, int.MaxValue);
            Assert.True(layout.SlotWidthDip <= viewportDip + 0.01);
            Assert.True(layout.Columns * layout.SlotWidthDip <= viewportDip + 0.01);
            Assert.True(layout.CoverWidthDip <= layout.SlotWidthDip);
        }
    }
    [Theory]
    [InlineData(0, false)]
    [InlineData(-1, false)]
    [InlineData(double.NaN, false)]
    [InlineData(double.PositiveInfinity, false)]
    [InlineData(double.NegativeInfinity, false)]
    [InlineData(double.MaxValue, false)]
    [InlineData(0.5, false)]
    [InlineData(1, true)]
    [InlineData(50, true)]
    [InlineData(118, true)]
    [InlineData(125, true)]
    [InlineData(126, true)]
    public void Steam_cover_grid_rejects_unmeasured_width_and_keeps_valid_viewports_interactive(double width, bool expectedSuccess)
    {
        var result = SteamCoverGridLayout.TryCalculate(width, out var layout);
        Assert.Equal(expectedSuccess, result);
        if (!result) return;
        Assert.True(layout.Columns >= 1);
        Assert.True(layout.SlotWidthDip > 0 && layout.SlotWidthDip <= width + 0.01);
        Assert.True(layout.CoverWidthDip > 0);
        if (layout.CoverWidthDip < SteamCoverGridLayout.MinimumCoverWidthDip)
            Assert.Equal(1, layout.Columns);
    }
    [Theory]
    [InlineData(0, 900, true)]
    [InlineData(double.NaN, 900, true)]
    [InlineData(900, 900, false)]
    [InlineData(900, 901, true)]
    [InlineData(900, 0, false)]
    [InlineData(900, double.PositiveInfinity, false)]
    public void Steam_cover_grid_relayout_policy_requires_a_valid_changed_width(
        double previousWidthDip,
        double newWidthDip,
        bool expected)
    {
        Assert.Equal(expected, SteamCoverGridLayout.HasMeaningfulWidthChange(previousWidthDip, newWidthDip));
    }

    [Fact]
    public void Steam_cover_grid_reflow_uses_the_loaded_items_panel_width_and_keeps_resize_and_DPI_hooks()
    {
        var root = FindRepositoryRoot();
        var xaml = File.ReadAllText(Path.Combine(root, "src/SteamInputAddonforClaw.UI/Views/ProfilePage.xaml"));
        var code = File.ReadAllText(Path.Combine(root, "src/SteamInputAddonforClaw.UI/Views/ProfilePage.xaml.cs"));
        var catalog = xaml[..xaml.IndexOf("<Grid x:Name=\"DetailPanel\"", StringComparison.Ordinal)];
        var panelLoaded = Method(code, "private void CoverItemsPanel_Loaded(", "private void AttachCoverItemsPanel(");
        var attach = Method(code, "private void AttachCoverItemsPanel(", "private void DetachCoverItemsPanel(");
        var updateLayout = Method(code, "private void ApplyCoverGridLayout(", "private static bool LayoutsMatch(");
        var rootChanged = Method(code, "private void XamlRoot_Changed(", "private void GameGrid_SizeChanged(");
        var gridSizeChanged = Method(code, "private void GameGrid_SizeChanged(", "private void GameGridItemsPanel_SizeChanged(");
        var itemsPanelSizeChanged = Method(code, "private void GameGridItemsPanel_SizeChanged(", "private void ScheduleCoverGridLayoutUpdate(");
        var resize = Method(code, "private void ScheduleCoverGridLayoutUpdate(", "private void CoverGridResizeTimer_Tick(");

        Assert.DoesNotContain("Loaded=\"GameGrid_Loaded\"", catalog, StringComparison.Ordinal);
        Assert.Contains("SizeChanged=\"GameGrid_SizeChanged\"", catalog, StringComparison.Ordinal);
        Assert.Contains("sender is ItemsWrapGrid itemsPanel", panelLoaded, StringComparison.Ordinal);
        Assert.Contains("AttachCoverItemsPanel(itemsPanel)", panelLoaded, StringComparison.Ordinal);
        Assert.Contains("ItemsWrapGrid itemsPanel", attach, StringComparison.Ordinal);
        Assert.Contains("_coverItemsPanel.SizeChanged += GameGridItemsPanel_SizeChanged", attach, StringComparison.Ordinal);
        Assert.Contains("_coverGridLayout = null", attach, StringComparison.Ordinal);
        Assert.Contains("itemsPanel.ActualWidth", updateLayout, StringComparison.Ordinal);
        Assert.DoesNotContain("GameGrid.ActualWidth", updateLayout, StringComparison.Ordinal);
        Assert.DoesNotContain("CatalogScrollViewer", code, StringComparison.Ordinal);
        Assert.DoesNotContain("TryResolveAvailableWidthDip", code, StringComparison.Ordinal);
        Assert.DoesNotContain("MaximumRowsOrColumns", code, StringComparison.Ordinal);
        Assert.DoesNotContain("UniformGridLayout", code, StringComparison.Ordinal);
        Assert.Contains("itemsPanel.ItemWidth = nextLayout.SlotWidthDip", updateLayout, StringComparison.Ordinal);
        Assert.Contains("itemsPanel.ItemHeight = nextLayout.SlotHeightDip", updateLayout, StringComparison.Ordinal);
        Assert.Contains("SetCardLayout(card.Root, nextLayout)", updateLayout, StringComparison.Ordinal);
        Assert.Contains("if (!snapshotChanged)", updateLayout, StringComparison.Ordinal);
        Assert.Contains("ApplyCoverGridLayout(scale)", rootChanged, StringComparison.Ordinal);
        Assert.Contains("HasMeaningfulWidthChange(e.PreviousSize.Width, e.NewSize.Width)", gridSizeChanged, StringComparison.Ordinal);
        Assert.Contains("HasMeaningfulWidthChange(e.PreviousSize.Width, e.NewSize.Width)", itemsPanelSizeChanged, StringComparison.Ordinal);
        Assert.Contains("TimeSpan.FromMilliseconds(180)", code, StringComparison.Ordinal);
        Assert.Contains("_coverGridResizeTimer.Stop()", resize, StringComparison.Ordinal);
        Assert.Contains("_coverGridResizeTimer.Start()", resize, StringComparison.Ordinal);
        Assert.DoesNotContain("ChangeView(", code, StringComparison.Ordinal);
    }

    [Fact]
    public void Steam_cover_grid_attaches_when_items_are_created_after_the_initial_page_load()
    {
        var root = FindRepositoryRoot();
        var xaml = File.ReadAllText(Path.Combine(root, "src/SteamInputAddonforClaw.UI/Views/ProfilePage.xaml"));
        var code = File.ReadAllText(Path.Combine(root, "src/SteamInputAddonforClaw.UI/Views/ProfilePage.xaml.cs"));
        var refresh = Method(code, "private async Task RefreshGamesAsync(", "private void GameSearchBox_TextChanged(");
        var applyFilter = Method(code, "private void ApplyCatalogFilter(", "private void ProfilePage_Loaded(");
        var pageLoaded = Method(code, "private void ProfilePage_Loaded(", "private void CoverItemsPanel_Loaded(");
        var panelLoaded = Method(code, "private void CoverItemsPanel_Loaded(", "private void AttachCoverItemsPanel(");
        var attach = Method(code, "private void AttachCoverItemsPanel(", "private void DetachCoverItemsPanel(");
        var applyLayout = Method(code, "private void ApplyCoverGridLayout(", "private static bool LayoutsMatch(");
        var scanIndex = refresh.IndexOf("await _frontend.ScanProfileGamesAsync()", StringComparison.Ordinal);
        var filterIndex = refresh.IndexOf("ApplyCatalogFilter()", StringComparison.Ordinal);

        Assert.Contains("Loaded=\"CoverItemsPanel_Loaded\"", xaml, StringComparison.Ordinal);
        Assert.True(scanIndex >= 0 && filterIndex > scanIndex);
        Assert.Contains("GameGrid.ItemsSource = _visibleGameCards", applyFilter, StringComparison.Ordinal);
        Assert.DoesNotContain("AttachCoverItemsPanel", pageLoaded, StringComparison.Ordinal);
        Assert.Contains("if (sender is ItemsWrapGrid itemsPanel)", panelLoaded, StringComparison.Ordinal);
        Assert.Contains("AttachCoverItemsPanel(itemsPanel)", panelLoaded, StringComparison.Ordinal);
        Assert.Contains("_coverGridLayout = null", attach, StringComparison.Ordinal);
        Assert.Contains("ApplyCoverGridLayout(GameGrid.XamlRoot?.RasterizationScale ?? 1.0)", attach, StringComparison.Ordinal);
        Assert.Contains("GameGrid.Opacity = 1", applyLayout, StringComparison.Ordinal);
    }

    [Fact]
    public void Steam_favorite_action_remains_accessible_and_state_gated()
    {
        var root = FindRepositoryRoot();
        var xaml = File.ReadAllText(Path.Combine(root, "src/SteamInputAddonforClaw.UI/Views/ProfilePage.xaml"));
        var styleStart = xaml.IndexOf("<Style x:Key=\"SteamCoverFavoriteStarButtonStyle\"", StringComparison.Ordinal);
        var styleEnd = xaml.IndexOf("</Style>", styleStart, StringComparison.Ordinal);
        Assert.True(styleStart >= 0 && styleEnd > styleStart);
        var style = xaml[styleStart..(styleEnd + "</Style>".Length)];
        var catalog = xaml[..xaml.IndexOf("<Grid x:Name=\"DetailPanel\"", StringComparison.Ordinal)];
        var document = XDocument.Parse(xaml);
        var favorite = Assert.Single(document.Descendants(), element => element.Name.LocalName == "Button"
            && (string?)element.Attribute(XName.Get("Name", "http://schemas.microsoft.com/winfx/2006/xaml")) == "FavoriteButton");
        Assert.True(double.TryParse((string?)favorite.Attribute("Width"), out var width) && width >= 32);
        Assert.True(double.TryParse((string?)favorite.Attribute("Height"), out var height) && height >= 32);
        Assert.Equal("FavoriteButton_Click", (string?)favorite.Attribute("Click"));
        Assert.Equal("{Binding FavoriteAutomationName}", (string?)favorite.Attribute("AutomationProperties.Name"));
        Assert.Equal("{Binding FavoriteStateKnown}", (string?)favorite.Attribute("IsEnabled"));
        Assert.Contains("FocusRing.Visibility", style, StringComparison.Ordinal);
        Assert.Contains("FavoriteAutomationName", catalog, StringComparison.Ordinal);
        Assert.Contains("Click=\"GameCardButton_Click\"", catalog, StringComparison.Ordinal);
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
        var capsuleCache = temp.PathFor($"appcache/librarycache/{appId}/library_capsule_2x.jpg");
        temp.CreateFile(customPng);
        temp.CreateFile(customJpg);
        temp.CreateFile(customJpeg);
        temp.CreateFile(cache);
        temp.CreateFile(capsuleCache);
        temp.CreateFile(temp.PathFor($"appcache/librarycache/{appId}/header.jpg"));
        temp.CreateFile(temp.PathFor($"appcache/librarycache/{appId}/library_hero.jpg"));

        var candidates = SteamPortraitArtworkResolver.ResolveCandidates(
            temp.Root,
            appId,
            FrontendProfileGameSource.Steam,
            accountId);

        Assert.Equal([customPng, customJpg, customJpeg, cache, capsuleCache], candidates);
        Assert.DoesNotContain(candidates, path => Path.GetFileName(path).Contains("header", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(candidates, path => Path.GetFileName(path).Contains("hero", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Steam_portrait_cache_uses_capsule_names_in_direct_and_hashed_directories()
    {
        using var temp = new TemporaryDirectory();
        const uint appId = 730;
        var cache = $"appcache/librarycache/{appId}";
        var directTwoX = temp.PathFor($"{cache}/library_600x900_2x.jpg");
        var hashedTwoX = temp.PathFor($"{cache}/abc/library_600x900_2x.jpg");
        var directCapsuleTwoX = temp.PathFor($"{cache}/library_capsule_2x.jpg");
        var hashedCapsuleTwoX = temp.PathFor($"{cache}/abc/library_capsule_2x.jpg");
        var flatTwoX = temp.PathFor($"appcache/librarycache/{appId}_library_600x900_2x.jpg");
        var direct = temp.PathFor($"{cache}/library_600x900.jpg");
        var hashed = temp.PathFor($"{cache}/abc/library_600x900.jpg");
        var directCapsule = temp.PathFor($"{cache}/library_capsule.jpg");
        var hashedCapsule = temp.PathFor($"{cache}/abc/library_capsule.jpg");
        var flat = temp.PathFor($"appcache/librarycache/{appId}_library_600x900.jpg");
        var unverifiedFlatCapsule = temp.PathFor($"appcache/librarycache/{appId}_library_capsule.jpg");
        foreach (var path in new[] {
            directTwoX, hashedTwoX, directCapsuleTwoX, hashedCapsuleTwoX, flatTwoX,
            direct, hashed, directCapsule, hashedCapsule, flat, unverifiedFlatCapsule
        })
            temp.CreateFile(path);

        var candidates = SteamPortraitArtworkResolver.ResolveCandidates(
            temp.Root,
            appId,
            FrontendProfileGameSource.Steam,
            accountId: null);

        Assert.Equal([
            directTwoX, hashedTwoX, directCapsuleTwoX, hashedCapsuleTwoX, flatTwoX,
            direct, hashed, directCapsule, hashedCapsule, flat
        ], candidates);
        Assert.DoesNotContain(unverifiedFlatCapsule, candidates);
    }

    [Fact]
    public void Portrait_candidate_paths_are_cached_per_source_and_app_id_for_the_current_catalog_view()
    {
        using var temp = new TemporaryDirectory();
        const uint appId = 480;
        const uint accountId = 123;
        var context = new SteamPortraitArtworkContext(temp.Root, accountId);
        var steamGame = new FrontendProfileGameCatalogEntry(appId, "Spacewar", FrontendProfileGameSource.Steam);
        var shortcut = new FrontendProfileGameCatalogEntry(appId, "Shortcut", FrontendProfileGameSource.NonSteam);
        var steamArt = temp.PathFor($"appcache/librarycache/{appId}/library_600x900.jpg");
        var shortcutArt = temp.PathFor($"userdata/{accountId}/config/grid/{appId}p.png");
        temp.CreateFile(steamArt);
        temp.CreateFile(shortcutArt);

        Assert.Equal([shortcutArt, steamArt], context.GetCandidates(steamGame));
        Assert.Equal([shortcutArt], context.GetCandidates(shortcut));
    }

    [Fact]
    public void Non_steam_shortcuts_use_active_account_shortcut_app_id_before_launch_id_fallback()
    {
        using var temp = new TemporaryDirectory();
        const uint accountId = 123;
        const uint appId = 123456789;
        var customDirectory = temp.PathFor($"userdata/{accountId}/config/grid");
        var appIdPng = Path.Combine(customDirectory, $"{appId}p.png");
        var appIdJpg = Path.Combine(customDirectory, $"{appId}p.jpg");
        var appIdJpeg = Path.Combine(customDirectory, $"{appId}p.jpeg");
        var launchId = ((ulong)appId << 32) | 0x02000000UL;
        var launchArt = temp.PathFor($"userdata/{accountId}/config/grid/{launchId}p.png");
        temp.CreateFile(appIdPng);
        temp.CreateFile(appIdJpg);
        temp.CreateFile(appIdJpeg);
        temp.CreateFile(launchArt);

        Assert.Equal([appIdPng, appIdJpg, appIdJpeg, launchArt], SteamPortraitArtworkResolver.ResolveCandidates(
            temp.Root,
            appId,
            FrontendProfileGameSource.NonSteam,
            accountId));

        const uint fallbackAppId = 987654321;
        var fallbackLaunchId = ((ulong)fallbackAppId << 32) | 0x02000000UL;
        var fallbackArt = temp.CreateFile(temp.PathFor($"userdata/{accountId}/config/grid/{fallbackLaunchId}p.jpg"));
        Assert.Equal([fallbackArt], SteamPortraitArtworkResolver.ResolveCandidates(
            temp.Root,
            fallbackAppId,
            FrontendProfileGameSource.NonSteam,
            accountId));

        var maxAppIdArt = temp.CreateFile(temp.PathFor($"userdata/{accountId}/config/grid/{uint.MaxValue}p.png"));
        Assert.Equal([maxAppIdArt], SteamPortraitArtworkResolver.ResolveCandidates(
            temp.Root,
            uint.MaxValue,
            FrontendProfileGameSource.NonSteam,
            accountId));

        temp.CreateFile(temp.PathFor("userdata/456/config/grid/321p.png"));
        Assert.Empty(SteamPortraitArtworkResolver.ResolveCandidates(
            temp.Root,
            321,
            FrontendProfileGameSource.NonSteam,
            accountId));
        Assert.Empty(SteamPortraitArtworkResolver.ResolveCandidates(
            temp.Root,
            321,
            FrontendProfileGameSource.NonSteam,
            accountId: null));
        temp.CreateFile(temp.PathFor("appcache/librarycache/654/library_600x900.jpg"));
        Assert.Empty(SteamPortraitArtworkResolver.ResolveCandidates(
            temp.Root,
            654,
            FrontendProfileGameSource.NonSteam,
            accountId));
        Assert.Empty(SteamPortraitArtworkResolver.ResolveCandidates(
            temp.Root,
            appId: 480,
            FrontendProfileGameSource.Steam,
            accountId: null));

        var code = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "src/SteamInputAddonforClaw.UI/Views/ProfilePage.xaml.cs"));
        var refresh = Method(code, "private async Task RefreshGamesAsync(", "private void GameSearchBox_TextChanged(");
        Assert.Contains("if (scannedCatalog.Count > 0)", refresh, StringComparison.Ordinal);
        var load = Method(code, "private void StartGameCardArtworkLoad(", "private async Task LoadGameCardArtworkAsync(");
        Assert.DoesNotContain("card.Item.Source != FrontendProfileGameSource.Steam", load, StringComparison.Ordinal);
        Assert.Contains("_portraitArtworkContext is null", load, StringComparison.Ordinal);
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
    public void Gridview_cover_loading_uses_container_phases_and_cleans_up_recycled_items()
    {
        var root = FindRepositoryRoot();
        var xaml = File.ReadAllText(Path.Combine(root, "src/SteamInputAddonforClaw.UI/Views/ProfilePage.xaml"));
        var code = File.ReadAllText(Path.Combine(root, "src/SteamInputAddonforClaw.UI/Views/ProfilePage.xaml.cs"));
        Assert.Contains("ContainerContentChanging=\"GameGrid_ContainerContentChanging\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("ElementPrepared", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("ElementClearing", xaml, StringComparison.Ordinal);
        Assert.Contains("Dictionary<SelectorItem, RealizedGameCard>", code, StringComparison.Ordinal);
        Assert.Contains("cancellationToken.ThrowIfCancellationRequested()", code, StringComparison.Ordinal);
        Assert.Contains("card.Root.DataContext is GameCardItem item", code, StringComparison.Ordinal);
        Assert.Contains("card.Matches(item)", code, StringComparison.Ordinal);
        Assert.Contains("recycledCard.ClearArtwork()", code, StringComparison.Ordinal);
        Assert.Contains("_realizedGameCards.TryGetValue(card.Container, out var current)", code, StringComparison.Ordinal);

        var handler = Method(code, "private void GameGrid_ContainerContentChanging(", "private void StartGameCardArtworkLoad(");
        var recycleBranch = handler.IndexOf("if (args.InRecycleQueue)", StringComparison.Ordinal);
        var itemValidation = handler.IndexOf("args.Item is not GameCardItem item", StringComparison.Ordinal);
        var templateRoot = handler.IndexOf("container.ContentTemplateRoot is not Border root", StringComparison.Ordinal);
        var rootContextAssignment = handler.IndexOf("root.DataContext = item", StringComparison.Ordinal);
        var namedElementLookup = handler.IndexOf("root.FindName(\"CoverImageFrame\")", StringComparison.Ordinal);
        var phaseZeroFallback = handler.IndexOf("if (args.Phase == 0)", StringComparison.Ordinal);
        var deferredCallback = handler.IndexOf("args.RegisterUpdateCallback(GameGrid_ContainerContentChanging)", StringComparison.Ordinal);
        var layoutApplication = handler.IndexOf("SetCardLayout(root, layout)", StringComparison.Ordinal);
        var cardLookup = handler.IndexOf("_realizedGameCards.TryGetValue(container, out var existing)", StringComparison.Ordinal);
        Assert.True(recycleBranch >= 0 && itemValidation > recycleBranch && templateRoot > itemValidation);
        Assert.True(rootContextAssignment > templateRoot && namedElementLookup > rootContextAssignment);
        Assert.True(phaseZeroFallback > templateRoot && deferredCallback > phaseZeroFallback);
        Assert.True(layoutApplication > deferredCallback && cardLookup > layoutApplication);
        Assert.DoesNotContain("if (args.Phase < 1", handler, StringComparison.Ordinal);
        Assert.Contains("_realizedGameCards.Remove(container, out var recycledCard)", handler, StringComparison.Ordinal);
        Assert.Contains("recycledCard.CancelLoad()", handler, StringComparison.Ordinal);
        Assert.Contains("recycledCard.ClearArtwork()", handler, StringComparison.Ordinal);
        Assert.Contains("recycledCard.FallbackCard.Visibility = Visibility.Visible", handler, StringComparison.Ordinal);
        Assert.Contains("args.RegisterUpdateCallback(GameGrid_ContainerContentChanging)", handler, StringComparison.Ordinal);
        Assert.DoesNotContain("args.Handled = true", handler, StringComparison.Ordinal);
        Assert.Contains("args.Item is not GameCardItem item", handler, StringComparison.Ordinal);
        Assert.Contains("ShouldSetGameCardDataContext(root.DataContext, item)", handler, StringComparison.Ordinal);
        Assert.Contains("new RealizedGameCard(container, root", handler, StringComparison.Ordinal);
        Assert.Contains("StartGameCardArtworkLoad(existing)", handler, StringComparison.Ordinal);

        var load = Method(code, "private async Task LoadGameCardArtworkAsync(", "private static Task<BitmapImage?> TryLoadProfileArtworkAsync(");
        var identityCheck = load.IndexOf("!IsCurrentGameCard(card, generation)", StringComparison.Ordinal);
        var imageAssignment = load.IndexOf("card.CoverImageFrame.Background = new ImageBrush", StringComparison.Ordinal);
        Assert.True(identityCheck >= 0 && imageAssignment > identityCheck);
        Assert.Contains("_observedXamlRoot.Changed += XamlRoot_Changed", code, StringComparison.Ordinal);
        Assert.Contains("_observedXamlRoot.Changed -= XamlRoot_Changed", code, StringComparison.Ordinal);
        Assert.Contains("_coverItemsPanel.SizeChanged += GameGridItemsPanel_SizeChanged", code, StringComparison.Ordinal);
        Assert.Contains("_coverItemsPanel.SizeChanged -= GameGridItemsPanel_SizeChanged", code, StringComparison.Ordinal);
        Assert.DoesNotContain("ApplyCoverGridLayout", handler, StringComparison.Ordinal);
        Assert.Contains("RestartMissingGameCardArtworkLoads()", code, StringComparison.Ordinal);
    }


    [Fact]
    public void Steam_grid_rebinds_null_or_stale_item_context_before_artwork_initialization()
    {
        var current = new ProfilePage.GameCardItem(
            new FrontendProfileGameCatalogEntry(100u, "Current game", FrontendProfileGameSource.Steam));
        var stale = new ProfilePage.GameCardItem(
            new FrontendProfileGameCatalogEntry(99u, "Previously recycled game", FrontendProfileGameSource.Steam));

        Assert.True(ProfilePage.ShouldSetGameCardDataContext(null, current));
        Assert.True(ProfilePage.ShouldSetGameCardDataContext(stale, current));
        Assert.False(ProfilePage.ShouldSetGameCardDataContext(current, current));

        var root = FindRepositoryRoot();
        var code = File.ReadAllText(Path.Combine(root, "src/SteamInputAddonforClaw.UI/Views/ProfilePage.xaml.cs"));
        var handler = Method(code, "private void GameGrid_ContainerContentChanging(", "private void StartGameCardArtworkLoad(");
        var rootContextAssignment = handler.IndexOf("root.DataContext = item", StringComparison.Ordinal);
        var favoriteAssignment = handler.IndexOf("favoriteButton.DataContext = item", StringComparison.Ordinal);
        var openAssignment = handler.IndexOf("openButton.DataContext = item", StringComparison.Ordinal);
        var artworkStart = handler.IndexOf("StartGameCardArtworkLoad(existing)", StringComparison.Ordinal);

        Assert.True(rootContextAssignment >= 0);
        Assert.True(favoriteAssignment > rootContextAssignment && openAssignment > rootContextAssignment);
        Assert.True(artworkStart > openAssignment);
        Assert.DoesNotContain("args.Handled = true", handler, StringComparison.Ordinal);
    }

    [Fact]
    public void Steam_artwork_initialization_diagnostics_are_bounded_and_distinguish_failure_paths()
    {
        var loggedFlags = 0;
        Assert.True(ProfilePage.TryMarkArtworkDiagnostic(ref loggedFlags, 1));
        Assert.False(ProfilePage.TryMarkArtworkDiagnostic(ref loggedFlags, 1));
        Assert.True(ProfilePage.TryMarkArtworkDiagnostic(ref loggedFlags, 2));
        Assert.False(ProfilePage.TryMarkArtworkDiagnostic(ref loggedFlags, 2));

        var root = FindRepositoryRoot();
        var code = File.ReadAllText(Path.Combine(root, "src/SteamInputAddonforClaw.UI/Views/ProfilePage.xaml.cs"));
        var refresh = Method(code, "private async Task RefreshGamesAsync(", "private void GameSearchBox_TextChanged(");
        var load = Method(code, "private async Task LoadGameCardArtworkAsync(", "private static Task<BitmapImage?> TryLoadProfileArtworkAsync(");
        var logger = Method(code, "private void LogArtworkDiagnosticOnce(", "private FrontendProfileGameCatalogEntry? ResolveClickedGame(");

        Assert.Contains("SteamRootUnavailable", refresh, StringComparison.Ordinal);
        Assert.Contains("ContextCreationFailed", refresh, StringComparison.Ordinal);
        Assert.Contains("exception.GetType().Name", refresh, StringComparison.Ordinal);
        Assert.Contains("NoArtworkCandidate", load, StringComparison.Ordinal);
        Assert.Contains("CandidatesUnreadableOrUnsupported", load, StringComparison.Ordinal);
        Assert.Contains("StaleArtworkCompletion", load, StringComparison.Ordinal);
        Assert.Contains("ArtworkLoadCanceled", load, StringComparison.Ordinal);
        Assert.Contains("TryMarkArtworkDiagnostic(ref _artworkDiagnosticFlags, diagnosticFlag)", logger, StringComparison.Ordinal);
        Assert.DoesNotContain("exception.Message", logger, StringComparison.Ordinal);
        Assert.DoesNotContain("SteamRoot", logger, StringComparison.Ordinal);
        Assert.DoesNotContain("AppId", logger, StringComparison.Ordinal);
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
        Assert.DoesNotContain("ApplyCatalogFilter()", favorite, StringComparison.Ordinal);
        Assert.Contains("ApplyFavoriteToVisibleCards", favorite, StringComparison.Ordinal);
        Assert.Contains("IsEnabled=\"{Binding FavoriteStateKnown}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("FavoriteAutomationName", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void Steam_cover_and_favorite_resolve_the_current_grid_item_for_steam_and_non_steam_games()
    {
        var steamGame = new FrontendProfileGameCatalogEntry(100u, "Steam game", FrontendProfileGameSource.Steam);
        var nonSteamGame = new FrontendProfileGameCatalogEntry(200u, "Non-Steam game", FrontendProfileGameSource.NonSteam);
        var staleGame = new FrontendProfileGameCatalogEntry(1u, "Previously realized game", FrontendProfileGameSource.Steam);

        var steamCard = new ProfilePage.GameCardItem(steamGame);
        var nonSteamCard = new ProfilePage.GameCardItem(nonSteamGame);

        Assert.Same(steamGame, ProfilePage.ResolveGameCardItem(steamCard));
        Assert.Same(nonSteamGame, ProfilePage.ResolveGameCardItem(nonSteamCard));
        Assert.NotSame(staleGame, ProfilePage.ResolveGameCardItem(steamCard));
    }

    [Fact]
    public void Steam_grid_item_resolution_rejects_missing_or_unexpected_items()
    {
        Assert.Null(ProfilePage.ResolveGameCardItem(null));
        Assert.Null(ProfilePage.ResolveGameCardItem(new object()));
    }

    [Fact]
    public void Steam_grid_rebinds_open_and_favorite_buttons_to_the_current_recycled_item()
    {
        var root = FindRepositoryRoot();
        var code = File.ReadAllText(Path.Combine(root, "src/SteamInputAddonforClaw.UI/Views/ProfilePage.xaml.cs"));
        var xaml = File.ReadAllText(Path.Combine(root, "src/SteamInputAddonforClaw.UI/Views/ProfilePage.xaml"));
        var contentChanging = Method(code, "private void GameGrid_ContainerContentChanging(", "private void StartGameCardArtworkLoad(");
        var currentItemValidation = contentChanging.IndexOf("args.Item is not GameCardItem item", StringComparison.Ordinal);
        var rootContextAssignment = contentChanging.IndexOf("root.DataContext = item", StringComparison.Ordinal);
        var favoriteAssignment = contentChanging.IndexOf("favoriteButton.DataContext = item", StringComparison.Ordinal);
        var openAssignment = contentChanging.IndexOf("openButton.DataContext = item", StringComparison.Ordinal);
        var reuseFastPath = contentChanging.IndexOf("if (existing.Matches(item))", StringComparison.Ordinal);

        Assert.True(currentItemValidation >= 0 && rootContextAssignment > currentItemValidation);
        Assert.True(favoriteAssignment > rootContextAssignment && openAssignment > rootContextAssignment);
        Assert.True(reuseFastPath > favoriteAssignment && reuseFastPath > openAssignment);
        Assert.Contains("!ReferenceEquals(favoriteButton.DataContext, item)", contentChanging, StringComparison.Ordinal);
        Assert.Contains("!ReferenceEquals(openButton.DataContext, item)", contentChanging, StringComparison.Ordinal);

        var cardTemplate = xaml[xaml.IndexOf("x:Name=\"OpenGameButton\"", StringComparison.Ordinal)..xaml.IndexOf("</GridView>", StringComparison.Ordinal)];
        Assert.Contains("IsEnabled=\"{Binding FavoriteStateKnown}\"", cardTemplate, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.Name=\"{Binding FavoriteAutomationName}\"", cardTemplate, StringComparison.Ordinal);
        Assert.Contains("ToolTipService.ToolTip=\"{Binding FavoriteAutomationName}\"", cardTemplate, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding FavoriteGlyph}\"", cardTemplate, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.Name=\"{Binding Name}\"", cardTemplate, StringComparison.Ordinal);

        var cardItem = Method(code, "internal sealed class GameCardItem", "private sealed class RealizedGameCard(");
        Assert.Contains("public bool FavoriteStateKnown => Game.FavoriteStateKnown", cardItem, StringComparison.Ordinal);
        Assert.Contains("public string FavoriteGlyph => GetFavoriteGlyph(Game.Favorite, Game.FavoriteStateKnown)", cardItem, StringComparison.Ordinal);
        Assert.Contains("PropertyChanged?.Invoke(this, new(nameof(FavoriteGlyph)))", cardItem, StringComparison.Ordinal);
        Assert.Contains("PropertyChanged?.Invoke(this, new(nameof(FavoriteAutomationName)))", cardItem, StringComparison.Ordinal);
    }

    [Fact]
    public void Steam_cover_click_resolves_identity_before_opening_detail_and_capturing_profile()
    {
        var root = FindRepositoryRoot();
        var code = File.ReadAllText(Path.Combine(root, "src/SteamInputAddonforClaw.UI/Views/ProfilePage.xaml.cs"));
        var resolve = Method(code, "private FrontendProfileGameCatalogEntry? ResolveClickedGame(", "private async void GameCardButton_Click(");
        var click = Method(code, "private async void GameCardButton_Click(", "private void BackButton_Click(");
        var select = Method(code, "private async Task SelectGameAsync(", "private void BeginProfileLoad(");
        var invalidContext = click.IndexOf("if (game is null)", StringComparison.Ordinal);
        var warning = click.IndexOf("AppLog.Warn(\"Profiles.Catalog\", \"Steam cover click could not resolve its GridView item.\")", StringComparison.Ordinal);
        var invalidReturn = click.IndexOf("return;", warning, StringComparison.Ordinal);
        var profileUiMutation = click.IndexOf("CancelTdpDebounce()", StringComparison.Ordinal);

        var containerLookup = resolve.IndexOf("node is GridViewItem itemContainer", StringComparison.Ordinal);
        var gridBoundary = resolve.IndexOf("ReferenceEquals(node, GameGrid)", StringComparison.Ordinal);
        var itemLookup = resolve.IndexOf("GameGrid.ItemFromContainer(container)", StringComparison.Ordinal);
        Assert.True(containerLookup >= 0 && gridBoundary > containerLookup && itemLookup > gridBoundary);
        Assert.Contains("VisualTreeHelper.GetParent(node)", resolve, StringComparison.Ordinal);
        Assert.DoesNotContain("button.Tag", resolve, StringComparison.Ordinal);
        Assert.DoesNotContain("button.DataContext", resolve, StringComparison.Ordinal);
        Assert.Contains("if (sender is not Button button)", click, StringComparison.Ordinal);
        Assert.Contains("ResolveClickedGame(button)", click, StringComparison.Ordinal);
        Assert.True(invalidContext >= 0 && warning > invalidContext && invalidReturn > warning && profileUiMutation > invalidReturn);
        Assert.Contains("DetailPanel.Visibility = Visibility.Visible", click, StringComparison.Ordinal);
        Assert.Contains("await SelectGameAsync(game)", click, StringComparison.Ordinal);
        Assert.Contains("_selectedGame = game", select, StringComparison.Ordinal);
        Assert.Contains("await CaptureSelectedAsync(game.AppId", select, StringComparison.Ordinal);

        var favorite = Method(code, "private async void FavoriteButton_Click(", "private async Task SelectGameAsync(");
        Assert.Contains("ResolveClickedGame(button)", favorite, StringComparison.Ordinal);
        Assert.Contains("SetGameProfileFavoriteAsync(game.AppId, !game.Favorite, game.Name)", favorite, StringComparison.Ordinal);
        Assert.DoesNotContain("SelectGameAsync", favorite, StringComparison.Ordinal);
        Assert.DoesNotContain("button.Tag", favorite, StringComparison.Ordinal);
        Assert.DoesNotContain("button.DataContext", favorite, StringComparison.Ordinal);

        var xaml = File.ReadAllText(Path.Combine(root, "src/SteamInputAddonforClaw.UI/Views/ProfilePage.xaml"));
        var openButton = xaml.IndexOf("x:Name=\"OpenGameButton\"", StringComparison.Ordinal);
        var favoriteButton = xaml.IndexOf("x:Name=\"FavoriteButton\"", StringComparison.Ordinal);
        var gridEnd = xaml.IndexOf("</GridView>", favoriteButton, StringComparison.Ordinal);
        var cardTemplate = xaml[openButton..gridEnd];
        Assert.DoesNotContain("Tag=\"{Binding Game}\"", cardTemplate, StringComparison.Ordinal);
        Assert.Contains("IsEnabled=\"{Binding FavoriteStateKnown}\"", cardTemplate, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.Name=\"{Binding FavoriteAutomationName}\"", cardTemplate, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding FavoriteGlyph}\"", cardTemplate, StringComparison.Ordinal);
    }

    [Fact]
    public void Favorite_toggle_does_not_replace_the_visible_game_collection()
    {
        var root = FindRepositoryRoot();
        var code = File.ReadAllText(Path.Combine(root, "src/SteamInputAddonforClaw.UI/Views/ProfilePage.xaml.cs"));
        var applyFilter = Method(code, "private void ApplyCatalogFilter()", "private void ProfilePage_Loaded(");
        var favorite = Method(code, "private async void FavoriteButton_Click(", "private async Task SelectGameAsync(");

        Assert.Contains("_visibleGameCards = new ObservableCollection<GameCardItem>", applyFilter, StringComparison.Ordinal);
        Assert.Contains("GameGrid.ItemsSource = _visibleGameCards", applyFilter, StringComparison.Ordinal);
        Assert.DoesNotContain("ApplyCatalogFilter()", favorite, StringComparison.Ordinal);
        Assert.DoesNotContain("GameGrid.ItemsSource", favorite, StringComparison.Ordinal);
        Assert.Contains("currentGame.Favorite != game.Favorite", favorite, StringComparison.Ordinal);
        Assert.Contains("!currentGame.FavoriteStateKnown", favorite, StringComparison.Ordinal);

        var failedSave = favorite.IndexOf("if (!result.Succeeded)", StringComparison.Ordinal);
        var failedSaveReturn = favorite.IndexOf("return;", failedSave, StringComparison.Ordinal);
        var incrementalUpdate = favorite.IndexOf("ApplyFavoriteToVisibleCards", StringComparison.Ordinal);
        Assert.True(failedSave >= 0 && failedSaveReturn > failedSave && incrementalUpdate > failedSaveReturn);

        var update = Method(code, "internal static IReadOnlyList<FrontendProfileGameCatalogEntry> ApplyFavoriteToVisibleCards(", "private async void GameCardButton_Click(");
        Assert.DoesNotContain("ApplyCatalogFilter()", update, StringComparison.Ordinal);
        Assert.DoesNotContain("GameGrid.ItemsSource", update, StringComparison.Ordinal);
        Assert.DoesNotContain("ClearArtwork()", update, StringComparison.Ordinal);
    }

    [Fact]
    public void Favorite_toggle_notifies_only_its_card_and_moves_it_only_when_sort_position_changes()
    {
        var games = new[]
        {
            new FrontendProfileGameCatalogEntry(1u, "Beta", FrontendProfileGameSource.Steam),
            new FrontendProfileGameCatalogEntry(2u, "Alpha", FrontendProfileGameSource.Steam, Favorite: true),
            new FrontendProfileGameCatalogEntry(3u, "Zulu", FrontendProfileGameSource.Steam)
        };
        var cards = new ObservableCollection<ProfilePage.GameCardItem>(
            ProfilePage.FilterAndSort(games, null).Select(game => new ProfilePage.GameCardItem(game)));
        var installedItemsSource = cards;
        var originalCards = cards.ToDictionary(card => card.AppId);
        var changedCard = originalCards[3u];
        var propertyChanges = new List<string?>();
        var collectionChanges = new List<NotifyCollectionChangedEventArgs>();
        changedCard.PropertyChanged += (_, args) => propertyChanges.Add(args.PropertyName);
        cards.CollectionChanged += (_, args) => collectionChanges.Add(args);
        var oldAutomationName = changedCard.FavoriteAutomationName;

        var favorite = changedCard.Game with { Favorite = true };
        var updatedCatalog = ProfilePage.ApplyFavoriteToVisibleCards(games, cards, null, changedCard.Game, favorite);

        Assert.Same(installedItemsSource, cards);
        Assert.Equal(new uint[] { 2u, 3u, 1u }, cards.Select(card => card.AppId));
        Assert.Equal(new uint[] { 1u, 2u, 3u }, updatedCatalog.Select(game => game.AppId));
        Assert.Same(originalCards[1u], cards.Single(card => card.AppId == 1u));
        Assert.Same(originalCards[2u], cards.Single(card => card.AppId == 2u));
        Assert.Same(changedCard, cards.Single(card => card.AppId == 3u));
        Assert.Same(favorite, changedCard.Game);
        Assert.True(changedCard.Favorite);
        Assert.NotEqual(oldAutomationName, changedCard.FavoriteAutomationName);
        Assert.Equal(new[] { "Game", "Favorite", "FavoriteGlyph", "FavoriteAutomationName" }, propertyChanges);
        Assert.Collection(collectionChanges, change => Assert.Equal(NotifyCollectionChangedAction.Move, change.Action));

        propertyChanges.Clear();
        collectionChanges.Clear();
        var unfavorite = favorite with { Favorite = false };
        updatedCatalog = ProfilePage.ApplyFavoriteToVisibleCards(updatedCatalog, cards, null, favorite, unfavorite);

        Assert.Same(installedItemsSource, cards);
        Assert.Equal(new uint[] { 2u, 1u, 3u }, cards.Select(card => card.AppId));
        Assert.Same(changedCard, cards.Single(card => card.AppId == 3u));
        Assert.False(changedCard.Favorite);
        Assert.Equal(new[] { "Game", "Favorite", "FavoriteGlyph", "FavoriteAutomationName" }, propertyChanges);
        Assert.Collection(collectionChanges, change => Assert.Equal(NotifyCollectionChangedAction.Move, change.Action));
    }

    [Fact]
    public void Favorite_update_matches_catalog_entries_by_source_and_app_id()
    {
        var games = new[]
        {
            new FrontendProfileGameCatalogEntry(570u, "Same display name", FrontendProfileGameSource.Steam),
            new FrontendProfileGameCatalogEntry(570u, "Same display name", FrontendProfileGameSource.NonSteam)
        };
        var cards = new ObservableCollection<ProfilePage.GameCardItem>(
            ProfilePage.FilterAndSort(games, null).Select(game => new ProfilePage.GameCardItem(game)));
        var steamCard = cards.Single(card => card.Source == FrontendProfileGameSource.Steam);
        var shortcutCard = cards.Single(card => card.Source == FrontendProfileGameSource.NonSteam);
        var unaffectedNotifications = new List<string?>();
        steamCard.PropertyChanged += (_, args) => unaffectedNotifications.Add(args.PropertyName);

        var updatedShortcut = shortcutCard.Game with { Favorite = true };
        var updatedCatalog = ProfilePage.ApplyFavoriteToVisibleCards(
            games, cards, null, shortcutCard.Game, updatedShortcut);

        Assert.False(updatedCatalog.Single(game => game.Source == FrontendProfileGameSource.Steam).Favorite);
        Assert.True(updatedCatalog.Single(game => game.Source == FrontendProfileGameSource.NonSteam).Favorite);
        Assert.Same(steamCard, cards.Single(card => card.Source == FrontendProfileGameSource.Steam));
        Assert.Empty(unaffectedNotifications);
    }

    [Fact]
    public void Favorite_update_preserves_source_identity_and_does_not_resurrect_a_search_filtered_card()
    {
        var scanned = new[]
        {
            new FrontendProfileGameCatalogEntry(480u, "Native Steam entry", FrontendProfileGameSource.Steam),
            new FrontendProfileGameCatalogEntry(480u, "User shortcut", FrontendProfileGameSource.NonSteam),
            new FrontendProfileGameCatalogEntry(570u, "Search match", FrontendProfileGameSource.Steam),
            new FrontendProfileGameCatalogEntry(571u, "Outside current search", FrontendProfileGameSource.Steam)
        };
        var catalog = ProfilePage.FilterSteamCatalog(scanned);
        var visibleCards = new ObservableCollection<ProfilePage.GameCardItem>(
            ProfilePage.FilterAndSort(catalog, "User shortcut").Select(game => new ProfilePage.GameCardItem(game)));
        var installedItemsSource = visibleCards;
        var shortcut = visibleCards.Single();
        var collectionChanges = new List<NotifyCollectionChangedEventArgs>();
        visibleCards.CollectionChanged += (_, args) => collectionChanges.Add(args);

        var updatedShortcut = shortcut.Game with { Favorite = true };
        var afterShortcutUpdate = ProfilePage.ApplyFavoriteToVisibleCards(
            catalog, visibleCards, "User shortcut", shortcut.Game, updatedShortcut);

        Assert.Same(installedItemsSource, visibleCards);
        Assert.Same(shortcut, visibleCards.Single());
        Assert.Same(updatedShortcut, shortcut.Game);
        Assert.DoesNotContain(afterShortcutUpdate, game => game.Source == FrontendProfileGameSource.Steam && game.AppId == 480u);
        Assert.Contains(afterShortcutUpdate, game => game.Source == FrontendProfileGameSource.NonSteam && game.AppId == 480u && game.Favorite);
        Assert.Empty(collectionChanges);

        var searchVisible = new ObservableCollection<ProfilePage.GameCardItem>(
            ProfilePage.FilterAndSort(afterShortcutUpdate, "Search match").Select(game => new ProfilePage.GameCardItem(game)));
        var stillVisible = searchVisible.Single();
        var filteredOut = afterShortcutUpdate.Single(game => game.AppId == 571u);
        collectionChanges.Clear();
        searchVisible.CollectionChanged += (_, args) => collectionChanges.Add(args);

        var updatedFilteredOut = filteredOut with { Favorite = true };
        var afterFilteredUpdate = ProfilePage.ApplyFavoriteToVisibleCards(
            afterShortcutUpdate, searchVisible, "Search match", filteredOut, updatedFilteredOut);

        Assert.Same(stillVisible, searchVisible.Single());
        Assert.Single(searchVisible);
        Assert.Contains(afterFilteredUpdate, game => game.AppId == 571u && game.Favorite);
        Assert.Empty(collectionChanges);
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
