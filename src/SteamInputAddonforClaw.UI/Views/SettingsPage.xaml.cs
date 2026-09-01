using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SteamInputAddonforClaw.Contracts.Frontend;

namespace SteamInputAddonforClaw.Views;

public sealed partial class SettingsPage : UserControl
{
    private IAddonFrontendControl? _frontend;
    private bool _isLoadingStartupSettings;
    private bool _lastKnownLaunchAtWindowsStartup;

    public event EventHandler? DeveloperMenuRequested;

    public SettingsPage()
    {
        InitializeComponent();
    }

    internal void Initialize(IAddonFrontendControl frontend, FrontendBootstrapSnapshot bootstrap)
    {
        _frontend = frontend;
        DeveloperMenuCard.Visibility = GetDeveloperMenuCardVisibility(bootstrap.Settings.DeveloperMenuEnabled);
        RenderLaunchAtStartup(bootstrap.Settings, bootstrap.StartupRegistrationMessage);
    }

    // PR2.5: while MSI Center M is Disabled the Runtime forces launch-at-startup ON and rejects an
    // OFF request; lock the control and explain why rather than let a user toggle something the
    // backend immediately reverts.
    private void RenderLaunchAtStartup(FrontendSettingsSnapshot settings, string registrationMessage)
    {
        _isLoadingStartupSettings = true;
        LaunchAtWindowsStartupToggleSwitch.IsOn = settings.LaunchAtWindowsStartup;
        _lastKnownLaunchAtWindowsStartup = settings.LaunchAtWindowsStartup;
        _isLoadingStartupSettings = false;
        LaunchAtWindowsStartupToggleSwitch.IsEnabled = !settings.LaunchAtWindowsStartupRequired;
        LaunchAtStartupCard.Description = settings.LaunchAtWindowsStartupRequired
            ? "Required while MSI Center M is disabled."
            : registrationMessage;
    }

    internal static Visibility GetDeveloperMenuCardVisibility(bool developerMenuEnabled) =>
        developerMenuEnabled ? Visibility.Visible : Visibility.Collapsed;

    private async void LaunchAtWindowsStartupToggleSwitch_Toggled(object sender, RoutedEventArgs args)
    {
        if (_isLoadingStartupSettings || _frontend is null)
        {
            return;
        }

        try
        {
            var result = await _frontend.SetLaunchAtWindowsStartupAsync(LaunchAtWindowsStartupToggleSwitch.IsOn);
            RenderLaunchAtStartup(result.Settings, result.RegistrationMessage);
        }
        catch (Exception exception)
        {
            AppLog.Warn("Settings", "Launch-at-startup update failed.", exception);
            try
            {
                var bootstrap = await _frontend.GetBootstrapAsync();
                RenderLaunchAtStartup(bootstrap.Settings, bootstrap.StartupRegistrationMessage);
            }
            catch (Exception refreshException)
            {
                AppLog.Warn("Settings", "Launch-at-startup state refresh failed.", refreshException);
                SetLaunchAtWindowsStartupToggle(_lastKnownLaunchAtWindowsStartup);
            }
        }
    }

    private void SetLaunchAtWindowsStartupToggle(bool value)
    {
        _isLoadingStartupSettings = true;
        LaunchAtWindowsStartupToggleSwitch.IsOn = value;
        _isLoadingStartupSettings = false;
    }

    private void DeveloperMenuButton_Click(object sender, RoutedEventArgs args)
    {
        DeveloperMenuRequested?.Invoke(this, EventArgs.Empty);
    }
}
