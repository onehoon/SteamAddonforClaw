using CommunityToolkit.WinUI.Controls;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using SteamInputAddonforClaw.Contracts.Frontend;
using Windows.System;

namespace SteamInputAddonforClaw.Views;

public sealed partial class OverlayPage : UserControl
{
    private IAddonFrontendControl? _frontend;
    private FrontendClawHudSnapshot _clawHudSnapshot = new(false, FrontendClawHudRuntimeState.Disabled, "Off", null, null, null);
    private int _clawHudOperationInProgress;
    private bool _applyingClawHudState;
    private Task _opacityPreviewTail = Task.CompletedTask;
    private readonly object _opacityPreviewGate = new();
    private int _opacityCommitQueued;
    private int? _pendingOpacityCommitValue;

    public OverlayPage()
    {
        InitializeComponent();
    }

    internal void Initialize(IAddonFrontendControl frontend)
    {
        _frontend = frontend ?? throw new ArgumentNullException(nameof(frontend));
    }

    internal void Activate() => RequestClawHudRefresh();

    internal void RequestClawHudRefresh() => _ = RefreshClawHudAsync();

    private async Task RefreshClawHudAsync()
    {
        if (_frontend is null || Volatile.Read(ref _clawHudOperationInProgress) != 0) return;

        try
        {
            RenderClawHud(await _frontend.CaptureClawHudAsync().ConfigureAwait(true));
        }
        catch (Exception exception)
        {
            AppLog.Warn("ClawHUD.UI", "Main UI ClawHUD state refresh failed.", exception);
        }
    }

    private void RenderClawHud(FrontendClawHudSnapshot snapshot)
    {
        _clawHudSnapshot = snapshot;
        var hasSettings = snapshot.Settings is not null;

        _applyingClawHudState = true;
        try
        {
            ClawHudEnabledToggleSwitch.IsOn = snapshot.DesiredEnabled;
            if (snapshot.Settings is { } settings)
            {
                ClawHudDisplayModeComboBox.SelectedIndex = settings.DisplayMode == FrontendClawHudDisplayMode.InGameOnly ? 0 : 1;
                ClawHudSizeComboBox.SelectedIndex = settings.HudSizeOffset + 2;
                ClawHudFontComboBox.SelectedIndex = settings.Font == FrontendClawHudFont.Unispace ? 0 : 1;
                ClawHudAlignmentComboBox.SelectedIndex = (int)settings.Alignment;
                ClawHudBackgroundComboBox.SelectedIndex = settings.BackgroundMode == FrontendClawHudBackgroundMode.FullWidth ? 0 : 1;
                ClawHudOpacitySlider.Value = settings.BackgroundOpacityPercent;
                ClawHudOpacityValueText.Text = $"{settings.BackgroundOpacityPercent}%";
                ClawHudOpacitySlider.Visibility = Visibility.Visible;
                ClawHudIntelVrrToggleSwitch.IsOn = settings.IntelVrrRangeFixEnabled;
                ClawHudVrrResultText.Text = FormatVrrResult(settings.IntelVrrLastResult);
            }
            else
            {
                ClawHudDisplayModeComboBox.SelectedIndex = -1;
                ClawHudSizeComboBox.SelectedIndex = -1;
                ClawHudFontComboBox.SelectedIndex = -1;
                ClawHudAlignmentComboBox.SelectedIndex = -1;
                ClawHudBackgroundComboBox.SelectedIndex = -1;
                ClawHudOpacityValueText.Text = "—";
                ClawHudOpacitySlider.Visibility = Visibility.Collapsed;
                ClawHudIntelVrrToggleSwitch.IsOn = false;
                ClawHudVrrResultText.Text = string.Empty;
            }
        }
        finally
        {
            _applyingClawHudState = false;
        }

        ClawHudStatusText.Text = snapshot.StatusMessage;

        var canRetry = snapshot.DesiredEnabled && snapshot.RuntimeState is
            FrontendClawHudRuntimeState.Unavailable or FrontendClawHudRuntimeState.StandaloneConflict;
        var showInfoBar = snapshot.RuntimeState is
            FrontendClawHudRuntimeState.Unavailable or FrontendClawHudRuntimeState.StandaloneConflict;
        ClawHudInfoBar.Message = snapshot.StatusMessage;
        ClawHudInfoBar.Severity = snapshot.RuntimeState == FrontendClawHudRuntimeState.StandaloneConflict
            ? InfoBarSeverity.Warning
            : InfoBarSeverity.Error;
        ClawHudInfoBar.IsOpen = showInfoBar;
        ClawHudInfoBar.Visibility = showInfoBar ? Visibility.Visible : Visibility.Collapsed;
        ClawHudRetryButton.Visibility = canRetry ? Visibility.Visible : Visibility.Collapsed;

        var nestedEnabled = snapshot.RuntimeState == FrontendClawHudRuntimeState.Ready && hasSettings &&
            Volatile.Read(ref _clawHudOperationInProgress) == 0;
        ClawHudEnabledToggleSwitch.IsEnabled = Volatile.Read(ref _clawHudOperationInProgress) == 0;
        ClawHudRetryButton.IsEnabled = canRetry && ClawHudEnabledToggleSwitch.IsEnabled;
        ClawHudDisplayModeComboBox.IsEnabled = nestedEnabled;
        ClawHudSizeComboBox.IsEnabled = nestedEnabled;
        ClawHudFontComboBox.IsEnabled = nestedEnabled;
        ClawHudAlignmentComboBox.IsEnabled = nestedEnabled;
        ClawHudBackgroundComboBox.IsEnabled = nestedEnabled;
        ClawHudOpacitySlider.IsEnabled = nestedEnabled;
        ClawHudIntelVrrToggleSwitch.IsEnabled = nestedEnabled;
    }

