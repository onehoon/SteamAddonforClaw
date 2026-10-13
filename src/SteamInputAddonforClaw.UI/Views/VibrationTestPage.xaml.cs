using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SteamInputAddonforClaw.Contracts.Frontend;

namespace SteamInputAddonforClaw.Views;

/// <summary>The Developer-only physical vibration profile write probe.</summary>
public sealed partial class VibrationTestPage : UserControl
{
    private IAddonFrontendControl? _frontend;
    private bool _busy;

    public event EventHandler? BackRequested;

    public VibrationTestPage() => InitializeComponent();

    internal void Initialize(IAddonFrontendControl frontend) => _frontend = frontend;

    private async void ApplyVibrationProfileProbe_Click(object sender, RoutedEventArgs e) =>
        await RunVibrationProfileProbeAsync(FrontendControllerVibrationProfileWriteProbeMode.ApplyZeroHundred);

    private async void RestoreVibrationProfileProbe_Click(object sender, RoutedEventArgs e) =>
        await RunVibrationProfileProbeAsync(FrontendControllerVibrationProfileWriteProbeMode.RestoreFiftyFifty);

    private async Task RunVibrationProfileProbeAsync(FrontendControllerVibrationProfileWriteProbeMode mode)
    {
        if (_frontend is null || _busy)
            return;

        SetBusy(true);
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
            SetBusy(false);
        }
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        ApplyVibrationProfileProbeButton.IsEnabled = !busy;
        RestoreVibrationProfileProbeButton.IsEnabled = !busy;
    }

    private void Back_Click(object sender, RoutedEventArgs e) => BackRequested?.Invoke(this, EventArgs.Empty);
}
