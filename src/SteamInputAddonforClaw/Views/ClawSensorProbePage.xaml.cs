using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SteamInputAddonforClaw.Diagnostics;
using SteamInputAddonforClaw.Diagnostics.ClawSensorProbe;
using SteamInputAddonforClaw.Status;
using System.Diagnostics;

namespace SteamInputAddonforClaw.Views;

public sealed partial class ClawSensorProbePage : UserControl
{
    private ClawSensorProbeCoordinator _clawSensorProbe = new();
    private DispatcherQueueTimer? _clawSensorProbeUiTimer;
    private Func<SystemStatusSnapshot?>? _latestSystemStatusProvider;

    public event EventHandler? ReturnToDeveloperMenuRequested;

    public ClawSensorProbePage()
    {
        InitializeComponent();
    }

    internal void Initialize(Func<SystemStatusSnapshot?> latestSystemStatusProvider)
    {
        _latestSystemStatusProvider = latestSystemStatusProvider;
    }

    internal async Task PrepareForShowAsync()
    {
        if (_clawSensorProbe.State is ClawSensorProbeState.Completed or ClawSensorProbeState.Failed)
        {
            await _clawSensorProbe.DisposeAsync();
            _clawSensorProbe = new ClawSensorProbeCoordinator();
        }
        ResetClawSensorProbeUi();
        ClawSensorProbeStatusText.Text = "Ready. This diagnostic is read-only.";
    }

    internal async Task ShutdownAsync()
    {
        if (_clawSensorProbe.State is ClawSensorProbeState.Starting or ClawSensorProbeState.Countdown or ClawSensorProbeState.RecordingPhase)
            await StopClawSensorProbeSafelyAsync();
        try { await _clawSensorProbe.DisposeAsync(); }
        catch (Exception exception) { AppLog.Warn("ClawSensorProbe", "Probe cleanup failed.", exception); }
    }

    private async void BackButton_Click(object sender, RoutedEventArgs args)
    {
        if (_clawSensorProbe.State is ClawSensorProbeState.Starting or ClawSensorProbeState.Countdown or ClawSensorProbeState.RecordingPhase)
            await StopClawSensorProbeSafelyAsync();
        _clawSensorProbeUiTimer?.Stop();
        ReturnToDeveloperMenuRequested?.Invoke(this, EventArgs.Empty);
    }

    private async void ClawSensorProbeStartButton_Click(object sender, RoutedEventArgs args)
    {
        try
        {
            ClawSensorProbeStartButton.IsEnabled = false;
            _clawSensorProbe.Prepare();
            ClawSensorProbeStatusText.Text = "Discovering Windows motion sensors...";
            _clawSensorProbe.Start();
            var latest = _latestSystemStatusProvider?.Invoke();
            var device = latest?.Device;
            var hardware = latest?.HardwareCompatibility;
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

    private async void ClawSensorProbeDoneButton_Click(object sender, RoutedEventArgs args)
    {
        if (_clawSensorProbe.State is ClawSensorProbeState.Starting or ClawSensorProbeState.Countdown or ClawSensorProbeState.RecordingPhase)
            await StopClawSensorProbeSafelyAsync();
        ReturnToDeveloperMenuRequested?.Invoke(this, EventArgs.Empty);
    }

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

    private void UpdateClawSensorProbePhaseUi() { var index = _clawSensorProbe.Workflow.CurrentIndex; var phase = index >= 0 ? _clawSensorProbe.Workflow.Visits.Last().Phase : ClawSensorProbePhase.REST; ClawSensorProbePhaseText.Text = index >= 0 ? $"Step: {index + 1} of {ClawSensorProbeWorkflow.Phases.Count} - {ClawSensorProbePhaseLabel(phase)}" : "Step: Not started"; ClawSensorProbeInstructionText.Text = phase switch { ClawSensorProbePhase.REST => "Keep Still: Place the device on a flat surface and do not touch it.", ClawSensorProbePhase.ROLL_LEFT => "Roll Left: Slowly lower the left side, then return to the starting position.", ClawSensorProbePhase.ROLL_RIGHT => "Roll Right: Slowly lower the right side, then return to the starting position.", ClawSensorProbePhase.PITCH_UP => "Pitch Up: Slowly tilt the top upward, then return to the starting position.", ClawSensorProbePhase.PITCH_DOWN => "Pitch Down: Slowly tilt the top downward, then return to the starting position.", ClawSensorProbePhase.YAW_LEFT => "Yaw Left: Keep the device level and rotate it left, then return to center.", _ => "Yaw Right: Keep the device level and rotate it right, then return to center." }; var recording = _clawSensorProbe.State == ClawSensorProbeState.RecordingPhase; ClawSensorProbeBackPhaseButton.IsEnabled = recording && index > 0; ClawSensorProbeNextPhaseButton.IsEnabled = recording; ClawSensorProbeNextPhaseButton.Content = index == ClawSensorProbeWorkflow.Phases.Count - 1 ? "Finish Test" : "Next"; }

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
                try
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
                }
                catch (Exception exception)
                {
                    AppLog.Warn("ClawSensorProbe", "Automatic reader-fault cleanup failed.", exception);
                    ClawSensorProbeErrorText.Text = $"Sensor reader failed and cleanup reported a warning: {exception.Message}";
                }
            });
            return;
        }
        var gyro = snapshot.Gyro; var accel = snapshot.Accel;
        ClawSensorProbeLiveText.Text = $"Status: {_clawSensorProbe.CaptureContext.Mode}{Environment.NewLine}Gyroscope raw: X={gyro.X:0.###}, Y={gyro.Y:0.###}, Z={gyro.Z:0.###} | {gyro.Hz:0.0} Hz | {gyro.Count} samples{Environment.NewLine}Accelerometer raw: X={accel.X:0.###}, Y={accel.Y:0.###}, Z={accel.Z:0.###} | {accel.Hz:0.0} Hz | {accel.Count} samples";
    }
}
