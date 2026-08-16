using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SteamInputAddonforClaw.Contracts.Frontend;

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

    private void LaunchAtWindowsStartupToggleSwitch_Toggled(object sender, RoutedEventArgs args)
    {
        if (_isLoadingStartupSettings || _frontend is null)
        {
            return;
        }

        var launchAtWindowsStartup = LaunchAtWindowsStartupToggleSwitch.IsOn;
        _ = UpdateLaunchAtStartupAsync(launchAtWindowsStartup);
    }

    private async Task UpdateLaunchAtStartupAsync(bool enabled)
    {
        var result = await _frontend!.SetLaunchAtWindowsStartupAsync(enabled);
        LaunchAtStartupCard.Description = result.RegistrationMessage;
    }

    private void DeveloperMenuButton_Click(object sender, RoutedEventArgs args)
    {
        DeveloperMenuRequested?.Invoke(this, EventArgs.Empty);
    }
}
