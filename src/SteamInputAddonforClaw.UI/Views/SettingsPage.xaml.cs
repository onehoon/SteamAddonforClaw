using CommunityToolkit.WinUI.Controls;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Controls.Primitives;
using SteamInputAddonforClaw.Contracts.Frontend;

namespace SteamInputAddonforClaw.Views;

public sealed partial class SettingsPage : UserControl
{
    private IAddonFrontendControl? _frontend;
    private FrontendUpdateSnapshot _updateSnapshot = FrontendUpdateSnapshot.Unavailable;
    private int _updateOperationInProgress;
    private int _enterBiosOperationInProgress;
    private FrontendSteamFseSnapshot _steamFseSnapshot = FrontendSteamFseSnapshot.Unavailable("Steam Big Picture Full Screen Experience is unavailable.");
    private int _steamFseMutationInProgress;
    private bool _applyingSteamFseState;
    private bool _applyingQuickSettingsPowerSourcePreference;
    private bool _lastKnownQuickSettingsCurrentPowerSourceOnly;
    private FrontendClawHudSnapshot _clawHudSnapshot = new(false, FrontendClawHudRuntimeState.Disabled, "Off", null, null, null);
    private int _clawHudOperationInProgress;
    private bool _applyingClawHudState;
    private Task _opacityPreviewTail = Task.CompletedTask;
    private readonly object _opacityPreviewGate = new();
    private int _opacityCommitQueued;
    public event EventHandler? DeveloperMenuRequested;

    public SettingsPage()
    {
        InitializeComponent();
    }

    internal void Initialize(FrontendBootstrapSnapshot bootstrap, IAddonFrontendControl frontend)
    {
        _frontend = frontend ?? throw new ArgumentNullException(nameof(frontend));
        DeveloperMenuCard.Visibility = GetDeveloperMenuCardVisibility(bootstrap.Settings.DeveloperMenuEnabled);
        _lastKnownQuickSettingsCurrentPowerSourceOnly = bootstrap.Settings.QuickSettingsCurrentPowerSourceOnly;
        SetQuickSettingsPowerSourceToggle(_lastKnownQuickSettingsCurrentPowerSourceOnly);
        _ = RefreshAppUpdateAsync();
        _ = RefreshSteamFseAsync();
        _ = RefreshClawHudAsync();
    }

    internal void RequestAppUpdateRefresh() => _ = RefreshAppUpdateAsync();
    internal void RequestSteamFseRefresh() => _ = RefreshSteamFseAsync();
    internal void RequestClawHudRefresh() => _ = RefreshClawHudAsync();

    private async Task RefreshClawHudAsync()
    {
        if (_frontend is null || Volatile.Read(ref _clawHudOperationInProgress) != 0) return;
        try { RenderClawHud(await _frontend.CaptureClawHudAsync().ConfigureAwait(true)); }
        catch (Exception exception) { AppLog.Warn("ClawHUD.UI", "Main UI ClawHUD state refresh failed.", exception); }
    }

