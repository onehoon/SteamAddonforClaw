using SteamInputAddonforClaw.Views;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class QuickOverlayPageTests
{
    [Theory]
    [InlineData(false, null, false)]
    [InlineData(false, true, true)]
    [InlineData(true, false, false)]
    public void Failed_mutation_prefers_readback_and_falls_back_to_last_confirmed_value(
        bool lastKnownValue,
        bool? refreshedValue,
        bool expectedValue)
    {
        Assert.Equal(expectedValue,
            QuickOverlayPage.ResolveQuickSettingsPowerSourcePreference(lastKnownValue, refreshedValue));
    }

    [Fact]
    public void Initialization_uses_startup_snapshot_and_activation_refreshes_authoritative_state()
    {
        var root = FindRepositoryRoot();
        var page = File.ReadAllText(Path.Combine(root, "src", "SteamInputAddonforClaw.UI", "Views", "QuickOverlayPage.xaml.cs"));
        var initialize = ExtractMethod(page, "internal void Initialize(");
        var refresh = ExtractMethod(page, "private async Task RefreshQuickSettingsPowerSourcePreferenceAsync()");

        Assert.Contains("bootstrap.Settings.QuickSettingsCurrentPowerSourceOnly", initialize, StringComparison.Ordinal);
        Assert.Contains("SetQuickSettingsPowerSourceToggle(_lastKnownQuickSettingsCurrentPowerSourceOnly)", initialize, StringComparison.Ordinal);
        Assert.DoesNotContain("GetBootstrapAsync", initialize, StringComparison.Ordinal);
        Assert.Contains("internal void Activate() => RequestRefresh()", page, StringComparison.Ordinal);
        var requestRefresh = ExtractMethod(page, "internal void RequestRefresh()");
        Assert.Contains("_ = RefreshQuickSettingsPowerSourcePreferenceAsync()", requestRefresh, StringComparison.Ordinal);
        Assert.Contains("_ = RefreshTabOrderAsync()", requestRefresh, StringComparison.Ordinal);
        Assert.Contains("await _frontend.GetBootstrapAsync()", refresh, StringComparison.Ordinal);
        Assert.Contains("_powerSourceMutationInProgress || _discardInFlightPowerSourceRefresh", refresh, StringComparison.Ordinal);
        Assert.Contains("_discardInFlightPowerSourceRefresh = false", refresh, StringComparison.Ordinal);
        Assert.Contains("_lastKnownQuickSettingsCurrentPowerSourceOnly = bootstrap.Settings.QuickSettingsCurrentPowerSourceOnly", refresh, StringComparison.Ordinal);
        Assert.Contains("catch (Exception exception)", refresh, StringComparison.Ordinal);
        Assert.Contains("finally", refresh, StringComparison.Ordinal);
    }

    [Fact]
    public void Tab_order_rows_follow_only_runtime_snapshots_and_keep_tab_identity_for_moves()
    {
        var page = File.ReadAllText(Path.Combine(FindRepositoryRoot(),
            "src", "SteamInputAddonforClaw.UI", "Views", "QuickOverlayPage.xaml.cs"));
        var refresh = ExtractMethod(page, "private async Task RefreshTabOrderAsync()");
        var apply = ExtractMethod(page, "private void ApplyAuthoritativeTabOrder(");
        var move = ExtractMethod(page, "private async Task MoveTabOrderAsync(");
        var button = ExtractMethod(page, "private Button CreateTabOrderButton(");
        var buttonStates = ExtractMethod(page, "private void UpdateTabOrderButtonStates()");

        Assert.Contains("await _frontend.CaptureAddonQuickSettingsTabOrderAsync()", refresh, StringComparison.Ordinal);
        Assert.Contains("_frontend is null || _tabOrderMoveInProgress || _tabOrderRefreshInProgress", refresh, StringComparison.Ordinal);
        Assert.Contains("foreach (var row in snapshot.Rows)", apply, StringComparison.Ordinal);
        Assert.Contains("QuickOverlayTabOrderExpander.Items.Add(controls.Card)", apply, StringComparison.Ordinal);
        Assert.Contains("row.CanMoveEarlier", apply, StringComparison.Ordinal);
        Assert.Contains("row.CanMoveLater", apply, StringComparison.Ordinal);
        Assert.Contains("!_tabOrderStateAvailable", move, StringComparison.Ordinal);
        Assert.Contains("!CanMoveTab(tabId, delta)", move, StringComparison.Ordinal);
        Assert.Contains("new AddonQuickSettingsTabOrderMoveIntent(tabId, delta)", move, StringComparison.Ordinal);
        Assert.Contains("ApplyAuthoritativeTabOrder(result.State)", move, StringComparison.Ordinal);
        Assert.True(move.IndexOf("await _frontend.MoveAddonQuickSettingsTabAsync(", StringComparison.Ordinal) <
                    move.IndexOf("ApplyAuthoritativeTabOrder(result.State)", StringComparison.Ordinal));
        Assert.Contains("Tag = tabId", button, StringComparison.Ordinal);
        Assert.Contains("MoveTabOrderAsync(tabId, delta)", button, StringComparison.Ordinal);
        Assert.Contains("_tabOrderMoveInProgress", buttonStates, StringComparison.Ordinal);
        Assert.Contains("controls.MoveEarlier.IsEnabled = available && row!.CanMoveEarlier", buttonStates, StringComparison.Ordinal);
        Assert.Contains("controls.MoveLater.IsEnabled = available && row!.CanMoveLater", buttonStates, StringComparison.Ordinal);
        Assert.DoesNotContain("QuickOverlayTabOrderExpander.IsExpanded =", apply, StringComparison.Ordinal);
    }

    [Fact]
    public void Tab_order_move_exception_reads_runtime_directly_while_mutation_is_in_flight()
    {
        var page = File.ReadAllText(Path.Combine(FindRepositoryRoot(),
            "src", "SteamInputAddonforClaw.UI", "Views", "QuickOverlayPage.xaml.cs"));
        var move = ExtractMethod(page, "private async Task MoveTabOrderAsync(");
        var catchStart = move.IndexOf("catch (Exception exception)", StringComparison.Ordinal);
        var readbackStart = move.IndexOf("await _frontend.CaptureAddonQuickSettingsTabOrderAsync()", catchStart, StringComparison.Ordinal);
        var fallbackStart = move.IndexOf("catch (Exception readbackException)", readbackStart, StringComparison.Ordinal);

        Assert.True(catchStart >= 0 && readbackStart > catchStart && fallbackStart > readbackStart);
        Assert.Contains("_tabOrderMoveInProgress = true", move[..catchStart], StringComparison.Ordinal);
        Assert.DoesNotContain("RefreshTabOrderAsync()", move[catchStart..fallbackStart], StringComparison.Ordinal);
        Assert.Contains("ApplyAuthoritativeTabOrder(snapshot)", move[catchStart..fallbackStart], StringComparison.Ordinal);
        Assert.Contains("MarkTabOrderUnavailable()", move[fallbackStart..], StringComparison.Ordinal);
        Assert.Contains("keeping the last confirmed order", move[fallbackStart..], StringComparison.Ordinal);
    }

    [Fact]
    public void Toggle_uses_the_existing_typed_mutation_and_rolls_back_from_readback_or_last_known_value()
    {
        var page = File.ReadAllText(Path.Combine(FindRepositoryRoot(),
            "src", "SteamInputAddonforClaw.UI", "Views", "QuickOverlayPage.xaml.cs"));
        var handler = ExtractMethod(page, "private async void QuickSettingsPowerSourceToggleSwitch_Toggled(");
        var xaml = File.ReadAllText(Path.Combine(FindRepositoryRoot(),
            "src", "SteamInputAddonforClaw.UI", "Views", "QuickOverlayPage.xaml"));

        Assert.Contains("_applyingPowerSourcePreference || _frontend is null || _powerSourceMutationInProgress", handler, StringComparison.Ordinal);
        Assert.Contains("_powerSourceMutationInProgress = true", handler, StringComparison.Ordinal);
        Assert.Contains("_discardInFlightPowerSourceRefresh = _powerSourceRefreshInProgress", handler, StringComparison.Ordinal);
        Assert.Contains("QuickSettingsPowerSourceToggleSwitch.IsEnabled = false", handler, StringComparison.Ordinal);
        Assert.Contains("await _frontend.SetQuickSettingsCurrentPowerSourceOnlyAsync(", handler, StringComparison.Ordinal);
        Assert.Contains("settings.QuickSettingsCurrentPowerSourceOnly", handler, StringComparison.Ordinal);
        Assert.Contains("await _frontend.GetBootstrapAsync()", handler, StringComparison.Ordinal);
        Assert.Contains("ResolveQuickSettingsPowerSourcePreference(", handler, StringComparison.Ordinal);
        Assert.Contains("finally", handler, StringComparison.Ordinal);
        Assert.Contains("_powerSourceMutationInProgress = false", handler, StringComparison.Ordinal);
        Assert.Contains("QuickSettingsPowerSourceToggleSwitch.IsEnabled = true", handler, StringComparison.Ordinal);
        Assert.Contains("Toggled=\"QuickSettingsPowerSourceToggleSwitch_Toggled\"", xaml, StringComparison.Ordinal);

        var overlayPage = File.ReadAllText(Path.Combine(FindRepositoryRoot(),
            "src", "SteamInputAddonforClaw.Overlay", "OverlayWindow.ClawHud.cs"));
        var overlayApp = File.ReadAllText(Path.Combine(FindRepositoryRoot(),
            "src", "SteamInputAddonforClaw.Overlay", "App.xaml.cs"));
        Assert.Contains("QuickSettingsCurrentPowerSourceOnlyRequested", overlayPage, StringComparison.Ordinal);
        Assert.Contains("ApplyQuickSettingsCurrentPowerSourceOnly", overlayPage, StringComparison.Ordinal);
        Assert.Contains("_client!.SendCurrentPowerSourceMutationAsync(enabled)", overlayApp, StringComparison.Ordinal);
    }

    [Fact]
    public void Main_window_creates_the_page_only_on_demand_and_refreshes_it_when_active_or_revisited()
    {
        var root = FindRepositoryRoot();
        var mainWindow = File.ReadAllText(Path.Combine(root, "src", "SteamInputAddonforClaw.UI", "MainWindow.xaml.cs"));
        var constructor = ExtractMethod(mainWindow, "internal MainWindow(");
        var warmupStart = mainWindow.IndexOf("private static readonly MainNavigationPage[] DeferredWarmupPages =", StringComparison.Ordinal);
        var warmupEnd = mainWindow.IndexOf("private readonly IAddonFrontendControl", warmupStart, StringComparison.Ordinal);
        var warmup = mainWindow[warmupStart..warmupEnd];
        var materialize = ExtractMethod(mainWindow, "private bool TryMaterializeMainPage(");
        var showPage = ExtractMethod(mainWindow, "private void ShowPage(");
        var invalidation = ExtractMethod(mainWindow, "private void RefreshInvalidatedFrontendStateOnUiThread()");

        Assert.DoesNotContain("GetOrCreateQuickOverlayPage", constructor, StringComparison.Ordinal);
        Assert.DoesNotContain("QuickOverlay", warmup, StringComparison.Ordinal);
        Assert.Contains("case MainNavigationPage.QuickOverlay: _ = GetOrCreateQuickOverlayPage(); break;", materialize, StringComparison.Ordinal);
        Assert.Contains("QuickOverlayPageHost.Visibility = page == MainNavigationPage.QuickOverlay", showPage, StringComparison.Ordinal);
        Assert.Contains("if (page == MainNavigationPage.QuickOverlay) _quickOverlayContent!.Activate();", showPage, StringComparison.Ordinal);
        Assert.Contains("MainNavigationPage.QuickOverlay => \"QuickOverlay\"", mainWindow, StringComparison.Ordinal);
        Assert.Contains("if (_navigationState.CurrentPage == MainNavigationPage.QuickOverlay)", invalidation, StringComparison.Ordinal);
        Assert.Contains("_quickOverlayContent?.RequestRefresh()", invalidation, StringComparison.Ordinal);

        var shell = File.ReadAllText(Path.Combine(root, "src", "SteamInputAddonforClaw.UI", "MainWindow.xaml"));
        Assert.Contains("x:Name=\"OverlayPageHost\"", shell, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"QuickOverlayPageHost\"", shell, StringComparison.Ordinal);
        Assert.Contains("Tag=\"Overlay\"", shell, StringComparison.Ordinal);
        Assert.Contains("Tag=\"QuickOverlay\"", shell, StringComparison.Ordinal);
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

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "SteamInputAddonforClaw.slnx")))
                return directory.FullName;
        }

        throw new DirectoryNotFoundException("Repository root was not found.");
    }
}
