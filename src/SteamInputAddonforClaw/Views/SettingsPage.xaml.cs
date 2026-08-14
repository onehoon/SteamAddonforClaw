using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SteamInputAddonforClaw.Settings;

namespace SteamInputAddonforClaw.Views;

public sealed partial class SettingsPage : UserControl
{
    private StartupSettingsCoordinator? _startupSettings;
    private bool _isLoadingStartupSettings;

    public event EventHandler? DeveloperMenuRequested;

    public SettingsPage()
    {
        InitializeComponent();
    }

    internal void Initialize(StartupSettingsCoordinator startupSettings, string startupRegistrationMessage)
    {
        _startupSettings = startupSettings;
        _isLoadingStartupSettings = true;
        LaunchAtWindowsStartupToggleSwitch.IsOn = startupSettings.Settings.LaunchAtWindowsStartup;
        _isLoadingStartupSettings = false;
        StartupSettingsStatusText.Text = startupRegistrationMessage;
    }

    private void LaunchAtWindowsStartupToggleSwitch_Toggled(object sender, RoutedEventArgs args)
    {
        if (_isLoadingStartupSettings || _startupSettings is null)
        {
            return;
        }

        var launchAtWindowsStartup = LaunchAtWindowsStartupToggleSwitch.IsOn;
        var result = _startupSettings.ChangeLaunchAtWindowsStartup(launchAtWindowsStartup);
        StartupSettingsStatusText.Text = result.Message;
    }

    private void DeveloperMenuButton_Click(object sender, RoutedEventArgs args)
    {
        DeveloperMenuRequested?.Invoke(this, EventArgs.Empty);
    }
}
