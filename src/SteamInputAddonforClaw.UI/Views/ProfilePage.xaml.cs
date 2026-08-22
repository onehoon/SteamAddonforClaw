using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using SteamInputAddonforClaw.Contracts.DeviceProfiles;
using SteamInputAddonforClaw.Contracts.Frontend;

namespace SteamInputAddonforClaw.Views;

public sealed partial class ProfilePage : UserControl
{
    private IAddonFrontendControl? _frontend;
    private bool _active, _suppressEvents, _suppressTdpEvents;
    private IReadOnlyList<FrontendProfileGameCatalogEntry> _catalog = [];
    private FrontendProfileGameCatalogEntry? _selectedGame;
    private FrontendGameProfileSnapshot? _snapshot;
    private int? _acPl1, _acPl2, _dcPl1, _dcPl2;
    private CancellationTokenSource? _tdpDebounce;
    private long _tdpGeneration;
    private bool _tdpDraftDirty;
    private static readonly CpuBoostModeItem[] Modes = [new(CpuBoostMode.Disabled, "Disabled"), new(CpuBoostMode.Enabled, "Enabled"), new(CpuBoostMode.Aggressive, "Aggressive"), new(CpuBoostMode.EfficientEnabled, "Efficient Enabled"), new(CpuBoostMode.EfficientAggressive, "Efficient Aggressive"), new(CpuBoostMode.AggressiveAtGuaranteed, "Aggressive At Guaranteed"), new(CpuBoostMode.EfficientAggressiveAtGuaranteed, "Efficient Aggressive At Guaranteed")];

    public ProfilePage()
    {
        InitializeComponent(); CpuBoostAcComboBox.ItemsSource = Modes; CpuBoostDcComboBox.ItemsSource = Modes; SetEditorsEnabled(false);
    }

    internal void Initialize(IAddonFrontendControl frontend) => _frontend = frontend;
    internal void Activate() { _active = true; if (_frontend is not null) _frontend.StateInvalidated += OnStateInvalidated; if (_selectedGame is not null) _ = CaptureSelectedAsync(_selectedGame.AppId); }
    internal void Deactivate() { _active = false; _frontend?.StateInvalidated -= OnStateInvalidated; }
    private void OnStateInvalidated(object? sender, EventArgs e) { if (_active && _selectedGame is not null) DispatcherQueue.TryEnqueue(() => _ = CaptureSelectedAsync(_selectedGame.AppId)); }

    private async void RefreshGamesButton_Click(object sender, RoutedEventArgs e)
    {
        if (_frontend is null) return;
        try
        {
            var selected = _selectedGame?.AppId; _catalog = await _frontend.ScanProfileGamesAsync(); GameSelector.ItemsSource = _catalog;
            _selectedGame = selected is { } id ? _catalog.FirstOrDefault(x => x.AppId == id) : null;
            if (_selectedGame is null) { ClearSelection(); ClearError(); } else { GameSelector.Text = _selectedGame.Name; await CaptureSelectedAsync(_selectedGame.AppId); }
        }
        catch (Exception exception) { ShowError("Game catalog could not be refreshed.", exception); }
    }

