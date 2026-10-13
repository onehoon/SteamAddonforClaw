using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SteamInputAddonforClaw.Contracts.Frontend;
using System.Diagnostics;

namespace SteamInputAddonforClaw.Views;

public sealed partial class DeveloperPage : UserControl
{
    private IAddonFrontendControl? _frontend;
    private Func<bool>? _isPrerequisiteSetupInProgress;
    private int _isGeneratingEnvironmentDiscoveryReport;
    private bool _syncingExperimentalVirtualTouchpadToggle;
    private bool _confirmedExperimentalVirtualTouchpadEnabled;
    private string _logDirectoryPath = string.Empty;

    public event EventHandler? BackRequested;
    public event EventHandler? VibrationTestRequested;
    private void OpenVibrationTestButton_Click(object sender, RoutedEventArgs args) => VibrationTestRequested?.Invoke(this, EventArgs.Empty);
    public event EventHandler? SensorProbeRequested;
    public event EventHandler? FanHardwareProbeRequested;
    private void OpenSensorProbeButton_Click(object sender, RoutedEventArgs args) => SensorProbeRequested?.Invoke(this, EventArgs.Empty);
    private void OpenFanHardwareProbeButton_Click(object sender, RoutedEventArgs args) => FanHardwareProbeRequested?.Invoke(this, EventArgs.Empty);

    public DeveloperPage() => InitializeComponent();

    internal void Initialize(
        IAddonFrontendControl frontend,
        FrontendBootstrapSnapshot bootstrap,
        Func<bool> isPrerequisiteSetupInProgress)
    {
        _frontend = frontend;
        _logDirectoryPath = bootstrap.LogDirectoryPath;
        _isPrerequisiteSetupInProgress = isPrerequisiteSetupInProgress;
        _confirmedExperimentalVirtualTouchpadEnabled = bootstrap.Settings.ExperimentalVirtualTouchpadEnabled;
        _syncingExperimentalVirtualTouchpadToggle = true;
        ExperimentalVirtualTouchpadToggle.IsOn = _confirmedExperimentalVirtualTouchpadEnabled;
        _syncingExperimentalVirtualTouchpadToggle = false;
    }

    private async void ExperimentalVirtualTouchpadToggle_Toggled(object sender, RoutedEventArgs args)
    {
        if (_syncingExperimentalVirtualTouchpadToggle || _frontend is null)
            return;

        var requested = ExperimentalVirtualTouchpadToggle.IsOn;
        var previous = _confirmedExperimentalVirtualTouchpadEnabled;
        ExperimentalVirtualTouchpadToggle.IsEnabled = false;
        SetExperimentalVirtualTouchpadStatus(string.Empty);
        try
        {
            var settings = await _frontend.SetExperimentalVirtualTouchpadEnabledAsync(requested);
            _confirmedExperimentalVirtualTouchpadEnabled = settings.ExperimentalVirtualTouchpadEnabled;
            SetExperimentalVirtualTouchpadToggle(_confirmedExperimentalVirtualTouchpadEnabled);
        }
        catch (Exception exception)
        {
            AppLog.Warn("DeveloperMenu", "Experimental virtual touchpad preference could not be applied.", exception);
            _confirmedExperimentalVirtualTouchpadEnabled = previous;
            SetExperimentalVirtualTouchpadToggle(previous);
            SetExperimentalVirtualTouchpadStatus("The setting could not be applied. See the application log for details.");
        }
        finally
        {
            ExperimentalVirtualTouchpadToggle.IsEnabled = true;
        }
    }

    private void SetExperimentalVirtualTouchpadToggle(bool enabled)
    {
        _syncingExperimentalVirtualTouchpadToggle = true;
        ExperimentalVirtualTouchpadToggle.IsOn = enabled;
        _syncingExperimentalVirtualTouchpadToggle = false;
    }

    private void SetExperimentalVirtualTouchpadStatus(string text)
    {
        ExperimentalVirtualTouchpadStatusText.Text = text;
        ExperimentalVirtualTouchpadStatusText.Visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
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
