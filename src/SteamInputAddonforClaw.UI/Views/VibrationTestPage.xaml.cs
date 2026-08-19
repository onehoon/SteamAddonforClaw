using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SteamInputAddonforClaw.Contracts.Frontend;

namespace SteamInputAddonforClaw.Views;

public sealed partial class VibrationTestPage : UserControl
{
    private IAddonFrontendControl? _frontend;
    public event EventHandler? BackRequested;
    public VibrationTestPage() => InitializeComponent();
    internal void Initialize(IAddonFrontendControl frontend, FrontendBootstrapSnapshot bootstrap) { _frontend = frontend; StatusText.Text = bootstrap.Developer.TestModeEnabled ? "Developer Test Mode active. Confirm Steam Deck output is active." : "Enable Test Mode from Developer Menu."; }
    private void Back_Click(object sender, RoutedEventArgs e) => BackRequested?.Invoke(this, EventArgs.Empty);
    private async void Rumble_Click(object s, RoutedEventArgs e) => await RunAsync(FrontendVibrationTestCommand.Rumble);
    private async void Haptic_Click(object s, RoutedEventArgs e) => await RunAsync(FrontendVibrationTestCommand.Haptic);
    private async void Pulse_Click(object s, RoutedEventArgs e) => await RunAsync(FrontendVibrationTestCommand.HapticPulse);
    private async void Stop_Click(object s, RoutedEventArgs e) => await RunAsync(FrontendVibrationTestCommand.Stop);
    private async Task RunAsync(FrontendVibrationTestCommand command)
    { if (_frontend is null) return; var result = await _frontend.RunVibrationTestAsync(command); ResultText.Text = $"{command}: {result.Reason}"; }
}
