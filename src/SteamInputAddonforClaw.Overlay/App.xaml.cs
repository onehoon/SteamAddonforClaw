using Microsoft.UI.Xaml;

namespace SteamInputAddonforClaw.Overlay;

public partial class App : Application
{
    private OverlayWindow? _window;

    public App() => InitializeComponent();

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _window = new OverlayWindow();
        _window.Closed += (_, _) => Exit();
        _window.ShowForPoc();
    }
}
