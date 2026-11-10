using SteamInputAddonforClaw.Views;
using SteamInputAddonforClaw.Contracts.Frontend;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
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

        Assert.Contains("TdpExpander.Visibility = _featureSupport.Tdp ? Visibility.Visible : Visibility.Collapsed", initialize);
        Assert.Contains("IntelFpsExpander.Visibility = _featureSupport.IntelFpsLimit ? Visibility.Visible : Visibility.Collapsed", initialize);
        Assert.Contains("GpuMinimumClockExpander.Visibility = _featureSupport.IntelGpuMinimumClock ? Visibility.Visible : Visibility.Collapsed", initialize);
        Assert.Contains("TdpEnabledToggle.IsEnabled = snapshot.Exists && snapshot.Enabled && snapshot.PersistenceWritable && _featureSupport.Tdp && snapshot.Limits is not null", render);
        Assert.Contains("snapshot.FpsLimit?.Available == true", render);
        Assert.Contains("_featureSupport.Tdp && snapshot.Exists && snapshot.Enabled && snapshot.Limits is null", status);
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
        var failure = refresh.IndexOf("catch (Exception exception)", StringComparison.Ordinal);
        Assert.True(scan >= 0 && successfulCache > scan && loadedFlag > successfulCache);
        Assert.True(favoriteWarning > loadedFlag && applyFilter > favoriteWarning);
        Assert.True(failure > loadedFlag);
        Assert.DoesNotContain("_catalogLoaded = false", refresh, StringComparison.Ordinal);
        Assert.Contains("await RefreshGamesAsync(explicitRefresh: true)", manualRefresh, StringComparison.Ordinal);
        Assert.DoesNotContain("if (_catalogLoaded) return", refresh, StringComparison.Ordinal);
    }

    [Fact]
    public void Steam_catalog_uses_a_native_virtualized_gridview_with_left_aligned_cards_and_independent_favorite_target()
    {
        var root = FindRepositoryRoot();
        var xaml = File.ReadAllText(Path.Combine(root, "src/SteamInputAddonforClaw.UI/Views/ProfilePage.xaml"));
        var catalog = xaml[..xaml.IndexOf("<Grid x:Name=\"DetailPanel\"", StringComparison.Ordinal)];

        Assert.Contains("<GridView x:Name=\"GameGrid\"", catalog, StringComparison.Ordinal);
        var gridViewStart = catalog.IndexOf("<GridView x:Name=\"GameGrid\"", StringComparison.Ordinal);
        var gridViewTagEnd = catalog.IndexOf('>', gridViewStart);
        var gridViewTag = catalog[gridViewStart..(gridViewTagEnd + 1)];
        Assert.Contains("HorizontalAlignment=\"Stretch\"", gridViewTag, StringComparison.Ordinal);
        Assert.DoesNotContain("HorizontalAlignment=\"Center\"", gridViewTag, StringComparison.Ordinal);
        Assert.Contains("SelectionMode=\"None\"", catalog, StringComparison.Ordinal);
        Assert.Contains("IsItemClickEnabled=\"False\"", catalog, StringComparison.Ordinal);
        Assert.Contains("CanDragItems=\"False\"", catalog, StringComparison.Ordinal);
        Assert.Contains("CanReorderItems=\"False\"", catalog, StringComparison.Ordinal);
        Assert.Contains("ItemContainerTransitions=\"{x:Null}\"", catalog, StringComparison.Ordinal);
        Assert.Contains("UseLayoutRounding=\"False\"", catalog, StringComparison.Ordinal);
        Assert.Contains("<GridView.ItemsPanel>", catalog, StringComparison.Ordinal);
        Assert.Contains("<ItemsWrapGrid Orientation=\"Horizontal\" ItemWidth=\"183\" ItemHeight=\"270.5\" Margin=\"0\" Loaded=\"CoverItemsPanel_Loaded\"/>", catalog, StringComparison.Ordinal);
        Assert.DoesNotContain("MaximumRowsOrColumns", catalog, StringComparison.Ordinal);
        Assert.DoesNotContain("<ScrollViewer", catalog, StringComparison.Ordinal);
        Assert.DoesNotContain("ItemsRepeater", catalog, StringComparison.Ordinal);
        Assert.Contains("ScrollViewer.VerticalScrollMode=\"Enabled\"", catalog, StringComparison.Ordinal);
        Assert.Contains("ScrollViewer.VerticalScrollBarVisibility=\"Hidden\"", catalog, StringComparison.Ordinal);
        Assert.Contains("ScrollViewer.HorizontalScrollMode=\"Disabled\"", catalog, StringComparison.Ordinal);
        Assert.Contains("ContainerContentChanging=\"GameGrid_ContainerContentChanging\"", catalog, StringComparison.Ordinal);
        Assert.Contains("Margin=\"0,0,8,8\"", catalog, StringComparison.Ordinal);
        Assert.Contains("Width=\"175\" Height=\"262.5\"", catalog, StringComparison.Ordinal);
        Assert.Equal(3, System.Text.RegularExpressions.Regex.Matches(catalog, "CornerRadius=\"0\"").Count);
        Assert.DoesNotContain("Shadow", catalog, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Gradient", catalog, StringComparison.OrdinalIgnoreCase);

        var code = File.ReadAllText(Path.Combine(root, "src/SteamInputAddonforClaw.UI/Views/ProfilePage.xaml.cs"));
        Assert.Contains("Stretch = Stretch.UniformToFill", code, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"FallbackCard\"", catalog, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"OpenGameButton\"", catalog, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"FavoriteButton\"", catalog, StringComparison.Ordinal);
        Assert.Contains("Click=\"GameCardButton_Click\"", catalog, StringComparison.Ordinal);
        Assert.Contains("Click=\"FavoriteButton_Click\"", catalog, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.Name=\"{Binding Name}\"", catalog, StringComparison.Ordinal);
        Assert.DoesNotContain("HomeCoverGridPanel", code, StringComparison.Ordinal);
    }

    [Fact]
    public void Steam_gridview_item_style_removes_container_size_and_padding_interference()
    {
        var root = FindRepositoryRoot();
        var xaml = File.ReadAllText(Path.Combine(root, "src/SteamInputAddonforClaw.UI/Views/ProfilePage.xaml"));
        var styleStart = xaml.IndexOf("<Style x:Key=\"SteamCoverGridViewItemStyle\"", StringComparison.Ordinal);
        var styleEnd = xaml.IndexOf("</Style>", styleStart, StringComparison.Ordinal);
        Assert.True(styleStart >= 0 && styleEnd > styleStart);
        var style = xaml[styleStart..(styleEnd + "</Style>".Length)];

        Assert.Contains("TargetType=\"GridViewItem\"", style, StringComparison.Ordinal);
        Assert.Contains("<Setter Property=\"Margin\" Value=\"0\"/>", style, StringComparison.Ordinal);
        Assert.Contains("<Setter Property=\"Padding\" Value=\"0\"/>", style, StringComparison.Ordinal);
        Assert.Contains("<Setter Property=\"MinWidth\" Value=\"0\"/>", style, StringComparison.Ordinal);
        Assert.Contains("<Setter Property=\"MinHeight\" Value=\"0\"/>", style, StringComparison.Ordinal);
        Assert.Contains("<Setter Property=\"HorizontalContentAlignment\" Value=\"Stretch\"/>", style, StringComparison.Ordinal);
        Assert.Contains("<Setter Property=\"VerticalContentAlignment\" Value=\"Stretch\"/>", style, StringComparison.Ordinal);
        Assert.Contains("<Setter Property=\"BorderThickness\" Value=\"0\"/>", style, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(1130, 6, 170, 18)]
    [InlineData(1200, 7, 163, 8)]
    [InlineData(980, 6, 155, 8)]
    [InlineData(380, 2, 170, 20)]
    [InlineData(300, 2, 142, 8)]
    [InlineData(1000, 6, 158, 8)]
    [InlineData(1210, 7, 164, 8)]
    [InlineData(709, 4, 169, 8)]
    [InlineData(240, 1, 180, 24)]
    public void Steam_cover_grid_layout_uses_preferred_slot_width_and_explicit_trailing_gaps(
        double availableWidthDip,
        int expectedColumns,
        double expectedCoverWidthDip,
        double expectedGapDip)
    {
        Assert.True(SteamCoverGridLayout.TryCalculate(availableWidthDip, out var layout));

        Assert.Equal(expectedColumns, layout.Columns);
        Assert.Equal(expectedCoverWidthDip, layout.CoverWidthDip, 2);
        Assert.Equal(layout.CoverWidthDip * 1.5, layout.CoverHeightDip, 6);
        Assert.Equal(expectedGapDip, layout.ColumnGapDip, 2);
        Assert.Equal(layout.ColumnGapDip, layout.RowGapDip, 2);
        Assert.Equal(layout.CoverWidthDip + layout.ColumnGapDip, layout.SlotWidthDip, 6);
        Assert.Equal(layout.CoverHeightDip + layout.RowGapDip, layout.SlotHeightDip, 6);
        Assert.InRange(layout.CoverWidthDip, SteamCoverGridLayout.MinimumCoverWidthDip, SteamCoverGridLayout.MaximumCoverWidthDip);
        Assert.InRange(layout.ColumnGapDip, SteamCoverGridLayout.MinimumGapDip, SteamCoverGridLayout.MaximumGapDip);
        Assert.True(layout.Columns * layout.SlotWidthDip <= availableWidthDip + 0.01);
        Assert.True((layout.Columns + 1) * layout.SlotWidthDip > availableWidthDip);
    }

    [Theory]
    [InlineData(1.0)]
    [InlineData(1.25)]
    [InlineData(1.5)]
    [InlineData(2.0)]
    public void Steam_cover_grid_slots_fit_physical_pixels_at_supported_DPI_scales(double rasterizationScale)
    {
        foreach (var widthDip in new[] { 240.0, 300.0, 380.0, 709.0, 980.0, 1000.0, 1130.0, 1200.0, 1210.0 })
        foreach (var pixelOffset in new[] { -2, -1, 0, 1, 2 })
        {
            var availableWidthPixels = (long)Math.Floor(widthDip * rasterizationScale + 1e-9) + pixelOffset;
            if (availableWidthPixels <= 0)
                continue;

            var measuredWidthDip = availableWidthPixels / rasterizationScale;
            Assert.True(SteamCoverGridLayout.TryCalculate(measuredWidthDip, rasterizationScale, out var layout));

            var slotWidthPixels = (long)Math.Round(layout.SlotWidthDip * rasterizationScale);
            var coverWidthPixels = (long)Math.Round(layout.CoverWidthDip * rasterizationScale);
            var gapPixels = (long)Math.Round(layout.ColumnGapDip * rasterizationScale);
            var minimumSlotPixels = (long)Math.Ceiling(
                (SteamCoverGridLayout.MinimumCoverWidthDip + SteamCoverGridLayout.MinimumGapDip) * rasterizationScale - 1e-9);

            Assert.Equal(slotWidthPixels, coverWidthPixels + gapPixels);
            Assert.True(layout.Columns * slotWidthPixels <= availableWidthPixels);
            Assert.True((layout.Columns + 1L) * slotWidthPixels > availableWidthPixels);
            Assert.InRange(layout.CoverWidthDip, 1, SteamCoverGridLayout.MaximumCoverWidthDip);
            Assert.Equal(layout.CoverWidthDip * 1.5, layout.CoverHeightDip, 6);
            Assert.Equal(layout.ColumnGapDip, layout.RowGapDip, 6);
            if (availableWidthPixels >= minimumSlotPixels)
            {
                Assert.InRange(layout.CoverWidthDip, SteamCoverGridLayout.MinimumCoverWidthDip, SteamCoverGridLayout.MaximumCoverWidthDip);
                Assert.InRange(layout.ColumnGapDip, SteamCoverGridLayout.MinimumGapDip, SteamCoverGridLayout.MaximumGapDip);
            }
            else
            {
                Assert.Equal(1, layout.Columns);
            }
        }
    }

    [Theory]
    [InlineData(1.0)]
    [InlineData(1.25)]
    [InlineData(1.5)]
    [InlineData(2.0)]
    public void Steam_cover_grid_column_transitions_follow_the_preferred_physical_slot(double rasterizationScale)
    {
        foreach (var transitionColumns in new[] { 1, 2, 3, 4, 5, 6 })
        foreach (var offsetPixels in new[] { -2, -1, 0, 1, 2 })
        {
            var thresholdPixels = (long)Math.Floor(
                (transitionColumns + 0.5) * (SteamCoverGridLayout.PreferredCoverWidthDip + SteamCoverGridLayout.MinimumGapDip)
                    * rasterizationScale);
            var availableWidthPixels = thresholdPixels + offsetPixels;
            if (availableWidthPixels <= 0)
                continue;

            var availableWidthDip = availableWidthPixels / rasterizationScale;
            Assert.True(SteamCoverGridLayout.TryCalculate(availableWidthDip, rasterizationScale, out var layout));
            var expectedColumns = (int)Math.Round(
                availableWidthPixels / ((SteamCoverGridLayout.PreferredCoverWidthDip + SteamCoverGridLayout.MinimumGapDip) * rasterizationScale),
                MidpointRounding.AwayFromZero);
            var maxFeasibleColumns = availableWidthPixels / (long)Math.Ceiling(
                (SteamCoverGridLayout.MinimumCoverWidthDip + SteamCoverGridLayout.MinimumGapDip) * rasterizationScale - 1e-9);
            expectedColumns = Math.Clamp(expectedColumns, 1, (int)Math.Max(1, maxFeasibleColumns));

            Assert.Equal(expectedColumns, layout.Columns);
            Assert.True(layout.Columns * layout.SlotWidthDip <= availableWidthDip + 1e-6);
        }
    }

    [Theory]
    [InlineData(0, false, 0)]
    [InlineData(-1, false, 0)]
    [InlineData(double.NaN, false, 0)]
    [InlineData(double.PositiveInfinity, false, 0)]
    [InlineData(double.NegativeInfinity, false, 0)]
    [InlineData(double.MaxValue, false, 0)]
    [InlineData(0.5, false, 0)]
    [InlineData(1, true, 1)]
    [InlineData(50, true, 1)]
    [InlineData(118, true, 1)]
    [InlineData(125, true, 1)]
    [InlineData(126, true, 1)]
    public void Steam_cover_grid_handles_narrow_viewports_and_rejects_unmeasured_widths(
        double availableWidthDip,
        bool expectedSuccess,
        int expectedColumns)
    {
        var result = SteamCoverGridLayout.TryCalculate(availableWidthDip, out var layout);
        Assert.Equal(expectedSuccess, result);
        if (!expectedSuccess)
            return;

        Assert.Equal(expectedColumns, layout.Columns);
        Assert.True(layout.SlotWidthDip <= availableWidthDip + 0.01);
        Assert.True(layout.CoverWidthDip > 0);
        if (layout.CoverWidthDip < SteamCoverGridLayout.MinimumCoverWidthDip)
            Assert.Equal(1, layout.Columns);
    }

    [Theory]
    [InlineData(0, 900, true)]
    [InlineData(double.NaN, 900, true)]
    [InlineData(900, 900.49, false)]
    [InlineData(900, 900.5, true)]
    [InlineData(900, 901, true)]
    [InlineData(900, 0, false)]
    [InlineData(900, double.PositiveInfinity, false)]
    public void Steam_cover_grid_relayout_policy_ignores_height_only_and_subpixel_width_changes(
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
    public void Steam_favorite_uses_a_chromeless_star_button_with_accessible_keyboard_focus()
    {
        var root = FindRepositoryRoot();
        var xaml = File.ReadAllText(Path.Combine(root, "src/SteamInputAddonforClaw.UI/Views/ProfilePage.xaml"));
        var styleStart = xaml.IndexOf("<Style x:Key=\"SteamCoverFavoriteStarButtonStyle\"", StringComparison.Ordinal);
        var styleEnd = xaml.IndexOf("</Style>", styleStart, StringComparison.Ordinal);
        Assert.True(styleStart >= 0 && styleEnd > styleStart);
        var style = xaml[styleStart..(styleEnd + "</Style>".Length)];
        var catalog = xaml[..xaml.IndexOf("<Grid x:Name=\"DetailPanel\"", StringComparison.Ordinal)];

        Assert.Contains("Width=\"32\" Height=\"32\"", catalog, StringComparison.Ordinal);
        Assert.Contains("Style=\"{StaticResource SteamCoverFavoriteStarButtonStyle}\"", catalog, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding FavoriteGlyph}\" FontSize=\"24\"", catalog, StringComparison.Ordinal);
        Assert.Contains("Property=\"Foreground\" Value=\"{ThemeResource TextFillColorPrimaryBrush}\"", style, StringComparison.Ordinal);
        Assert.Contains("<VisualState x:Name=\"PointerOver\"/>", style, StringComparison.Ordinal);
        Assert.Contains("<VisualState x:Name=\"Pressed\"/>", style, StringComparison.Ordinal);
        Assert.Contains("UseSystemFocusVisuals\" Value=\"False\"", style, StringComparison.Ordinal);
        Assert.Contains(
            "<VisualState x:Name=\"Focused\"><VisualState.Setters><Setter Target=\"FocusRing.Visibility\" Value=\"Visible\"/></VisualState.Setters></VisualState>",
            style,
            StringComparison.Ordinal);
        Assert.Contains("<VisualState x:Name=\"PointerFocused\"/>", style, StringComparison.Ordinal);
        Assert.Contains("IsEnabled=\"{Binding FavoriteStateKnown}\"", catalog, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.Name=\"{Binding FavoriteAutomationName}\"", catalog, StringComparison.Ordinal);
        Assert.Contains("ToolTipService.ToolTip=\"{Binding FavoriteAutomationName}\"", catalog, StringComparison.Ordinal);
        Assert.Contains("Click=\"FavoriteButton_Click\"", catalog, StringComparison.Ordinal);
        Assert.Contains("Click=\"GameCardButton_Click\"", catalog, StringComparison.Ordinal);
    }

    [Fact]
    public void Steam_portrait_artwork_resolver_keeps_legacy_physical_size_helper()
    {
        Assert.Equal((175, 264), SteamPortraitArtworkResolver.GetCoverSizeDip(1.0));

        var at150Percent = SteamPortraitArtworkResolver.GetCoverSizeDip(1.5);
        Assert.Equal(175.0 / 1.5, at150Percent.Width, 5);
        Assert.Equal(264.0 / 1.5, at150Percent.Height, 5);
        Assert.Equal(175.0, at150Percent.Width * 1.5, 5);
        Assert.Equal(264.0, at150Percent.Height * 1.5, 5);

        Assert.Equal((87.5, 132), SteamPortraitArtworkResolver.GetCoverSizeDip(2.0));
        Assert.Equal((175, 264), SteamPortraitArtworkResolver.GetCoverSizeDip(0));
        Assert.Equal((175, 264), SteamPortraitArtworkResolver.GetCoverSizeDip(double.NaN));
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
        Assert.Contains("DecodePixelWidth = 352", code, StringComparison.Ordinal);
        Assert.Contains("cancellationToken.ThrowIfCancellationRequested()", code, StringComparison.Ordinal);
        Assert.Contains("card.Root.DataContext is GameCardItem item", code, StringComparison.Ordinal);
        Assert.Contains("card.Matches(item)", code, StringComparison.Ordinal);
        Assert.Contains("recycledCard.ClearArtwork()", code, StringComparison.Ordinal);
        Assert.Contains("_realizedGameCards.TryGetValue(card.Container, out var current)", code, StringComparison.Ordinal);

        var handler = Method(code, "private void GameGrid_ContainerContentChanging(", "private void StartGameCardArtworkLoad(");
        var recycleBranch = handler.IndexOf("if (args.InRecycleQueue)", StringComparison.Ordinal);
        var phaseZero = handler.IndexOf("if (args.Phase == 0)", StringComparison.Ordinal);
        var templateRoot = handler.IndexOf("container.ContentTemplateRoot is not Border root", StringComparison.Ordinal);
        Assert.True(recycleBranch >= 0 && phaseZero > recycleBranch && templateRoot > phaseZero);
        Assert.Contains("_realizedGameCards.Remove(container, out var recycledCard)", handler, StringComparison.Ordinal);
        Assert.Contains("recycledCard.CancelLoad()", handler, StringComparison.Ordinal);
        Assert.Contains("recycledCard.ClearArtwork()", handler, StringComparison.Ordinal);
        Assert.Contains("args.RegisterUpdateCallback(GameGrid_ContainerContentChanging)", handler, StringComparison.Ordinal);
        Assert.Contains("args.Handled = true", handler, StringComparison.Ordinal);
        Assert.Contains("args.Item is not GameCardItem item", handler, StringComparison.Ordinal);
        Assert.Contains("ReferenceEquals(item, dataContextItem)", handler, StringComparison.Ordinal);
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
        Assert.Equal("★", changedCard.FavoriteGlyph);
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
        Assert.Equal("☆", changedCard.FavoriteGlyph);
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
