using System.Xml.Linq;
using SteamInputAddonforClaw.Contracts.BackButtons;
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
    public void Xbox_page_contains_display_name_cards_favorites_and_offline_profile_controls()
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
        Assert.Contains("FavoriteButton_Click", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ProfileEnabledToggle\"", xaml, StringComparison.Ordinal);
        foreach (var control in new[] { "TdpEnabledToggle", "CpuBoostEnabledToggle", "PowerModeEnabledToggle", "FpsEnabledToggle", "ResolutionComboBox" })
            Assert.Contains($"x:Name=\"{control}\"", xaml, StringComparison.Ordinal);
        foreach (var internalField in new[] { "StoreId", "TitleId", "PackageFamilyName", "PackageFullName", "ConfigPath", "Executable", "AUMID", "ProcessPath" })
        {
            Assert.DoesNotContain(internalField, xaml, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(internalField, code, StringComparison.OrdinalIgnoreCase);
        }
        Assert.DoesNotContain("Text=\"{Binding Key}\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("StoreId", code, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("TitleId", code, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("PackageFullName", code, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ConfigPath", code, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Steam_and_Xbox_game_pages_edit_minimum_gpu_clock_with_driver_indexes_and_cancel_stale_drafts()
    {
        var steamXaml = XDocument.Load(Source("src", "SteamInputAddonforClaw.UI", "Views", "ProfilePage.xaml")).ToString();
        var steamCode = File.ReadAllText(Source("src", "SteamInputAddonforClaw.UI", "Views", "ProfilePage.xaml.cs"));
        var xboxXaml = XDocument.Load(Source("src", "SteamInputAddonforClaw.UI", "Views", "XboxPage.xaml")).ToString();
        var xboxCode = File.ReadAllText(Source("src", "SteamInputAddonforClaw.UI", "Views", "XboxPage.xaml.cs"));

        foreach (var xaml in new[] { steamXaml, xboxXaml })
        {
            Assert.Contains("x:Name=\"GpuMinimumClockEnabledToggle\"", xaml, StringComparison.Ordinal);
            Assert.Contains("x:Name=\"GpuMinimumClockAcSlider\"", xaml, StringComparison.Ordinal);
            Assert.Contains("x:Name=\"GpuMinimumClockDcSlider\"", xaml, StringComparison.Ordinal);
            Assert.Contains("Minimum=\"0\" Maximum=\"0\" StepFrequency=\"1\"", xaml, StringComparison.Ordinal);
        }

        foreach (var code in new[] { steamCode, xboxCode })
        {
            Assert.Contains("Task.Delay(300", code, StringComparison.Ordinal);
            Assert.Contains("GpuMinimumClockEnabledToggle_Toggled", code, StringComparison.Ordinal);
            Assert.Contains("CancelGpuMinimumClockDebounce()", code, StringComparison.Ordinal);
            Assert.Contains("SelectableClocksMhz", code, StringComparison.Ordinal);
            Assert.Contains("Math.Round(e.NewValue)", code, StringComparison.Ordinal);
        }

        Assert.Contains("SetGameProfileGpuMinimumClockAcAsync(appId, index)", steamCode, StringComparison.Ordinal);
        Assert.Contains("SetGameProfileGpuMinimumClockDcAsync(appId, index)", steamCode, StringComparison.Ordinal);
        Assert.Contains("SetXboxGameProfileGpuMinimumClockAcAsync(key, index)", xboxCode, StringComparison.Ordinal);
        Assert.Contains("SetXboxGameProfileGpuMinimumClockDcAsync(key, index)", xboxCode, StringComparison.Ordinal);
        Assert.Contains("_selectedGame?.AppId != appId", steamCode, StringComparison.Ordinal);
        Assert.Contains("_selectedGame?.Key != key", xboxCode, StringComparison.Ordinal);
    }

    [Fact]
    public void Xbox_controller_editor_uses_per_game_override_toggle_and_persists_one_complete_mapping()
    {
        var xaml = XDocument.Load(Source("src", "SteamInputAddonforClaw.UI", "Views", "XboxPage.xaml")).ToString();
        var code = File.ReadAllText(Source("src", "SteamInputAddonforClaw.UI", "Views", "XboxPage.xaml.cs"));
        var options = File.ReadAllText(Source("src", "SteamInputAddonforClaw.UI", "Views", "BackButtonMappingUiOptions.cs"));
        var render = Method(code, "private void Render(", "private void RenderProfileStatus(");
        var perGameToggle = Method(code, "private void PerGameBackButtonMappingEnabledToggle_Toggled", "private void BackButtonTargetComboBox_SelectionChanged");
        var targetChanged = Method(code, "private void BackButtonTargetComboBox_SelectionChanged", "private void QueueBackButtonMappingSave");
        var queue = Method(code, "private void QueueBackButtonMappingSave", "private BackButtonMappingSettings? ReadSelectedBackButtonMapping");
        var save = Method(code, "private async Task SaveBackButtonMappingAfterAsync", "internal static async Task<TResult> RunAfterPreviousAsync");

        Assert.Contains("Header=\"M1 / M2 Button Mapping\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Header=\"Use global M1 / M2 mapping\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"PerGameBackButtonMappingEnabledToggle\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("x:Name=\"UseGlobalBackButtonMappingToggle\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"PerGameBackButtonMappingEnabledToggle\" IsOn=\"False\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"M1BackButtonTargetComboBox\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"M2BackButtonTargetComboBox\"", xaml, StringComparison.Ordinal);
        Assert.Contains("var perGameEnabled = !configuration.UseGlobalMapping;", code, StringComparison.Ordinal);
        Assert.Contains("PerGameBackButtonMappingEnabledToggle.IsOn = perGameEnabled;", code, StringComparison.Ordinal);
        Assert.Contains("snapshot.PersistenceWritable && perGameEnabled", code, StringComparison.Ordinal);
        Assert.Contains("var perGameEnabled = PerGameBackButtonMappingEnabledToggle.IsOn;", perGameToggle, StringComparison.Ordinal);
        Assert.Contains("var mapping = perGameEnabled ? ReadSelectedBackButtonMapping() : null;", perGameToggle, StringComparison.Ordinal);
        Assert.Contains("if (perGameEnabled && mapping is null)", perGameToggle, StringComparison.Ordinal);
        Assert.Contains("snapshot.PersistenceWritable && perGameEnabled", perGameToggle, StringComparison.Ordinal);
        Assert.Contains("ReadSelectedBackButtonMapping()", targetChanged, StringComparison.Ordinal);
        Assert.Contains("!PerGameBackButtonMappingEnabledToggle.IsOn", targetChanged, StringComparison.Ordinal);
        Assert.Contains("new BackButtonMappingSettings(m1, m2)", code, StringComparison.Ordinal);
        Assert.Contains("QueueBackButtonMappingSave(key, mapping)", perGameToggle, StringComparison.Ordinal);
        Assert.Contains("QueueBackButtonMappingSave(_selectedGame.Key, mapping)", targetChanged, StringComparison.Ordinal);
        Assert.Contains("++_backButtonEditVersion", queue, StringComparison.Ordinal);
        Assert.Contains("SaveBackButtonMappingAfterAsync(_backButtonSaveChain, key, mapping, version)", queue, StringComparison.Ordinal);
        Assert.Contains("RunAfterPreviousAsync(", save, StringComparison.Ordinal);
        Assert.Contains("previous,", save, StringComparison.Ordinal);
        Assert.Contains("SetXboxGameProfileBackButtonMappingAsync(key, mapping)", save, StringComparison.Ordinal);
        Assert.Contains("IsCurrentBackButtonMappingEdit(version, _backButtonEditVersion)", save, StringComparison.Ordinal);
        Assert.Contains("IsCurrentProfileResponse(_active, _selectedGame?.Key, key, result.Snapshot.Key)", save, StringComparison.Ordinal);
        Assert.Contains("Render(result.Snapshot)", save, StringComparison.Ordinal);
        Assert.True(render.IndexOf("_suppressEvents =", StringComparison.Ordinal) >= 0);
        Assert.DoesNotContain("SetXboxGameProfileBackButtonMappingAsync", render, StringComparison.Ordinal);
        Assert.Contains("Enum.GetValues<Xbox360BackButtonTarget>()", options, StringComparison.Ordinal);
        Assert.Contains("BackButtonMappingUiOptions.AddTargets(M1BackButtonTargetComboBox)", code, StringComparison.Ordinal);
        Assert.Contains("BackButtonMappingUiOptions.AddTargets(M2BackButtonTargetComboBox)", code, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Rapid_whole_mapping_edits_save_in_order_and_only_render_the_latest_edit()
    {
        var firstMapping = new BackButtonMappingSettings(Xbox360BackButtonTarget.X, Xbox360BackButtonTarget.Y);
        var latestMapping = new BackButtonMappingSettings(Xbox360BackButtonTarget.LeftBumper, Xbox360BackButtonTarget.RightBumper);
        var firstSaveStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirstSave = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var persistedMapping = BackButtonMappingSettings.Default;
        var renderedMapping = BackButtonMappingSettings.Default;
        var version = 1L;
        var writes = 0;

        async Task<BackButtonMappingSettings> SaveFirstAsync()
        {
            firstSaveStarted.SetResult();
            await releaseFirstSave.Task;
            writes++;
            persistedMapping = firstMapping;
            return firstMapping;
        }

        var firstSave = XboxPage.RunAfterPreviousAsync(Task.CompletedTask, SaveFirstAsync);
        await firstSaveStarted.Task;

        version++;
        var latestVersion = version;
        var latestSave = XboxPage.RunAfterPreviousAsync(firstSave, () =>
        {
            writes++;
            persistedMapping = latestMapping;
            return Task.FromResult(latestMapping);
        });

        Assert.Equal(0, writes);
        releaseFirstSave.SetResult();

        var firstResult = await firstSave;
        if (XboxPage.IsCurrentBackButtonMappingEdit(1, version)) renderedMapping = firstResult;
        var latestResult = await latestSave;
        if (XboxPage.IsCurrentBackButtonMappingEdit(latestVersion, version)) renderedMapping = latestResult;

        Assert.Equal(2, writes);
        Assert.Equal(latestMapping, persistedMapping);
        Assert.Equal(latestMapping, renderedMapping);
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
        Assert.Contains("_active = false", deactivate, StringComparison.Ordinal);
        Assert.Contains("CancelScan()", deactivate, StringComparison.Ordinal);
        Assert.Contains("CancelCapture()", deactivate, StringComparison.Ordinal);
        Assert.DoesNotContain("CancelTdpDebounce()", deactivate, StringComparison.Ordinal);
        Assert.DoesNotContain("CancelFpsDebounce()", deactivate, StringComparison.Ordinal);
        Assert.Contains("await RefreshGamesAsync()", refreshButton, StringComparison.Ordinal);
        Assert.Contains("previous?.Cancel()", refresh, StringComparison.Ordinal);
        var scanCall = refresh.IndexOf("await _frontend.ScanXboxGamesAsync(scan.Token)", StringComparison.Ordinal);
        var staleGuard = refresh.IndexOf("if (!IsCurrentScan(_active, _scanCancellation, scan)) return", StringComparison.Ordinal);
        Assert.True(scanCall >= 0 && staleGuard > scanCall);
        Assert.Contains("XboxContent.Activate()", showPage, StringComparison.Ordinal);
        Assert.Contains("else if (wasXbox) XboxContent.Deactivate()", showPage, StringComparison.Ordinal);
        Assert.DoesNotContain("StateInvalidated", pageCode, StringComparison.Ordinal);
        Assert.DoesNotContain("PackageCatalog", pageCode, StringComparison.Ordinal);
        Assert.DoesNotContain("Timer", pageCode, StringComparison.Ordinal);
    }

    [Fact]
    public void Xbox_page_returns_to_catalog_when_refreshed_catalog_retires_the_selected_game()
    {
        var pageCode = File.ReadAllText(Source("src", "SteamInputAddonforClaw.UI", "Views", "XboxPage.xaml.cs"));
        var activate = Method(pageCode, "internal void Activate()", "internal void Deactivate()");
        var refresh = Method(pageCode, "private async Task RefreshGamesAsync()", "private void GameSearchBox_TextChanged");
        var back = Method(pageCode, "private void BackButton_Click", "private async void FavoriteButton_Click");
        var returnToCatalog = Method(pageCode, "private void ReturnToCatalog()", "private async void FavoriteButton_Click");

        Assert.Contains("_ = RefreshGamesAsync()", activate, StringComparison.Ordinal);
        Assert.Contains("var selectedKey = _selectedGame?.Key", refresh, StringComparison.Ordinal);
        Assert.Contains("_catalog.FirstOrDefault(x => x.Key == selectedKey)", refresh, StringComparison.Ordinal);
        Assert.Contains("if (snapshot.Outcome != FrontendXboxGameCatalogOutcome.Ready)", refresh, StringComparison.Ordinal);
        Assert.Contains("else if (_selectedGame is null)", refresh, StringComparison.Ordinal);
        Assert.True(refresh.Split("ReturnToCatalog();", StringSplitOptions.None).Length - 1 >= 3);
        Assert.Contains("ReturnToCatalog()", back, StringComparison.Ordinal);
        Assert.Contains("ClearSelection()", returnToCatalog, StringComparison.Ordinal);
        Assert.Contains("SelectedGameNameText.Text = string.Empty", returnToCatalog, StringComparison.Ordinal);
        Assert.Contains("DetailPanel.Visibility = Visibility.Collapsed", returnToCatalog, StringComparison.Ordinal);
        Assert.Contains("CatalogPanel.Visibility = Visibility.Visible", returnToCatalog, StringComparison.Ordinal);
        Assert.Contains("RefreshGamesButton.Visibility = Visibility.Visible", returnToCatalog, StringComparison.Ordinal);
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

        var withFavorite = games[0] with { Favorite = true };
        var favoritesFirst = XboxPage.FilterAndSort(games.Select(game => game.Key == games[0].Key ? withFavorite : game), null);
        Assert.Equal(withFavorite.Key, favoritesFirst[0].Key);
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

    [Fact]
    public void Xbox_profile_responses_require_active_page_selected_key_and_matching_response_key()
    {
        Assert.True(XboxPage.IsCurrentProfileResponse(true, "store:one", "store:one", "store:one"));
        Assert.False(XboxPage.IsCurrentProfileResponse(false, "store:one", "store:one", "store:one"));
        Assert.False(XboxPage.IsCurrentProfileResponse(true, "store:two", "store:one", "store:one"));
        Assert.False(XboxPage.IsCurrentProfileResponse(true, "store:one", "store:one", "store:two"));
    }

    [Fact]
    public void Xbox_detail_handlers_use_XBOX_Rpcs_and_preserve_cancel_boundaries_for_profile_edits()
    {
        var code = File.ReadAllText(Source("src", "SteamInputAddonforClaw.UI", "Views", "XboxPage.xaml.cs"));
        var steamCode = File.ReadAllText(Source("src", "SteamInputAddonforClaw.UI", "Views", "ProfilePage.xaml.cs"));
        foreach (var method in new[]
        {
            "SetXboxGameProfileFavoriteAsync", "SetXboxGameProfileEnabledAsync", "SetXboxGameProfileCpuBoostEnabledAsync",
            "SetXboxGameProfileCpuBoostAcAsync", "SetXboxGameProfileCpuBoostDcAsync", "SetXboxGameProfileTdpEnabledAsync",
            "SetXboxGameProfileTdpAsync", "SetXboxGameProfilePowerModeEnabledAsync", "SetXboxGameProfilePowerModeAcAsync",
            "SetXboxGameProfilePowerModeDcAsync", "SetXboxGameProfileFpsLimitEnabledAsync", "SetXboxGameProfileFpsLimitAcAsync",
            "SetXboxGameProfileFpsLimitDcAsync", "SetXboxGameProfileResolutionAsync", "SetXboxGameProfileBackButtonMappingAsync"
        }) Assert.Contains(method, code, StringComparison.Ordinal);
        Assert.Contains("Task.Delay(300", code, StringComparison.Ordinal);
        Assert.Contains("Task.Delay(300", steamCode, StringComparison.Ordinal);
        Assert.DoesNotContain("Task.Delay(275", code, StringComparison.Ordinal);
        Assert.DoesNotContain("Task.Delay(275", steamCode, StringComparison.Ordinal);
        Assert.Contains("DevicePage.TdpDraftPolicy.AdjustAfterEdit", code, StringComparison.Ordinal);
        var select = Method(code, "private async Task SelectGameAsync", "private void BeginProfileLoad");
        var back = Method(code, "private void BackButton_Click", "private async void FavoriteButton_Click");
        var deactivate = Method(code, "internal void Deactivate()", "private async void RefreshGamesButton_Click");
        var clearSelection = Method(code, "private void ClearSelection()", "private void Render(");
        var tdpSubmit = Method(code, "private async Task SubmitTdpAfterDelayAsync", "private void SetTdpText");
        var fpsSubmit = Method(code, "private async Task SubmitFpsAfterDelayAsync", "private void CancelFpsDebounce");
        var profileEnabled = Method(code, "private async void ProfileEnabledToggle_Toggled", "private void RenderBackButtonMapping");
        Assert.Contains("CancelTdpDebounce()", select, StringComparison.Ordinal);
        Assert.Contains("CancelFpsDebounce()", select, StringComparison.Ordinal);
        Assert.Contains("ClearSelection()", back, StringComparison.Ordinal);
        Assert.Contains("CancelTdpDebounce()", clearSelection, StringComparison.Ordinal);
        Assert.Contains("CancelFpsDebounce()", clearSelection, StringComparison.Ordinal);
        Assert.DoesNotContain("CancelTdpDebounce()", deactivate, StringComparison.Ordinal);
        Assert.DoesNotContain("CancelFpsDebounce()", deactivate, StringComparison.Ordinal);
        Assert.Contains("CancelTdpDebounce()", profileEnabled, StringComparison.Ordinal);
        Assert.DoesNotContain("|| !_active", tdpSubmit, StringComparison.Ordinal);
        Assert.DoesNotContain("|| !_active", fpsSubmit, StringComparison.Ordinal);

        var tdpMutation = tdpSubmit.IndexOf("await _frontend.SetXboxGameProfileTdpAsync", StringComparison.Ordinal);
        var dirtySettle = tdpSubmit.IndexOf("_tdpDraftDirty = false", StringComparison.Ordinal);
        var tdpResponseGuard = tdpSubmit.IndexOf("if (!IsCurrentProfileResponse(_active, _selectedGame?.Key, key, result.Snapshot.Key))", StringComparison.Ordinal);
        var tdpRender = tdpSubmit.IndexOf("Render(result.Snapshot, preserveDraft)", StringComparison.Ordinal);
        Assert.True(tdpMutation >= 0 && dirtySettle > tdpMutation && tdpResponseGuard > dirtySettle && tdpRender > tdpResponseGuard);

        var fpsMutation = fpsSubmit.IndexOf("await _frontend.SetXboxGameProfileFpsLimit", StringComparison.Ordinal);
        var fpsResponseGuard = fpsSubmit.IndexOf("if (!IsCurrentProfileResponse(_active, _selectedGame?.Key, key, result.Snapshot.Key))", StringComparison.Ordinal);
        var fpsRender = fpsSubmit.IndexOf("Render(result.Snapshot)", StringComparison.Ordinal);
        Assert.True(fpsMutation >= 0 && fpsResponseGuard > fpsMutation && fpsRender > fpsResponseGuard);

        Assert.False(XboxPage.ShouldPreserveDirtyTdpDraft(true, 5, 5));
        Assert.True(XboxPage.ShouldPreserveDirtyTdpDraft(true, 5, 6));
        Assert.DoesNotContain("StateInvalidated", code, StringComparison.Ordinal);
        Assert.DoesNotContain("ActualRunningAppId", code, StringComparison.Ordinal);
        Assert.DoesNotContain("Reconcile", code, StringComparison.Ordinal);
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
