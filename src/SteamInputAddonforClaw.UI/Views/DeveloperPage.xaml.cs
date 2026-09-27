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
    private int _isRunningPid1902InputCadenceDiagnostic;
    private bool _lastKnownTestMode;
    private FrontendLogLevel _lastKnownLogLevel;
    private string _logDirectoryPath = string.Empty;

    public event EventHandler? BackRequested;
    public event EventHandler? VibrationTestRequested;
    private void OpenVibrationTestButton_Click(object sender, RoutedEventArgs args) => VibrationTestRequested?.Invoke(this, EventArgs.Empty);
    public event EventHandler? SensorProbeRequested;
    public event EventHandler? FanHardwareProbeRequested;
    public event EventHandler? BatteryChargeLimitTestRequested;
    private void OpenSensorProbeButton_Click(object sender, RoutedEventArgs args) => SensorProbeRequested?.Invoke(this, EventArgs.Empty);
    private void OpenFanHardwareProbeButton_Click(object sender, RoutedEventArgs args) => FanHardwareProbeRequested?.Invoke(this, EventArgs.Empty);
    private void OpenBatteryChargeLimitTestButton_Click(object sender, RoutedEventArgs args) => BatteryChargeLimitTestRequested?.Invoke(this, EventArgs.Empty);

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

    private async void RunPid1902InputCadenceButton_Click(object sender, RoutedEventArgs args)
    {
        if (_isPrerequisiteSetupInProgress?.Invoke() == true || _frontend is null) return;
        if (Interlocked.Exchange(ref _isRunningPid1902InputCadenceDiagnostic, 1) != 0) return;

        RunPid1902InputCadenceButton.IsEnabled = false;
        SetPid1902InputCadenceStatus("Running...\r\nContinuously move both sticks and LT/RT for 10 seconds.");
        try
        {
            var result = await _frontend.RunPid1902InputCadenceDiagnosticAsync();
            SetPid1902InputCadenceStatus(FormatPid1902InputCadenceResult(result));
        }
        catch (Exception exception)
        {
            AppLog.Warn("DeveloperMenu", "PID1902 input cadence diagnostic failed.", exception);
            SetPid1902InputCadenceStatus("Diagnostic failed. See the application log for details.");
        }
        finally
        {
            RunPid1902InputCadenceButton.IsEnabled = true;
            Volatile.Write(ref _isRunningPid1902InputCadenceDiagnostic, 0);
        }
    }

    private void SetPid1902InputCadenceStatus(string text)
    {
        Pid1902InputCadenceStatusText.Text = text;
        Pid1902InputCadenceStatusText.Visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
    }

    private static string FormatPid1902InputCadenceResult(FrontendPid1902InputCadenceResult result)
    {
        if (result.Outcome != FrontendPid1902InputCadenceOutcome.Completed)
            return result.Status;

        var text = $"Completed\r\nReads: {result.SuccessfulReadCount:N0} ({FormatHz(result.ObservedReadHz)})\r\n" +
                   $"Distinct states: {result.DistinctStateCount:N0} ({FormatHz(result.DistinctStateHz)})\r\n" +
                   $"Duplicates: {result.DuplicatePercent?.ToString("F1") ?? "n/a"}%\r\n\r\n" +
                   "Distinct interval:\r\n" +
                   $"Min       {FormatMilliseconds(result.MinDistinctIntervalMs)}\r\n" +
                   $"Mean      {FormatMilliseconds(result.MeanDistinctIntervalMs)}\r\n" +
                   $"Median    {FormatMilliseconds(result.MedianDistinctIntervalMs)}\r\n" +
                   $"P95       {FormatMilliseconds(result.P95DistinctIntervalMs)}\r\n" +
                   $"Max       {FormatMilliseconds(result.MaxDistinctIntervalMs)}";

        if (result.DistinctStateCount < 2)
            text += "\r\n\r\nCompleted, but too few changing raw states were observed. Run again while continuously moving both sticks and triggers.";
        else if (result.ObservedReadHz is not > 250)
            text += $"\r\n\r\nSampling rate was too low for a reliable 125 Hz vs 250 Hz comparison. Observed read rate: {FormatHz(result.ObservedReadHz)}.";

        return text;
    }

    private static string FormatHz(double? value) => value is { } hz ? $"{hz:F1} Hz" : "n/a";
    private static string FormatMilliseconds(double? value) => value is { } milliseconds ? $"{milliseconds:F2} ms" : "n/a";
}
