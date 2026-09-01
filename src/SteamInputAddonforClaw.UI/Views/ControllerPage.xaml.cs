using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SteamInputAddonforClaw.Contracts.Frontend;
using SteamInputAddonforClaw.Contracts.Oem1;

namespace SteamInputAddonforClaw.Views;

public sealed partial class ControllerPage : UserControl
{
    private IAddonFrontendControl? _frontend;
    private bool _isLoading;
    private bool _lastKnownSteamInputRoutingEnabled;
    private Oem1MappingSettings _oem1Mapping = Oem1MappingSettings.Default;
    private bool _oem1MappingAvailable;
    private FrontendCenterMStartupSnapshot _centerMStartupSnapshot = FrontendCenterMStartupSnapshot.Unavailable;
    private bool _centerMStartupBusy;

    public ControllerPage() => InitializeComponent();

    internal event EventHandler<Oem1MappingSettings>? MappingEditRequested;

    /// <summary>Re-reads the MSI Center M controller-authority state on every entry to this page. The
    /// reboot-bound transition (work order PR3) raises no <c>StateInvalidated</c>, so a tab re-entry
    /// read is the only refresh path.</summary>
    internal void Activate() => _ = RefreshCenterMStartupAsync();

    private async Task RefreshCenterMStartupAsync()
    {
        if (_frontend is null) return;
        try { RenderCenterMStartup(await _frontend.CaptureCenterMStartupAsync()); }
        catch (Exception exception)
        {
            AppLog.Warn("Controller", "MSI Center M startup snapshot capture failed.", exception, ("Reason", exception.GetType().Name));
            RenderCenterMStartup(FrontendCenterMStartupSnapshot.Unavailable);
        }
    }

    internal void Initialize(IAddonFrontendControl frontend, FrontendBootstrapSnapshot bootstrap, Func<nint> windowHandleProvider)
    {
        _frontend = frontend;
        _isLoading = true;
        _lastKnownSteamInputRoutingEnabled = bootstrap.Settings.SteamInputRoutingEnabled;
        SteamInputRoutingToggleSwitch.IsOn = _lastKnownSteamInputRoutingEnabled;
        _oem1MappingAvailable = bootstrap.Oem1MappingAvailable;
        CenterMInlineContent.Visibility = _oem1MappingAvailable ? Visibility.Visible : Visibility.Collapsed;
        CenterMUnavailableText.Visibility = _oem1MappingAvailable ? Visibility.Collapsed : Visibility.Visible;
        CenterMInlineContent.Initialize(bootstrap, windowHandleProvider);
        CenterMInlineContent.MappingEditRequested += (_, mapping) => MappingEditRequested?.Invoke(this, mapping with { RemappingEnabled = true });
        ApplyOem1Mapping(bootstrap.Settings.Oem1Mapping);
        _isLoading = false;
    }

    internal void ApplyOem1Mapping(Oem1MappingSettings mapping)
    {
        _oem1Mapping = mapping with { RemappingEnabled = true };
        CenterMInlineContent.Apply(_oem1Mapping);
    }

    private async void SteamInputRoutingToggleSwitch_Toggled(object sender, RoutedEventArgs args)
    {
        if (_isLoading || _frontend is null) return;
        if (SteamInputRoutingToggleSwitch.IsOn && !_lastKnownSteamInputRoutingEnabled)
        {
            if (!await ConfirmRoutingEnableAsync())
            {
                SetRouteToggle(false);
                return;
            }
        }

        try
        {
            var result = await _frontend.SetSteamInputRoutingEnabledAsync(SteamInputRoutingToggleSwitch.IsOn);
            _lastKnownSteamInputRoutingEnabled = result.Settings.SteamInputRoutingEnabled;
            SetRouteToggle(_lastKnownSteamInputRoutingEnabled);
        }
        catch (Exception exception)
        {
            AppLog.Warn("Controller", "Steam Input routing update failed.", exception);
            SetRouteToggle(_lastKnownSteamInputRoutingEnabled);
        }
    }

