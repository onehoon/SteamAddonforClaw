using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SteamInputAddonforClaw.Diagnostics;
using SteamInputAddonforClaw.Contracts.Frontend;
using SteamInputAddonforClaw.Settings;
using System.Diagnostics;

namespace SteamInputAddonforClaw.Views;

public sealed partial class DeveloperPage : UserControl
{
    private IAddonFrontendControl? _frontend;
    private FrontendBootstrapSnapshot? _bootstrap;
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
        IAddonFrontendControl frontend,
        FrontendBootstrapSnapshot bootstrap,
        Func<bool> isPrerequisiteSetupInProgress)
    {
        _frontend = frontend;
        _bootstrap = bootstrap;
        _isPrerequisiteSetupInProgress = isPrerequisiteSetupInProgress;

        _isInitializingTestMode = true;
        TestModeToggleSwitch.IsOn = bootstrap.Developer.TestModeEnabled;
        _isInitializingTestMode = false;

        _isInitializingLogLevel = true;
        LogLevelComboBox.SelectedIndex = bootstrap.Settings.LogLevel switch
        {
            FrontendLogLevel.Info => 1,
            FrontendLogLevel.Debug => 2,
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
            _ = _frontend?.SetDeveloperTestModeAsync(TestModeToggleSwitch.IsOn);
    }

    private void LogLevelComboBox_SelectionChanged(object sender, SelectionChangedEventArgs args)
    {
        if (_isInitializingLogLevel || LogLevelComboBox.SelectedItem is not ComboBoxItem item || item.Content is not string value) return;
        var level = value switch { "Debug" => FrontendLogLevel.Debug, "Info" => FrontendLogLevel.Info, _ => FrontendLogLevel.Off };
        _ = _frontend?.SetLogLevelAsync(level);
    }

    private async void GenerateEnvironmentDiscoveryReportButton_Click(object sender, RoutedEventArgs args)
    {
        if (_isPrerequisiteSetupInProgress?.Invoke() == true) return;
        if (Interlocked.Exchange(ref _isGeneratingEnvironmentDiscoveryReport, 1) != 0) return;
        GenerateEnvironmentDiscoveryReportButton.IsEnabled = false;
        SetEnvironmentDiscoveryStatus("Generating...");
        try
        {
            var result = await _frontend!.GenerateEnvironmentReportAsync();
            SetEnvironmentDiscoveryStatus(result.Succeeded ? string.Empty : "Report generation failed.\r\nSee the application log for details.");
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
