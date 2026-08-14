using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SteamInputAddonforClaw.Developer;
using SteamInputAddonforClaw.Diagnostics;
using SteamInputAddonforClaw.Diagnostics.EnvironmentDiscovery;
using SteamInputAddonforClaw.Settings;
using System.Diagnostics;

namespace SteamInputAddonforClaw.Views;

public sealed partial class DeveloperPage : UserControl
{
    private StartupSettingsCoordinator? _startupSettings;
    private DeveloperTestModeState? _developerTestModeState;
    private IEnvironmentDiscoveryReportGenerator? _environmentDiscoveryReportGenerator;
    private Func<bool>? _isPrerequisiteSetupInProgress;
    private bool _isInitializingTestMode;
    private bool _isInitializingLogLevel;
    private int _isGeneratingEnvironmentDiscoveryReport;

    public event EventHandler? BackRequested;
    public event EventHandler? ClawSensorProbeRequested;

    public DeveloperPage()
    {
        InitializeComponent();
    }

    internal void Initialize(
        StartupSettingsCoordinator startupSettings,
        DeveloperTestModeState? developerTestModeState,
        IEnvironmentDiscoveryReportGenerator environmentDiscoveryReportGenerator,
        Func<bool> isPrerequisiteSetupInProgress)
    {
        _startupSettings = startupSettings;
        _developerTestModeState = developerTestModeState;
        _environmentDiscoveryReportGenerator = environmentDiscoveryReportGenerator;
        _isPrerequisiteSetupInProgress = isPrerequisiteSetupInProgress;

        _isInitializingTestMode = true;
        TestModeToggleSwitch.IsOn = developerTestModeState?.IsEnabled == true;
        _isInitializingTestMode = false;

        _isInitializingLogLevel = true;
        LogLevelComboBox.SelectedIndex = startupSettings.Settings.LogLevel switch
        {
            AppLogPreference.Info => 1,
            AppLogPreference.Debug => 2,
            _ => 0,
        };
        _isInitializingLogLevel = false;
    }

    private void BackButton_Click(object sender, RoutedEventArgs args)
    {
        BackRequested?.Invoke(this, EventArgs.Empty);
    }

    private void ClawSensorProbeButton_Click(object sender, RoutedEventArgs args)
    {
        ClawSensorProbeRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OpenLogFolderButton_Click(object sender, RoutedEventArgs args)
    {
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{AppLog.DirectoryPath}\"") { UseShellExecute = true });
        }
        catch (Exception exception)
        {
            AppLog.Warn("DeveloperMenu", "Log folder could not be opened.", exception);
        }
    }

    private void TestModeToggleSwitch_Toggled(object sender, RoutedEventArgs args)
    {
        if (!_isInitializingTestMode && _isPrerequisiteSetupInProgress?.Invoke() != true)
            _developerTestModeState?.SetEnabled(TestModeToggleSwitch.IsOn);
    }

    private void LogLevelComboBox_SelectionChanged(object sender, SelectionChangedEventArgs args)
    {
        if (_isInitializingLogLevel || LogLevelComboBox.SelectedItem is not ComboBoxItem item || item.Content is not string value) return;
        var level = AppSettingsPolicy.Normalize(value);
        _startupSettings?.ChangeLogLevel(level);
    }

    private async void GenerateEnvironmentDiscoveryReportButton_Click(object sender, RoutedEventArgs args)
    {
        if (_isPrerequisiteSetupInProgress?.Invoke() == true) return;
        if (Interlocked.Exchange(ref _isGeneratingEnvironmentDiscoveryReport, 1) != 0) return;
        GenerateEnvironmentDiscoveryReportButton.IsEnabled = false;
        SetEnvironmentDiscoveryStatus("Generating...");
        try
        {
            await _environmentDiscoveryReportGenerator!.GenerateAsync();
            SetEnvironmentDiscoveryStatus(string.Empty);
        }
        catch (Exception exception)
        {
            AppLog.Warn("EnvironmentDiscovery", "Environment discovery report generation failed.", exception, ("Reason", exception.GetType().Name));
            SetEnvironmentDiscoveryStatus("Report generation failed.\r\nSee the application log for details.");
        }
        finally
        {
            GenerateEnvironmentDiscoveryReportButton.IsEnabled = true;
            Volatile.Write(ref _isGeneratingEnvironmentDiscoveryReport, 0);
        }
    }

    private void SetEnvironmentDiscoveryStatus(string text)
    {
        EnvironmentDiscoveryReportStatusText.Text = text;
        EnvironmentDiscoveryReportStatusText.Visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
    }
}
