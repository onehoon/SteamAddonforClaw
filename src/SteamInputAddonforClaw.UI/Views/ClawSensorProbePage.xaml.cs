using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Dispatching;
using SteamInputAddonforClaw.Contracts.Frontend;
using System.Diagnostics;

namespace SteamInputAddonforClaw.Views;

public sealed partial class ClawSensorProbePage : UserControl
{
    private IAddonFrontendControl? _frontend;
    private bool _active;
    private DispatcherQueueTimer? _pollTimer;
    private FrontendClawSensorProbeSnapshot? _latest;
    private CancellationTokenSource? _pageCancellation;
    private bool _pollInFlight;

    public event EventHandler? BackRequested;
    public ClawSensorProbePage() => InitializeComponent();
    internal void Initialize(IAddonFrontendControl frontend) { _frontend = frontend; }

    /// <summary>Page entry: opens (or re-opens, if the previous session finished) the Runtime-owned
    /// diagnostic session and starts a page-local ~200ms poll of the live snapshot. Polling, not
    /// per-sample events, on purpose -- sensor sampling rate is not the same thing as UI refresh
    /// rate.</summary>
    internal void Activate()
    {
        _pageCancellation?.Dispose();
        _pageCancellation = new CancellationTokenSource();
        _active = true;
        ResetUi();
        _ = OpenAsync();
        StartPollTimer();
    }

    private async Task OpenAsync()
    {
        if (_frontend is null) return;
        try
        {
            var token = _pageCancellation?.Token ?? CancellationToken.None;
            var snapshot = await _frontend.OpenClawSensorProbeAsync(token);
            if (_active) Render(snapshot);
        }
        catch (OperationCanceledException) { /* page left before Open returned */ }
        catch (Exception exception)
        {
            AppLog.Warn("ClawSensorProbe", "Probe session open failed.", exception, ("Reason", exception.GetType().Name));
            if (_active) ErrorText.Text = $"Could not open the diagnostic session: {exception.Message}";
        }
    }

    /// <summary>Page exit, however it happens: stops the poll timer and closes the Runtime-owned
    /// session (stopping any in-progress capture) so leaving the page never leaves a capture
    /// running.</summary>
    internal void Deactivate() => _ = DeactivateAsync();

    internal async Task DeactivateAsync()
    {
        if (!_active) return;
        _active = false;
        _pollTimer?.Stop();
        // Cancel any in-flight Start/Next/Previous/Open request BEFORE awaiting Close -- the
        // named-pipe server serializes RPCs through one operation gate, so without this, Close would
        // queue behind a long-running countdown/phase request (review finding #2 on PR #290).
        _pageCancellation?.Cancel();
        if (_frontend is null) return;
        try { await _frontend.CloseClawSensorProbeAsync().ConfigureAwait(true); }
        catch (Exception exception) { AppLog.Warn("ClawSensorProbe", "Probe session close failed.", exception, ("Reason", exception.GetType().Name)); }
    }

    private void StartPollTimer()
    {
        _pollTimer ??= DispatcherQueue.GetForCurrentThread().CreateTimer();
        _pollTimer.Interval = TimeSpan.FromMilliseconds(200);
        _pollTimer.Tick -= PollTimer_Tick;
        _pollTimer.Tick += PollTimer_Tick;
        _pollTimer.Start();
    }

    private void PollTimer_Tick(DispatcherQueueTimer sender, object args) => _ = PollAsync();

    private async Task PollAsync()
    {
        // Single-flight guard: never let a second ~200ms poll queue up behind one that's still
        // in-flight (e.g. stuck behind a long Start/phase RPC on the shared operation gate) (review
        // finding #2 on PR #290).
        if (!_active || _frontend is null || _pollInFlight) return;
        _pollInFlight = true;
        try
        {
            var token = _pageCancellation?.Token ?? CancellationToken.None;
            var snapshot = await _frontend.CaptureClawSensorProbeAsync(token);
            if (_active) Render(snapshot);
        }
        catch (OperationCanceledException) { /* page left while this poll was in flight */ }
        catch (Exception exception)
        {
            AppLog.Warn("ClawSensorProbe", "Probe snapshot poll failed.", exception, ("Reason", exception.GetType().Name));
        }
        finally { _pollInFlight = false; }
    }

