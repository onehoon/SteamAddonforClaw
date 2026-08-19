using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml;
using SteamInputAddonforClaw.Contracts.Frontend;

namespace SteamInputAddonforClaw.Views;

public sealed partial class ControllerPage : UserControl
{
    private IAddonFrontendControl? _frontend;
    private bool _isLoading;
    private bool _lastKnownSteamInputRoutingEnabled;
    public ControllerPage()
    {
        InitializeComponent();
    }

    internal void Initialize(IAddonFrontendControl frontend, FrontendBootstrapSnapshot bootstrap)
    {
        _frontend = frontend;
        _isLoading = true;
        SteamInputRoutingToggleSwitch.IsOn = bootstrap.Settings.SteamInputRoutingEnabled;
        _lastKnownSteamInputRoutingEnabled = bootstrap.Settings.SteamInputRoutingEnabled;
        _isLoading = false;
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
