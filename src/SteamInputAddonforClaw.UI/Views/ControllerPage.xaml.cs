using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml;
using SteamInputAddonforClaw.Contracts.Frontend;
using SteamInputAddonforClaw.Contracts.Oem1;

namespace SteamInputAddonforClaw.Views;

public sealed partial class ControllerPage : UserControl
{
    private IAddonFrontendControl? _frontend;
    private bool _isLoading;
    private bool _lastKnownSteamInputRoutingEnabled;
    /// <summary>The last mapping this page knows to be persisted. Kept whole so the remapping
    /// toggle can send back the four bindings UNCHANGED alongside the new switch value -- switching
    /// the feature off must never erase what the user configured.</summary>
    private Oem1MappingSettings _oem1Mapping = Oem1MappingSettings.Default;

    public ControllerPage()
    {
        InitializeComponent();
    }

    /// <summary>Raised when the card (not its toggle) is clicked. The host owns navigation, exactly
    /// as it does for the Settings page's Developer Menu card.</summary>
    internal event EventHandler? CenterMButtonRequested;

    internal void Initialize(IAddonFrontendControl frontend, FrontendBootstrapSnapshot bootstrap)
    {
        _frontend = frontend;
        _isLoading = true;
        SteamInputRoutingToggleSwitch.IsOn = bootstrap.Settings.SteamInputRoutingEnabled;
        _lastKnownSteamInputRoutingEnabled = bootstrap.Settings.SteamInputRoutingEnabled;
        ApplyOem1Mapping(bootstrap.Settings.Oem1Mapping);
        _isLoading = false;
    }

    /// <summary>Lets the host push back a mapping saved on the detail page, so returning to this
    /// page never shows a stale toggle.</summary>
    internal void ApplyOem1Mapping(Oem1MappingSettings mapping)
    {
        var wasLoading = _isLoading;
        _isLoading = true;
        _oem1Mapping = mapping;
        CenterMRemappingToggleSwitch.IsOn = mapping.RemappingEnabled;
        _isLoading = wasLoading;
    }

    private void CenterMButtonCard_Click(object sender, RoutedEventArgs args) =>
        CenterMButtonRequested?.Invoke(this, EventArgs.Empty);

    private async void CenterMRemappingToggleSwitch_Toggled(object sender, RoutedEventArgs args)
    {
        if (_isLoading || _frontend is null) return;
        try
        {
            var result = await _frontend.SetOem1MappingAsync(_oem1Mapping with { RemappingEnabled = CenterMRemappingToggleSwitch.IsOn });
            ApplyOem1Mapping(result.Oem1Mapping);
        }
        catch (Exception exception)
        {
            AppLog.Warn("Controller", "Center M remapping update failed.", exception);
            // Re-show what is actually persisted rather than leaving the toggle asserting a change
            // that did not happen.
            ApplyOem1Mapping(_oem1Mapping);
        }
    }

    private async void SteamInputRoutingToggleSwitch_Toggled(object sender, RoutedEventArgs args)
    {
        if (_isLoading || _frontend is null) return;
        try
        {
            var result = await _frontend.SetSteamInputRoutingEnabledAsync(SteamInputRoutingToggleSwitch.IsOn);
            _lastKnownSteamInputRoutingEnabled = result.SteamInputRoutingEnabled;
            SetRouteToggle(_lastKnownSteamInputRoutingEnabled);
        }
        catch (Exception exception)
        {
            AppLog.Warn("Controller", "Steam Input routing update failed.", exception);
            try
            {
                var bootstrap = await _frontend.GetBootstrapAsync();
                _lastKnownSteamInputRoutingEnabled = bootstrap.Settings.SteamInputRoutingEnabled;
                SetRouteToggle(_lastKnownSteamInputRoutingEnabled);
            }
            catch (Exception refreshException)
            {
                AppLog.Warn("Controller", "Steam Input routing state refresh failed.", refreshException);
                SetRouteToggle(_lastKnownSteamInputRoutingEnabled);
            }
        }
    }

    private void SetRouteToggle(bool value)
    {
        _isLoading = true;
        SteamInputRoutingToggleSwitch.IsOn = value;
        _isLoading = false;
    }
}