    private static string FormatVrrResult(FrontendClawHudIntelVrrResult? result)
    {
        if (result is null) return string.Empty;

        var summary = result.Status.ToString();
        if (!string.IsNullOrWhiteSpace(result.RangeBefore) && !string.IsNullOrWhiteSpace(result.RangeAfter))
            summary += $" · {result.RangeBefore} → {result.RangeAfter}";
        if (!string.IsNullOrWhiteSpace(result.PanelName))
            summary += $" · {result.PanelName}";
        if (!string.IsNullOrWhiteSpace(result.Message))
            summary += $" · {result.Message}";
        return summary;
    }

    private async void ClawHudEnabledToggleSwitch_Toggled(object sender, RoutedEventArgs args)
    {
        if (_applyingClawHudState || _frontend is null || Interlocked.Exchange(ref _clawHudOperationInProgress, 1) != 0) return;

        var desired = ClawHudEnabledToggleSwitch.IsOn;
        RenderClawHud(_clawHudSnapshot with
        {
            DesiredEnabled = desired,
            RuntimeState = desired ? FrontendClawHudRuntimeState.Starting : FrontendClawHudRuntimeState.Disabled,
            StatusMessage = desired ? "Starting…" : "Off",
            Settings = null,
        });

        try
        {
            RenderClawHud(await _frontend.SetClawHudEnabledAsync(desired).ConfigureAwait(true));
        }
        catch (Exception exception)
        {
            AppLog.Warn("ClawHUD.UI", "Main UI ClawHUD enable mutation failed.", exception);
            try
            {
                RenderClawHud(await _frontend.CaptureClawHudAsync().ConfigureAwait(true));
            }
            catch
            {
                RenderClawHud(_clawHudSnapshot with
                {
                    DesiredEnabled = desired,
                    RuntimeState = FrontendClawHudRuntimeState.Unavailable,
                    StatusMessage = "ClawHUD could not be changed.",
                    Settings = null,
                });
            }
        }
        finally
        {
            Volatile.Write(ref _clawHudOperationInProgress, 0);
            RenderClawHud(_clawHudSnapshot);
        }
    }

