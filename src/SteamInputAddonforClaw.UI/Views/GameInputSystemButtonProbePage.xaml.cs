using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SteamInputAddonforClaw.Contracts.Frontend;

namespace SteamInputAddonforClaw.Views;

public sealed partial class GameInputSystemButtonProbePage : UserControl
{
    private IAddonFrontendControl? _frontend;
    private FrontendGameInputSystemButtonProbeSnapshot _snapshot =
        FrontendGameInputSystemButtonProbeSnapshot.Unavailable();
    private DispatcherTimer? _refreshTimer;
    private Task? _stopTask;
    private bool _active;
    private bool _busy;

    public event EventHandler? BackRequested;

    public GameInputSystemButtonProbePage() => InitializeComponent();

    internal void Initialize(IAddonFrontendControl frontend)
    {
        _frontend = frontend;
        _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _refreshTimer.Tick += RefreshTimer_Tick;
        Render(_snapshot);
    }

    internal void Activate()
    {
        _active = true;
        _ = CaptureAsync();
    }

    internal void Deactivate()
    {
        _active = false;
        _refreshTimer?.Stop();
        _ = StopIfRunningAsync(forceRequest: true);
    }

    internal async Task DeactivateAsync()
    {
        _active = false;
        _refreshTimer?.Stop();
        await StopIfRunningAsync(forceRequest: true);
    }

    private async void RefreshTimer_Tick(object? sender, object args)
    {
        if (!_active || _snapshot.State != FrontendGameInputSystemButtonProbeState.Running)
        {
            if (_snapshot.State != FrontendGameInputSystemButtonProbeState.Running)
                _refreshTimer?.Stop();
            return;
        }

        await CaptureAsync();
    }

    private async Task CaptureAsync()
    {
        if (_frontend is null || !_active) return;
        try
        {
            Render(await _frontend.CaptureGameInputSystemButtonProbeAsync());
            if (_snapshot.State == FrontendGameInputSystemButtonProbeState.Running)
                _refreshTimer?.Start();
            else
                _refreshTimer?.Stop();
        }
        catch (Exception exception)
        {
            StatusText.Text = "The probe state could not be read: " + exception.Message;
        }
    }

    private async void Start_Click(object sender, RoutedEventArgs args)
    {
        if (_frontend is null || _busy) return;
        SetBusy(true);
        try
        {
            Render(await _frontend.StartGameInputSystemButtonProbeAsync());
            if (_active && _snapshot.State == FrontendGameInputSystemButtonProbeState.Running)
                _refreshTimer?.Start();
            else if (_snapshot.State == FrontendGameInputSystemButtonProbeState.Running)
                await StopIfRunningAsync();
        }
        catch (Exception exception)
        {
            StatusText.Text = "The probe could not start: " + exception.Message;
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void Stop_Click(object sender, RoutedEventArgs args) => await StopIfRunningAsync();

    private async Task StopIfRunningAsync(bool forceRequest = false)
    {
        _refreshTimer?.Stop();
        if (_stopTask is { IsCompleted: false } pending)
        {
            await pending;
            return;
        }

        if (_frontend is null) return;
        if (!forceRequest && _snapshot.State != FrontendGameInputSystemButtonProbeState.Running) return;

        _stopTask = StopCoreAsync();
        await _stopTask;
    }

    private async Task StopCoreAsync()
    {
        if (_frontend is null) return;
        SetBusy(true);
        try { Render(await _frontend.StopGameInputSystemButtonProbeAsync()); }
        catch (Exception exception)
        {
            StatusText.Text = "The probe stop could not be confirmed: " + exception.Message;
        }
        finally { SetBusy(false); }
    }

    private void Render(FrontendGameInputSystemButtonProbeSnapshot snapshot)
    {
        _snapshot = snapshot;
        StatusText.Text = $"State: {snapshot.State}\n{snapshot.Status}";
        EventCountText.Text = $"Events: {snapshot.EventCount:N0}";
        LastEventText.Text = snapshot.LastEvent is { } item ? FormatEvent(item) : "No Guide/Share button events captured yet.";
        StartButton.IsEnabled = !_busy && snapshot.Available && snapshot.State != FrontendGameInputSystemButtonProbeState.Running;
        StopButton.IsEnabled = !_busy && snapshot.State == FrontendGameInputSystemButtonProbeState.Running;
    }

    private static string FormatEvent(FrontendGameInputSystemButtonEventSnapshot item)
    {
        var changes = new List<string>();
        if (item.GuidePressed) changes.Add("Guide Pressed");
        if (item.GuideReleased) changes.Add("Guide Released");
        if (item.SharePressed) changes.Add("Share Pressed");
        if (item.ShareReleased) changes.Add("Share Released");
        return string.Join(Environment.NewLine,
            $"Button change: {(changes.Count == 0 ? "No Guide/Share transition" : string.Join(", ", changes))}",
            $"Timestamp: {item.TimestampMicroseconds} us    Sequence: {item.Sequence}",
            $"Current: 0x{item.CurrentButtonsRaw:X8}    Previous: 0x{item.PreviousButtonsRaw:X8}",
            $"Device info: {(item.DeviceInfoSucceeded ? "Succeeded" : "Failed: " + item.DeviceInfoFailure)}",
            $"VID: {item.VendorId ?? "Unavailable"}    PID: {item.ProductId ?? "Unavailable"}",
            $"Name: {item.DisplayName ?? "Unavailable"}",
            $"PnP Path: {item.PnpPath ?? "Unavailable"}",
            $"Container ID: {item.ContainerId ?? "Unavailable"}",
            $"Device ID: {item.DeviceId ?? "Unavailable"}",
            $"Device Root ID: {item.DeviceRootId ?? "Unavailable"}",
            $"Supported Input: {item.SupportedInput ?? "Unavailable"} (0x{unchecked((uint)(item.SupportedInputRaw ?? 0)):X8})",
            $"Supported System Buttons: {item.SupportedSystemButtons ?? "Unavailable"} (0x{unchecked((uint)(item.SupportedSystemButtonsRaw ?? 0)):X8})");
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        Render(_snapshot);
    }

    private void Back_Click(object sender, RoutedEventArgs args) => BackRequested?.Invoke(this, EventArgs.Empty);
}
