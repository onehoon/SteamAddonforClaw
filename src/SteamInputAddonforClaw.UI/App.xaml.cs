using Microsoft.UI.Xaml;
using SteamInputAddonforClaw.UI.Lifecycle;

namespace SteamInputAddonforClaw.UI;

public partial class App : Application
{
    private readonly UiSingleInstanceGate? _singleInstanceGate;

    public App() => InitializeComponent();

    internal App(UiSingleInstanceGate singleInstanceGate)
    {
        _singleInstanceGate = singleInstanceGate;
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        if (_singleInstanceGate is null)
        {
            Exit();
            return;
        }

        _singleInstanceGate.RegisterActivation(static () => { });

        // Foundation only. Production Runtime does not launch this executable yet.
        // MainWindow ownership moves here in the frontend process cutover revision.
        Exit();
    }
}
