using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Dispatching;
using SteamInputAddonforClaw.Contracts.Frontend;
using SteamInputAddonforClaw.FrontendTransport;
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
        catch (FrontendTransportException exception)
        {
            AppLog.Warn("ClawSensorProbe", "Runtime connection lost during probe open.", exception, ("Reason", exception.GetType().Name));
            OnTransportLost(exception);
        }
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
        catch (FrontendTransportException exception)
        {
            // A broken pipe/dead Runtime connection is not a transient error to retry every 200ms --
            // stop polling and surface it, otherwise this loops indefinitely logging the same failure
            // while showing stale telemetry (PR #290 review).
            AppLog.Warn("ClawSensorProbe", "Runtime connection lost during probe poll.", exception, ("Reason", exception.GetType().Name));
            OnTransportLost(exception);
        }
        catch (Exception exception)
        {
            AppLog.Warn("ClawSensorProbe", "Probe snapshot poll failed.", exception, ("Reason", exception.GetType().Name));
        }
        finally { _pollInFlight = false; }
    }

    /// <summary>Common handling for a broken Runtime connection detected from any RPC call on this
    /// page: stop the poll timer (there is nothing left to poll) and disable all mutating controls so
    /// the user sees the diagnostic stopped rather than a page that looks alive but silently fails.</summary>
    private void OnTransportLost(Exception exception)
    {
        _pollTimer?.Stop();
        if (!_active) return;
        ErrorText.Text = $"Runtime connection lost: {exception.Message}";
        StartButton.IsEnabled = false;
        ModeComboBox.IsEnabled = false;
        StopButton.IsEnabled = false;
        BackPhaseButton.IsEnabled = false;
        NextPhaseButton.IsEnabled = false;
    }

    private void Back_Click(object sender, RoutedEventArgs e) => BackRequested?.Invoke(this, EventArgs.Empty);

    private async void StartButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_frontend is null) return;
            StartButton.IsEnabled = false;
            ModeComboBox.IsEnabled = false;
            StatusText.Text = "Discovering Windows motion sensors...";
            var token = _pageCancellation?.Token ?? CancellationToken.None;
            var snapshot = await _frontend.StartClawSensorProbeAsync(SelectedMode(), token);
            if (_active) Render(snapshot);
        }
        catch (OperationCanceledException) { /* page left during Start */ }
        catch (FrontendTransportException exception)
        {
            AppLog.Warn("ClawSensorProbe", "Runtime connection lost during probe start.", exception, ("Reason", exception.GetType().Name));
            OnTransportLost(exception);
        }
        catch (Exception exception)
        {
            AppLog.Warn("ClawSensorProbe", "Probe start failed.", exception, ("Reason", exception.GetType().Name));
            if (_active) { ErrorText.Text = $"Test failed to start: {exception.Message}"; StartButton.IsEnabled = true; ModeComboBox.IsEnabled = true; }
        }
    }

    private FrontendClawSensorProbeMode SelectedMode() => ModeComboBox.SelectedIndex switch
    {
        1 => FrontendClawSensorProbeMode.AxisCharacterization,
        2 => FrontendClawSensorProbeMode.StationaryBias,
        _ => FrontendClawSensorProbeMode.LiveSanity
    };

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
        catch (FrontendTransportException exception)
        {
            AppLog.Warn("ClawSensorProbe", "Runtime connection lost during phase advance.", exception, ("Reason", exception.GetType().Name));
            OnTransportLost(exception);
        }
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
        catch (FrontendTransportException exception)
        {
            AppLog.Warn("ClawSensorProbe", "Runtime connection lost during phase revisit.", exception, ("Reason", exception.GetType().Name));
            OnTransportLost(exception);
        }
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
            var snapshot = await _frontend.StopClawSensorProbeAsync();
            Render(snapshot);
            // Coordinator.Workflow.Stop() intentionally transitions to Completed regardless of
            // whether all phases actually ran, so Render() alone would report a manual/aborted Stop
            // as "Test completed" -- override the presentation to keep that distinction visible, as
            // the old page did (PR #290 re-review finding #2).
            if (_active && snapshot.State == FrontendClawSensorProbeState.Completed)
            {
                StatusText.Text = "Test stopped. Output: " + (snapshot.OutputDirectory ?? "Unavailable");
                UpdateSummary(snapshot, "Test stopped");
            }
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
        ModeComboBox.IsEnabled = true;
        ModeComboBox.SelectedIndex = 0;
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

        // Coordinator.Discovery becomes null once ShutdownReadersAndApiAsync() releases the readers
        // (Stop/Complete/Failed) -- only replace the discovery text when the Runtime still reports a
        // concrete discovery snapshot, so a successful discovery stays visible through the final
        // result instead of being overwritten back to "Not discovered" (PR #290 re-review finding #2).
        if (snapshot.Discovery is { } discovery)
        {
            GyroDiscoveryText.Text = discovery.Gyroscope is { } gyro
                ? $"Gyroscope: {gyro.Backend} | {gyro.FriendlyName} | ID: {gyro.SensorId} | Path: {gyro.DevicePath} | State: {gyro.State} | Unit: {gyro.UnitBasis} | Selection: {gyro.SelectionReason ?? "Unavailable"} | Type: {gyro.TypeGuid} | Category: {gyro.CategoryGuid} | Manufacturer: {gyro.Manufacturer} | Model: {gyro.Model} | Persistent ID: {gyro.PersistentUniqueId} | Min interval: {gyro.MinimumReportInterval} ms | HID usage: {gyro.CustomUsage}"
                : "Gyroscope: Not discovered";
            AccelDiscoveryText.Text = discovery.Accelerometer is { } accel
                ? $"Accelerometer: {accel.Backend} | {accel.FriendlyName} | ID: {accel.SensorId} | Path: {accel.DevicePath} | State: {accel.State} | Unit: {accel.UnitBasis} | Selection: {accel.SelectionReason ?? "Unavailable"} | Type: {accel.TypeGuid} | Category: {accel.CategoryGuid} | Manufacturer: {accel.Manufacturer} | Model: {accel.Model} | Persistent ID: {accel.PersistentUniqueId} | Min interval: {accel.MinimumReportInterval} ms | HID usage: {accel.CustomUsage}"
                : "Accelerometer: Not discovered";
        }

        // Once the Runtime has accepted a mode, it is authoritative -- render it instead of the local
        // selector state, and Previous/Next only ever apply to Axis Characterization (work order
        // sections 6/19: no fake seven-phase traversal for Live Sanity / Stationary Bias).
        var isAxis = snapshot.Mode is null or FrontendClawSensorProbeMode.AxisCharacterization;
        PhaseText.Text = isAxis && snapshot.PhaseIndex >= 0
            ? $"Step: {snapshot.PhaseIndex + 1} of {snapshot.PhaseCount} - {PhaseLabel(snapshot.Phase)}"
            : snapshot.Mode switch
            {
                FrontendClawSensorProbeMode.LiveSanity => "Live Sanity: continuous read-only capture.",
                FrontendClawSensorProbeMode.StationaryBias => "Stationary Bias: continuous read-only capture.",
                _ => "Step: Not started"
            };
        InstructionText.Text = snapshot.Mode switch
        {
            FrontendClawSensorProbeMode.LiveSanity => "Live Sanity: confirm the selected gyroscope and accelerometer are producing usable current data, then press Stop.",
            FrontendClawSensorProbeMode.StationaryBias => "Place the device still on a stable surface. Leave it untouched during the capture, then press Stop.",
            _ => PhaseInstruction(snapshot.Phase)
        };

        var gyroTiming = snapshot.GyroTiming;
        var accelTiming = snapshot.AccelTiming;
        LiveText.Text = $"Status: {snapshot.State} | Elapsed: {snapshot.ElapsedMs / 1000d:0.0}s{Environment.NewLine}" +
            $"Gyroscope raw: X={snapshot.Gyro.X:0.###}, Y={snapshot.Gyro.Y:0.###}, Z={snapshot.Gyro.Z:0.###} | {snapshot.Gyro.Hz:0.0} Hz | {snapshot.Gyro.Count} samples | fresh age {snapshot.Gyro.FreshAgeMs:0} ms | last read {snapshot.Gyro.LastReadDurationMs:0.##} ms | {(snapshot.Gyro.IsFresh ? "fresh" : "stale")}" +
            (gyroTiming is { } gt ? $" | dup {gt.DuplicateCount} | no-data {gt.NoDataCount} | fail {gt.ReadFailureCount} | max read {gt.MaxReadDurationMs:0.##} ms | max age {gt.MaxFreshAgeMs:0} ms" : string.Empty) + Environment.NewLine +
            $"Accelerometer raw: X={snapshot.Accel.X:0.###}, Y={snapshot.Accel.Y:0.###}, Z={snapshot.Accel.Z:0.###} | {snapshot.Accel.Hz:0.0} Hz | {snapshot.Accel.Count} samples | fresh age {snapshot.Accel.FreshAgeMs:0} ms | last read {snapshot.Accel.LastReadDurationMs:0.##} ms | {(snapshot.Accel.IsFresh ? "fresh" : "stale")}" +
            (snapshot.Accel.MagnitudeG is { } magnitude ? $" | |g|={magnitude:0.###}" : string.Empty) +
            (accelTiming is { } at ? $" | dup {at.DuplicateCount} | no-data {at.NoDataCount} | fail {at.ReadFailureCount} | max read {at.MaxReadDurationMs:0.##} ms | max age {at.MaxFreshAgeMs:0} ms" : string.Empty);

        var recording = snapshot.State == FrontendClawSensorProbeState.RecordingPhase;
        StartButton.IsEnabled = snapshot.State == FrontendClawSensorProbeState.Ready;
        ModeComboBox.IsEnabled = snapshot.State == FrontendClawSensorProbeState.Ready;
        StopButton.IsEnabled = snapshot.State is FrontendClawSensorProbeState.Starting or FrontendClawSensorProbeState.Countdown or FrontendClawSensorProbeState.RecordingPhase;
        BackPhaseButton.IsEnabled = isAxis && recording && snapshot.PhaseIndex > 0;
        NextPhaseButton.IsEnabled = isAxis && recording;
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
        // The JSON report is the authoritative detailed artifact -- this compact page summary shows
        // only the evidence needed to confirm the run and locate the report (work order section 19).
        var biasLine = snapshot.Mode == FrontendClawSensorProbeMode.StationaryBias && snapshot.BiasSummary is { } bias
            ? $"{Environment.NewLine}Bias gyro mean: X={bias.GyroMeanX:0.###}, Y={bias.GyroMeanY:0.###}, Z={bias.GyroMeanZ:0.###} | stddev: X={bias.GyroStandardDeviationX:0.###}, Y={bias.GyroStandardDeviationY:0.###}, Z={bias.GyroStandardDeviationZ:0.###}" +
              $"{Environment.NewLine}Bias accel span: X={bias.AccelSpanX:0.###}, Y={bias.AccelSpanY:0.###}, Z={bias.AccelSpanZ:0.###}" + (bias.AccelMagnitudeGMean is { } meanG ? $" | |g| mean={meanG:0.###}, span={bias.AccelMagnitudeGSpan:0.###}" : string.Empty)
            : string.Empty;
        var gyroTiming = snapshot.GyroTiming;
        var accelTiming = snapshot.AccelTiming;
        SummaryText.Text = $"{result}{Environment.NewLine}Mode: {snapshot.Mode?.ToString() ?? "Unknown"}{Environment.NewLine}Output directory: {snapshot.OutputDirectory ?? "Unavailable"}{Environment.NewLine}" +
            $"Gyroscope samples: {gyro?.SampleCount ?? 0}, average rate: {gyro?.EffectiveHz ?? 0:0.0} Hz, interval: {gyro?.MinimumIntervalMs ?? 0:0.###}-{gyro?.MaximumIntervalMs ?? 0:0.###} ms, dropped: {snapshot.DroppedGyroscopeCount}" +
            (gyroTiming is { } gt ? $", duplicate: {gt.DuplicateCount}, no-data: {gt.NoDataCount}, read-failure: {gt.ReadFailureCount}, max read: {gt.MaxReadDurationMs:0.##} ms, max fresh age: {gt.MaxFreshAgeMs:0} ms" : string.Empty) + Environment.NewLine +
            $"Accelerometer samples: {accel?.SampleCount ?? 0}, average rate: {accel?.EffectiveHz ?? 0:0.0} Hz, interval: {accel?.MinimumIntervalMs ?? 0:0.###}-{accel?.MaximumIntervalMs ?? 0:0.###} ms, dropped: {snapshot.DroppedAccelerometerCount}" +
            (accelTiming is { } at ? $", duplicate: {at.DuplicateCount}, no-data: {at.NoDataCount}, read-failure: {at.ReadFailureCount}, max read: {at.MaxReadDurationMs:0.##} ms, max fresh age: {at.MaxFreshAgeMs:0} ms" : string.Empty) + Environment.NewLine +
            $"Dropped samples total: {snapshot.DroppedSampleCount}" + biasLine;
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