    private void ClawHudRetryButton_Click(object sender, RoutedEventArgs args) => _ = RetryClawHudAsync();

    private async Task RetryClawHudAsync()
    {
        if (_frontend is null || Interlocked.Exchange(ref _clawHudOperationInProgress, 1) != 0) return;

        try
        {
            RenderClawHud(await _frontend.SetClawHudEnabledAsync(true).ConfigureAwait(true));
        }
        catch (Exception exception)
        {
            AppLog.Warn("ClawHUD.UI", "Main UI ClawHUD retry failed.", exception);
        }
        finally
        {
            Volatile.Write(ref _clawHudOperationInProgress, 0);
            await RefreshClawHudAsync().ConfigureAwait(true);
        }
    }

    private void ClawHudDisplayModeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs args)
    {
        if (!_applyingClawHudState && ClawHudDisplayModeComboBox.SelectedIndex >= 0)
        {
            var displayMode = ClawHudDisplayModeComboBox.SelectedIndex == 0
                ? FrontendClawHudDisplayMode.InGameOnly
                : FrontendClawHudDisplayMode.Always;
            _ = SubmitClawHudMutationAsync(new(FrontendClawHudMutationKind.DisplayMode, DisplayMode: displayMode));
        }
    }

    private void ClawHudSizeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs args)
    {
        if (!_applyingClawHudState && ClawHudSizeComboBox.SelectedIndex >= 0)
            _ = SubmitClawHudMutationAsync(new(FrontendClawHudMutationKind.HudSizeOffset, HudSizeOffset: ClawHudSizeComboBox.SelectedIndex - 2));
    }

    private void ClawHudFontComboBox_SelectionChanged(object sender, SelectionChangedEventArgs args)
    {
        if (!_applyingClawHudState && ClawHudFontComboBox.SelectedIndex >= 0)
        {
            var font = ClawHudFontComboBox.SelectedIndex == 0
                ? FrontendClawHudFont.Unispace
                : FrontendClawHudFont.SegoeUiVariable;
            _ = SubmitClawHudMutationAsync(new(FrontendClawHudMutationKind.Font, Font: font));
        }
    }

    private void ClawHudAlignmentComboBox_SelectionChanged(object sender, SelectionChangedEventArgs args)
    {
        if (!_applyingClawHudState && ClawHudAlignmentComboBox.SelectedIndex >= 0)
            _ = SubmitClawHudMutationAsync(new(FrontendClawHudMutationKind.Alignment, Alignment: (FrontendClawHudAlignment)ClawHudAlignmentComboBox.SelectedIndex));
    }

    private void ClawHudBackgroundComboBox_SelectionChanged(object sender, SelectionChangedEventArgs args)
    {
        if (!_applyingClawHudState && ClawHudBackgroundComboBox.SelectedIndex >= 0)
        {
            var backgroundMode = ClawHudBackgroundComboBox.SelectedIndex == 0
                ? FrontendClawHudBackgroundMode.FullWidth
                : FrontendClawHudBackgroundMode.ContentWidth;
            _ = SubmitClawHudMutationAsync(new(FrontendClawHudMutationKind.BackgroundMode, BackgroundMode: backgroundMode));
        }
    }

    private void ClawHudIntelVrrToggleSwitch_Toggled(object sender, RoutedEventArgs args)
    {
        if (!_applyingClawHudState)
            _ = SubmitClawHudMutationAsync(new(FrontendClawHudMutationKind.IntelVrrRangeFixEnabled, IntelVrrRangeFixEnabled: ClawHudIntelVrrToggleSwitch.IsOn));
    }

    private void ClawHudOpacitySlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs args)
    {
        if (_applyingClawHudState || _frontend is null || _clawHudSnapshot.RuntimeState != FrontendClawHudRuntimeState.Ready || _clawHudSnapshot.Settings is null)
            return;

        var value = (int)Math.Round(ClawHudOpacitySlider.Value);
        ClawHudOpacityValueText.Text = $"{value}%";
        lock (_opacityPreviewGate)
            _opacityPreviewTail = _opacityPreviewTail.ContinueWith(_ => SendClawHudOpacityPreviewAsync(value), TaskScheduler.Default).Unwrap();
    }

    private void ClawHudOpacitySlider_PointerReleased(object sender, PointerRoutedEventArgs args) => QueueClawHudOpacityCommit();
    private void ClawHudOpacitySlider_PointerCaptureLost(object sender, PointerRoutedEventArgs args) => QueueClawHudOpacityCommit();
    private void ClawHudOpacitySlider_KeyUp(object sender, KeyRoutedEventArgs args) => QueueClawHudOpacityCommit();

    private void QueueClawHudOpacityCommit()
    {
        if (_applyingClawHudState || _frontend is null || _clawHudSnapshot.RuntimeState != FrontendClawHudRuntimeState.Ready || _clawHudSnapshot.Settings is null)
            return;
        var value = (int)Math.Round(ClawHudOpacitySlider.Value);
        if (!TryClaimOpacityCommit(ref _opacityCommitQueued, ref _pendingOpacityCommitValue, value)) return;
        _ = CommitClawHudOpacityAsync(value);
    }

    internal static bool TryClaimOpacityCommit(ref int queued, ref int? pending, int value)
    {
        if (Interlocked.Exchange(ref queued, 1) != 0)
        {
            pending = value;
            return false;
        }

        return true;
    }

    internal static int? CompleteOpacityCommit(ref int queued, ref int? pending)
    {
        var pendingValue = pending;
        pending = null;
        if (pendingValue is null)
            Volatile.Write(ref queued, 0);
        return pendingValue;
    }

    private async Task SendClawHudOpacityPreviewAsync(int value)
    {
        if (_frontend is null) return;

        try
        {
            var result = await _frontend.MutateClawHudSettingAsync(new(FrontendClawHudMutationKind.PreviewOpacity, OpacityPercent: value)).ConfigureAwait(false);
            if (!result.Succeeded)
                AppLog.Warn("ClawHUD.UI", "HUD opacity preview was rejected.", null, ("Reason", result.FailureMessage ?? "Unknown"));
        }
        catch (Exception exception)
        {
            AppLog.Warn("ClawHUD.UI", "HUD opacity preview failed.", exception);
        }
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
            RenderClawHud(result.Snapshot with
            {
                StatusMessage = result.Succeeded ? result.Snapshot.StatusMessage : result.FailureMessage ?? result.Snapshot.StatusMessage,
            });
        }
        catch (Exception exception)
        {
            AppLog.Warn("ClawHUD.UI", "HUD opacity commit failed.", exception);
        }
        finally
        {
            if (CompleteOpacityCommit(ref _opacityCommitQueued, ref _pendingOpacityCommitValue) is { } pendingValue)
                _ = CommitClawHudOpacityAsync(pendingValue);
        }
    }

    private async Task SubmitClawHudMutationAsync(FrontendClawHudMutationIntent intent)
    {
        if (_frontend is null || Interlocked.Exchange(ref _clawHudOperationInProgress, 1) != 0) return;

        try
        {
            var result = await _frontend.MutateClawHudSettingAsync(intent).ConfigureAwait(true);
            RenderClawHud(result.Snapshot with
            {
                StatusMessage = result.Succeeded ? result.Snapshot.StatusMessage : result.FailureMessage ?? result.Snapshot.StatusMessage,
            });
        }
        catch (Exception exception)
        {
            AppLog.Warn("ClawHUD.UI", "Main UI ClawHUD setting mutation failed.", exception);
            try
            {
                RenderClawHud(await _frontend.CaptureClawHudAsync().ConfigureAwait(true));
            }
            catch
            {
                RenderClawHud(_clawHudSnapshot with { Settings = null, StatusMessage = "HUD setting could not be verified." });
            }
        }
        finally
        {
            Volatile.Write(ref _clawHudOperationInProgress, 0);
            RenderClawHud(_clawHudSnapshot);
        }
    }
}