    private void RenderClawHud(FrontendClawHudSnapshot snapshot)
    {
        _clawHudSnapshot = snapshot;
        _applyingClawHudState = true;
        try
        {
            ClawHudEnabledToggleSwitch.IsOn = snapshot.DesiredEnabled;
            if (snapshot.Settings is { } settings)
            {
                ClawHudDisplayModeComboBox.SelectedIndex = settings.DisplayMode == FrontendClawHudDisplayMode.Always ? 0 : 1;
                ClawHudSizeComboBox.SelectedIndex = settings.HudSizeOffset + 2;
                ClawHudFontComboBox.SelectedIndex = settings.Font == FrontendClawHudFont.Unispace ? 0 : 1;
                ClawHudAlignmentComboBox.SelectedIndex = (int)settings.Alignment;
                ClawHudBackgroundComboBox.SelectedIndex = settings.BackgroundMode == FrontendClawHudBackgroundMode.FullWidth ? 0 : 1;
                ClawHudOpacitySlider.Value = settings.BackgroundOpacityPercent;
                ClawHudIntelVrrToggleSwitch.IsOn = settings.IntelVrrRangeFixEnabled;
                ClawHudVrrResultText.Text = settings.IntelVrrLastResult is { } result
                    ? $"Intel VRR: {result.Status}\nPanel: {result.PanelName}\nRange: {result.RangeBefore} → {result.RangeAfter}\n{result.Message}"
                    : string.Empty;
            }
            else
            {
                ClawHudDisplayModeComboBox.SelectedIndex = -1;
                ClawHudSizeComboBox.SelectedIndex = -1;
                ClawHudFontComboBox.SelectedIndex = -1;
                ClawHudAlignmentComboBox.SelectedIndex = -1;
                ClawHudBackgroundComboBox.SelectedIndex = -1;
                ClawHudVrrResultText.Text = string.Empty;
            }
        }
        finally { _applyingClawHudState = false; }

        ClawHudStatusText.Text = snapshot.StatusMessage;
        ClawHudRetryButton.Visibility = snapshot.DesiredEnabled && snapshot.RuntimeState is (FrontendClawHudRuntimeState.Unavailable or FrontendClawHudRuntimeState.StandaloneConflict)
            ? Visibility.Visible : Visibility.Collapsed;
        var nestedEnabled = snapshot.RuntimeState == FrontendClawHudRuntimeState.Ready && snapshot.Settings is not null && Volatile.Read(ref _clawHudOperationInProgress) == 0;
        ClawHudEnabledToggleSwitch.IsEnabled = Volatile.Read(ref _clawHudOperationInProgress) == 0;
        ClawHudRetryButton.IsEnabled = ClawHudEnabledToggleSwitch.IsEnabled;
        ClawHudDisplayModeComboBox.IsEnabled = nestedEnabled;
        ClawHudSizeComboBox.IsEnabled = nestedEnabled;
        ClawHudFontComboBox.IsEnabled = nestedEnabled;
        ClawHudAlignmentComboBox.IsEnabled = nestedEnabled;
        ClawHudBackgroundComboBox.IsEnabled = nestedEnabled;
        ClawHudOpacitySlider.IsEnabled = nestedEnabled;
        ClawHudIntelVrrToggleSwitch.IsEnabled = nestedEnabled;
    }

    private async void ClawHudEnabledToggleSwitch_Toggled(object sender, RoutedEventArgs args)
    {
        if (_applyingClawHudState || _frontend is null || Interlocked.Exchange(ref _clawHudOperationInProgress, 1) != 0) return;
        var desired = ClawHudEnabledToggleSwitch.IsOn;
        RenderClawHud(_clawHudSnapshot with { DesiredEnabled = desired, RuntimeState = desired ? FrontendClawHudRuntimeState.Starting : FrontendClawHudRuntimeState.Disabled, StatusMessage = desired ? "Starting…" : "Off", Settings = null });
        try { RenderClawHud(await _frontend.SetClawHudEnabledAsync(desired).ConfigureAwait(true)); }
        catch (Exception exception)
        {
            AppLog.Warn("ClawHUD.UI", "Main UI ClawHUD enable mutation failed.", exception);
            try { RenderClawHud(await _frontend.CaptureClawHudAsync().ConfigureAwait(true)); }
            catch { RenderClawHud(_clawHudSnapshot with { DesiredEnabled = desired, RuntimeState = FrontendClawHudRuntimeState.Unavailable, StatusMessage = "ClawHUD could not be changed.", Settings = null }); }
        }
        finally { Volatile.Write(ref _clawHudOperationInProgress, 0); RenderClawHud(_clawHudSnapshot); }
    }

    private void ClawHudRetryButton_Click(object sender, RoutedEventArgs args) => _ = RetryClawHudAsync();

    private async Task RetryClawHudAsync()
    {
        if (_frontend is null || Interlocked.Exchange(ref _clawHudOperationInProgress, 1) != 0) return;
        try { RenderClawHud(await _frontend.SetClawHudEnabledAsync(true).ConfigureAwait(true)); }
        catch (Exception exception) { AppLog.Warn("ClawHUD.UI", "Main UI ClawHUD retry failed.", exception); }
        finally { Volatile.Write(ref _clawHudOperationInProgress, 0); await RefreshClawHudAsync().ConfigureAwait(true); }
    }

