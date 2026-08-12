using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using SteamInputAddonforClaw.Controllers.Detection;
using SteamInputAddonforClaw.Diagnostics;
using SteamInputAddonforClaw.Install;
using SteamInputAddonforClaw.Settings;
using SteamInputAddonforClaw.Steam;
using SteamInputAddonforClaw.Devices;
using SteamInputAddonforClaw.Devices.MSI.Claw;
using SteamInputAddonforClaw.Recovery;
using SteamInputAddonforClaw.HidHide;
using SteamInputAddonforClaw.Windowing;
using System.Reflection;
using System.Runtime.InteropServices;
using Windows.Foundation;
using WinRT.Interop;
using SteamInputAddonforClaw.Status;
using SteamInputAddonforClaw.Prerequisites;
using System.Collections.ObjectModel;
using System.Diagnostics;
using SteamInputAddonforClaw.Startup;
using SteamInputAddonforClaw.Diagnostics.EnvironmentDiscovery;
using SteamInputAddonforClaw.Developer;
using SteamInputAddonforClaw.Diagnostics.ClawSensorProbe;
using Microsoft.UI.Dispatching;

namespace SteamInputAddonforClaw;

public sealed partial class MainWindow : Window
{
    private readonly StartupSettingsCoordinator _startupSettings;
    private bool _isLoadingStartupSettings;
    private readonly MainNavigationState _navigationState = new();
    private readonly ISystemStatusProvider _systemStatusProvider;
    private readonly IEnvironmentDiscoveryReportGenerator _environmentDiscoveryReportGenerator;
    private readonly IHidHideProvisioningReceiptStore _hidHideReceiptStore;
    private readonly IElevatedProcessRunner _prerequisiteSetupRunner;
    private readonly DeveloperTestModeState? _developerTestModeState;
    private bool _isInitializingTestMode;
    private bool _isInitializingLogLevel;
    private SystemStatusSnapshot? _latestSystemStatus;
    private readonly ObservableCollection<StatusCardViewModel> _softwareCards = [];
    private readonly ObservableCollection<StatusCardViewModel> _componentCards = [];
    private readonly ObservableCollection<StatusCardViewModel> _externalControllerCards = [];
    private readonly ObservableCollection<StatusCardViewModel> _runtimeCards = [];
    private int _isRefreshingStatus;
    private int _isGeneratingEnvironmentDiscoveryReport;
    private string? _environmentDiscoveryDirectory;
    private bool _setupPromptActive;
    private bool _setupPromptDeclinedForCurrentProcess;
    private bool _windowActivatedForUser;
    private bool _setupPromptPendingActivation;
    private bool _prerequisiteSetupInProgress;
    private ClawSensorProbeCoordinator _clawSensorProbe = new();
    private DispatcherQueueTimer? _clawSensorProbeUiTimer;

    public MainWindow(
        StartupSettingsCoordinator startupSettings,
        string startupRegistrationMessage)
        : this(startupSettings, startupRegistrationMessage, null)
    {
    }

    internal MainWindow(
        StartupSettingsCoordinator startupSettings,
        string startupRegistrationMessage,
        RecoveryManager? recoveryManager,
        ISystemStatusProvider? systemStatusProvider = null,
        IEnvironmentDiscoveryReportGenerator? environmentDiscoveryReportGenerator = null,
        IHidHideProvisioningReceiptStore? hidHideReceiptStore = null,
        DeveloperTestModeState? developerTestModeState = null,
        IElevatedProcessRunner? prerequisiteSetupRunner = null)
    {
        _startupSettings = startupSettings ?? throw new ArgumentNullException(nameof(startupSettings));
        _systemStatusProvider = systemStatusProvider ?? CreateDefaultSystemStatusProvider();
        _hidHideReceiptStore = hidHideReceiptStore ?? new HidHideProvisioningReceiptStore(VelopackAppPaths.HidHideProvisioningReceiptPath);
        _prerequisiteSetupRunner = prerequisiteSetupRunner ?? new ElevatedProcessRunner();
        _developerTestModeState = developerTestModeState;
        _environmentDiscoveryReportGenerator = environmentDiscoveryReportGenerator ?? new EnvironmentDiscoveryReportGenerator(
            new WindowsEnvironmentDiscoverySnapshotSource(),
            new EnvironmentDiscoveryReportStore(AppLog.DirectoryPath),
            new EnvironmentDiscoveryReportWriter());

        InitializeComponent();
        Title = FormatWindowTitle(GetDisplayVersion());
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico"));
        ApplyDefaultWindowSize();
        Closed += OnWindowClosed;
        Activated += OnWindowActivated;
        _isLoadingStartupSettings = true;
        LaunchAtWindowsStartupToggleSwitch.IsOn = _startupSettings.Settings.LaunchAtWindowsStartup;
        _isLoadingStartupSettings = false;
        _isInitializingTestMode = true;
        TestModeToggleSwitch.IsOn = _developerTestModeState?.IsEnabled == true;
        _isInitializingTestMode = false;
        _isInitializingLogLevel = true;
        LogLevelComboBox.SelectedIndex = _startupSettings.Settings.LogLevel == AppLogPreference.Debug ? 1 : 0;
        _isInitializingLogLevel = false;
        StartupSettingsStatusText.Text = startupRegistrationMessage;
        ControllerSoftwareRepeater.ItemsSource = _softwareCards;
        RoutingComponentsRepeater.ItemsSource = _componentCards;
        ExternalControllersList.ItemsSource = _externalControllerCards;
        RuntimeStatusList.ItemsSource = _runtimeCards;
        MainNavigationView.SelectedItem = StatusNavigationItem;
        _ = RefreshSystemStatusAsync();
    }

    private void OnWindowActivated(object sender, WindowActivatedEventArgs args)
    {
        if (args.WindowActivationState == WindowActivationState.Deactivated) return;
        if (_windowActivatedForUser)
        {
            if (_setupPromptPendingActivation) _ = RefreshSystemStatusAsync();
            return;
        }
        _windowActivatedForUser = true;
        _ = RefreshSystemStatusAsync();
    }

