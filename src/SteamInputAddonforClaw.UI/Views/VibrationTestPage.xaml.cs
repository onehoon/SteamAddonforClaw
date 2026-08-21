using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Dispatching;
using SteamInputAddonforClaw.Contracts.Frontend;

namespace SteamInputAddonforClaw.Views;

public sealed partial class VibrationTestPage : UserControl
{
    private IAddonFrontendControl? _frontend;
    private bool _active;
    public event EventHandler? BackRequested;
    public VibrationTestPage() => InitializeComponent();
    internal void Initialize(IAddonFrontendControl frontend, FrontendBootstrapSnapshot bootstrap) { _frontend = frontend; }

    /// <summary>Page entry: opens the dedicated diagnostic session (so a file exists even if no
    /// command is ever run), subscribes to live state changes, and pulls a fresh Test Mode / Steam
    /// Deck output status so the buttons and status text reflect current reality rather than the
    /// state captured once at process startup.</summary>
    internal void Activate()
    {
        _active = true;
        if (_frontend is not null) _frontend.StateInvalidated += OnStateInvalidated;
        _ = OpenSessionAsync();
        _ = RefreshAsync();
    }

    private async Task OpenSessionAsync()
    {
        if (_frontend is null) return;
        try { await _frontend.OpenVibrationTestSessionAsync(); }
        catch (Exception exception) { AppLog.Warn("Window", "Vibration test session open failed.", exception, ("Reason", exception.GetType().Name)); }
    }

    /// <summary>Page exit, however it happens (Back button, mouse-back, or navigating elsewhere):
    /// unsubscribes from live state changes and closes the dedicated Vibration Test diagnostic
    /// session (cancels any pending developer-owned delayed STOP and issues a best-effort physical
    /// STOP so leaving the page never leaves the motors running or a stale STOP armed).</summary>
    internal void Deactivate()
    {
        _ = DeactivateAsync();
    }

    internal async Task DeactivateAsync()
    {
        if (!_active) return;
        _active = false;
        if (_frontend is not null) _frontend.StateInvalidated -= OnStateInvalidated;
        if (_frontend is null) return;
        try { await _frontend.CloseVibrationTestSessionAsync().ConfigureAwait(true); }
        catch (Exception exception) { AppLog.Warn("Window", "Vibration test session close failed.", exception, ("Reason", exception.GetType().Name)); }
    }

    private void OnStateInvalidated(object? sender, EventArgs e)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            if (_active)
                _ = RefreshAsync();
        });
    }

    internal async Task RefreshAsync()
    {
        if (_frontend is null) return;
        var bootstrap = await _frontend.GetBootstrapAsync();
        var status = await _frontend.CaptureStatusAsync();
        var enabled = bootstrap.Developer.TestModeEnabled && status.Routing.SteamOutputActive;

        RumbleButton.IsEnabled = enabled;
        HapticButton.IsEnabled = enabled;
        StopButton.IsEnabled = enabled;

        StatusText.Text = !bootstrap.Developer.TestModeEnabled
            ? "Enable Test Mode from Developer Menu."
            : !status.Routing.SteamOutputActive
                ? "Steam Deck output is not active."
                : "Developer Test Mode active. Steam Deck output active.";
    }

    private void Back_Click(object sender, RoutedEventArgs e) => BackRequested?.Invoke(this, EventArgs.Empty);
    private async void Rumble_Click(object s, RoutedEventArgs e) => await RunAsync(FrontendVibrationTestCommand.Rumble);
    private async void Haptic_Click(object s, RoutedEventArgs e) => await RunAsync(FrontendVibrationTestCommand.Haptic);
    private async void Stop_Click(object s, RoutedEventArgs e) => await RunAsync(FrontendVibrationTestCommand.Stop);
    private async Task RunAsync(FrontendVibrationTestCommand command)
    { if (_frontend is null) return; var result = await _frontend.RunVibrationTestAsync(command); ResultText.Text = $"{command}: {result.Reason}"; }
}
