using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml;
using SteamInputAddonforClaw.Contracts.Frontend;

namespace SteamInputAddonforClaw.Views;

public sealed partial class ControllerPage : UserControl
{
    private IAddonFrontendControl? _frontend;
    private bool _isLoading;
    public ControllerPage()
    {
        InitializeComponent();
    }

    internal void Initialize(IAddonFrontendControl frontend, FrontendBootstrapSnapshot bootstrap)
    {
        _frontend = frontend;
        _isLoading = true;
        RouteInSteamBigPictureToggleSwitch.IsOn = bootstrap.Settings.RouteInSteamBigPicture;
        _isLoading = false;
    }

    private void RouteInSteamBigPictureToggleSwitch_Toggled(object sender, RoutedEventArgs args)
    {
        if (_isLoading || _frontend is null) return;
        _ = _frontend.SetRouteInSteamBigPictureAsync(RouteInSteamBigPictureToggleSwitch.IsOn);
    }
}
