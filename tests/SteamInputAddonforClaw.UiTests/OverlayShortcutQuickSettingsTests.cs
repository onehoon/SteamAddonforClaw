using System.IO;
using System.Reflection;
using SteamInputAddonforClaw.Contracts.Frontend;
using SteamInputAddonforClaw.Overlay;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class OverlayShortcutQuickSettingsTests
{
    [Fact]
    public void Shortcut_page_hosts_shared_quick_settings_and_reconciles_tiles_by_identity()
    {
        var shortcuts = ReadSource("src", "SteamInputAddonforClaw.Overlay", "OverlayWindow.Shortcuts.cs");
        var quickSettings = ReadSource("src", "SteamInputAddonforClaw.Overlay", "OverlayWindow.QuickSettings.cs");
        var buildStart = shortcuts.IndexOf("private FrameworkElement BuildShortcutPage()", StringComparison.Ordinal);
        var buildEnd = shortcuts.IndexOf("internal void ApplyShortcutState(", buildStart, StringComparison.Ordinal);
        var build = shortcuts[buildStart..buildEnd];

        Assert.Contains("_shortcutQuickSettingsHost", build, StringComparison.Ordinal);
        Assert.Contains("root.Children.Add(_shortcutQuickSettingsHost)", build, StringComparison.Ordinal);
        Assert.Contains("root.Children.Add(_shortcutGrid)", build, StringComparison.Ordinal);
        Assert.DoesNotContain("Text = \"Shortcuts\"", build, StringComparison.Ordinal);
        Assert.DoesNotContain("ScrollViewer", build, StringComparison.Ordinal);
        Assert.Contains("if (rendered.Card.Visibility != visibility)", quickSettings, StringComparison.Ordinal);
        Assert.Contains("if (_shortcutQuickSettingsStatus!.Text != statusText)", quickSettings, StringComparison.Ordinal);
        Assert.Contains("if (card.BorderThickness != thickness)", quickSettings, StringComparison.Ordinal);
        Assert.Contains("desiredSource.HomeHost.Children.Remove(desiredSource.Content)", quickSettings, StringComparison.Ordinal);
        Assert.Contains("_shortcutQuickSettingsHost.Children.Add(desiredSource.Content)", quickSettings, StringComparison.Ordinal);
        Assert.DoesNotContain("QuickSettingsPageId.Shortcut", quickSettings, StringComparison.Ordinal);
        Assert.DoesNotContain("new OverlayQuickSettingsPageBinding(\n            QuickSettingsPageId.Shortcut", quickSettings, StringComparison.Ordinal);

        var tileGridStart = shortcuts.IndexOf("private void RenderShortcutSnapshot()", StringComparison.Ordinal);
        var tileGridEnd = shortcuts.IndexOf("private ShortcutTileControls CreateShortcutTile(", tileGridStart, StringComparison.Ordinal);
        var tileRenderer = shortcuts[tileGridStart..tileGridEnd];
        Assert.Contains("OverlayShortcutTileReconciliation.Between(_renderedShortcutTiles, tiles)", tileRenderer, StringComparison.Ordinal);
        Assert.Contains("private const int ShortcutVisualColumnCount = 3;", shortcuts, StringComparison.Ordinal);
        Assert.DoesNotContain("_shortcutGrid.Children.Clear()", tileRenderer, StringComparison.Ordinal);
    }

    [Fact]
    public void Shortcut_uses_only_original_typed_tdp_and_cpu_boost_sections()
    {
        var quickSettings = ReadSource("src", "SteamInputAddonforClaw.Overlay", "OverlayWindow.QuickSettings.cs");
        var presentation = ReadSource("src", "SteamInputAddonforClaw", "Frontend", "QuickSettingsPresentation.cs");

        Assert.Contains("OverlayShortcutQuickSettingsProjection.SelectPerformanceSections(page)", quickSettings, StringComparison.Ordinal);
        Assert.Contains("QuickSettingsSectionId.DeviceTdp", quickSettings, StringComparison.Ordinal);
        Assert.Contains("QuickSettingsSectionId.DeviceCpuBoost", quickSettings, StringComparison.Ordinal);
        Assert.Contains("QuickSettingsSectionId.ProfileTdp", quickSettings, StringComparison.Ordinal);
        Assert.Contains("QuickSettingsSectionId.ProfileCpuBoost", quickSettings, StringComparison.Ordinal);
        Assert.Contains("surface.Binding", quickSettings, StringComparison.Ordinal);
        Assert.Contains("QuickSettingsCommitGroupId.ProfileTdpConfiguration", presentation, StringComparison.Ordinal);
        Assert.Contains("QuickSettingsCommitGroupId.DeviceTdpConfiguration", presentation, StringComparison.Ordinal);
        Assert.DoesNotContain("QuickSettingsPageId.Shortcut", presentation, StringComparison.Ordinal);
        Assert.Contains("row.Visible", quickSettings, StringComparison.Ordinal);
    }

    [Fact]
    public void Shortcut_navigation_keeps_tile_actions_separate_from_shared_rows_and_allows_slider_repeat()
    {
        var navigation = ReadSource("src", "SteamInputAddonforClaw.Overlay", "OverlayWindow.Navigation.cs");

        Assert.Contains("_shortcutSettingsSelected", navigation, StringComparison.Ordinal);
        Assert.Contains("_rowSelection.MoveNext()", navigation, StringComparison.Ordinal);
        Assert.Contains("_rowSelection.MovePrevious()", navigation, StringComparison.Ordinal);
        Assert.Contains("_numericSliderRepeatTarget.TryAdjust(_tabState.SelectedTab, _rowSelection.SelectedCapabilities, delta)", navigation, StringComparison.Ordinal);
        Assert.Contains("if (_shortcutSettingsSelected)", navigation, StringComparison.Ordinal);
        Assert.Contains("RequestSelectedShortcutExecution();", navigation, StringComparison.Ordinal);
        Assert.Contains("ApplyShortcutSelectionVisual(bringIntoView: true)", navigation, StringComparison.Ordinal);
        Assert.Contains("FindInitialShortcutTileIndex(_shortcutSnapshot.Tiles)", navigation, StringComparison.Ordinal);
        Assert.Contains("if (tiles[index].Enabled)", navigation, StringComparison.Ordinal);

        var shell = ReadSource("src", "SteamInputAddonforClaw.Overlay", "OverlayWindow.Shell.cs");
        Assert.Contains("_rowSelection.SetRows(_shortcutSettingsSelected ? CapabilitiesFor(selected) : [])", shell, StringComparison.Ordinal);
    }

    [Fact]
    public void Delayed_settings_snapshot_selects_first_adjustable_row_unless_user_navigated_shortcut()
    {
        Assert.True(OverlayWindow.ShouldAutoSelectShortcutSettings(
            hasSelectableSettings: true,
            userNavigatedThisEntry: false));
        Assert.False(OverlayWindow.ShouldAutoSelectShortcutSettings(
            hasSelectableSettings: true,
            userNavigatedThisEntry: true));
        Assert.False(OverlayWindow.ShouldAutoSelectShortcutSettings(
            hasSelectableSettings: false,
            userNavigatedThisEntry: false));

        var cachedTileSelection = new OverlayShortcutSelection();
        cachedTileSelection.Configure(tileCount: 3, columnCount: 3, preferredIndex: 1);
        Assert.Equal(3, cachedTileSelection.TileCount);
        var selectedTileIndex = cachedTileSelection.SelectedIndex;

        var section = new QuickSettingsSection(QuickSettingsSectionId.ProfileTdp, "TDP Control",
        [
            new QuickSettingsRow(QuickSettingsRowId.ProfileTdpEnabled, "TDP Control", QuickSettingsControlKind.Toggle,
                false, false, null, null, QuickSettingsCommitPolicy.Immediate) { Visible = false },
            NumericRow(QuickSettingsRowId.ProfileTdpAcPl1, "AC · PL1", 20),
            NumericRow(QuickSettingsRowId.ProfileTdpAcPl2, "AC · PL2", 25)
        ]);
        var profilePage = new QuickSettingsPageSnapshot(QuickSettingsPageId.Profile,
            QuickSettingsProfileTarget.ForSteam(570), true, null, [section], []);
        var visibleRows = OverlayShortcutQuickSettingsProjection.SelectPerformanceSections(profilePage)
            .SelectMany(OverlayQuickSettingsSectionRendering.DescribeRows)
            .ToArray();
        Assert.Equal(QuickSettingsRowId.ProfileTdpAcPl1, visibleRows[0].Row.RowId);

        var adjustments = 0;
        var selection = new OverlayRowSelection();
        selection.SetRows(visibleRows.Select(row => new OverlayRowCapabilities(
            () => row.Row.Available && row.Row.Writable,
            Adjust: row.Row.RowId == QuickSettingsRowId.ProfileTdpAcPl1 ? _ => adjustments++ : null)).ToArray());

        Assert.Equal(0, selection.SelectedIndex);
        Assert.Equal(QuickSettingsRowId.ProfileTdpAcPl1, visibleRows[selection.SelectedIndex!.Value].Row.RowId);
        selection.AdjustSelected(+1);
        Assert.Equal(1, adjustments);

        var quickSettings = ReadSource("src", "SteamInputAddonforClaw.Overlay", "OverlayWindow.QuickSettings.cs");
        var refreshStart = quickSettings.IndexOf("private void RefreshShortcutQuickSettingsView(QuickSettingsRowId? preferredShortcutRowId)", StringComparison.Ordinal);
        var refreshEnd = quickSettings.IndexOf("private string? ResolveShortcutQuickSettingsStatus(", refreshStart, StringComparison.Ordinal);
        var refresh = quickSettings[refreshStart..refreshEnd];
        Assert.Contains("ShouldAutoSelectShortcutSettings(", refresh, StringComparison.Ordinal);
        Assert.Contains("_rowSelection.SetRows(CapabilitiesFor(AddonQuickSettingsTabId.Shortcut))", refresh, StringComparison.Ordinal);
        Assert.Contains("RefreshShortcutQuickSettingsView(preferredShortcutRowId);", quickSettings, StringComparison.Ordinal);

        Assert.False(OverlayWindow.ShouldAutoSelectShortcutSettings(
            hasSelectableSettings: true,
            userNavigatedThisEntry: true));
        Assert.Equal(selectedTileIndex, cachedTileSelection.SelectedIndex);
    }

    [Fact]
    public void Explicit_shortcut_navigation_and_tile_tap_are_preserved_when_settings_arrive_later()
    {
        var navigation = ReadSource("src", "SteamInputAddonforClaw.Overlay", "OverlayWindow.Navigation.cs");
        foreach (var method in new[]
                 {
                     "internal void NavigateUp()",
                     "internal void NavigateDown()",
                     "internal void AdjustSelectedRow(int delta)",
                     "internal void ActivateSelectedRow()"
                 })
            Assert.Contains("MarkShortcutUserNavigatedThisEntry();", ExtractMethod(navigation, method), StringComparison.Ordinal);

        var markInteraction = ExtractMethod(navigation, "private void MarkShortcutUserNavigatedThisEntry()");
        Assert.Contains("if (OnShortcutPage())", markInteraction, StringComparison.Ordinal);
        Assert.Contains("_shortcutUserNavigatedThisEntry = true;", markInteraction, StringComparison.Ordinal);
        Assert.Contains("MarkShortcutUserNavigatedThisEntry();",
            ExtractMethod(navigation, "private void SelectShortcutTile(Guid tileId, string source)"), StringComparison.Ordinal);

        var shell = ReadSource("src", "SteamInputAddonforClaw.Overlay", "OverlayWindow.Shell.cs");
        Assert.Contains("_shortcutUserNavigatedThisEntry = false;",
            ExtractMethod(shell, "private void ApplySelectedTabVisualState()"), StringComparison.Ordinal);
        var quickSettings = ReadSource("src", "SteamInputAddonforClaw.Overlay", "OverlayWindow.QuickSettings.cs");
        Assert.Contains("_shortcutUserNavigatedThisEntry = false;",
            ExtractMethod(quickSettings, "private void ResetShortcutQuickSettingsForShow()"), StringComparison.Ordinal);
    }

    [Fact]
    public void Shortcut_profile_artwork_reuses_the_existing_profile_host_and_loader()
    {
        var profile = ReadSource("src", "SteamInputAddonforClaw.Overlay", "OverlayWindow.Profile.cs");
        var xaml = ReadSource("src", "SteamInputAddonforClaw.Overlay", "OverlayWindow.xaml");

        Assert.Contains("AddonQuickSettingsTabId.Shortcut", profile, StringComparison.Ordinal);
        Assert.Contains("ResolveAndApplyProfileArtworkAsync", profile, StringComparison.Ordinal);
        Assert.Contains("ProfileArtworkHost", xaml, StringComparison.Ordinal);
        Assert.Equal(1, CountOccurrences(xaml, "x:Name=\"ProfileArtworkHost\""));
        Assert.DoesNotContain("ShortcutArtworkHost", profile + xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void Shortcut_source_uses_profile_for_recognized_targets_and_global_only_for_exact_no_game()
    {
        var steam = ProfilePage(QuickSettingsProfileTarget.ForSteam(570));
        var xbox = ProfilePage(QuickSettingsProfileTarget.ForXbox("xbox:game"));
        var unavailableSteam = QuickSettingsPageSnapshot.Unavailable(
            QuickSettingsPageId.Profile, QuickSettingsProfileTarget.ForSteam(570), "Profile storage is read-only.");
        var noGame = QuickSettingsPageSnapshot.Unavailable(
            QuickSettingsPageId.Profile, null, ReadNoGameProfileMessage());
        var unrelatedFailure = QuickSettingsPageSnapshot.Unavailable(
            QuickSettingsPageId.Profile, null, "Active profile capture failed.");

        Assert.Equal(QuickSettingsPageId.Profile, OverlayShortcutQuickSettingsProjection.ResolveSourcePage(steam, devicePageReceived: false));
        Assert.Equal(QuickSettingsPageId.Profile, OverlayShortcutQuickSettingsProjection.ResolveSourcePage(xbox, devicePageReceived: false));
        Assert.Equal(QuickSettingsPageId.Profile, OverlayShortcutQuickSettingsProjection.ResolveSourcePage(unavailableSteam, devicePageReceived: true));
        Assert.Null(OverlayShortcutQuickSettingsProjection.ResolveSourcePage(noGame, devicePageReceived: false));
        Assert.Equal(QuickSettingsPageId.Device, OverlayShortcutQuickSettingsProjection.ResolveSourcePage(noGame, devicePageReceived: true));
        Assert.Equal(QuickSettingsPageId.Profile, OverlayShortcutQuickSettingsProjection.ResolveSourcePage(unrelatedFailure, devicePageReceived: true));
        Assert.Null(OverlayShortcutQuickSettingsProjection.ResolveSourcePage(null, devicePageReceived: true));
        Assert.Equal("Global", OverlayShortcutQuickSettingsProjection.ResolveTitle(noGame));
        Assert.Equal("Example Game", OverlayShortcutQuickSettingsProjection.ResolveTitle(ProfilePage(
            QuickSettingsProfileTarget.ForSteam(570), gameName: "Example Game")));

        var quickSettings = ReadSource("src", "SteamInputAddonforClaw.Overlay", "OverlayWindow.QuickSettings.cs");
        var profileSource = ReadSource("src", "SteamInputAddonforClaw.Overlay", "OverlayWindow.Profile.cs");
        Assert.Contains("IsExactNoRunningGamePage(surface.Binding?.AuthoritativePage)", quickSettings, StringComparison.Ordinal);
        Assert.Contains("var preferredShortcutRowId = CaptureShortcutSelectionRowId();", profileSource, StringComparison.Ordinal);
        Assert.Contains("RefreshShortcutQuickSettingsView(preferredShortcutRowId);", profileSource, StringComparison.Ordinal);
    }

    [Fact]
    public void New_show_retains_quick_settings_controls_but_hides_them_until_fresh_pages_arrive()
    {
        var quickSettings = ReadSource("src", "SteamInputAddonforClaw.Overlay", "OverlayWindow.QuickSettings.cs");
        var profile = ReadSource("src", "SteamInputAddonforClaw.Overlay", "OverlayWindow.Profile.cs");
        var presentation = ReadSource("src", "SteamInputAddonforClaw.Overlay", "OverlayWindow.Presentation.cs");
        var reset = ExtractMethod(quickSettings, "private void ResetShortcutQuickSettingsForShow()");
        var prepare = ExtractMethod(profile, "private void PrepareActiveProfileFirstShow()");
        var applyDevice = ExtractMethod(quickSettings, "internal string ApplyQuickSettingsPage(QuickSettingsPageSnapshot page)");
        var applyProfile = ExtractMethod(profile, "internal string ApplyActiveProfilePage(QuickSettingsPageSnapshot page)");
        var refresh = ExtractMethod(quickSettings, "private void RefreshShortcutQuickSettingsView(QuickSettingsRowId? preferredShortcutRowId)");
        var show = ExtractMethod(presentation, "internal async Task ShowForPocAsync()");
        var hide = ExtractMethod(presentation, "internal Task HideForPocAsync()");

        Assert.Contains("_devicePageReceivedThisShow = false;", reset);
        Assert.Contains("_profilePageReceivedThisShow = false;", reset);
        Assert.Contains("deviceSurface.Content.Visibility = Visibility.Collapsed;", reset);
        Assert.Contains("deviceSurface.LoadingStatus", reset);
        Assert.DoesNotContain("ApplyAuthoritativePage", prepare);
        Assert.DoesNotContain("RenderQuickSettingsPage", prepare);
        Assert.Contains("surface.Content.Visibility = Visibility.Collapsed;", prepare);
        Assert.Contains("HideProfileArtworkPreservingCache();", prepare);

        var profileGate = refresh.IndexOf("var profilePage = _profilePageReceivedThisShow", StringComparison.Ordinal);
        var deviceGate = refresh.IndexOf("var devicePage = _devicePageReceivedThisShow", StringComparison.Ordinal);
        Assert.True(profileGate >= 0 && deviceGate > profileGate);
        Assert.Contains("if (!_profilePageReceivedThisShow || profilePage is null)",
            ExtractMethod(quickSettings, "private string? ResolveShortcutQuickSettingsStatus("));
        Assert.Contains("ApplyQuickSettingsSurfacePresentation(surface, page.Available, page.Message);", applyDevice);
        Assert.Contains("surface.Content.Visibility = HasRenderableActiveProfile(page) ? Visibility.Visible : Visibility.Collapsed;", applyProfile);
        Assert.Contains("ValueOnly", ExtractMethod(quickSettings, "private void RenderQuickSettingsPage("));
        Assert.Contains("SectionReconcile", ExtractMethod(quickSettings, "private void RenderQuickSettingsPage("));
        Assert.Contains("FullRebuild", ExtractMethod(quickSettings, "private void RenderQuickSettingsPage("));

        Assert.Contains("_showStartedTimestamp = Stopwatch.GetTimestamp();", show);
        Assert.Contains("OverlayShortcutContentReady", refresh);
        Assert.Contains("Stopwatch.GetElapsedTime(_showStartedTimestamp)", refresh);
        Assert.Contains("HideProfileArtworkPreservingCache();", hide);
        Assert.DoesNotContain("ClearProfileArtwork();", hide);
    }

    [Theory]
    [InlineData(AddonQuickSettingsTabId.Device)]
    [InlineData(AddonQuickSettingsTabId.Profile)]
    public void Retained_rows_are_not_actionable_until_the_current_show_receives_the_page(
        AddonQuickSettingsTabId tab)
    {
        var mutationRequests = 0;
        var retainedRows = new OverlayRowCapabilities[]
        {
            new(() => true, () => mutationRequests++, _ => mutationRequests++, AllowsDpadHoldRepeat: true),
        };
        var selection = new OverlayRowSelection();
        var repeatTarget = new OverlayNumericSliderRepeatTarget();
        repeatTarget.Arm(tab, retainedRows[0], delta: 1);

        selection.SetRows(OverlayShortcutQuickSettingsProjection.FilterInputCapabilitiesForCurrentShow(
            tab, retainedRows, devicePageReceivedThisShow: false, profilePageReceivedThisShow: false));
        Assert.Null(selection.SelectedCapabilities);
        Assert.False(repeatTarget.TryAdjust(tab, selection.SelectedCapabilities, delta: 1));
        selection.ActivateSelected();
        selection.AdjustSelected(1);
        Assert.Equal(0, mutationRequests);

        var deviceReceived = tab == AddonQuickSettingsTabId.Device;
        var profileReceived = tab == AddonQuickSettingsTabId.Profile;
        selection.SetRows(OverlayShortcutQuickSettingsProjection.FilterInputCapabilitiesForCurrentShow(
            tab, retainedRows, deviceReceived, profileReceived));
        Assert.NotNull(selection.SelectedCapabilities);
        selection.ActivateSelected();
        selection.AdjustSelected(1);
        repeatTarget.Arm(tab, selection.SelectedCapabilities, delta: 1);
        Assert.True(repeatTarget.TryAdjust(tab, selection.SelectedCapabilities, delta: 1));
        Assert.Equal(3, mutationRequests);
    }

    [Fact]
    public void Active_quick_settings_capability_selection_uses_current_show_receipt_gates()
    {
        var navigation = ReadSource("src", "SteamInputAddonforClaw.Overlay", "OverlayWindow.Navigation.cs");
        var quickSettings = ReadSource("src", "SteamInputAddonforClaw.Overlay", "OverlayWindow.QuickSettings.cs");
        var capabilitiesFor = ExtractMethod(navigation, "private IReadOnlyList<OverlayRowCapabilities> CapabilitiesFor(");

        Assert.Contains("FilterInputCapabilitiesForCurrentShow", capabilitiesFor, StringComparison.Ordinal);
        Assert.Contains("_devicePageReceivedThisShow", capabilitiesFor, StringComparison.Ordinal);
        Assert.Contains("_profilePageReceivedThisShow", capabilitiesFor, StringComparison.Ordinal);
        Assert.Contains("if ((tab == AddonQuickSettingsTabId.Device && !devicePageReceivedThisShow)",
            quickSettings, StringComparison.Ordinal);
        Assert.Contains("tab == AddonQuickSettingsTabId.Profile && !profilePageReceivedThisShow",
            quickSettings, StringComparison.Ordinal);
    }

    [Fact]
    public void Shortcut_section_projection_keeps_original_hidden_rows_and_group_constraints()
    {
        var hiddenEnabled = new QuickSettingsRow(QuickSettingsRowId.ProfileTdpEnabled, "TDP", QuickSettingsControlKind.Toggle,
            Available: false, Writable: false, Value: null, SliderSpec: null, QuickSettingsCommitPolicy.Immediate) { Visible = false };
        var pl1 = NumericRow(QuickSettingsRowId.ProfileTdpAcPl1, "AC · PL1", 20);
        var hiddenPl2 = NumericRow(QuickSettingsRowId.ProfileTdpAcPl2, "AC · PL2", 25) with { Visible = false };
        var tdp = new QuickSettingsSection(QuickSettingsSectionId.ProfileTdp, "TDP Control", [hiddenEnabled, pl1, hiddenPl2]);
        var cpuBoost = new QuickSettingsSection(QuickSettingsSectionId.ProfileCpuBoost, "CPU Boost", []);
        var powerMode = new QuickSettingsSection(QuickSettingsSectionId.ProfilePowerMode, "Windows Power Mode", []);
        var page = new QuickSettingsPageSnapshot(QuickSettingsPageId.Profile, QuickSettingsProfileTarget.ForSteam(570), true,
            null, [tdp, cpuBoost, powerMode],
            [new QuickSettingsLinkedSliderConstraint(QuickSettingsRowId.ProfileTdpAcPl1, QuickSettingsRowId.ProfileTdpAcPl2, 5) { ExactGap = true }]);

        var selected = OverlayShortcutQuickSettingsProjection.SelectPerformanceSections(page);

        Assert.Collection(selected, section => Assert.Same(tdp, section), section => Assert.Same(cpuBoost, section));
        Assert.Same(hiddenEnabled, selected[0].Rows[0]);
        Assert.Same(hiddenPl2, selected[0].Rows[2]);
        Assert.False(selected[0].Rows[0].Visible);
        Assert.False(selected[0].Rows[2].Visible);
        Assert.Contains(QuickSettingsRowId.ProfileTdpAcPl2, selected[0].Rows.Select(row => row.RowId));
        Assert.True(page.LinkedSliderConstraints[0].ExactGap);
        Assert.Empty(OverlayShortcutQuickSettingsProjection.SelectPerformanceSections(page with { Available = false }));
    }

    [Theory]
    [InlineData(QuickSettingsRowId.ProfileTdpAcPl1, QuickSettingsRowId.ProfileTdpDcPl1, true)]
    [InlineData(QuickSettingsRowId.ProfileTdpDcPl1, QuickSettingsRowId.ProfileTdpAcPl1, false)]
    public void First_rendered_tdp_row_is_the_visible_pl1_for_the_current_power_source(
        QuickSettingsRowId firstVisible,
        QuickSettingsRowId hiddenPowerSourceRow,
        bool acVisible)
    {
        var tdp = new QuickSettingsSection(QuickSettingsSectionId.ProfileTdp, "TDP Control",
        [
            new QuickSettingsRow(QuickSettingsRowId.ProfileTdpEnabled, "TDP Control", QuickSettingsControlKind.Toggle,
                true, false, QuickSettingsValue.Boolean(true), null, QuickSettingsCommitPolicy.Immediate) { Visible = false },
            NumericRow(QuickSettingsRowId.ProfileTdpAcPl1, "AC · PL1", 20, acVisible),
            NumericRow(QuickSettingsRowId.ProfileTdpDcPl1, "DC · PL1", 15, !acVisible),
        ]);
        var cpuBoost = new QuickSettingsSection(QuickSettingsSectionId.ProfileCpuBoost, "CPU Boost", []);
        var page = new QuickSettingsPageSnapshot(QuickSettingsPageId.Profile, QuickSettingsProfileTarget.ForSteam(570), true,
            null, [tdp, cpuBoost], []);

        var projection = OverlayShortcutQuickSettingsProjection.SelectPerformanceSections(page);
        var firstRenderedRow = OverlayQuickSettingsSectionRendering.DescribeRows(projection[0])[0].Row;

        Assert.Equal(firstVisible, firstRenderedRow.RowId);
        Assert.False(projection[0].Rows.Single(row => row.RowId == hiddenPowerSourceRow).Visible);
    }

    private static QuickSettingsPageSnapshot ProfilePage(QuickSettingsProfileTarget target, string gameName = "") =>
        new(QuickSettingsPageId.Profile, target, true, null,
            [new QuickSettingsSection(QuickSettingsSectionId.ProfileGeneral, gameName, [])], []);

    private static string ReadNoGameProfileMessage() =>
        (string?)typeof(OverlayWindow).GetField(
            "NoActiveGameMessage",
            BindingFlags.Static | BindingFlags.NonPublic)?.GetRawConstantValue()
        ?? throw new InvalidOperationException("The no-game profile message is unavailable.");

    private static QuickSettingsRow NumericRow(QuickSettingsRowId rowId, string label, int value, bool visible = true) =>
        new(rowId, label, QuickSettingsControlKind.Slider, true, true, QuickSettingsValue.Integer(value),
            new QuickSettingsSliderSpec(QuickSettingsSliderKind.Numeric, 5, 50, 1, " W"),
            new QuickSettingsCommitPolicy(QuickSettingsCommitMode.TrailingDebounce, 300),
            QuickSettingsCommitGroupId.ProfileTdpConfiguration) { Visible = visible };

    private static int CountOccurrences(string source, string value)
    {
        var count = 0;
        for (var index = 0; (index = source.IndexOf(value, index, StringComparison.Ordinal)) >= 0; index += value.Length)
            count++;
        return count;
    }

    private static string ExtractMethod(string source, string signature)
    {
        var start = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Could not find method {signature}.");
        var openBrace = source.IndexOf('{', start);
        Assert.True(openBrace >= 0, $"Could not find the body for {signature}.");

        var depth = 0;
        for (var index = openBrace; index < source.Length; index++)
        {
            if (source[index] == '{') depth++;
            else if (source[index] == '}' && --depth == 0)
                return source[start..(index + 1)];
        }

        throw new InvalidOperationException($"Could not find the end of method {signature}.");
    }

    private static string ReadSource(params string[] parts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "README.md")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        return File.ReadAllText(Path.Combine([directory!.FullName, .. parts]));
    }
}