    private void Back_Click(object sender, RoutedEventArgs e) => BackRequested?.Invoke(this, EventArgs.Empty);

    private async void StartButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_frontend is null) return;
            StartButton.IsEnabled = false;
            StatusText.Text = "Discovering Windows motion sensors...";
            var token = _pageCancellation?.Token ?? CancellationToken.None;
            var snapshot = await _frontend.StartClawSensorProbeAsync(token);
            if (_active) Render(snapshot);
        }
        catch (OperationCanceledException) { /* page left during Start */ }
        catch (Exception exception)
        {
            AppLog.Warn("ClawSensorProbe", "Probe start failed.", exception, ("Reason", exception.GetType().Name));
            if (_active) { ErrorText.Text = $"Test failed to start: {exception.Message}"; StartButton.IsEnabled = true; }
        }
    }

    private async void NextPhaseButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_frontend is null) return;
            NextPhaseButton.IsEnabled = false;
            BackPhaseButton.IsEnabled = false;
            var token = _pageCancellation?.Token ?? CancellationToken.None;
            var snapshot = await _frontend.NextClawSensorProbePhaseAsync(token);
            if (_active) Render(snapshot);
        }
        catch (OperationCanceledException) { /* page left during phase advance */ }
        catch (Exception exception)
        {
            AppLog.Warn("ClawSensorProbe", "Probe next-phase failed.", exception, ("Reason", exception.GetType().Name));
            if (_active) ErrorText.Text = $"Test failed: {exception.Message}";
        }
    }

    private async void BackPhaseButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_frontend is null) return;
            NextPhaseButton.IsEnabled = false;
            BackPhaseButton.IsEnabled = false;
            var token = _pageCancellation?.Token ?? CancellationToken.None;
            var snapshot = await _frontend.PreviousClawSensorProbePhaseAsync(token);
            if (_active) Render(snapshot);
        }
        catch (OperationCanceledException) { /* page left during phase revisit */ }
        catch (Exception exception)
        {
            AppLog.Warn("ClawSensorProbe", "Probe previous-phase failed.", exception, ("Reason", exception.GetType().Name));
            if (_active) ErrorText.Text = $"Test failed: {exception.Message}";
        }
    }

    private async void StopButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_frontend is null) return;
            StopButton.IsEnabled = false;
            NextPhaseButton.IsEnabled = false;
            BackPhaseButton.IsEnabled = false;
            StatusText.Text = "Stopping sensor capture...";
            Render(await _frontend.StopClawSensorProbeAsync());
        }
        catch (Exception exception)
        {
            AppLog.Warn("ClawSensorProbe", "Probe stop failed.", exception, ("Reason", exception.GetType().Name));
            ErrorText.Text = $"Probe shutdown warning: {exception.Message}";
        }
    }

    private void OpenFolderButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(_latest?.OutputDirectory))
            {
                ErrorText.Text = "The diagnostic output directory is unavailable.";
                return;
            }
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{_latest.OutputDirectory}\"") { UseShellExecute = true });
        }
        catch (Exception exception)
        {
            AppLog.Warn("ClawSensorProbe", "Log folder could not be opened.", exception);
            ErrorText.Text = $"The diagnostic log folder could not be opened: {exception.Message}";
        }
    }

    private async void DoneButton_Click(object sender, RoutedEventArgs e)
    {
        try { await DeactivateAsync(); }
        catch (Exception exception) { AppLog.Warn("ClawSensorProbe", "Probe close-on-done failed.", exception); }
        BackRequested?.Invoke(this, EventArgs.Empty);
    }

    private void ResetUi()
    {
        _latest = null;
        StartButton.IsEnabled = true;
        StopButton.IsEnabled = false;
        BackPhaseButton.IsEnabled = false;
        NextPhaseButton.IsEnabled = false;
        DoneButton.IsEnabled = false;
        OpenFolderButton.IsEnabled = false;
        StatusText.Text = "Ready. This diagnostic is read-only.";
        DeviceText.Text = "Device: Unavailable";
        ModelText.Text = "Model: Unknown / unresolved";
        BoardText.Text = "Base board: Unavailable";
        GyroDiscoveryText.Text = "Gyroscope: Not discovered";
        AccelDiscoveryText.Text = "Accelerometer: Not discovered";
        PhaseText.Text = "Step: Not started";
        InstructionText.Text = "Keep Still: Place the device on a flat surface and do not touch it.";
        LiveText.Text = $"Gyroscope: waiting{Environment.NewLine}Accelerometer: waiting";
        SummaryText.Text = string.Empty;
        ErrorText.Text = string.Empty;
    }

    private void Render(FrontendClawSensorProbeSnapshot snapshot)
    {
        _latest = snapshot;

        if (!snapshot.Available)
        {
            StartButton.IsEnabled = false;
            StopButton.IsEnabled = false;
            BackPhaseButton.IsEnabled = false;
            NextPhaseButton.IsEnabled = false;
            StatusText.Text = snapshot.ErrorMessage ?? "This diagnostic is available only on an identified MSI Claw device.";
            DeviceText.Text = $"Device: {snapshot.Manufacturer} {snapshot.Model}";
            return;
        }

        DeviceText.Text = $"Device: {snapshot.Manufacturer} {snapshot.Model}";
        ModelText.Text = $"Model: {snapshot.ResolvedModel}";
        BoardText.Text = $"Base board: {snapshot.BaseBoard}";

        var discovery = snapshot.Discovery;
        GyroDiscoveryText.Text = discovery?.Gyroscope is { } gyro
            ? $"Gyroscope: {gyro.FriendlyName} | ID: {gyro.SensorId} | Type: {gyro.TypeGuid} | Category: {gyro.CategoryGuid} | Manufacturer: {gyro.Manufacturer} | Model: {gyro.Model} | Persistent ID: {gyro.PersistentUniqueId} | Min interval: {gyro.MinimumReportInterval} ms | HID usage: {gyro.CustomUsage}"
            : "Gyroscope: Not discovered";
        AccelDiscoveryText.Text = discovery?.Accelerometer is { } accel
            ? $"Accelerometer: {accel.FriendlyName} | ID: {accel.SensorId} | Type: {accel.TypeGuid} | Category: {accel.CategoryGuid} | Manufacturer: {accel.Manufacturer} | Model: {accel.Model} | Persistent ID: {accel.PersistentUniqueId} | Min interval: {accel.MinimumReportInterval} ms | HID usage: {accel.CustomUsage}"
            : "Accelerometer: Not discovered";

        PhaseText.Text = snapshot.PhaseIndex >= 0
            ? $"Step: {snapshot.PhaseIndex + 1} of {snapshot.PhaseCount} - {PhaseLabel(snapshot.Phase)}"
            : "Step: Not started";
        InstructionText.Text = PhaseInstruction(snapshot.Phase);

        LiveText.Text = $"Status: {snapshot.State}{Environment.NewLine}Gyroscope raw: X={snapshot.Gyro.X:0.###}, Y={snapshot.Gyro.Y:0.###}, Z={snapshot.Gyro.Z:0.###} | {snapshot.Gyro.Hz:0.0} Hz | {snapshot.Gyro.Count} samples{Environment.NewLine}Accelerometer raw: X={snapshot.Accel.X:0.###}, Y={snapshot.Accel.Y:0.###}, Z={snapshot.Accel.Z:0.###} | {snapshot.Accel.Hz:0.0} Hz | {snapshot.Accel.Count} samples";

        var recording = snapshot.State == FrontendClawSensorProbeState.RecordingPhase;
        StartButton.IsEnabled = snapshot.State == FrontendClawSensorProbeState.Ready;
        StopButton.IsEnabled = snapshot.State is FrontendClawSensorProbeState.Starting or FrontendClawSensorProbeState.Countdown or FrontendClawSensorProbeState.RecordingPhase;
        BackPhaseButton.IsEnabled = recording && snapshot.PhaseIndex > 0;
        NextPhaseButton.IsEnabled = recording;
        NextPhaseButton.Content = snapshot.PhaseIndex == snapshot.PhaseCount - 1 ? "Finish Test" : "Next";
        DoneButton.IsEnabled = snapshot.State is FrontendClawSensorProbeState.Completed or FrontendClawSensorProbeState.Failed;
        OpenFolderButton.IsEnabled = snapshot.HasReport;

        if (snapshot.ReaderErrors.Count > 0)
            ErrorText.Text = string.Join(Environment.NewLine, snapshot.ReaderErrors);
        else if (!string.IsNullOrEmpty(snapshot.ErrorMessage))
            ErrorText.Text = snapshot.ErrorMessage;

        switch (snapshot.State)
        {
            case FrontendClawSensorProbeState.RecordingPhase:
                StatusText.Text = "Recording. Sensor discovery and capture are read-only.";
                break;
            case FrontendClawSensorProbeState.Completed:
                StatusText.Text = "Test completed. Output: " + (snapshot.OutputDirectory ?? "Unavailable");
                UpdateSummary(snapshot, "Test completed");
                break;
            case FrontendClawSensorProbeState.Failed:
                StatusText.Text = $"Test failed: {snapshot.ErrorMessage}";
                UpdateSummary(snapshot, "Test failed");
                break;
        }
    }

    private void UpdateSummary(FrontendClawSensorProbeSnapshot snapshot, string result)
    {
        var gyro = snapshot.GyroscopeSummary;
        var accel = snapshot.AccelerometerSummary;
        SummaryText.Text = $"{result}{Environment.NewLine}Output directory: {snapshot.OutputDirectory ?? "Unavailable"}{Environment.NewLine}" +
            $"Gyroscope samples: {gyro?.SampleCount ?? 0}, average rate: {gyro?.EffectiveHz ?? 0:0.0} Hz, interval: {gyro?.MinimumIntervalMs ?? 0:0.###}-{gyro?.MaximumIntervalMs ?? 0:0.###} ms, dropped: {snapshot.DroppedGyroscopeCount}{Environment.NewLine}" +
            $"Accelerometer samples: {accel?.SampleCount ?? 0}, average rate: {accel?.EffectiveHz ?? 0:0.0} Hz, interval: {accel?.MinimumIntervalMs ?? 0:0.###}-{accel?.MaximumIntervalMs ?? 0:0.###} ms, dropped: {snapshot.DroppedAccelerometerCount}{Environment.NewLine}" +
            $"Dropped samples total: {snapshot.DroppedSampleCount}";
    }

    private static string PhaseLabel(FrontendClawSensorProbePhase phase) => phase switch
    {
        FrontendClawSensorProbePhase.Rest => "Keep Still",
        FrontendClawSensorProbePhase.RollLeft => "Roll Left",
        FrontendClawSensorProbePhase.RollRight => "Roll Right",
        FrontendClawSensorProbePhase.PitchUp => "Pitch Up",
        FrontendClawSensorProbePhase.PitchDown => "Pitch Down",
        FrontendClawSensorProbePhase.YawLeft => "Yaw Left",
        _ => "Yaw Right"
    };

    private static string PhaseInstruction(FrontendClawSensorProbePhase phase) => phase switch
    {
        FrontendClawSensorProbePhase.Rest => "Keep Still: Place the device on a flat surface and do not touch it.",
        FrontendClawSensorProbePhase.RollLeft => "Roll Left: Slowly lower the left side, then return to the starting position.",
        FrontendClawSensorProbePhase.RollRight => "Roll Right: Slowly lower the right side, then return to the starting position.",
        FrontendClawSensorProbePhase.PitchUp => "Pitch Up: Slowly tilt the top upward, then return to the starting position.",
        FrontendClawSensorProbePhase.PitchDown => "Pitch Down: Slowly tilt the top downward, then return to the starting position.",
        FrontendClawSensorProbePhase.YawLeft => "Yaw Left: Keep the device level and rotate it left, then return to center.",
        _ => "Yaw Right: Keep the device level and rotate it right, then return to center."
    };
}
