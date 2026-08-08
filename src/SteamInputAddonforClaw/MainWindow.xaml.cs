using Microsoft.UI.Xaml;
using SteamInputAddonforClaw.Controllers.Detection;
using SteamInputAddonforClaw.Install;
using SteamInputAddonforClaw.Settings;
using SteamInputAddonforClaw.Steam;
using System.Reflection;

namespace SteamInputAddonforClaw;

public sealed partial class MainWindow : Window
{
    private readonly StartupSettingsCoordinator _startupSettings;

    public MainWindow(
        StartupSettingsCoordinator startupSettings,
        string startupRegistrationMessage)
    {
        _startupSettings = startupSettings ?? throw new ArgumentNullException(nameof(startupSettings));

        InitializeComponent();
        VersionText.Text = $"Version {GetDisplayVersion()}";
        LaunchAtWindowsStartupCheckBox.IsChecked = _startupSettings.Settings.LaunchAtWindowsStartup;
        StartupSettingsStatusText.Text = startupRegistrationMessage;
    }

    public void UpdateSteamSessionState(SteamSessionState state)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            StatusText.Text = state.IsActive ? "Steam session active" : "Steam inactive";
            RunningAppIdText.Text = $"RunningAppID: {state.RunningAppId}";
        });
    }

    public void UpdateExternalControllerAssessment(ExternalControllerAssessment assessment)
    {
        ExternalControllerText.Text = assessment.Status switch
        {
            ExternalControllerAssessmentStatus.Clear => "External controller: None",
            ExternalControllerAssessmentStatus.ExternalPresent => "External controller: Detected",
            _ => "External controller: Indeterminate"
        };
    }

    private void LaunchAtWindowsStartupCheckBox_Click(object sender, RoutedEventArgs args)
    {
        var launchAtWindowsStartup = LaunchAtWindowsStartupCheckBox.IsChecked == true;
        var result = _startupSettings.ChangeLaunchAtWindowsStartup(launchAtWindowsStartup);
        StartupSettingsStatusText.Text = result.Message;
    }

    private static string GetDisplayVersion()
    {
        return Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? "0.1.0";
    }
}