    private void ClawHudDisplayModeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs args)
    {
        if (!_applyingClawHudState && ClawHudDisplayModeComboBox.SelectedIndex >= 0)
            _ = SubmitClawHudMutationAsync(new(FrontendClawHudMutationKind.DisplayMode, DisplayMode: ClawHudDisplayModeComboBox.SelectedIndex == 0 ? FrontendClawHudDisplayMode.Always : FrontendClawHudDisplayMode.InGameOnly));
    }

    private void ClawHudSizeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs args)
    {
        if (!_applyingClawHudState && ClawHudSizeComboBox.SelectedIndex >= 0)
            _ = SubmitClawHudMutationAsync(new(FrontendClawHudMutationKind.HudSizeOffset, HudSizeOffset: ClawHudSizeComboBox.SelectedIndex - 2));
    }

    private void ClawHudFontComboBox_SelectionChanged(object sender, SelectionChangedEventArgs args)
    {
        if (!_applyingClawHudState && ClawHudFontComboBox.SelectedIndex >= 0)
            _ = SubmitClawHudMutationAsync(new(FrontendClawHudMutationKind.Font, Font: ClawHudFontComboBox.SelectedIndex == 0 ? FrontendClawHudFont.Unispace : FrontendClawHudFont.SegoeUiVariable));
    }

    private void ClawHudAlignmentComboBox_SelectionChanged(object sender, SelectionChangedEventArgs args)
    {
        if (!_applyingClawHudState && ClawHudAlignmentComboBox.SelectedIndex >= 0)
            _ = SubmitClawHudMutationAsync(new(FrontendClawHudMutationKind.Alignment, Alignment: (FrontendClawHudAlignment)ClawHudAlignmentComboBox.SelectedIndex));
    }

    private void ClawHudBackgroundComboBox_SelectionChanged(object sender, SelectionChangedEventArgs args)
    {
        if (!_applyingClawHudState && ClawHudBackgroundComboBox.SelectedIndex >= 0)
            _ = SubmitClawHudMutationAsync(new(FrontendClawHudMutationKind.BackgroundMode, BackgroundMode: ClawHudBackgroundComboBox.SelectedIndex == 0 ? FrontendClawHudBackgroundMode.FullWidth : FrontendClawHudBackgroundMode.ContentWidth));
    }

    private void ClawHudIntelVrrToggleSwitch_Toggled(object sender, RoutedEventArgs args)
    {
        if (!_applyingClawHudState)
            _ = SubmitClawHudMutationAsync(new(FrontendClawHudMutationKind.IntelVrrRangeFixEnabled, IntelVrrRangeFixEnabled: ClawHudIntelVrrToggleSwitch.IsOn));
    }

    private void ClawHudOpacitySlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs args)
    {
        if (_applyingClawHudState || _frontend is null || _clawHudSnapshot.RuntimeState != FrontendClawHudRuntimeState.Ready) return;
        var value = (int)Math.Round(ClawHudOpacitySlider.Value);
        lock (_opacityPreviewGate)
            _opacityPreviewTail = _opacityPreviewTail.ContinueWith(_ => SendClawHudOpacityPreviewAsync(value), TaskScheduler.Default).Unwrap();
    }

    private void ClawHudOpacitySlider_PointerReleased(object sender, PointerRoutedEventArgs args) => QueueClawHudOpacityCommit();
    private void ClawHudOpacitySlider_PointerCaptureLost(object sender, PointerRoutedEventArgs args) => QueueClawHudOpacityCommit();

    private void QueueClawHudOpacityCommit()
    {
        if (Interlocked.Exchange(ref _opacityCommitQueued, 1) != 0) return;
        _ = CommitClawHudOpacityAsync((int)Math.Round(ClawHudOpacitySlider.Value));
    }

    private async Task SendClawHudOpacityPreviewAsync(int value)
    {
        if (_frontend is null) return;
        try
        {
            var result = await _frontend.MutateClawHudSettingAsync(new(FrontendClawHudMutationKind.PreviewOpacity, OpacityPercent: value)).ConfigureAwait(true);
            if (!result.Succeeded) ClawHudStatusText.Text = result.FailureMessage ?? "HUD opacity preview failed.";
        }
        catch (Exception exception) { AppLog.Warn("ClawHUD.UI", "HUD opacity preview failed.", exception); }
    }

    private async Task CommitClawHudOpacityAsync(int value)
    {
        try
        {
            Task previews;
            lock (_opacityPreviewGate) previews = _opacityPreviewTail;
            await previews.ConfigureAwait(true);
            if (_frontend is null) return;
            var result = await _frontend.MutateClawHudSettingAsync(new(FrontendClawHudMutationKind.CommitOpacity, OpacityPercent: value)).ConfigureAwait(true);
            RenderClawHud(result.Snapshot);
            if (!result.Succeeded) ClawHudStatusText.Text = result.FailureMessage ?? "HUD opacity could not be changed.";
        }
        catch (Exception exception) { AppLog.Warn("ClawHUD.UI", "HUD opacity commit failed.", exception); }
        finally { Volatile.Write(ref _opacityCommitQueued, 0); }
    }

    private async Task SubmitClawHudMutationAsync(FrontendClawHudMutationIntent intent)
    {
        if (_frontend is null || Interlocked.Exchange(ref _clawHudOperationInProgress, 1) != 0) return;
        try
        {
            var result = await _frontend.MutateClawHudSettingAsync(intent).ConfigureAwait(true);
            RenderClawHud(result.Snapshot);
            if (!result.Succeeded) ClawHudStatusText.Text = result.FailureMessage ?? "HUD setting could not be changed.";
        }
        catch (Exception exception)
        {
            AppLog.Warn("ClawHUD.UI", "Main UI ClawHUD setting mutation failed.", exception);
            try { RenderClawHud(await _frontend.CaptureClawHudAsync().ConfigureAwait(true)); }
            catch { ClawHudStatusText.Text = "HUD setting could not be verified."; }
        }
        finally { Volatile.Write(ref _clawHudOperationInProgress, 0); RenderClawHud(_clawHudSnapshot); }
    }

    private async Task RefreshAppUpdateAsync()
    {
        if (_frontend is null || Volatile.Read(ref _updateOperationInProgress) != 0) return;
        try { RenderAppUpdate(await _frontend.CaptureAppUpdateAsync().ConfigureAwait(true)); }
        catch (Exception exception) { AppLog.Warn("Update", "Main UI update state refresh failed.", exception); }
    }

    private void RenderAppUpdate(FrontendUpdateSnapshot snapshot)
    {
        _updateSnapshot = snapshot;
        UpdateCard.Description = snapshot.Message;
        UpdateButton.Content = snapshot.CanInstall ? "Install update" : "Check";
        UpdateButton.IsEnabled = snapshot.CanCheck || snapshot.CanInstall;
    }

    private async void EnterBiosButton_Click(object sender, RoutedEventArgs args)
    {
        if (_frontend is null || !TryBeginEnterBiosOperation(ref _enterBiosOperationInProgress)) return;

        EnterBiosCard.IsEnabled = false;
        EnterBiosButton.IsEnabled = false;
        try
        {
            var confirmation = new ContentDialog
            {
                Title = "Enter BIOS?",
                Content = "The device will restart directly into BIOS settings.\n\nSave your work before continuing.",
                PrimaryButtonText = "Restart and Enter BIOS",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = XamlRoot,
            };
            if (await confirmation.ShowAsync() != ContentDialogResult.Primary)
                return;

            var result = await _frontend.RequestEnterBiosAsync().ConfigureAwait(true);
            if (!result.Succeeded)
                await ShowEnterBiosFailureAsync(result.FailureMessage ?? "Enter BIOS could not be started. Try again.").ConfigureAwait(true);
        }
        catch (Exception exception)
        {
            AppLog.Warn("EnterBios", "Enter BIOS UI action failed.", exception);
            await ShowEnterBiosFailureAsync("Enter BIOS could not be started. Try again.").ConfigureAwait(true);
        }
        finally
        {
            EnterBiosButton.IsEnabled = true;
            EnterBiosCard.IsEnabled = true;
            Volatile.Write(ref _enterBiosOperationInProgress, 0);
        }
    }

    private async Task ShowEnterBiosFailureAsync(string message)
    {
        if (XamlRoot is null) return;
        await new ContentDialog
        {
            Title = "Enter BIOS unavailable",
            Content = message,
            CloseButtonText = "OK",
            XamlRoot = XamlRoot,
        }.ShowAsync();
    }

    internal static bool TryBeginEnterBiosOperation(ref int operationInProgress) =>
        Interlocked.Exchange(ref operationInProgress, 1) == 0;

    private async Task RefreshSteamFseAsync()
    {
        if (_frontend is null || Volatile.Read(ref _steamFseMutationInProgress) != 0) return;
        try { RenderSteamFse(await _frontend.CaptureSteamFseAsync().ConfigureAwait(true)); }
        catch (Exception exception)
        {
            AppLog.Warn("SteamFSE", "Main UI SteamFSE state refresh failed.", exception);
            RenderSteamFse(FrontendSteamFseSnapshot.Unavailable("Steam Big Picture Full Screen Experience could not be verified."));
        }
    }

    private void RenderSteamFse(FrontendSteamFseSnapshot snapshot)
    {
        _steamFseSnapshot = snapshot;
        _applyingSteamFseState = true;
        try { SteamFseToggleSwitch.IsOn = snapshot.Enabled; }
        finally { _applyingSteamFseState = false; }
        SteamFseCard.Description = snapshot.Available
            ? "Start Windows directly in Steam Big Picture."
            : snapshot.UnavailableReason ?? "Steam Big Picture Full Screen Experience is unavailable.";
        SteamFseToggleSwitch.IsEnabled = snapshot.Available && Volatile.Read(ref _steamFseMutationInProgress) == 0;
    }

    private async void SteamFseToggleSwitch_Toggled(object sender, RoutedEventArgs args)
    {
        if (_applyingSteamFseState || _frontend is null || Interlocked.Exchange(ref _steamFseMutationInProgress, 1) != 0) return;
        SteamFseToggleSwitch.IsEnabled = false;
        try
        {
            var result = await _frontend.SetSteamFseEnabledAsync(SteamFseToggleSwitch.IsOn).ConfigureAwait(true);
            RenderSteamFse(result.Snapshot);
            if (!result.Succeeded && result.FailureMessage is not null)
                SteamFseCard.Description = result.FailureMessage;
        }
        catch (Exception exception)
        {
            AppLog.Warn("SteamFSE", "Main UI SteamFSE mutation failed.", exception);
            try { RenderSteamFse(await _frontend.CaptureSteamFseAsync().ConfigureAwait(true)); }
            catch (Exception refreshException)
            {
                AppLog.Warn("SteamFSE", "Main UI SteamFSE rollback refresh failed.", refreshException);
                RenderSteamFse(_steamFseSnapshot);
                SteamFseCard.Description = "The Steam Big Picture Full Screen Experience setting could not be changed.";
            }
        }
        finally
        {
            Volatile.Write(ref _steamFseMutationInProgress, 0);
            SteamFseToggleSwitch.IsEnabled = _steamFseSnapshot.Available;
        }
    }

    private async void UpdateButton_Click(object sender, RoutedEventArgs args)
    {
        if (_frontend is null || Interlocked.Exchange(ref _updateOperationInProgress, 1) != 0) return;
        try
        {
            if (_updateSnapshot.CanInstall)
            {
                var result = await _frontend.InstallAppUpdateAsync();
                RenderAppUpdate(result.Snapshot);
                if (!result.Succeeded && result.FailureMessage is not null)
                    UpdateCard.Description = result.FailureMessage;
                return;
            }

            RenderAppUpdate(new(FrontendUpdateState.Checking, "Checking for updates…"));
            RenderAppUpdate(await _frontend.CheckAndDownloadAppUpdateAsync().ConfigureAwait(true));
        }
        catch (Exception exception)
        {
            AppLog.Warn("Update", "Main UI update action failed.", exception);
            RenderAppUpdate(new(FrontendUpdateState.Failed, "The update operation failed. Try again."));
        }
        finally
        {
            Volatile.Write(ref _updateOperationInProgress, 0);
            await RefreshAppUpdateAsync().ConfigureAwait(true);
        }
    }

    private void SetQuickSettingsPowerSourceToggle(bool enabled)
    {
        _applyingQuickSettingsPowerSourcePreference = true;
        try { QuickSettingsPowerSourceToggleSwitch.IsOn = enabled; }
        finally { _applyingQuickSettingsPowerSourcePreference = false; }
    }

    private async void QuickSettingsPowerSourceToggleSwitch_Toggled(object sender, RoutedEventArgs args)
    {
        if (_applyingQuickSettingsPowerSourcePreference || _frontend is null) return;

        try
        {
            var settings = await _frontend.SetQuickSettingsCurrentPowerSourceOnlyAsync(QuickSettingsPowerSourceToggleSwitch.IsOn).ConfigureAwait(true);
            _lastKnownQuickSettingsCurrentPowerSourceOnly = settings.QuickSettingsCurrentPowerSourceOnly;
            SetQuickSettingsPowerSourceToggle(_lastKnownQuickSettingsCurrentPowerSourceOnly);
        }
        catch (Exception exception)
        {
            AppLog.Warn("QuickSettings", "Current power-source preference update failed.", exception);
            bool? refreshedValue = null;
            try
            {
                var bootstrap = await _frontend.GetBootstrapAsync().ConfigureAwait(true);
                refreshedValue = bootstrap.Settings.QuickSettingsCurrentPowerSourceOnly;
            }
            catch (Exception refreshException)
            {
                AppLog.Warn("QuickSettings", "Current power-source preference rollback failed.", refreshException);
            }

            _lastKnownQuickSettingsCurrentPowerSourceOnly = ResolveQuickSettingsPowerSourcePreference(
                _lastKnownQuickSettingsCurrentPowerSourceOnly,
                refreshedValue);
            SetQuickSettingsPowerSourceToggle(_lastKnownQuickSettingsCurrentPowerSourceOnly);
        }
    }

    /// <summary>Renders the read-only Required Components list (moved here from the Status page) from
    /// the same authoritative frontend status snapshot MainWindow already captures. Diagnostic only --
    /// no repair/install controls: Runtime lifecycle/reconciliation stays the authority for setup.</summary>
    internal void RenderRequiredComponents(FrontendStatusSnapshot snapshot)
    {
        var components = new (string Name, string Status, string Reason)[]
        {
            ("HidHide", snapshot.Prerequisites.HidHideStatus.ToString(), snapshot.Prerequisites.HidHideReason),
            ("usbip-win2", snapshot.Prerequisites.UsbIpStatus.ToString(), snapshot.Prerequisites.UsbIpReason),
            ("VIIPER", snapshot.Prerequisites.ViiperStatus.ToString(), snapshot.Prerequisites.ViiperReason),
        };

        var readyCount = components.Count(item => string.Equals(item.Status, "Ready", StringComparison.OrdinalIgnoreCase));
        RequiredComponentsExpander.Description = new TextBlock
        {
            Text = $"{readyCount} of {components.Length} ready",
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
        };

        RequiredComponentsExpander.Items.Clear();
        foreach (var (name, status, reason) in components)
        {
            RequiredComponentsExpander.Items.Add(new SettingsCard
            {
                Header = name,
                Description = reason,
                Content = new TextBlock { Text = status, Opacity = 0.7 },
            });
        }
    }

    internal static Visibility GetDeveloperMenuCardVisibility(bool developerMenuEnabled) =>
        developerMenuEnabled ? Visibility.Visible : Visibility.Collapsed;

    internal static bool ResolveQuickSettingsPowerSourcePreference(bool lastKnownValue, bool? refreshedValue) =>
        refreshedValue ?? lastKnownValue;

    private void DeveloperMenuButton_Click(object sender, RoutedEventArgs args)
    {
        DeveloperMenuRequested?.Invoke(this, EventArgs.Empty);
    }
}
