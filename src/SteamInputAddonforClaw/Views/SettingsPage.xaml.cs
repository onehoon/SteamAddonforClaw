using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SteamInputAddonforClaw.Contracts.Frontend;
using SteamInputAddonforClaw.Diagnostics;

namespace SteamInputAddonforClaw.Views;

public sealed partial class SettingsPage : UserControl
{
    private IAddonFrontendControl? _frontend;
    private bool _isLoadingStartupSettings;

    public event EventHandler? DeveloperMenuRequested;

    public SettingsPage()
    {
        InitializeComponent();
    }

    internal void Initialize(IAddonFrontendControl frontend, FrontendBootstrapSnapshot bootstrap)
    {
        _frontend = frontend;
        _isLoadingStartupSettings = true;
        LaunchAtWindowsStartupToggleSwitch.IsOn = bootstrap.Settings.LaunchAtWindowsStartup;
        _isLoadingStartupSettings = false;
        LaunchAtStartupCard.Description = bootstrap.StartupRegistrationMessage;
    }

    private async void LaunchAtWindowsStartupToggleSwitch_Toggled(object sender, RoutedEventArgs args)
    {
        if (_isLoadingStartupSettings || _frontend is null)
        {
            return;
        }

        try
        {
            var result = await _frontend.SetLaunchAtWindowsStartupAsync(LaunchAtWindowsStartupToggleSwitch.IsOn);
            LaunchAtStartupCard.Description = result.RegistrationMessage;
        }
        catch (Exception exception)
        {
            AppLog.Warn("Settings", "Launch-at-startup update failed.", exception);
        }
    }

    private void DeveloperMenuButton_Click(object sender, RoutedEventArgs args)
    {
        DeveloperMenuRequested?.Invoke(this, EventArgs.Empty);
    }
}
