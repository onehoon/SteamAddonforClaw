using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SteamInputAddonforClaw.Contracts.Frontend;

namespace SteamInputAddonforClaw.Views;

/// <summary>Full1902 Cleanup J: the Vibration Test page is parked as a static, unavailable
/// placeholder for a future Full1902 rumble feature. Entering or leaving it performs no Runtime
/// work -- there is no diagnostic session, no state subscription, and no vibration RPC. The buttons
/// stay disabled and the status text says the feature is unavailable in this build.</summary>
public sealed partial class VibrationTestPage : UserControl
{
    public event EventHandler? BackRequested;
    public VibrationTestPage() => InitializeComponent();

    // Kept for the existing MainWindow navigation shell; the frontend/bootstrap are not used while
    // the feature is unavailable.
    internal void Initialize(IAddonFrontendControl frontend, FrontendBootstrapSnapshot bootstrap) { }

    internal void Activate()
    {
        RumbleButton.IsEnabled = false;
        HapticButton.IsEnabled = false;
        StopButton.IsEnabled = false;
        StatusText.Text = "The developer vibration test is unavailable in this build.";
    }

    internal void Deactivate() { }

    internal Task DeactivateAsync() => Task.CompletedTask;

    private void Back_Click(object sender, RoutedEventArgs e) => BackRequested?.Invoke(this, EventArgs.Empty);
}