    private async Task<bool> ConfirmRoutingEnableAsync()
    {
        var dialog = new ContentDialog
        {
            Title = "Enable Steam Input Routing?",
            Content = "This feature manages HidHide configuration for Steam Input routing.\n\n" +
                      "If another application also uses HidHide, its controller hiding or remapping features may not work correctly while this feature is enabled.",
            PrimaryButtonText = "Enable",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot
        };

        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    private void SetRouteToggle(bool value)
    {
        _isLoading = true;
        SteamInputRoutingToggleSwitch.IsOn = value;
        _isLoading = false;
    }

    // ---- MSI Center M controller-authority card (moved here from the Device page) ----

    /// <summary>Renders the MSI Center M controller-authority card from the authoritative snapshot
    /// (work order PR3 section 12). Explicit Enable/Disable buttons -- no inverted toggle; the button
    /// matching the current authority is disabled. Real Windows state is the only source of truth, so
    /// nothing is persisted here and there is no PR1-era sticky "restart later" state: a confirmed
    /// transition restarts Windows immediately.</summary>
    private void RenderCenterMStartup(FrontendCenterMStartupSnapshot snapshot)
    {
        _centerMStartupSnapshot = snapshot;

        // The feature simply does not apply to this machine (non-Claw) and there is nothing to
        // report -- collapse the card rather than show a dead "Unavailable" row.
        if (snapshot.State == FrontendCenterMStartupState.Unavailable && snapshot.FailureMessage is null)
        {
            CenterMStartupCard.Visibility = Visibility.Collapsed;
            CenterMStartupInfoBar.IsOpen = false;
            return;
        }

        CenterMStartupCard.Visibility = Visibility.Visible;
        CenterMStartupStatusText.Text = snapshot.State switch
        {
            FrontendCenterMStartupState.Enabled => "Status: Enabled",
            FrontendCenterMStartupState.Disabled => "Status: Disabled",
            FrontendCenterMStartupState.Partial => "Status: Needs attention",
            _ => "Status: Unavailable",
        };

        var operable = !_centerMStartupBusy && snapshot.State != FrontendCenterMStartupState.Unavailable;
        CenterMStartupEnableButton.IsEnabled = operable && snapshot.State != FrontendCenterMStartupState.Enabled;
        CenterMStartupDisableButton.IsEnabled = operable && snapshot.State != FrontendCenterMStartupState.Disabled;

        switch (CenterMStartupPresentation.ResolveInfoBar(snapshot.State))
        {
            case CenterMStartupInfoBarKind.Partial:
                CenterMStartupInfoBar.Severity = InfoBarSeverity.Warning;
                CenterMStartupInfoBar.Message = "MSI Center M startup configuration is inconsistent. Choose Enable or Disable to repair it.";
                CenterMStartupInfoBar.IsOpen = true;
                break;
            case CenterMStartupInfoBarKind.Unavailable:
                CenterMStartupInfoBar.Severity = InfoBarSeverity.Warning;
                CenterMStartupInfoBar.Message = snapshot.FailureMessage ?? "MSI Center M controller authority control is unavailable.";
                CenterMStartupInfoBar.IsOpen = true;
                break;
            default:
                CenterMStartupInfoBar.IsOpen = false;
                break;
        }
    }

    internal enum CenterMStartupInfoBarKind { None, Partial, Unavailable }

    /// <summary>Pure InfoBar-precedence rule for the MSI Center M card, extracted so it can be tested
    /// without a XAML root.</summary>
    internal static class CenterMStartupPresentation
    {
        internal static CenterMStartupInfoBarKind ResolveInfoBar(FrontendCenterMStartupState state) => state switch
        {
            FrontendCenterMStartupState.Partial => CenterMStartupInfoBarKind.Partial,
            FrontendCenterMStartupState.Unavailable => CenterMStartupInfoBarKind.Unavailable,
            _ => CenterMStartupInfoBarKind.None,
        };
    }

    private async void CenterMStartupEnableButton_Click(object sender, RoutedEventArgs e) => await RequestCenterMTransitionAsync(centerMEnabled: true);
    private async void CenterMStartupDisableButton_Click(object sender, RoutedEventArgs e) => await RequestCenterMTransitionAsync(centerMEnabled: false);

    private async Task RequestCenterMTransitionAsync(bool centerMEnabled)
    {
        if (_frontend is null || _centerMStartupBusy) return;

        // Confirmation happens before any backend request (work order PR3 section 6.1/12.3). Cancel
        // (or dismiss) issues zero RPC. The transition always restarts immediately -- there is no
        // deferred-restart choice.
        var dialog = new ContentDialog
        {
            Title = centerMEnabled ? "Enable MSI Center M" : "Disable MSI Center M",
            Content = new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                Text = centerMEnabled
                    ? "Restore MSI Center M controller authority.\n\nWindows must restart to apply this change."
                    : "Disable MSI Center M and switch controller authority to Steam Addon for Claw.\n\nWindows must restart to apply this change.",
            },
            PrimaryButtonText = centerMEnabled ? "Enable and Restart" : "Disable and Restart",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot,
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

        _centerMStartupBusy = true;
        CenterMStartupEnableButton.IsEnabled = false;
        CenterMStartupDisableButton.IsEnabled = false;
        try
        {
            var result = await _frontend.RequestCenterMAuthorityTransitionAsync(centerMEnabled);
            _centerMStartupBusy = false;
            RenderCenterMStartup(result.Snapshot);
            if (result.Succeeded)
            {
                // Windows is restarting now -- no long-lived success screen is needed.
                CenterMStartupEnableButton.IsEnabled = false;
                CenterMStartupDisableButton.IsEnabled = false;
                CenterMStartupInfoBar.Severity = InfoBarSeverity.Success;
                CenterMStartupInfoBar.Message = "Controller authority updated. Restarting Windows…";
                CenterMStartupInfoBar.IsOpen = true;
            }
            else
            {
                // A failed/cancelled Disable can leave verified startup/HidHide preparation behind
                // while the Center M roots are still Enabled. The backend explicitly offers
                // "Enable and Restart" as the cleanup path, so expose it here even though a plain
                // Enabled snapshot would normally disable the redundant Enable button (PR3 review).
                if (!centerMEnabled && result.Snapshot.State == FrontendCenterMStartupState.Enabled)
                    CenterMStartupEnableButton.IsEnabled = true;

                CenterMStartupInfoBar.Severity = result.Outcome == FrontendCenterMStartupMutationOutcome.Cancelled
                    ? InfoBarSeverity.Informational
                    : InfoBarSeverity.Warning;
                // Always prefer the backend's authoritative message: a cancelled elevation prompt on
                // Disable/Enable can still have left verified startup/HidHide preparation in place, so
                // the UI must not invent a "nothing changed" claim (PR3 review).
                CenterMStartupInfoBar.Message = result.FailureMessage
                    ?? (result.Outcome == FrontendCenterMStartupMutationOutcome.Cancelled
                        ? "The controller authority change was cancelled."
                        : "The controller authority change could not be completed.");
                CenterMStartupInfoBar.IsOpen = true;
            }
        }
        catch (Exception exception)
        {
            _centerMStartupBusy = false;
            AppLog.Warn("Controller", "MSI Center M authority transition failed.", exception, ("Reason", exception.GetType().Name));
            CenterMStartupInfoBar.Severity = InfoBarSeverity.Error;
            CenterMStartupInfoBar.Message = "The controller authority change could not be completed because the Runtime connection was interrupted.";
            CenterMStartupInfoBar.IsOpen = true;
            await RefreshCenterMStartupAsync();
        }
    }
}
