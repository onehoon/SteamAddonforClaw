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
    private string? _environmentDiscoveryDirectory;

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
        LogLevelComboBox.SelectedIndex = startupSettings.Settings.LogLevel == AppLogPreference.Debug ? 1 : 0;
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
        OpenEnvironmentDiscoveryFolderButton.IsEnabled = false;
        OpenEnvironmentDiscoveryFolderButton.Visibility = Visibility.Collapsed;
        EnvironmentDiscoveryReportStatusText.Text = "Generating...";
        try
        {
            var result = await _environmentDiscoveryReportGenerator!.GenerateAsync();
            _environmentDiscoveryDirectory = result.DirectoryPath;
            EnvironmentDiscoveryReportStatusText.Text = $"Report generated successfully.{Environment.NewLine}{result.ReportFileName}";
            OpenEnvironmentDiscoveryFolderButton.Visibility = Visibility.Visible;
            OpenEnvironmentDiscoveryFolderButton.IsEnabled = true;
        }
        catch (Exception exception)
        {
            AppLog.Warn("EnvironmentDiscovery", "Environment discovery report generation failed.", exception, ("Reason", exception.GetType().Name));
            EnvironmentDiscoveryReportStatusText.Text = "Report generation failed.\r\nSee the application log for details.";
        }
        finally
        {
            GenerateEnvironmentDiscoveryReportButton.IsEnabled = true;
            Volatile.Write(ref _isGeneratingEnvironmentDiscoveryReport, 0);
        }
    }

    private void OpenEnvironmentDiscoveryFolderButton_Click(object sender, RoutedEventArgs args)
    {
        if (_isPrerequisiteSetupInProgress?.Invoke() == true) return;
        if (string.IsNullOrWhiteSpace(_environmentDiscoveryDirectory)) return;
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{_environmentDiscoveryDirectory}\"") { UseShellExecute = true });
        }
        catch (Exception exception)
        {
            AppLog.Warn("EnvironmentDiscovery", "Environment discovery folder could not be opened.", exception);
        }
    }
}
