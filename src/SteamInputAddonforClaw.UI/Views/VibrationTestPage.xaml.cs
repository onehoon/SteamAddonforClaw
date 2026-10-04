using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SteamInputAddonforClaw.Contracts.Frontend;

namespace SteamInputAddonforClaw.Views;

/// <summary>The Developer-only Full1902 Xbox360 terminal-STOP callback diagnostic.</summary>
public sealed partial class VibrationTestPage : UserControl
{
    private IAddonFrontendControl? _frontend;
    private FrontendXbox360RumbleLoopSnapshot _snapshot = FrontendXbox360RumbleLoopSnapshot.Unavailable();
    private DispatcherTimer? _refreshTimer;
    private Task? _stopTask;
    private bool _active;
    private bool _busy;
    private bool _profileProbeBusy;

    public event EventHandler? BackRequested;

    public VibrationTestPage() => InitializeComponent();

    internal void Initialize(IAddonFrontendControl frontend, FrontendBootstrapSnapshot bootstrap)
    {
        _frontend = frontend;
        _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _refreshTimer.Tick += RefreshTimer_Tick;
        Render(_snapshot);
    }

    internal void Activate()
    {
        _active = true;
        _refreshTimer?.Start();
        _ = CaptureAsync();
    }

    internal void Deactivate()
    {
        _active = false;
        _refreshTimer?.Stop();
        _ = StopIfRunningAsync();
    }

    internal async Task DeactivateAsync()
    {
        _active = false;
        _refreshTimer?.Stop();
        await StopIfRunningAsync();
    }

    private async void RefreshTimer_Tick(object? sender, object e)
    {
        if (!_active || _snapshot.State != FrontendXbox360RumbleLoopState.Running)
        {
            if (_snapshot.State != FrontendXbox360RumbleLoopState.Running)
                _refreshTimer?.Stop();
            return;
        }
        await CaptureAsync();
    }

    private async Task CaptureAsync()
    {
        if (_frontend is null || !_active) return;
        try { Render(await _frontend.CaptureXbox360RumbleLoopDiagnosticAsync()); }
        catch (Exception exception)
        {
            StatusText.Text = "The diagnostic state could not be read: " + exception.Message;
        }
    }

    private async void Start_Click(object sender, RoutedEventArgs e)
    {
        if (_frontend is null || _busy) return;
        SetBusy(true);
        try
        {
            Render(await _frontend.StartXbox360RumbleLoopDiagnosticAsync());
            if (_snapshot.State == FrontendXbox360RumbleLoopState.Running && _active)
                _refreshTimer?.Start();
            else if (_snapshot.State == FrontendXbox360RumbleLoopState.Running)
                await StopIfRunningAsync();
        }
        catch (Exception exception)
        {
            StatusText.Text = "The diagnostic could not start: " + exception.Message;
        }
        finally { SetBusy(false); }
    }

    private async void Stop_Click(object sender, RoutedEventArgs e) => await StopIfRunningAsync();

    private async void ApplyVibrationProfileProbe_Click(object sender, RoutedEventArgs e) =>
        await RunVibrationProfileProbeAsync(FrontendControllerVibrationProfileWriteProbeMode.ApplyZeroHundred);

    private async void RestoreVibrationProfileProbe_Click(object sender, RoutedEventArgs e) =>
        await RunVibrationProfileProbeAsync(FrontendControllerVibrationProfileWriteProbeMode.RestoreFiftyFifty);

