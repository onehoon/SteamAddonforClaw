using Microsoft.UI.Xaml;
using SteamInputAddonforClaw.Steam;

namespace SteamInputAddonforClaw;

public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    public void UpdateSteamSessionState(SteamSessionState state)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            StatusText.Text = state.IsActive ? "Steam session active" : "Steam inactive";
            RunningAppIdText.Text = $"RunningAppID: {state.RunningAppId}";
        });
    }
}
