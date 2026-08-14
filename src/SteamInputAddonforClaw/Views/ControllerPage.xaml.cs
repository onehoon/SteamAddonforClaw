using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml;
using SteamInputAddonforClaw.Settings;

namespace SteamInputAddonforClaw.Views;

public sealed partial class ControllerPage : UserControl
{
    private StartupSettingsCoordinator? _startupSettings;
    private bool _isLoading;
    public ControllerPage()
    {
        InitializeComponent();
    }

    internal void Initialize(StartupSettingsCoordinator startupSettings)
    {
        _startupSettings = startupSettings;
        _isLoading = true;
        RouteInSteamBigPictureToggleSwitch.IsOn = startupSettings.RouteInSteamBigPicture;
        _isLoading = false;
    }

    private void RouteInSteamBigPictureToggleSwitch_Toggled(object sender, RoutedEventArgs args)
    {
        if (_isLoading || _startupSettings is null) return;
        _startupSettings.ChangeRouteInSteamBigPicture(RouteInSteamBigPictureToggleSwitch.IsOn);
    }
}
