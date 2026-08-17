using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml;
using SteamInputAddonforClaw.Contracts.Frontend;

namespace SteamInputAddonforClaw.Views;

public sealed partial class ControllerPage : UserControl
{
    private IAddonFrontendControl? _frontend;
    private bool _isLoading;
    private bool _lastKnownRouteInSteamBigPicture;
    public ControllerPage()
    {
        InitializeComponent();
    }

    internal void Initialize(IAddonFrontendControl frontend, FrontendBootstrapSnapshot bootstrap)
    {
        _frontend = frontend;
        _isLoading = true;
        RouteInSteamBigPictureToggleSwitch.IsOn = bootstrap.Settings.RouteInSteamBigPicture;
        _lastKnownRouteInSteamBigPicture = bootstrap.Settings.RouteInSteamBigPicture;
        _isLoading = false;
    }

    private async void RouteInSteamBigPictureToggleSwitch_Toggled(object sender, RoutedEventArgs args)
    {
        if (_isLoading || _frontend is null) return;
        try
        {
            var result = await _frontend.SetRouteInSteamBigPictureAsync(RouteInSteamBigPictureToggleSwitch.IsOn);
            _lastKnownRouteInSteamBigPicture = result.RouteInSteamBigPicture;
            SetRouteToggle(_lastKnownRouteInSteamBigPicture);
        }
        catch (Exception exception)
        {
            AppLog.Warn("Controller", "Route-in-Big-Picture update failed.", exception);
            try
            {
                var bootstrap = await _frontend.GetBootstrapAsync();
                _lastKnownRouteInSteamBigPicture = bootstrap.Settings.RouteInSteamBigPicture;
                SetRouteToggle(_lastKnownRouteInSteamBigPicture);
            }
            catch (Exception refreshException)
            {
                AppLog.Warn("Controller", "Route-in-Big-Picture state refresh failed.", refreshException);
                SetRouteToggle(_lastKnownRouteInSteamBigPicture);
            }
        }
    }

    private void SetRouteToggle(bool value)
    {
        _isLoading = true;
        RouteInSteamBigPictureToggleSwitch.IsOn = value;
        _isLoading = false;
    }
}