    private async Task RunVibrationProfileProbeAsync(FrontendControllerVibrationProfileWriteProbeMode mode)
    {
        if (_frontend is null || _busy || _profileProbeBusy)
            return;
        if (_snapshot.State == FrontendXbox360RumbleLoopState.Running)
        {
            VibrationProfileProbeStatusText.Text = "Stop the Xbox360 terminal STOP loop before running the profile probe.";
            return;
        }

        SetProfileProbeBusy(true);
        VibrationProfileProbeStatusText.Text = "Running one developer-only profile write...";
        try
        {
            var result = await _frontend.RunControllerVibrationProfileWriteProbeAsync(mode);
            VibrationProfileProbeStatusText.Text = result.Status;
            if (!result.Succeeded)
                AppLog.Info("ControllerVibration", "ControllerVibrationProfileWriteProbeUiResult",
                    ("Mode", result.Mode), ("Outcome", result.Outcome));
        }
        catch (Exception exception)
        {
            VibrationProfileProbeStatusText.Text = mode == FrontendControllerVibrationProfileWriteProbeMode.RestoreFiftyFifty
                ? "Restore request failed; the motors may remain at test values. Press Restore 50 / 50 again manually."
                : "The developer profile write failed; production saved strength is unchanged. See the application log.";
            AppLog.Warn("ControllerVibration", "ControllerVibrationProfileWriteProbeUiFailed", exception,
                ("Mode", mode), ("Reason", exception.GetType().Name));
        }
        finally
        {
            SetProfileProbeBusy(false);
        }
    }

    private async Task StopIfRunningAsync()
    {
        _refreshTimer?.Stop();
        if (_stopTask is { IsCompleted: false } pendingStop)
        {
            await pendingStop;
            return;
        }
        if (_frontend is null || _snapshot.State != FrontendXbox360RumbleLoopState.Running) return;
        _stopTask = StopCoreAsync();
        await _stopTask;
    }

    private async Task StopCoreAsync()
    {
        if (_frontend is null) return;
        SetBusy(true);
        try { Render(await _frontend.StopXbox360RumbleLoopDiagnosticAsync()); }
        catch (Exception exception)
        {
            StatusText.Text = "The diagnostic stop could not be confirmed: " + exception.Message;
        }
        finally { SetBusy(false); }
    }

    private void Render(FrontendXbox360RumbleLoopSnapshot snapshot)
    {
        _snapshot = snapshot;
        StatusText.Text = snapshot.Status;
        RunDetailsText.Text = snapshot.RunId is null
            ? snapshot.Slot is int readySlot ? $"XInput slot: {readySlot}" : string.Empty
            : $"Run: {snapshot.RunId}\nSlot: {snapshot.Slot?.ToString() ?? "Unavailable"}    Cycle: {snapshot.Cycle}    Step: {snapshot.Step}/{snapshot.StepCount}\nCurrent: {(snapshot.CurrentValue8 is int value ? $"{value}/0x{value:X2}" : "—")}    Last callback: {FormatCallback(snapshot)}";
        FailureText.Text = snapshot.FailureReason is null ? string.Empty : $"Failure: {snapshot.FailureReason}";
        StartButton.IsEnabled = !_busy && !_profileProbeBusy && snapshot.Available && snapshot.State != FrontendXbox360RumbleLoopState.Running;
        StopButton.IsEnabled = !_busy && !_profileProbeBusy && snapshot.State == FrontendXbox360RumbleLoopState.Running;
        ApplyVibrationProfileProbeButton.IsEnabled = !_profileProbeBusy && !_busy && snapshot.State != FrontendXbox360RumbleLoopState.Running;
        RestoreVibrationProfileProbeButton.IsEnabled = !_profileProbeBusy && !_busy && snapshot.State != FrontendXbox360RumbleLoopState.Running;
    }

    private static string FormatCallback(FrontendXbox360RumbleLoopSnapshot snapshot) =>
        snapshot.LastObservedLeft8 is byte left && snapshot.LastObservedRight8 is byte right
            ? $"{left}/{right} (callback #{snapshot.LastObservedCallbackSequence?.ToString() ?? "?"})"
            : "None observed";

    private void SetBusy(bool busy)
    {
        _busy = busy;
        Render(_snapshot);
    }

    private void SetProfileProbeBusy(bool busy)
    {
        _profileProbeBusy = busy;
        Render(_snapshot);
    }

    private void Back_Click(object sender, RoutedEventArgs e) => BackRequested?.Invoke(this, EventArgs.Empty);
}
