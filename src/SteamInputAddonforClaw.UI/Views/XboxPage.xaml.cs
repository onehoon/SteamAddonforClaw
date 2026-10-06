using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using SteamInputAddonforClaw.Contracts.DeviceProfiles;
using SteamInputAddonforClaw.Contracts.Frontend;

namespace SteamInputAddonforClaw.Views;

public sealed partial class XboxPage : UserControl
{
    private IAddonFrontendControl? _frontend;
    private bool _active, _suppressEvents, _suppressTdpEvents, _suppressFpsEvents, _suppressFeatureEvents;
    private IReadOnlyList<FrontendXboxGameCatalogEntry> _catalog = [];
    private FrontendXboxGameCatalogEntry? _selectedGame;
    private FrontendXboxGameProfileSnapshot? _snapshot;
    private CancellationTokenSource? _scanCancellation, _captureCancellation;
    private int? _acPl1, _acPl2, _dcPl1, _dcPl2;
    private CancellationTokenSource? _tdpDebounce;
    private long _tdpGeneration;
    private bool _tdpDraftDirty;
    private CancellationTokenSource? _acFpsDebounce, _dcFpsDebounce; private long _acFpsGeneration, _dcFpsGeneration; private int? _acFpsDraft, _dcFpsDraft;
    private static readonly CpuBoostModeItem[] Modes = [new(CpuBoostMode.Disabled, "Disabled"), new(CpuBoostMode.Enabled, "Enabled"), new(CpuBoostMode.Aggressive, "Aggressive"), new(CpuBoostMode.EfficientEnabled, "Efficient Enabled"), new(CpuBoostMode.EfficientAggressive, "Efficient Aggressive"), new(CpuBoostMode.AggressiveAtGuaranteed, "Aggressive At Guaranteed"), new(CpuBoostMode.EfficientAggressiveAtGuaranteed, "Efficient Aggressive At Guaranteed")];
    private static readonly ResolutionItem[] ResolutionItems = [new(null, null, "Do not change"), new(1920, 1200, "1920 × 1200"), new(1920, 1080, "1920 × 1080"), new(1680, 1050, "1680 × 1050"), new(1440, 900, "1440 × 900")];

    public XboxPage()
    {
        InitializeComponent(); CpuBoostAcComboBox.ItemsSource = Modes; CpuBoostDcComboBox.ItemsSource = Modes; PowerModeAcComboBox.ItemsSource = PowerModes; PowerModeDcComboBox.ItemsSource = PowerModes; ResolutionComboBox.ItemsSource = ResolutionItems; SetEditorsEnabled(false);
}
    internal void Initialize(IAddonFrontendControl frontend) => _frontend = frontend;
    internal void Activate() { if (_active) return; _active = true; _ = RefreshGamesAsync(); }
    internal void Deactivate()
    {
        _active = false;
        CancelScan(); CancelCapture(); CancelTdpDebounce(); CancelFpsDebounce();
    }

    private async void RefreshGamesButton_Click(object sender, RoutedEventArgs e)
        => await RefreshGamesAsync();

    private async Task RefreshGamesAsync()
    {
        if (!_active || _frontend is null) return;
        var scan = new CancellationTokenSource();
        var previous = _scanCancellation;
        _scanCancellation = scan;
        previous?.Cancel();
        try
        {
            var selectedKey = _selectedGame?.Key;
            var snapshot = await _frontend.ScanXboxGamesAsync(scan.Token);
            if (!IsCurrentScan(_active, _scanCancellation, scan)) return;
            _catalog = snapshot.Outcome == FrontendXboxGameCatalogOutcome.Ready ? snapshot.Games : [];
            ApplyCatalogFilter();
            _selectedGame = selectedKey is null ? null : _catalog.FirstOrDefault(x => x.Key == selectedKey);
            if (snapshot.Outcome != FrontendXboxGameCatalogOutcome.Ready)
            {
                ClearSelection();
                ShowError(snapshot.FailureMessage ?? "XBOX game catalog could not be loaded.", null);
            }
            else if (_selectedGame is null)
            {
                ClearSelection();
                if (_catalog.Count == 0) ShowInfo("No installed XBOX games were found."); else ClearError();
            }
            else await CaptureSelectedAsync(_selectedGame.Key);
        }
        catch (OperationCanceledException) when (scan.IsCancellationRequested) { }
        catch (Exception exception) { if (IsCurrentScan(_active, _scanCancellation, scan)) ShowError("XBOX game catalog could not be refreshed.", exception); }
        finally { if (ReferenceEquals(_scanCancellation, scan)) _scanCancellation = null; scan.Dispose(); }
    }