    private void GameSelector_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason != AutoSuggestionBoxTextChangeReason.UserInput) return;
        var query = sender.Text.Trim(); sender.ItemsSource = string.IsNullOrEmpty(query) ? _catalog : _catalog.Where(x => x.Name.Contains(query, StringComparison.OrdinalIgnoreCase) || x.AppId.ToString().Contains(query, StringComparison.OrdinalIgnoreCase)).ToArray();
    }
    private void GameSelector_SuggestionChosen(AutoSuggestBox sender, AutoSuggestBoxSuggestionChosenEventArgs args) { if (args.SelectedItem is FrontendProfileGameCatalogEntry game) _ = SelectGameAsync(game); }
    private void GameSelector_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args) { if (args.ChosenSuggestion is FrontendProfileGameCatalogEntry game) _ = SelectGameAsync(game); else if (_catalog.FirstOrDefault(x => string.Equals(x.Name, sender.Text, StringComparison.OrdinalIgnoreCase) || x.AppId.ToString() == sender.Text) is { } exact) _ = SelectGameAsync(exact); }
    private async Task SelectGameAsync(FrontendProfileGameCatalogEntry game) { CancelTdpDebounce(); _tdpDraftDirty = false; _selectedGame = game; GameSelector.Text = game.Name; BeginProfileLoad(game); await CaptureSelectedAsync(game.AppId, preserveDirtyTdpDraft: false); }
    private void BeginProfileLoad(FrontendProfileGameCatalogEntry game) { _snapshot = null; _suppressEvents = _suppressTdpEvents = true; try { SelectedGameText.Text = $"{game.Name} ({game.AppId})"; ProfileEnabledToggle.Visibility = Visibility.Visible; ProfileEnabledToggle.IsOn = false; ProfileEnabledToggle.IsEnabled = false; CpuBoostAcComboBox.SelectedItem = null; CpuBoostDcComboBox.SelectedItem = null; _acPl1 = _acPl2 = _dcPl1 = _dcPl2 = null; SetTdpText(); } finally { _suppressEvents = _suppressTdpEvents = false; } SetEditorsEnabled(false); }
    private async Task CaptureSelectedAsync(uint appId, bool preserveDirtyTdpDraft = true) { if (_frontend is null) return; try { var snapshot = await _frontend.CaptureGameProfileAsync(appId); if (!IsCurrentProfileResponse(_selectedGame?.AppId, appId)) return; Render(snapshot, preserveDirtyTdpDraft && _tdpDraftDirty); } catch (Exception exception) { if (IsCurrentProfileResponse(_selectedGame?.AppId, appId)) ShowError("Profile settings could not be loaded.", exception); } }
    private void ClearSelection() { CancelTdpDebounce(); _tdpDraftDirty = false; _selectedGame = null; _snapshot = null; GameSelector.Text = string.Empty; SelectedGameText.Text = "Click Refresh, then select a game."; ProfileEnabledToggle.Visibility = Visibility.Collapsed; SetEditorsEnabled(false); }

    private void Render(FrontendGameProfileSnapshot snapshot, bool preserveDirtyTdpDraft = false)
    {
        _snapshot = snapshot; _suppressEvents = _suppressTdpEvents = true;
        try
        {
            SelectedGameText.Text = $"{_selectedGame?.Name ?? snapshot.DisplayName ?? "Game"} ({snapshot.AppId})"; ProfileEnabledToggle.Visibility = Visibility.Visible; ProfileEnabledToggle.IsOn = snapshot.Enabled; ProfileEnabledToggle.IsEnabled = snapshot.PersistenceWritable;
            CpuBoostAcComboBox.SelectedItem = Modes.FirstOrDefault(x => x.Mode == snapshot.CpuBoost.Ac); CpuBoostDcComboBox.SelectedItem = Modes.FirstOrDefault(x => x.Mode == snapshot.CpuBoost.Dc);
            if (!preserveDirtyTdpDraft || !_tdpDraftDirty)
            {
                _acPl1 = snapshot.Tdp.Ac.Pl1Watts; _acPl2 = snapshot.Tdp.Ac.Pl2Watts; _dcPl1 = snapshot.Tdp.Dc.Pl1Watts; _dcPl2 = snapshot.Tdp.Dc.Pl2Watts;
                ConfigureSlider(AcPl1Slider, snapshot.Limits?.Pl1MinimumWatts, snapshot.Limits?.Pl2MaximumWatts, _acPl1.Value); ConfigureSlider(AcPl2Slider, snapshot.Limits?.Pl2MinimumWatts, snapshot.Limits?.Pl2MaximumWatts, _acPl2.Value); ConfigureSlider(DcPl1Slider, snapshot.Limits?.Pl1MinimumWatts, snapshot.Limits?.Pl2MaximumWatts, _dcPl1.Value); ConfigureSlider(DcPl2Slider, snapshot.Limits?.Pl2MinimumWatts, snapshot.Limits?.Pl2MaximumWatts, _dcPl2.Value);
                SetTdpText();
            }
        }
        finally { _suppressEvents = _suppressTdpEvents = false; }
        SetEditorsEnabled(snapshot.Exists && snapshot.Enabled && snapshot.PersistenceWritable); RenderProfileStatus(snapshot);
    }
    private void RenderProfileStatus(FrontendGameProfileSnapshot snapshot) { if (!snapshot.PersistenceWritable) { ShowError("Profile settings could not be loaded, so changes are disabled to avoid overwriting the existing profile.", null); return; } if (snapshot.Exists && snapshot.Enabled && snapshot.Limits is null) { ProfileInfoBar.Severity = InfoBarSeverity.Warning; ProfileInfoBar.Message = "TDP Control is unavailable on this device."; ProfileInfoBar.IsOpen = true; return; } ClearError(); }
    private void SetEditorsEnabled(bool enabled) { CpuBoostAcComboBox.IsEnabled = CpuBoostDcComboBox.IsEnabled = enabled; var tdp = enabled && _snapshot?.Limits is not null; foreach (var slider in new[] { AcPl1Slider, AcPl2Slider, DcPl1Slider, DcPl2Slider }) slider.IsEnabled = tdp; }
    private static void ConfigureSlider(Slider slider, int? minimum, int? maximum, int value) { if (minimum is not { } min || maximum is not { } max) { slider.IsEnabled = false; return; } slider.Minimum = min; slider.Maximum = max; slider.StepFrequency = 1; slider.Value = Math.Clamp(value, min, max); }

    private async void ProfileEnabledToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_suppressEvents || _frontend is null || _selectedGame is null) return;
        var appId = _selectedGame.AppId;
        CancelTdpDebounce();
        try { var result = await _frontend.SetGameProfileEnabledAsync(appId, ProfileEnabledToggle.IsOn, _selectedGame.Name); if (!IsCurrentProfileResponse(_selectedGame?.AppId, appId)) return; Render(result.Snapshot); if (!result.Succeeded) ShowError(result.FailureMessage ?? "Profile could not be updated.", null); } catch (Exception exception) { await RestoreSelectedAfterMutationFailureAsync(appId, "Profile could not be updated because the Runtime connection was interrupted.", exception); }
    }
    private async void CpuBoostAcComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e) => await MutateCpuAsync();
    private async void CpuBoostDcComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e) => await MutateCpuAsync();
    private async Task MutateCpuAsync()
    {
        if (_suppressEvents || _frontend is null || _selectedGame is null || CpuBoostAcComboBox.SelectedItem is not CpuBoostModeItem ac || CpuBoostDcComboBox.SelectedItem is not CpuBoostModeItem dc) return;
        var appId = _selectedGame.AppId;
        try { var result = await _frontend.SetGameProfileCpuBoostAsync(appId, ac.Mode, dc.Mode); if (!IsCurrentProfileResponse(_selectedGame?.AppId, appId)) return; Render(result.Snapshot); if (!result.Succeeded) ShowError(result.FailureMessage ?? "CPU Boost could not be updated.", null); } catch (Exception exception) { await RestoreSelectedAfterMutationFailureAsync(appId, "CPU Boost could not be updated.", exception); }
    }
    private void TdpSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (_suppressTdpEvents || _selectedGame is null) return; var value = (int)Math.Round(e.NewValue); var slider = (Slider)sender; var ac = ReferenceEquals(slider, AcPl1Slider) || ReferenceEquals(slider, AcPl2Slider); var pl1Edited = ReferenceEquals(slider, AcPl1Slider) || ReferenceEquals(slider, DcPl1Slider); var pl1 = ac ? _acPl1 : _dcPl1; var pl2 = ac ? _acPl2 : _dcPl2; if (pl1Edited) { if (_snapshot?.Limits is { } currentLimits) value = Math.Min(value, currentLimits.Pl1MaximumWatts); pl1 = value; } else pl2 = value;
        if (_snapshot?.Limits is { } limits) { var adjusted = DevicePage.TdpDraftPolicy.AdjustAfterEdit(pl1Edited, pl1, pl2, limits); pl1 = adjusted.Pl1Watts; pl2 = adjusted.Pl2Watts; _suppressTdpEvents = true; try { (ac ? AcPl1Slider : DcPl1Slider).Value = pl1 ?? 0; (ac ? AcPl2Slider : DcPl2Slider).Value = pl2 ?? 0; } finally { _suppressTdpEvents = false; } }
        if (ac) { _acPl1 = pl1; _acPl2 = pl2; } else { _dcPl1 = pl1; _dcPl2 = pl2; } _tdpDraftDirty = true; SetTdpText(); _tdpGeneration++; var generation = _tdpGeneration; _tdpDebounce?.Cancel(); _tdpDebounce = new CancellationTokenSource(); _ = SubmitTdpAfterDelayAsync(generation, _tdpDebounce.Token);
    }
    private async Task SubmitTdpAfterDelayAsync(long generation, CancellationToken token)
    {
        uint? appId = null;
        try { await Task.Delay(300, token); if (!DevicePage.TdpDraftPolicy.CanSubmitDebouncedEdit(generation, Volatile.Read(ref _tdpGeneration), ProfileEnabledToggle.IsOn) || _frontend is null || _selectedGame is null || _acPl1 is not { } ac1 || _acPl2 is not { } ac2 || _dcPl1 is not { } dc1 || _dcPl2 is not { } dc2) return; appId = _selectedGame.AppId; var result = await _frontend.SetGameProfileTdpAsync(appId.Value, new(new(ac1, ac2), new(dc1, dc2))); if (!IsCurrentProfileResponse(_selectedGame?.AppId, appId.Value)) return; var preserveDraft = ShouldPreserveDirtyTdpDraft(_tdpDraftDirty, generation, _tdpGeneration); if (!preserveDraft) _tdpDraftDirty = false; Render(result.Snapshot, preserveDraft); if (!result.Succeeded) ShowError(result.FailureMessage ?? "TDP could not be updated.", null); } catch (OperationCanceledException) { } catch (Exception exception) { if (appId is { } targetAppId) await RestoreSelectedAfterMutationFailureAsync(targetAppId, "TDP could not be updated.", exception); }
    }
    private void SetTdpText() { AcPl1Value.Text = _acPl1 is { } x ? $"{x} W" : "— W"; AcPl2Value.Text = _acPl2 is { } y ? $"{y} W" : "— W"; DcPl1Value.Text = _dcPl1 is { } z ? $"{z} W" : "— W"; DcPl2Value.Text = _dcPl2 is { } w ? $"{w} W" : "— W"; }
    private void CancelTdpDebounce() { _tdpGeneration++; _tdpDebounce?.Cancel(); _tdpDebounce = null; }
    private void ShowError(string message, Exception? exception) { ProfileInfoBar.Severity = InfoBarSeverity.Error; ProfileInfoBar.Message = message; ProfileInfoBar.IsOpen = true; if (exception is not null) AppLog.Warn("Profile", message, exception); }
    private void ClearError() => ProfileInfoBar.IsOpen = false;
    internal static bool IsCurrentProfileResponse(uint? selectedAppId, uint responseAppId) => selectedAppId == responseAppId;
    internal static bool ShouldPreserveDirtyTdpDraft(bool dirty, long submittedGeneration, long currentGeneration) => dirty && submittedGeneration != currentGeneration;
    private async Task RestoreSelectedAfterMutationFailureAsync(uint appId, string message, Exception exception)
    {
        ShowError(message, exception);
        if (_frontend is null || _selectedGame?.AppId != appId) return;
        try { var snapshot = await _frontend.CaptureGameProfileAsync(appId); if (_selectedGame?.AppId == appId) Render(snapshot); } catch { }
    }
    private sealed record CpuBoostModeItem(CpuBoostMode Mode, string Label);
}