    public void UpdateSteamSessionState(SteamSessionState state)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            _ = RefreshSystemStatusAsync();
        });
    }

    private void LaunchAtWindowsStartupToggleSwitch_Toggled(object sender, RoutedEventArgs args)
    {
        if (_isLoadingStartupSettings)
        {
            return;
        }

        var launchAtWindowsStartup = LaunchAtWindowsStartupToggleSwitch.IsOn;
        var result = _startupSettings.ChangeLaunchAtWindowsStartup(launchAtWindowsStartup);
        StartupSettingsStatusText.Text = result.Message;
    }

    private void TestModeToggleSwitch_Toggled(object sender, RoutedEventArgs args)
    {
        if (!_isInitializingTestMode && !_prerequisiteSetupInProgress)
            _developerTestModeState?.SetEnabled(TestModeToggleSwitch.IsOn);
    }

    private void LogLevelComboBox_SelectionChanged(object sender, SelectionChangedEventArgs args)
    {
        if (_isInitializingLogLevel || LogLevelComboBox.SelectedItem is not ComboBoxItem item || item.Content is not string value) return;
        var level = AppSettingsPolicy.Normalize(value);
        _startupSettings.ChangeLogLevel(level);
    }


    private void DeveloperMenuButton_Click(object sender, RoutedEventArgs args)
    {
        var previousPage = _navigationState.CurrentPage;
        ShowPage(_navigationState.OpenDeveloperMenu());
        AppLog.Info("Window", "Developer menu opened.",
            ("PreviousPage", previousPage),
            ("CurrentPage", _navigationState.CurrentPage));
    }

    private async void GenerateEnvironmentDiscoveryReportButton_Click(object sender, RoutedEventArgs args)
    {
        if (_prerequisiteSetupInProgress) return;
        if (Interlocked.Exchange(ref _isGeneratingEnvironmentDiscoveryReport, 1) != 0) return;
        GenerateEnvironmentDiscoveryReportButton.IsEnabled = false;
        OpenEnvironmentDiscoveryFolderButton.IsEnabled = false;
        OpenEnvironmentDiscoveryFolderButton.Visibility = Visibility.Collapsed;
        EnvironmentDiscoveryReportStatusText.Text = "Generating...";
        try
        {
            var result = await _environmentDiscoveryReportGenerator.GenerateAsync();
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
        if (_prerequisiteSetupInProgress) return;
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

    private void DeveloperMenuBackButton_Click(object sender, RoutedEventArgs args)
    {
        ReturnToSettings("BackButton");
    }

    private async void ClawSensorProbeButton_Click(object sender, RoutedEventArgs args)
    {
        if (_clawSensorProbe.State is ClawSensorProbeState.Completed or ClawSensorProbeState.Failed)
        {
            await _clawSensorProbe.DisposeAsync();
            _clawSensorProbe = new ClawSensorProbeCoordinator();
        }
        ShowPage(_navigationState.OpenClawSensorProbe());
        ResetClawSensorProbeUi();
        ClawSensorProbeStatusText.Text = "Ready. This diagnostic is read-only.";
    }
    private async void ClawSensorProbeBackButton_Click(object sender, RoutedEventArgs args)
    {
        if (_clawSensorProbe.State is ClawSensorProbeState.Starting or ClawSensorProbeState.Countdown or ClawSensorProbeState.RecordingPhase)
            await StopClawSensorProbeSafelyAsync();
        ShowPage(_navigationState.ReturnToDeveloperMenu());
    }
    private async void ClawSensorProbeStartButton_Click(object sender, RoutedEventArgs args)
    {
        try
        {
            ClawSensorProbeStartButton.IsEnabled = false;
            _clawSensorProbe.Prepare();
            ClawSensorProbeStatusText.Text = "Discovering Windows motion sensors...";
            _clawSensorProbe.Start();
            var device = _latestSystemStatus?.Device;
            var hardware = _latestSystemStatus?.HardwareCompatibility;
            var resolvedModel = hardware?.DeviceModel?.Value ?? "Unknown / unresolved";
            _clawSensorProbe.SetDeviceIdentity(device?.Manufacturer ?? "Unavailable", device?.Model ?? "Unavailable", device?.BaseBoardProduct ?? "Unavailable", resolvedModel);
            _clawSensorProbe.SetHardwareCompatibility(hardware?.Status.ToString() ?? "Indeterminate", hardware?.DeviceFamily?.Value ?? "Unavailable", hardware?.DeviceModel?.Value ?? "Unavailable", hardware?.Reason ?? "Not captured");
            ClawSensorProbeDeviceText.Text = $"Device: {device?.Manufacturer ?? "Unavailable"} {device?.Model ?? "Unavailable"}";
            ClawSensorProbeModelText.Text = $"Model: {resolvedModel} | Production compatibility: {hardware?.Status.ToString() ?? "Indeterminate"}";
            ClawSensorProbeBoardText.Text = $"Base board: {device?.BaseBoardProduct ?? "Unavailable"}";
            if (!ClawSensorProbeCoordinator.AllowsReadOnlyDiagnostic(hardware))
                throw new InvalidOperationException("This diagnostic is available only on an identified MSI Claw device.");
            await _clawSensorProbe.StartCaptureAsync(_clawSensorProbe.LifecycleCancellation);
            var discovery = _clawSensorProbe.Discovery;
            ClawSensorProbeGyroText.Text = discovery?.Gyroscope is { } gyro ? $"Gyroscope: {gyro.FriendlyName} | ID: {gyro.SensorId} | Type: {gyro.TypeGuid} | Category: {gyro.CategoryGuid} | Manufacturer: {gyro.Manufacturer} | Model: {gyro.Model} | Persistent ID: {gyro.PersistentUniqueId} | Min interval: {gyro.MinimumReportInterval} ms | HID usage: {gyro.CustomUsage} | Status: Ready" : "Gyroscope: Not available";
            ClawSensorProbeAccelText.Text = discovery?.Accelerometer is { } accel ? $"Accelerometer: {accel.FriendlyName} | ID: {accel.SensorId} | Type: {accel.TypeGuid} | Category: {accel.CategoryGuid} | Manufacturer: {accel.Manufacturer} | Model: {accel.Model} | Persistent ID: {accel.PersistentUniqueId} | Min interval: {accel.MinimumReportInterval} ms | HID usage: {accel.CustomUsage} | Status: Ready" : "Accelerometer: Not available";
            StartClawSensorProbeUiTimer();
            UpdateClawSensorProbePhaseUi();
            await _clawSensorProbe.CountdownAsync(status => { ClawSensorProbeStatusText.Text = status; return Task.CompletedTask; }, ClawSensorProbePhaseLabel, _clawSensorProbe.LifecycleCancellation);
            _clawSensorProbe.BeginRecording();
            ClawSensorProbeStatusText.Text = "Recording. Sensor discovery and capture are read-only.";
            ClawSensorProbeStopButton.IsEnabled = true;
            ClawSensorProbeNextPhaseButton.IsEnabled = true;
        }
        catch (OperationCanceledException)
        {
            await _clawSensorProbe.StopAsync();
            _clawSensorProbeUiTimer?.Stop();
            ClawSensorProbeStatusText.Text = "Test stopped. Output: " + _clawSensorProbe.OutputDirectory;
            ClawSensorProbeStopButton.IsEnabled = false;
            ClawSensorProbeNextPhaseButton.IsEnabled = false;
            ClawSensorProbeDoneButton.IsEnabled = true;
            ClawSensorProbeOpenFolderButton.IsEnabled = _clawSensorProbe.HasReport;
            UpdateClawSensorProbeSummary("Test stopped");
        }
        catch (Exception exception)
        {
            await _clawSensorProbe.FailAsync(exception.Message);
            ClawSensorProbeOpenFolderButton.IsEnabled = _clawSensorProbe.HasReport;
            ClawSensorProbeStatusText.Text = $"Test failed: {exception.Message}";
            ClawSensorProbeErrorText.Text = exception.Message;
            ClawSensorProbeDoneButton.IsEnabled = true;
            UpdateClawSensorProbeSummary("Test failed");
        }
    }
    private async void ClawSensorProbeNextPhaseButton_Click(object sender, RoutedEventArgs args)
    {
        try
        {
            if (_clawSensorProbe.State != ClawSensorProbeState.RecordingPhase) return;
            ClawSensorProbeBackPhaseButton.IsEnabled = false;
            ClawSensorProbeNextPhaseButton.IsEnabled = false;
            await _clawSensorProbe.AdvancePhaseAsync(status => { ClawSensorProbeStatusText.Text = status; return Task.CompletedTask; }, ClawSensorProbePhaseLabel, UpdateClawSensorProbePhaseUi, _clawSensorProbe.LifecycleCancellation);
            if (_clawSensorProbe.State == ClawSensorProbeState.Completed)
            {
                await _clawSensorProbe.StopAsync(); _clawSensorProbeUiTimer?.Stop();
                ClawSensorProbeStatusText.Text = "Test completed. Output: " + _clawSensorProbe.OutputDirectory;
                ClawSensorProbeStopButton.IsEnabled = false; ClawSensorProbeDoneButton.IsEnabled = true; ClawSensorProbeOpenFolderButton.IsEnabled = _clawSensorProbe.HasReport;
                UpdateClawSensorProbeSummary("Test completed");
            }
            UpdateClawSensorProbePhaseUi();
        }
        catch (OperationCanceledException)
        {
            await _clawSensorProbe.StopAsync(); _clawSensorProbeUiTimer?.Stop();
            ClawSensorProbeStatusText.Text = "Test stopped. Output: " + _clawSensorProbe.OutputDirectory;
            ClawSensorProbeStopButton.IsEnabled = false; ClawSensorProbeNextPhaseButton.IsEnabled = false; ClawSensorProbeDoneButton.IsEnabled = true; ClawSensorProbeOpenFolderButton.IsEnabled = _clawSensorProbe.HasReport;
            UpdateClawSensorProbeSummary("Test stopped");
        }
        catch (Exception exception)
        {
            await _clawSensorProbe.FailAsync(exception.Message);
            _clawSensorProbeUiTimer?.Stop();
            ClawSensorProbeStatusText.Text = $"Test failed: {exception.Message}";
            ClawSensorProbeErrorText.Text = exception.Message;
            ClawSensorProbeStopButton.IsEnabled = false; ClawSensorProbeNextPhaseButton.IsEnabled = false; ClawSensorProbeBackPhaseButton.IsEnabled = false; ClawSensorProbeDoneButton.IsEnabled = true; ClawSensorProbeOpenFolderButton.IsEnabled = _clawSensorProbe.HasReport;
            UpdateClawSensorProbeSummary("Test failed");
        }
    }
    private async void ClawSensorProbeBackPhaseButton_Click(object sender, RoutedEventArgs args)
    {
        try
        {
            ClawSensorProbeBackPhaseButton.IsEnabled = false;
            ClawSensorProbeNextPhaseButton.IsEnabled = false;
            await _clawSensorProbe.RevisitPreviousPhaseAsync(status => { ClawSensorProbeStatusText.Text = status; return Task.CompletedTask; }, ClawSensorProbePhaseLabel, UpdateClawSensorProbePhaseUi, _clawSensorProbe.LifecycleCancellation);
            UpdateClawSensorProbePhaseUi();
        }
        catch (OperationCanceledException)
        {
            await _clawSensorProbe.StopAsync(); _clawSensorProbeUiTimer?.Stop();
            ClawSensorProbeStatusText.Text = "Test stopped. Output: " + _clawSensorProbe.OutputDirectory;
            ClawSensorProbeStopButton.IsEnabled = false; ClawSensorProbeNextPhaseButton.IsEnabled = false; ClawSensorProbeDoneButton.IsEnabled = true; ClawSensorProbeOpenFolderButton.IsEnabled = _clawSensorProbe.HasReport;
            UpdateClawSensorProbeSummary("Test stopped");
        }
        catch (Exception exception)
        {
            await _clawSensorProbe.FailAsync(exception.Message);
            _clawSensorProbeUiTimer?.Stop();
            ClawSensorProbeStatusText.Text = $"Test failed: {exception.Message}";
            ClawSensorProbeErrorText.Text = exception.Message;
            ClawSensorProbeStopButton.IsEnabled = false; ClawSensorProbeNextPhaseButton.IsEnabled = false; ClawSensorProbeBackPhaseButton.IsEnabled = false; ClawSensorProbeDoneButton.IsEnabled = true; ClawSensorProbeOpenFolderButton.IsEnabled = _clawSensorProbe.HasReport;
            UpdateClawSensorProbeSummary("Test failed");
        }
    }
    private async void ClawSensorProbeStopButton_Click(object sender, RoutedEventArgs args) { ClawSensorProbeStopButton.IsEnabled = false; ClawSensorProbeNextPhaseButton.IsEnabled = false; ClawSensorProbeStatusText.Text = "Stopping sensor capture..."; await StopClawSensorProbeSafelyAsync(); _clawSensorProbeUiTimer?.Stop(); ClawSensorProbeStatusText.Text = "Test stopped. Output: " + _clawSensorProbe.OutputDirectory; ClawSensorProbeDoneButton.IsEnabled = true; ClawSensorProbeOpenFolderButton.IsEnabled = _clawSensorProbe.HasReport; UpdateClawSensorProbeSummary(); }
    private void ClawSensorProbeOpenFolderButton_Click(object sender, RoutedEventArgs args)
    {
        if (string.IsNullOrWhiteSpace(_clawSensorProbe.OutputDirectory))
        {
            ClawSensorProbeErrorText.Text = "The diagnostic output directory is unavailable.";
            return;
        }

        try { Process.Start(new ProcessStartInfo("explorer.exe", $"\"{_clawSensorProbe.OutputDirectory}\"") { UseShellExecute = true }); }
        catch (Exception exception) { ClawSensorProbeErrorText.Text = $"The diagnostic log folder could not be opened: {exception.Message}"; }
    }
    private async void ClawSensorProbeDoneButton_Click(object sender, RoutedEventArgs args) { if (_clawSensorProbe.State is ClawSensorProbeState.Starting or ClawSensorProbeState.Countdown or ClawSensorProbeState.RecordingPhase) await StopClawSensorProbeSafelyAsync(); ShowPage(_navigationState.ReturnToDeveloperMenu()); }
    private async Task StopClawSensorProbeSafelyAsync()
    {
        try { await _clawSensorProbe.StopAsync(); }
        catch (Exception exception) { ClawSensorProbeErrorText.Text = $"Probe shutdown warning: {exception.Message}"; }
    }
    private void ResetClawSensorProbeUi()
    {
        ClawSensorProbeStartButton.IsEnabled = true;
        ClawSensorProbeStopButton.IsEnabled = false;
        ClawSensorProbeBackPhaseButton.IsEnabled = false;
        ClawSensorProbeNextPhaseButton.IsEnabled = false;
        ClawSensorProbeDoneButton.IsEnabled = false;
        ClawSensorProbeOpenFolderButton.IsEnabled = false;
        ClawSensorProbeDeviceText.Text = "Device: Unavailable";
        ClawSensorProbeModelText.Text = "Model: Unknown / unresolved";
        ClawSensorProbeBoardText.Text = "Base board: Unavailable";
        ClawSensorProbeGyroText.Text = "Gyroscope: Not discovered";
        ClawSensorProbeAccelText.Text = "Accelerometer: Not discovered";
        ClawSensorProbePhaseText.Text = "Step: Not started";
        ClawSensorProbeInstructionText.Text = "Keep Still: Place the device on a flat surface and do not touch it.";
        ClawSensorProbeLiveText.Text = $"Gyroscope: waiting{Environment.NewLine}Accelerometer: waiting";
        ClawSensorProbeSummaryText.Text = string.Empty;
        ClawSensorProbeErrorText.Text = string.Empty;
    }
    private void UpdateClawSensorProbeSummary(string result = "Test stopped") { var gyro = _clawSensorProbe.GyroscopeSummary; var accel = _clawSensorProbe.AccelerometerSummary; ClawSensorProbeSummaryText.Text = $"{result}{Environment.NewLine}Output directory: {_clawSensorProbe.OutputDirectory ?? "Unavailable"}{Environment.NewLine}Gyroscope samples: {gyro?.SampleCount ?? 0}, average rate: {gyro?.EffectiveHz:0.0} Hz, interval: {gyro?.MinimumIntervalMs:0.###}-{gyro?.MaximumIntervalMs:0.###} ms, dropped: {_clawSensorProbe.DroppedGyroscopeCount}{Environment.NewLine}Accelerometer samples: {accel?.SampleCount ?? 0}, average rate: {accel?.EffectiveHz:0.0} Hz, interval: {accel?.MinimumIntervalMs:0.###}-{accel?.MaximumIntervalMs:0.###} ms, dropped: {_clawSensorProbe.DroppedAccelerometerCount}{Environment.NewLine}Dropped samples total: {_clawSensorProbe.DroppedSampleCount}"; }
    private void UpdateClawSensorProbePhaseUi() { var index = _clawSensorProbe.Workflow.CurrentIndex; var phase = index >= 0 ? _clawSensorProbe.Workflow.Visits.Last().Phase : ClawSensorProbePhase.REST; ClawSensorProbePhaseText.Text = index >= 0 ? $"Step: {index + 1} of {ClawSensorProbeWorkflow.Phases.Count} - {ClawSensorProbePhaseLabel(phase)}" : "Step: Not started"; ClawSensorProbeInstructionText.Text = phase switch { ClawSensorProbePhase.REST => "Keep Still: Place the device on a flat surface and do not touch it.", ClawSensorProbePhase.ROLL_LEFT => "Roll Left: Slowly lower the left side, then return to the starting position.", ClawSensorProbePhase.ROLL_RIGHT => "Roll Right: Slowly lower the right side, then return to the starting position.", ClawSensorProbePhase.PITCH_UP => "Pitch Up: Slowly tilt the top upward, then return to the starting position.", ClawSensorProbePhase.PITCH_DOWN => "Pitch Down: Slowly tilt the top downward, then return to the starting position.", ClawSensorProbePhase.YAW_LEFT => "Yaw Left: Keep the device level and rotate it left, then return to center.", _ => "Yaw Right: Keep the device level and rotate it right, then return to center." }; ClawSensorProbeBackPhaseButton.IsEnabled = index > 0; ClawSensorProbeNextPhaseButton.Content = index == ClawSensorProbeWorkflow.Phases.Count - 1 ? "Finish Test" : "Next"; }
    private static string ClawSensorProbePhaseLabel(ClawSensorProbePhase phase) => phase switch
    {
        ClawSensorProbePhase.REST => "Keep Still",
        ClawSensorProbePhase.ROLL_LEFT => "Roll Left",
        ClawSensorProbePhase.ROLL_RIGHT => "Roll Right",
        ClawSensorProbePhase.PITCH_UP => "Pitch Up",
        ClawSensorProbePhase.PITCH_DOWN => "Pitch Down",
        ClawSensorProbePhase.YAW_LEFT => "Yaw Left",
        _ => "Yaw Right"
    };
    private void StartClawSensorProbeUiTimer()
    {
        _clawSensorProbeUiTimer ??= DispatcherQueue.GetForCurrentThread().CreateTimer();
        _clawSensorProbeUiTimer.Interval = TimeSpan.FromMilliseconds(200);
        _clawSensorProbeUiTimer.Tick -= ClawSensorProbeUiTimer_Tick;
        _clawSensorProbeUiTimer.Tick += ClawSensorProbeUiTimer_Tick;
        _clawSensorProbeUiTimer.Start();
    }
    private void ClawSensorProbeUiTimer_Tick(DispatcherQueueTimer sender, object args)
    {
        var snapshot = _clawSensorProbe.LiveSnapshot;
        if (snapshot is null) return;
        if (_clawSensorProbe.ReaderErrors.Count > 0)
        {
            ClawSensorProbeErrorText.Text = string.Join(Environment.NewLine, _clawSensorProbe.ReaderErrors);
            _ = DispatcherQueue.TryEnqueue(async () =>
            {
                await _clawSensorProbe.FailOnReaderFaultAsync();
                _clawSensorProbeUiTimer?.Stop();
                ClawSensorProbeStatusText.Text = "Test failed: sensor reader stopped unexpectedly.";
                ClawSensorProbeStopButton.IsEnabled = false;
                ClawSensorProbeBackPhaseButton.IsEnabled = false;
                ClawSensorProbeNextPhaseButton.IsEnabled = false;
                ClawSensorProbeDoneButton.IsEnabled = true;
                ClawSensorProbeOpenFolderButton.IsEnabled = _clawSensorProbe.HasReport;
                UpdateClawSensorProbeSummary("Test failed");
            });
            return;
        }
        var gyro = snapshot.Gyro; var accel = snapshot.Accel;
        ClawSensorProbeLiveText.Text = $"Status: {_clawSensorProbe.CaptureContext.Mode}{Environment.NewLine}Gyroscope raw: X={gyro.X:0.###}, Y={gyro.Y:0.###}, Z={gyro.Z:0.###} | {gyro.Hz:0.0} Hz | {gyro.Count} samples{Environment.NewLine}Accelerometer raw: X={accel.X:0.###}, Y={accel.Y:0.###}, Z={accel.Z:0.###} | {accel.Hz:0.0} Hz | {accel.Count} samples";
    }

    private async void OnWindowClosed(object sender, WindowEventArgs args)
    {
        if (_clawSensorProbe.State is ClawSensorProbeState.Starting or ClawSensorProbeState.Countdown or ClawSensorProbeState.RecordingPhase)
            await StopClawSensorProbeSafelyAsync();
        try { await _clawSensorProbe.DisposeAsync(); }
        catch (Exception exception) { AppLog.Warn("ClawSensorProbe", "Probe cleanup failed.", exception); }
    }

    private void MainNavigationView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        var selectedTag = (args.SelectedItem as NavigationViewItem)?.Tag as string;
        ShowPage(_navigationState.SelectNavigationItem(args.IsSettingsSelected, selectedTag));
    }

    private void ShowPage(MainNavigationPage page)
    {
        StatusContent.Visibility = page == MainNavigationPage.Status ? Visibility.Visible : Visibility.Collapsed;
        HowToUseContent.Visibility = page == MainNavigationPage.HowToUse ? Visibility.Visible : Visibility.Collapsed;
        SettingsContent.Visibility = page == MainNavigationPage.Settings ? Visibility.Visible : Visibility.Collapsed;
        DeveloperMenuContent.Visibility = page == MainNavigationPage.DeveloperMenu ? Visibility.Visible : Visibility.Collapsed;
        ClawSensorProbeContent.Visibility = page == MainNavigationPage.ClawSensorProbe ? Visibility.Visible : Visibility.Collapsed;
        if (page == MainNavigationPage.Status) _ = RefreshSystemStatusAsync();
    }

    private async void RefreshStatusButton_Click(object sender, RoutedEventArgs args) => await RefreshSystemStatusAsync();

    private async Task RefreshSystemStatusAsync()
    {
        if (_prerequisiteSetupInProgress) return;
        if (Interlocked.Exchange(ref _isRefreshingStatus, 1) != 0) return;
        RefreshStatusButton.IsEnabled = false;
        try { RenderSystemStatus(await _systemStatusProvider.CaptureAsync()); }
        catch (Exception exception) { AppLog.Warn("Status", "System status refresh failed.", exception, ("Reason", "SnapshotCaptureFailed")); }
        finally { RefreshStatusButton.IsEnabled = true; Volatile.Write(ref _isRefreshingStatus, 0); }
    }

    private void RenderSystemStatus(SystemStatusSnapshot snapshot)
    {
        _latestSystemStatus = snapshot;
        DeviceManufacturerText.Text = snapshot.Device.Manufacturer;
        DeviceModelText.Text = snapshot.Device.Model;
        DeviceSupportText.Text = snapshot.HardwareCompatibility.Status switch
        {
            HardwareCompatibilityStatus.Supported => "Supported",
            HardwareCompatibilityStatus.Unsupported => "Unsupported",
            _ => "Compatibility unknown"
        };
        DeviceBoardGpuText.Text = $"Board: {snapshot.Device.BaseBoardProduct}  GPU: {string.Join(", ", snapshot.Device.GpuModels)}";
        Replace(_softwareCards, snapshot.ControllerSoftware.Select(item => new StatusCardViewModel(item.DisplayName, FormatSoftwareStatus(item), item.Reason)));
        Replace(_componentCards,
        [
            new("HidHide", snapshot.Prerequisites.HidHide.Status.ToString(), snapshot.Prerequisites.HidHide.Reason),
            new("usbip-win2", snapshot.Prerequisites.UsbIpWin2.Status.ToString(), snapshot.Prerequisites.UsbIpWin2.Reason),
            new("VIIPER", snapshot.Prerequisites.Viiper.Status.ToString(), snapshot.Prerequisites.Viiper.Reason)
        ]);
        Replace(_externalControllerCards, ExternalControllerStatusCardFactory.Create(snapshot.ExternalController));
        var setup = EvaluateFirstTimeSetup(snapshot);
        var canInstall = setup.CanInstallRequiredComponents;
        var addonPresentation = FirstTimeSetupPresentation.GetAddonPresentation(setup, snapshot.Prerequisites, snapshot.Addon);
        Replace(_runtimeCards,
        [
            new("Steam", snapshot.Steam.IsActive ? "Active" : "Inactive", $"RunningAppID: {snapshot.Steam.RunningAppId}"),
            new("Steam Input Addon", addonPresentation.Status, addonPresentation.Reason)
        ]);
        if (PrerequisiteSetupPromptPolicy.IsInstallable(setup))
        {
            if (_windowActivatedForUser)
                _ = PromptForPrerequisiteSetupAsync();
            else
                RequestSetupPromptActivation();
        }
    }

    private void RequestSetupPromptActivation()
    {
        if (_setupPromptActive || _setupPromptDeclinedForCurrentProcess || _setupPromptPendingActivation) return;
        _setupPromptPendingActivation = true;
        DispatcherQueue.TryEnqueue(() =>
        {
            if (_windowActivatedForUser) return;
            AppWindow.Show();
            Activate();
        });
    }

    private async Task PromptForPrerequisiteSetupAsync()
    {
        if (_setupPromptActive || _setupPromptDeclinedForCurrentProcess || _prerequisiteSetupInProgress) return;
        if (Content.XamlRoot is null)
        {
            _setupPromptPendingActivation = true;
            return;
        }
        _setupPromptPendingActivation = false;
        _setupPromptActive = true;
        try
        {
            var dialog = new ContentDialog
            {
                Title = "Setup required",
                Content = "Steam Input Addon for Claw needs a few required components. Install them now?",
                PrimaryButtonText = "Install",
                CloseButtonText = "Not now",
                XamlRoot = Content.XamlRoot
            };
            AppLog.Info("PrerequisiteSetupPrompt", "Prerequisite setup prompt shown.", ("Action", "Shown"));
            if (await dialog.ShowAsync() != ContentDialogResult.Primary)
            {
                _setupPromptDeclinedForCurrentProcess = true;
                AppLog.Info("PrerequisiteSetupPrompt", "Prerequisite setup prompt declined.", ("Action", "Declined"));
                return;
            }
            AppLog.Info("PrerequisiteSetupPrompt", "Prerequisite setup prompt accepted.", ("Action", "Accepted"));
            await RunPrerequisiteSetupAsync();
        }
        catch (Exception exception) { AppLog.Warn("PrerequisiteSetup", "Prerequisite setup prompt failed.", exception); }
        finally { _setupPromptActive = false; }
    }

    private FirstTimeSetupAssessment EvaluateFirstTimeSetup(SystemStatusSnapshot snapshot)
    {
        var receipt = _hidHideReceiptStore.Load();
        var usbReceipt = new UsbIpWin2ProvisioningReceiptStore(VelopackAppPaths.UsbIpWin2ProvisioningReceiptPath).Load();
        var hidPackage = new WindowsHidHidePackageProbe().Inspect();
        var usbPackage = new WindowsUsbIpWin2PackageProbe().Inspect();
        var storage = ProvisioningStorageSecurity.Inspect(VelopackAppPaths.ProvisioningStateDirectory);
        var hidHideState = receipt.IsCorrupt || storage.Status is ProvisioningStorageStatus.Unsafe or ProvisioningStorageStatus.Indeterminate
            ? ComponentProvisioningState.Corrupt
            : receipt.Receipt is not null ? ToComponentProvisioningState(receipt.Receipt.State)
            : File.Exists(VelopackAppPaths.LegacyHidHideProvisioningReceiptPath) ? ComponentProvisioningState.Legacy
            : ComponentProvisioningState.None;
        var usbIpState = usbReceipt.IsCorrupt || storage.Status is ProvisioningStorageStatus.Unsafe or ProvisioningStorageStatus.Indeterminate
            ? ComponentProvisioningState.Corrupt
            : usbReceipt.Receipt is not null ? ToComponentProvisioningState(usbReceipt.Receipt.State)
            : ComponentProvisioningState.None;
        var hidBootChanged = receipt.Receipt is { State: HidHideProvisioningReceiptState.InstalledPendingReboot } hidPending && BootSession.HasChangedSince(hidPending.StartedAtUtc);
        var usbBootChanged = usbReceipt.Receipt is { State: UsbIpWin2ProvisioningReceiptState.InstalledPendingReboot } usbPending && BootSession.HasChangedSince(usbPending.StartedAtUtc);
        var hidInstallation = ComponentInstallationAssessmentPolicy.AssessHidHide(hidPackage, snapshot.Prerequisites.HidHide, HidHidePackageMetadata.BundledVersion.ToString());
        var usbInstallation = ComponentInstallationAssessmentPolicy.AssessUsbIp(usbPackage, snapshot.Prerequisites.UsbIpWin2, UsbIpWin2PackageMetadata.BundledVersion.ToString());
        return FirstTimeSetupPolicy.Evaluate(new FirstTimeSetupInput(
            snapshot.HardwareCompatibility, snapshot.Compatibility, snapshot.RecoverySafe, snapshot.ExternalController,
            SteamSessionState.FromRunningAppId(snapshot.Steam.IsActive ? snapshot.Steam.RunningAppId : 0),
            snapshot.Prerequisites.HidHide, snapshot.Prerequisites.UsbIpWin2, hidInstallation, usbInstallation, new(hidHideState, usbIpState, hidBootChanged, usbBootChanged)));
    }

    private async Task RunPrerequisiteSetupAsync()
    {
        if (_prerequisiteSetupInProgress) return;
        _prerequisiteSetupInProgress = true;
        UpdatePrerequisiteSetupBusyUi();
        try
        {
            var current = await _systemStatusProvider.CaptureAsync();
            var currentSetup = EvaluateFirstTimeSetup(current);
            AppLog.Info("PrerequisiteSetup", "Prerequisite setup requested.",
                ("HidHideStatus", current.Prerequisites.HidHide.Status),
                ("UsbIpWin2Status", current.Prerequisites.UsbIpWin2.Status),
                ("CompatibilityStatus", current.Compatibility.Status),
                ("CompatibilityReason", current.Compatibility.Reason),
                ("ExternalControllerStatus", current.ExternalController.Status),
                ("SteamActive", current.Steam.IsActive),
                ("RecoverySafe", current.RecoverySafe));
            if (!PrerequisiteSetupPromptPolicy.IsInstallable(currentSetup))
            {
                RenderSystemStatus(current);
                return;
            }
            var executable = Environment.ProcessPath ?? throw new InvalidOperationException("The executable path is unavailable.");
            var result = await PrerequisiteSetupRunnerPolicy.RunIfInstallableAsync(
                currentSetup, _prerequisiteSetupRunner, executable, ElevatedPrerequisiteSetup.Argument, CancellationToken.None);
            if (result is null) return;
            var resultKind = ElevatedPrerequisiteSetup.TranslateExitCode(result);
            AppLog.Info("PrerequisiteSetup", "Elevated prerequisite setup finished.", ("Result", resultKind));
            if (resultKind == ElevatedPrerequisiteSetup.ResultKind.RebootRequired)
            {
                var restartDialog = new ContentDialog
                {
                    Title = "Restart required",
                    Content = "Windows needs to restart to finish setting up Steam Input Addon for Claw.",
                    PrimaryButtonText = "Restart now",
                    CloseButtonText = "Later",
                    XamlRoot = Content.XamlRoot
                };
                if (await restartDialog.ShowAsync() == ContentDialogResult.Primary)
                    Process.Start(new ProcessStartInfo("shutdown.exe", "/r /t 0") { UseShellExecute = false });
            }
            else if (resultKind is ElevatedPrerequisiteSetup.ResultKind.Blocked or ElevatedPrerequisiteSetup.ResultKind.AlreadyInProgress ||
                     resultKind is ElevatedPrerequisiteSetup.ResultKind.Failed)
            {
                var message = resultKind == ElevatedPrerequisiteSetup.ResultKind.AlreadyInProgress
                    ? "Another setup operation is already in progress."
                    : "Setup couldn't be completed. Check Status or the application log for details.";
                await new ContentDialog
                {
                    Title = "Setup unavailable",
                    Content = message,
                    CloseButtonText = "OK",
                    XamlRoot = Content.XamlRoot
                }.ShowAsync();
            }
        }
        finally
        {
            _prerequisiteSetupInProgress = false;
            UpdatePrerequisiteSetupBusyUi();
            await RefreshSystemStatusAsync();
        }
    }

    private void UpdatePrerequisiteSetupBusyUi()
    {
        PrerequisiteSetupBusyOverlay.Visibility = _prerequisiteSetupInProgress ? Visibility.Visible : Visibility.Collapsed;
        MainNavigationView.IsHitTestVisible = !_prerequisiteSetupInProgress;
        RefreshStatusButton.IsEnabled = !_prerequisiteSetupInProgress;
    }

    private void ShowStatusButton_Click(object sender, RoutedEventArgs args)
    {
        MainNavigationView.SelectedItem = StatusNavigationItem;
        ShowPage(_navigationState.SelectNavigationItem(false, "Status"));
    }

    private static void Replace(ObservableCollection<StatusCardViewModel> destination, IEnumerable<StatusCardViewModel> source) { destination.Clear(); foreach (var item in source) destination.Add(item); }
    internal static string FormatSoftwareStatus(ControllerSoftwareStatus item) => item.Runtime switch
    {
        SoftwareRuntimeStatus.Running => "Running",
        SoftwareRuntimeStatus.Starting => "Starting",
        SoftwareRuntimeStatus.Indeterminate => "Indeterminate",
        _ when item.Installation == SoftwareInstallationStatus.Installed => "Installed / Not running",
        _ when item.Installation == SoftwareInstallationStatus.NotInstalled => "Not installed",
        _ => "Indeterminate"
    };
    private static string FormatAddonStatus(AddonOperationalStatus status) => status switch { AddonOperationalStatus.WaitingForSteam => "Waiting for Steam", AddonOperationalStatus.SetupRequired => "Setup required", AddonOperationalStatus.RecoveryRequired => "Recovery required", AddonOperationalStatus.Unsupported => "Unsupported", _ => status.ToString() };
    private static ComponentProvisioningState ToComponentProvisioningState(HidHideProvisioningReceiptState state) => state switch
    {
        HidHideProvisioningReceiptState.Provisioned => ComponentProvisioningState.Provisioned,
        HidHideProvisioningReceiptState.InstallStarted => ComponentProvisioningState.InstallStarted,
        HidHideProvisioningReceiptState.InstalledPendingReboot => ComponentProvisioningState.PendingReboot,
        HidHideProvisioningReceiptState.AttemptFailed => ComponentProvisioningState.AttemptFailed,
        HidHideProvisioningReceiptState.AttemptCancelled => ComponentProvisioningState.AttemptCancelled,
        _ => ComponentProvisioningState.Indeterminate
    };
    private static ComponentProvisioningState ToComponentProvisioningState(UsbIpWin2ProvisioningReceiptState state) => state switch
    {
        UsbIpWin2ProvisioningReceiptState.Provisioned => ComponentProvisioningState.Provisioned,
        UsbIpWin2ProvisioningReceiptState.InstallStarted => ComponentProvisioningState.InstallStarted,
        UsbIpWin2ProvisioningReceiptState.InstalledPendingReboot => ComponentProvisioningState.PendingReboot,
        UsbIpWin2ProvisioningReceiptState.AttemptFailed => ComponentProvisioningState.AttemptFailed,
        UsbIpWin2ProvisioningReceiptState.AttemptCancelled => ComponentProvisioningState.AttemptCancelled,
        _ => ComponentProvisioningState.Indeterminate
    };
    private static ISystemStatusProvider CreateDefaultSystemStatusProvider()
    {
        var devices = new WindowsControllerDeviceEnumerator();
        var adapter = new MsiClawDeviceAdapter(devices);
        var classifier = new ControllerDeviceClassifier(adapter.InternalControllerMatcher);
        return new SystemStatusProvider(new WindowsDeviceInformationProvider(),
        new WindowsDeviceProbeContextFactory(new WindowsDeviceIdentitySource(), devices),
        new HardwareCompatibilityEvaluator(new HandheldDeviceRegistry([adapter])),
        [new MsiCenterMSoftwareStatusProvider(), new ClawTweaksSoftwareStatusProvider(new ClawTweaksInstallationProbe(), new ClawTweaksRuntimeDetector()), new HandheldCompanionSoftwareStatusProvider(new HandheldCompanionRuntimeDetector())],
        new RuntimePrerequisiteInspector(new HidHidePrerequisiteInspector(new HidHideDriverClient()), new UsbIpWin2PrerequisiteInspector(new WindowsUsbIpWin2DeviceProbe(devices)), new ViiperRuntimeInspector()),
        () => SteamSessionState.FromRunningAppId(0), () => new ExternalControllerDetector(devices, classifier).Detect(), () => true);
    }

    private void MainNavigationView_PointerPressed(object sender, PointerRoutedEventArgs args)
    {
        if (_navigationState.CurrentPage != MainNavigationPage.DeveloperMenu ||
            !args.GetCurrentPoint(MainNavigationView).Properties.IsXButton1Pressed)
        {
            return;
        }

        ReturnToSettings("MouseBackButton");
        args.Handled = true;
    }

    private void ReturnToSettings(string reason)
    {
        var previousPage = _navigationState.CurrentPage;
        ShowPage(_navigationState.ReturnToSettings());
        AppLog.Info("Window", "Developer menu closed.",
            ("PreviousPage", previousPage),
            ("CurrentPage", _navigationState.CurrentPage),
            ("Reason", reason));
    }

    private void MainNavigationView_Loaded(object sender, RoutedEventArgs args)
    {
        SetEnglishSettingsItemContent();
        if (_setupPromptPendingActivation && _windowActivatedForUser)
            _ = PromptForPrerequisiteSetupAsync();

        var navigationItems = MainNavigationView.MenuItems
            .OfType<NavigationViewItem>()
            .Append(MainNavigationView.SettingsItem)
            .OfType<NavigationViewItem>();
        var openPaneLength = navigationItems
            .Select(MeasureDesiredWidth)
            .DefaultIfEmpty(MainNavigationView.OpenPaneLength)
            .Max();

        MainNavigationView.OpenPaneLength = openPaneLength;
    }

    private void SetEnglishSettingsItemContent()
    {
        if (MainNavigationView.SettingsItem is NavigationViewItem settingsItem)
        {
            settingsItem.Content = "Settings";
        }
    }

    private static double MeasureDesiredWidth(NavigationViewItem item)
    {
        item.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        return item.DesiredSize.Width;
    }

    private void ApplyDefaultWindowSize()
    {
        var windowHandle = WindowNative.GetWindowHandle(this);
        var dpi = GetDpiForWindow(windowHandle);
        var windowId = Win32Interop.GetWindowIdFromWindow(windowHandle);
        var displayArea = DisplayArea.GetFromWindowId(windowId, DisplayAreaFallback.Nearest);
        var workArea = displayArea?.WorkArea;
        var size = DpiAwareWindowSize.Calculate(dpi, workArea?.Width ?? 0, workArea?.Height ?? 0);

        AppWindow.Resize(new Windows.Graphics.SizeInt32(size.Width, size.Height));

        if (workArea is not null)
        {
            AppWindow.Move(new Windows.Graphics.PointInt32(
                workArea.Value.X + (workArea.Value.Width - size.Width) / 2,
                workArea.Value.Y + (workArea.Value.Height - size.Height) / 2));
        }
    }

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr windowHandle);

    private static string GetDisplayVersion()
    {
        return Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? "0.1.0";
    }

    internal static string FormatWindowTitle(string version) => $"Steam Input Addon for Claw v{version}";

}