    private void GameSearchBox_TextChanged(object sender, TextChangedEventArgs e) => ApplyCatalogFilter();
    private void ApplyCatalogFilter()
    {
        var query = GameSearchBox.Text.Trim();
        GameGrid.ItemsSource = FilterAndSort(_catalog, query).Select(x => new GameCardItem(x)).ToArray();
    }
    private async void GameCardButton_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is not FrontendXboxGameCatalogEntry game) return;
        SelectedGameNameText.Text = game.DisplayName; CatalogPanel.Visibility = Visibility.Collapsed; DetailPanel.Visibility = Visibility.Visible; RefreshGamesButton.Visibility = Visibility.Collapsed;
        await SelectGameAsync(game);
    }
    private void BackButton_Click(object sender, RoutedEventArgs e) { ClearSelection(); DetailPanel.Visibility = Visibility.Collapsed; CatalogPanel.Visibility = Visibility.Visible; RefreshGamesButton.Visibility = Visibility.Visible; }
    private async void FavoriteButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_active || _frontend is null || (sender as Button)?.Tag is not FrontendXboxGameCatalogEntry game) return;
        try
        {
            var result = await _frontend.SetXboxGameProfileFavoriteAsync(game.Key, !game.Favorite, game.DisplayName);
            if (!result.Succeeded || result.Snapshot.Key != game.Key) { ShowError(result.FailureMessage ?? "Favorite could not be saved.", null); return; }
            _catalog = _catalog.Select(x => x.Key == game.Key ? x with { Favorite = !game.Favorite } : x).ToArray(); ApplyCatalogFilter();
        }
        catch (Exception exception) { ShowError("Favorite could not be saved.", exception); }
    }

    private async Task SelectGameAsync(FrontendXboxGameCatalogEntry game) { CancelCapture(); CancelTdpDebounce(); CancelFpsDebounce(); _tdpDraftDirty = false; _selectedGame = game; BeginProfileLoad(game); await CaptureSelectedAsync(game.Key, preserveDirtyTdpDraft: false); }
    private void BeginProfileLoad(FrontendXboxGameCatalogEntry game) { _snapshot = null; _suppressEvents = _suppressTdpEvents = _suppressFpsEvents = true; CancelFpsDebounce(); try { ProfileEnabledToggle.IsOn = false; ProfileEnabledToggle.IsEnabled = false; FpsEnabledToggle.IsOn = false; FpsEnabledToggle.IsEnabled = false; CpuBoostAcComboBox.SelectedItem = null; CpuBoostDcComboBox.SelectedItem = null; PowerModeAcComboBox.SelectedItem = null; PowerModeDcComboBox.SelectedItem = null; ResolutionComboBox.SelectedItem = null; ResolutionComboBox.IsEnabled = false; _acPl1 = _acPl2 = _dcPl1 = _dcPl2 = null; SetTdpText(); } finally { _suppressEvents = _suppressTdpEvents = _suppressFpsEvents = false; } SetEditorsEnabled(false); }
    private async Task CaptureSelectedAsync(string key, bool preserveDirtyTdpDraft = true)
    {
        if (!_active || _frontend is null) return;
        var capture = new CancellationTokenSource();
        var previous = _captureCancellation;
        _captureCancellation = capture;
        previous?.Cancel();
        try
        {
            var snapshot = await _frontend.CaptureXboxGameProfileAsync(key, capture.Token);
            if (!IsCurrentProfileResponse(_active, _selectedGame?.Key, key, snapshot.Key)) return;
            Render(snapshot, preserveDirtyTdpDraft && _tdpDraftDirty);
        }
        catch (OperationCanceledException) when (capture.IsCancellationRequested) { }
        catch (Exception exception) { if (IsCurrentProfileResponse(_active, _selectedGame?.Key, key, key)) ShowError("XBOX profile settings could not be loaded.", exception); }
        finally { if (ReferenceEquals(_captureCancellation, capture)) _captureCancellation = null; capture.Dispose(); }
    }
    private void ClearSelection() { CancelCapture(); CancelTdpDebounce(); CancelFpsDebounce(); _tdpDraftDirty = false; _selectedGame = null; _snapshot = null; ProfileEnabledToggle.IsOn = false; ProfileEnabledToggle.IsEnabled = false; ResolutionComboBox.SelectedItem = null; ResolutionComboBox.IsEnabled = false; SetEditorsEnabled(false); }

    private void Render(FrontendXboxGameProfileSnapshot snapshot, bool preserveDirtyTdpDraft = false)
    {
        _snapshot = snapshot; _suppressEvents = _suppressTdpEvents = _suppressFpsEvents = _suppressFeatureEvents = true;
        try
        {
            ProfileEnabledToggle.IsOn = snapshot.Enabled; ProfileEnabledToggle.IsEnabled = snapshot.PersistenceWritable; CpuBoostEnabledToggle.IsOn = snapshot.CpuBoost.Enabled; TdpEnabledToggle.IsOn = snapshot.Tdp.Enabled; PowerModeEnabledToggle.IsOn = snapshot.PowerMode?.Enabled == true; FpsEnabledToggle.IsOn = snapshot.FpsLimit?.Enabled == true; CpuBoostEnabledToggle.IsEnabled = TdpEnabledToggle.IsEnabled = snapshot.Exists && snapshot.Enabled && snapshot.PersistenceWritable; PowerModeEnabledToggle.IsEnabled = snapshot.Exists && snapshot.Enabled && snapshot.PersistenceWritable && snapshot.PowerMode is not null; FpsEnabledToggle.IsEnabled = snapshot.Exists && snapshot.Enabled && snapshot.PersistenceWritable && snapshot.FpsLimit?.Available == true; _acFpsDraft = snapshot.FpsLimit?.AcFps ?? 60; _dcFpsDraft = snapshot.FpsLimit?.DcFps ?? 60; AcFpsSlider.Value = _acFpsDraft.Value; DcFpsSlider.Value = _dcFpsDraft.Value; AcFpsText.Text = $"{_acFpsDraft} FPS"; DcFpsText.Text = $"{_dcFpsDraft} FPS"; IntelFpsExpander.Description = snapshot.FpsLimit?.Available == true ? "Uses Intel's official API. Some games may not support FPS limiting." : snapshot.FpsLimit?.UnavailableReason ?? "Intel FPS Limit is unavailable.";
            CpuBoostAcComboBox.SelectedItem = Modes.FirstOrDefault(x => x.Mode == snapshot.CpuBoost.Ac); CpuBoostDcComboBox.SelectedItem = Modes.FirstOrDefault(x => x.Mode == snapshot.CpuBoost.Dc); PowerModeAcComboBox.SelectedItem = snapshot.PowerMode is { } power ? PowerModes.FirstOrDefault(x => x.Mode == power.Ac) : null; PowerModeDcComboBox.SelectedItem = snapshot.PowerMode is { } powerDc ? PowerModes.FirstOrDefault(x => x.Mode == powerDc.Dc) : null;
            ResolutionComboBox.SelectedItem = ResolutionItems.FirstOrDefault(x => x.Width == snapshot.Resolution?.Width && x.Height == snapshot.Resolution?.Height);
            if (!preserveDirtyTdpDraft || !_tdpDraftDirty)
            {
                _acPl1 = snapshot.Tdp.Ac.Pl1Watts; _acPl2 = snapshot.Tdp.Ac.Pl2Watts; _dcPl1 = snapshot.Tdp.Dc.Pl1Watts; _dcPl2 = snapshot.Tdp.Dc.Pl2Watts;
                ConfigureSlider(AcPl1Slider, snapshot.Limits?.Pl1MinimumWatts, snapshot.Limits?.Pl2MaximumWatts, _acPl1.Value); ConfigureSlider(AcPl2Slider, snapshot.Limits?.Pl2MinimumWatts, snapshot.Limits?.Pl2MaximumWatts, _acPl2.Value); ConfigureSlider(DcPl1Slider, snapshot.Limits?.Pl1MinimumWatts, snapshot.Limits?.Pl2MaximumWatts, _dcPl1.Value); ConfigureSlider(DcPl2Slider, snapshot.Limits?.Pl2MinimumWatts, snapshot.Limits?.Pl2MaximumWatts, _dcPl2.Value);
                SetTdpText();
            }
        }
        finally { _suppressEvents = _suppressTdpEvents = _suppressFpsEvents = _suppressFeatureEvents = false; }
        CpuBoostAcCard.Visibility = CpuBoostDcCard.Visibility = snapshot.CpuBoost.Enabled ? Visibility.Visible : Visibility.Collapsed; TdpAcCard.Visibility = TdpDcCard.Visibility = snapshot.Tdp.Enabled ? Visibility.Visible : Visibility.Collapsed; PowerModeAcCard.Visibility = PowerModeDcCard.Visibility = snapshot.PowerMode?.Enabled == true ? Visibility.Visible : Visibility.Collapsed; FpsAcCard.Visibility = FpsDcCard.Visibility = snapshot.FpsLimit?.Enabled == true ? Visibility.Visible : Visibility.Collapsed;
        SetEditorsEnabled(snapshot.Exists && snapshot.Enabled && snapshot.PersistenceWritable); ResolutionComboBox.IsEnabled = snapshot.PersistenceWritable; RenderProfileStatus(snapshot);
    }
    private void RenderProfileStatus(FrontendXboxGameProfileSnapshot snapshot) { if (!snapshot.PersistenceWritable) { ShowError("Profile settings could not be loaded, so changes are disabled to avoid overwriting the existing profile.", null); return; } if (snapshot.Exists && snapshot.Enabled && snapshot.Limits is null) { ProfileInfoBar.Severity = InfoBarSeverity.Warning; ProfileInfoBar.Message = "TDP Control is unavailable on this device."; ProfileInfoBar.IsOpen = true; return; } ClearError(); }
    private void SetEditorsEnabled(bool enabled) { var cpu = enabled && _snapshot?.CpuBoost.Enabled == true; CpuBoostAcComboBox.IsEnabled = CpuBoostDcComboBox.IsEnabled = cpu; var powerModeEditable = enabled && _snapshot?.PowerMode is { Enabled: true }; PowerModeAcComboBox.IsEnabled = PowerModeDcComboBox.IsEnabled = powerModeEditable; var tdp = enabled && _snapshot?.Tdp.Enabled == true && _snapshot.Limits is not null; foreach (var slider in new[] { AcPl1Slider, AcPl2Slider, DcPl1Slider, DcPl2Slider }) slider.IsEnabled = tdp; var fps = enabled && _snapshot?.FpsLimit?.Available == true && FpsEnabledToggle.IsOn; AcFpsSlider.IsEnabled = DcFpsSlider.IsEnabled = fps; }
    private async void FpsEnabledToggle_Toggled(object sender, RoutedEventArgs e) { if (!_active || _suppressFpsEvents || _frontend is null || _selectedGame is null) return; var key = _selectedGame.Key; try { var result = await _frontend.SetXboxGameProfileFpsLimitEnabledAsync(key, FpsEnabledToggle.IsOn); if (IsCurrentProfileResponse(_active, _selectedGame?.Key, key, result.Snapshot.Key)) Render(result.Snapshot); if (!result.Succeeded) ShowError(result.FailureMessage ?? "Intel FPS Limit could not be updated.", null); } catch (Exception ex) { await RestoreSelectedAfterMutationFailureAsync(key, "Intel FPS Limit could not be updated.", ex); } }
    private async void CpuBoostEnabledToggle_Toggled(object sender, RoutedEventArgs e) => await MutateFeatureEnabledAsync("CPU Boost", CpuBoostEnabledToggle.IsOn, (id, value) => _frontend!.SetXboxGameProfileCpuBoostEnabledAsync(id, value));
    private async void TdpEnabledToggle_Toggled(object sender, RoutedEventArgs e) => await MutateFeatureEnabledAsync("TDP Control", TdpEnabledToggle.IsOn, (id, value) => _frontend!.SetXboxGameProfileTdpEnabledAsync(id, value));
    private async void PowerModeEnabledToggle_Toggled(object sender, RoutedEventArgs e) => await MutateFeatureEnabledAsync("Windows Power Mode", PowerModeEnabledToggle.IsOn, (id, value) => _frontend!.SetXboxGameProfilePowerModeEnabledAsync(id, value));
    private async Task MutateFeatureEnabledAsync(string feature, bool enabled, Func<string, bool, Task<FrontendXboxGameProfileMutationResult>> mutation)
    {
        if (!_active || _suppressFeatureEvents || _frontend is null || _selectedGame is null || _snapshot?.Enabled != true) return;
        var key = _selectedGame.Key;
        try { var result = await mutation(key, enabled); if (IsCurrentProfileResponse(_active, _selectedGame?.Key, key, result.Snapshot.Key)) Render(result.Snapshot); if (!result.Succeeded) ShowError(result.FailureMessage ?? $"{feature} could not be updated.", null); }
        catch (Exception exception) { await RestoreSelectedAfterMutationFailureAsync(key, $"{feature} could not be updated.", exception); }
    }
    private void FpsSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e) { if (!_active || _suppressFpsEvents || _frontend is null || _selectedGame is null) return; var value = Math.Clamp((int)Math.Round(e.NewValue), 40, 120); var ac = ReferenceEquals(sender, AcFpsSlider); if (ac) { _acFpsDraft = value; AcFpsText.Text = $"{value} FPS"; _acFpsGeneration++; _acFpsDebounce?.Cancel(); _acFpsDebounce = new(); _ = SubmitFpsAfterDelayAsync(true, value, _acFpsGeneration, _acFpsDebounce.Token); } else { _dcFpsDraft = value; DcFpsText.Text = $"{value} FPS"; _dcFpsGeneration++; _dcFpsDebounce?.Cancel(); _dcFpsDebounce = new(); _ = SubmitFpsAfterDelayAsync(false, value, _dcFpsGeneration, _dcFpsDebounce.Token); } }
    private async Task SubmitFpsAfterDelayAsync(bool ac, int value, long generation, CancellationToken token) { string? key = null; try { await Task.Delay(275, token); if ((ac ? generation != _acFpsGeneration : generation != _dcFpsGeneration) || !_active || _frontend is null || _selectedGame is null) return; key = _selectedGame.Key; var result = ac ? await _frontend.SetXboxGameProfileFpsLimitAcAsync(key, value) : await _frontend.SetXboxGameProfileFpsLimitDcAsync(key, value); if (IsCurrentProfileResponse(_active, _selectedGame?.Key, key, result.Snapshot.Key)) { Render(result.Snapshot); if (!result.Succeeded) ShowError(result.FailureMessage ?? "Intel FPS Limit could not be updated.", null); } } catch (OperationCanceledException) { } catch (Exception ex) { if (key is not null) await RestoreSelectedAfterMutationFailureAsync(key, "Intel FPS Limit could not be updated.", ex); } }
    private void CancelFpsDebounce() { _acFpsGeneration++; _dcFpsGeneration++; _acFpsDebounce?.Cancel(); _dcFpsDebounce?.Cancel(); _acFpsDebounce = _dcFpsDebounce = null; }
    private static void ConfigureSlider(Slider slider, int? minimum, int? maximum, int value) { if (minimum is not { } min || maximum is not { } max) { slider.IsEnabled = false; return; } slider.Minimum = min; slider.Maximum = max; slider.StepFrequency = 1; slider.Value = Math.Clamp(value, min, max); }

    private async void ProfileEnabledToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (!_active || _suppressEvents || _frontend is null || _selectedGame is null) return;
        var key = _selectedGame.Key;
        CancelTdpDebounce();
        try { var result = await _frontend.SetXboxGameProfileEnabledAsync(key, ProfileEnabledToggle.IsOn, _selectedGame.DisplayName); if (!IsCurrentProfileResponse(_active, _selectedGame?.Key, key, result.Snapshot.Key)) return; Render(result.Snapshot); if (!result.Succeeded) ShowError(result.FailureMessage ?? "Profile could not be updated.", null); } catch (Exception exception) { await RestoreSelectedAfterMutationFailureAsync(key, "Profile could not be updated because the Runtime connection was interrupted.", exception); }
    }
    private async void CpuBoostAcComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e) => await MutateCpuAsync(true);
    private async void CpuBoostDcComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e) => await MutateCpuAsync(false);
    private async void PowerModeAcComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e) => await MutatePowerModeAsync(true);
    private async void PowerModeDcComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e) => await MutatePowerModeAsync(false);
    private async Task MutatePowerModeAsync(bool ac)
    {
        if (!_active || _suppressEvents || _frontend is null || _selectedGame is null) return;
        var item = (ac ? PowerModeAcComboBox : PowerModeDcComboBox).SelectedItem as PowerModeItem; if (item is null) return;
        var key = _selectedGame.Key;
        try { var result = ac ? await _frontend.SetXboxGameProfilePowerModeAcAsync(key, item.Mode) : await _frontend.SetXboxGameProfilePowerModeDcAsync(key, item.Mode); if (IsCurrentProfileResponse(_active, _selectedGame?.Key, key, result.Snapshot.Key)) Render(result.Snapshot); if (!result.Succeeded) ShowError(result.FailureMessage ?? "Power Mode could not be updated.", null); } catch (Exception exception) { await RestoreSelectedAfterMutationFailureAsync(key, "Power Mode could not be updated.", exception); }
    }
    private async void ResolutionComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_active || _suppressEvents || _frontend is null || _selectedGame is null || ResolutionComboBox.SelectedItem is not ResolutionItem item) return;
        var key = _selectedGame.Key;
        try { var result = await _frontend.SetXboxGameProfileResolutionAsync(key, item.Width is { } w && item.Height is { } h ? new(w, h) : null, _selectedGame.DisplayName); if (!IsCurrentProfileResponse(_active, _selectedGame?.Key, key, result.Snapshot.Key)) return; Render(result.Snapshot); if (!result.Succeeded) ShowError(result.FailureMessage ?? "Display resolution could not be updated.", null); }
        catch (Exception exception) { await RestoreSelectedAfterMutationFailureAsync(key, "Display resolution could not be updated.", exception); }
    }
    private async Task MutateCpuAsync(bool acSide)
    {
        if (!_active || _suppressEvents || _frontend is null || _selectedGame is null) return;
        var mode = (acSide ? CpuBoostAcComboBox : CpuBoostDcComboBox).SelectedItem as CpuBoostModeItem;
        if (mode is null) return;
        var key = _selectedGame.Key;
        try { var result = acSide ? await _frontend.SetXboxGameProfileCpuBoostAcAsync(key, mode.Mode) : await _frontend.SetXboxGameProfileCpuBoostDcAsync(key, mode.Mode); if (!IsCurrentProfileResponse(_active, _selectedGame?.Key, key, result.Snapshot.Key)) return; Render(result.Snapshot); if (!result.Succeeded) ShowError(result.FailureMessage ?? "CPU Boost could not be updated.", null); } catch (Exception exception) { await RestoreSelectedAfterMutationFailureAsync(key, "CPU Boost could not be updated.", exception); }
    }
    private void TdpSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (!_active || _suppressTdpEvents || _selectedGame is null) return; var value = (int)Math.Round(e.NewValue); var slider = (Slider)sender; var ac = ReferenceEquals(slider, AcPl1Slider) || ReferenceEquals(slider, AcPl2Slider); var pl1Edited = ReferenceEquals(slider, AcPl1Slider) || ReferenceEquals(slider, DcPl1Slider); var pl1 = ac ? _acPl1 : _dcPl1; var pl2 = ac ? _acPl2 : _dcPl2; if (pl1Edited) { if (_snapshot?.Limits is { } currentLimits) value = Math.Min(value, currentLimits.Pl1MaximumWatts); pl1 = value; } else pl2 = value;
        if (_snapshot?.Limits is { } limits) { var adjusted = DevicePage.TdpDraftPolicy.AdjustAfterEdit(pl1Edited, pl1, pl2, limits); pl1 = adjusted.Pl1Watts; pl2 = adjusted.Pl2Watts; _suppressTdpEvents = true; try { (ac ? AcPl1Slider : DcPl1Slider).Value = pl1 ?? 0; (ac ? AcPl2Slider : DcPl2Slider).Value = pl2 ?? 0; } finally { _suppressTdpEvents = false; } }
        if (ac) { _acPl1 = pl1; _acPl2 = pl2; } else { _dcPl1 = pl1; _dcPl2 = pl2; } _tdpDraftDirty = true; SetTdpText(); _tdpGeneration++; var generation = _tdpGeneration; _tdpDebounce?.Cancel(); _tdpDebounce = new CancellationTokenSource(); _ = SubmitTdpAfterDelayAsync(generation, _tdpDebounce.Token);
    }
    private async Task SubmitTdpAfterDelayAsync(long generation, CancellationToken token)
    {
        string? key = null;
        try { await Task.Delay(300, token); if (!DevicePage.TdpDraftPolicy.CanSubmitDebouncedEdit(generation, Volatile.Read(ref _tdpGeneration), ProfileEnabledToggle.IsOn) || !_active || _frontend is null || _selectedGame is null || _acPl1 is not { } ac1 || _acPl2 is not { } ac2 || _dcPl1 is not { } dc1 || _dcPl2 is not { } dc2) return; key = _selectedGame.Key; var result = await _frontend.SetXboxGameProfileTdpAsync(key, new(true, new(ac1, ac2), new(dc1, dc2))); if (!IsCurrentProfileResponse(_active, _selectedGame?.Key, key, result.Snapshot.Key)) return; var preserveDraft = ShouldPreserveDirtyTdpDraft(_tdpDraftDirty, generation, _tdpGeneration); if (!preserveDraft) _tdpDraftDirty = false; Render(result.Snapshot, preserveDraft); if (!result.Succeeded) ShowError(result.FailureMessage ?? "TDP could not be updated.", null); } catch (OperationCanceledException) { } catch (Exception exception) { if (key is not null) await RestoreSelectedAfterMutationFailureAsync(key, "TDP could not be updated.", exception); }
    }
    private void SetTdpText() { AcPl1Value.Text = _acPl1 is { } x ? $"{x} W" : "— W"; AcPl2Value.Text = _acPl2 is { } y ? $"{y} W" : "— W"; DcPl1Value.Text = _dcPl1 is { } z ? $"{z} W" : "— W"; DcPl2Value.Text = _dcPl2 is { } w ? $"{w} W" : "— W"; }
    private void CancelTdpDebounce() { _tdpGeneration++; _tdpDebounce?.Cancel(); _tdpDebounce = null; }
    private void ShowError(string message, Exception? exception) { ProfileInfoBar.Severity = InfoBarSeverity.Error; ProfileInfoBar.Message = message; ProfileInfoBar.IsOpen = true; if (exception is not null) AppLog.Warn("Profile", message, exception); }
    private void ShowInfo(string message) { ProfileInfoBar.Severity = InfoBarSeverity.Informational; ProfileInfoBar.Message = message; ProfileInfoBar.IsOpen = true; }
    private void ClearError() => ProfileInfoBar.IsOpen = false;
    internal static bool IsCurrentProfileResponse(bool active, string? selectedKey, string requestedKey, string responseKey) => active && selectedKey == requestedKey && responseKey == requestedKey;
    internal static bool ShouldPreserveDirtyTdpDraft(bool dirty, long submittedGeneration, long currentGeneration) => dirty && submittedGeneration != currentGeneration;
    internal static IReadOnlyList<FrontendXboxGameCatalogEntry> FilterAndSort(IEnumerable<FrontendXboxGameCatalogEntry> games, string? query)
    {
        var search = query?.Trim() ?? string.Empty;
        return games.Where(game => search.Length == 0 || game.DisplayName.Contains(search, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(game => game.Favorite)
            .ThenBy(game => game.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(game => game.Key, StringComparer.Ordinal)
            .ToArray();
    }
    internal static bool IsCurrentScan(bool active, CancellationTokenSource? current, CancellationTokenSource request)
        => active && ReferenceEquals(current, request) && !request.IsCancellationRequested;
    private void CancelScan() { var scan = _scanCancellation; _scanCancellation = null; scan?.Cancel(); }
    private void CancelCapture() { var capture = _captureCancellation; _captureCancellation = null; capture?.Cancel(); }
    private async Task RestoreSelectedAfterMutationFailureAsync(string key, string message, Exception exception)
    {
        ShowError(message, exception);
        if (_frontend is null || _selectedGame?.Key != key) return;
        try { var snapshot = await _frontend.CaptureXboxGameProfileAsync(key); if (IsCurrentProfileResponse(_active, _selectedGame?.Key, key, snapshot.Key)) { Render(snapshot); ShowError(message, null); } } catch { }
    }
    private sealed record CpuBoostModeItem(CpuBoostMode Mode, string Label);
    private static readonly PowerModeItem[] PowerModes = [new(WindowsPowerMode.BestPowerEfficiency, "Best power efficiency"), new(WindowsPowerMode.Balanced, "Balanced"), new(WindowsPowerMode.BestPerformance, "Best performance")];
    private sealed record PowerModeItem(WindowsPowerMode Mode, string Label);
    private sealed record ResolutionItem(int? Width, int? Height, string Label);
    private sealed record GameCardItem(FrontendXboxGameCatalogEntry Game)
    {
        public string Key => Game.Key;
        public string DisplayName => Game.DisplayName;
        public bool Favorite => Game.Favorite;
        public string FavoriteGlyph => Game.Favorite ? "★" : "☆";
    }
}
