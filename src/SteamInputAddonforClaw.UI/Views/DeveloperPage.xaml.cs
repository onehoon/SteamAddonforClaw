using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SteamInputAddonforClaw.Contracts.Frontend;
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
    private bool _lastKnownTestMode;
    private FrontendLogLevel _lastKnownLogLevel;
    private string _logDirectoryPath = string.Empty;

    public event EventHandler? BackRequested;
    public event EventHandler? VibrationTestRequested;
    private void OpenVibrationTestButton_Click(object sender, RoutedEventArgs args) => VibrationTestRequested?.Invoke(this, EventArgs.Empty);
    public event EventHandler? SensorProbeRequested;
    public event EventHandler? FanHardwareProbeRequested;
    private void OpenSensorProbeButton_Click(object sender, RoutedEventArgs args) => SensorProbeRequested?.Invoke(this, EventArgs.Empty);
    private void OpenFanHardwareProbeButton_Click(object sender, RoutedEventArgs args) => FanHardwareProbeRequested?.Invoke(this, EventArgs.Empty);

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
        _lastKnownTestMode = bootstrap.Developer.TestModeEnabled;
        _lastKnownLogLevel = bootstrap.Settings.LogLevel;
        _logDirectoryPath = bootstrap.LogDirectoryPath;
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

    private void OpenLogFolderButton_Click(object sender, RoutedEventArgs args)
    {
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{_logDirectoryPath}\"") { UseShellExecute = true });
        }
        catch (Exception exception)
        {
            AppLog.Warn("DeveloperMenu", "Log folder could not be opened.", exception);
        }
    }

    private async void TestModeToggleSwitch_Toggled(object sender, RoutedEventArgs args)
    {
        if (_isInitializingTestMode || _isPrerequisiteSetupInProgress?.Invoke() == true || _frontend is null) return;
        try
        {
            var result = await _frontend.SetDeveloperTestModeAsync(TestModeToggleSwitch.IsOn);
            _lastKnownTestMode = result.TestModeEnabled;
            SetTestModeToggle(_lastKnownTestMode);
        }
        catch (Exception exception)
        {
            AppLog.Warn("DeveloperMenu", "Developer test mode update failed.", exception);
            await RefreshAuthoritativeStateAsync(() => SetTestModeToggle(_lastKnownTestMode));
        }
    }

    private async void LogLevelComboBox_SelectionChanged(object sender, SelectionChangedEventArgs args)
    {
        if (_isInitializingLogLevel || _frontend is null || LogLevelComboBox.SelectedItem is not ComboBoxItem item || item.Content is not string value) return;
        var level = value switch { "Debug" => FrontendLogLevel.Debug, "Info" => FrontendLogLevel.Info, _ => FrontendLogLevel.Off };
        try
        {
            var result = await _frontend.SetLogLevelAsync(level);
            _lastKnownLogLevel = result.LogLevel;
            SetLogLevel(_lastKnownLogLevel);
        }
        catch (Exception exception)
        {
            AppLog.Warn("DeveloperMenu", "Log level update failed.", exception);
            await RefreshAuthoritativeStateAsync(() => SetLogLevel(_lastKnownLogLevel));
        }
    }

    private async Task RefreshAuthoritativeStateAsync(Action fallback)
    {
        try
        {
            var bootstrap = await _frontend!.GetBootstrapAsync();
            _bootstrap = bootstrap;
            _lastKnownTestMode = bootstrap.Developer.TestModeEnabled;
            _lastKnownLogLevel = bootstrap.Settings.LogLevel;
            _logDirectoryPath = bootstrap.LogDirectoryPath;
            SetTestModeToggle(_lastKnownTestMode);
            SetLogLevel(_lastKnownLogLevel);
        }
        catch (Exception refreshException)
        {
            AppLog.Warn("DeveloperMenu", "Developer settings state refresh failed.", refreshException);
            fallback();
        }
    }

    private void SetTestModeToggle(bool value)
    {
        _isInitializingTestMode = true;
        TestModeToggleSwitch.IsOn = value;
        _isInitializingTestMode = false;
    }

    private void SetLogLevel(FrontendLogLevel level)
    {
        _isInitializingLogLevel = true;
        LogLevelComboBox.SelectedIndex = level switch { FrontendLogLevel.Info => 1, FrontendLogLevel.Debug => 2, _ => 0 };
        _isInitializingLogLevel = false;
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
