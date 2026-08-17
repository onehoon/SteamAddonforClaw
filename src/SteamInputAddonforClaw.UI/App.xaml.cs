using Microsoft.UI.Xaml;
using SteamInputAddonforClaw.FrontendTransport;
using SteamInputAddonforClaw.UI.Lifecycle;
using SteamInputAddonforClaw.UI.Frontend;

namespace SteamInputAddonforClaw.UI;

public partial class App : Application
{
    private readonly UiSingleInstanceGate? _singleInstanceGate;
    private NamedPipeAddonFrontendClient? _frontendClient;
    private MainWindow? _mainWindow;

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

        _singleInstanceGate.RegisterActivation(() => _mainWindow?.Activate());
        _ = StartFrontendAsync();
    }

    private async Task StartFrontendAsync()
    {
        try
        {
            _frontendClient = UiFrontendClientFactory.Create();
            await _frontendClient.ConnectAsync().ConfigureAwait(true);
            var bootstrap = await _frontendClient.GetBootstrapAsync().ConfigureAwait(true);
            _mainWindow = new MainWindow(_frontendClient, bootstrap);
            _mainWindow.Closed += OnMainWindowClosed;
            _mainWindow.Activate();
        }
        catch (Exception exception)
        {
            AppLog.Error("Startup", "UI could not connect to the Runtime frontend.", exception);
            await DisposeClientAsync().ConfigureAwait(true);
            Exit();
        }
    }

    private async void OnMainWindowClosed(object sender, WindowEventArgs args)
    {
        await DisposeClientAsync().ConfigureAwait(true);
        Exit();
    }

    private async Task DisposeClientAsync()
    {
        if (_frontendClient is not null)
        {
            await _frontendClient.DisposeAsync().ConfigureAwait(false);
            _frontendClient = null;
        }
    }
}
