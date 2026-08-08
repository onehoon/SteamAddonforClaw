using Microsoft.UI.Xaml;
using SteamInputAddonforClaw.Controllers.Detection;
using SteamInputAddonforClaw.Install;
using SteamInputAddonforClaw.Settings;
using SteamInputAddonforClaw.Steam;
using System.Reflection;

namespace SteamInputAddonforClaw;

public sealed partial class MainWindow : Window
{
    private readonly SettingsStore _settingsStore;
    private readonly StartupRegistration _startupRegistration;
    private AppSettings _settings;

    public MainWindow(
        AppSettings settings,
        SettingsStore settingsStore,
        StartupRegistration startupRegistration,
        string startupRegistrationMessage)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _settingsStore = settingsStore ?? throw new ArgumentNullException(nameof(settingsStore));
        _startupRegistration = startupRegistration ?? throw new ArgumentNullException(nameof(startupRegistration));

        InitializeComponent();
        VersionText.Text = $"Version {GetDisplayVersion()}";
        LaunchAtWindowsStartupCheckBox.IsChecked = _settings.LaunchAtWindowsStartup;
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
        _settings = _settings with { LaunchAtWindowsStartup = launchAtWindowsStartup };
        _settingsStore.Save(_settings);

        var result = _startupRegistration.Synchronize(launchAtWindowsStartup);
        StartupSettingsStatusText.Text = result.Message;
    }

    private static string GetDisplayVersion()
    {
        return Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? "0.1.0";
    }
}
