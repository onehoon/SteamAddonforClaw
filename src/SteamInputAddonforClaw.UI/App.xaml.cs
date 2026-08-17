using Microsoft.UI.Xaml;
using Microsoft.UI.Dispatching;
using SteamInputAddonforClaw.FrontendTransport;
using SteamInputAddonforClaw.UI.Lifecycle;
using SteamInputAddonforClaw.UI.Frontend;

namespace SteamInputAddonforClaw.UI;

public partial class App : Application
{
    private readonly UiSingleInstanceGate? _singleInstanceGate;
    private NamedPipeAddonFrontendClient? _frontendClient;
    private MainWindow? _mainWindow;
    private DispatcherQueue? _dispatcherQueue;
    private bool _activationPending;
    private int _shuttingDown;

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

        _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
        _singleInstanceGate.RegisterActivation(() =>
        {
            if (_dispatcherQueue?.TryEnqueue(ActivateOrDeferOnUiThread) != true)
                AppLog.Info("Frontend activation ignored because the UI dispatcher is unavailable.");
        });
        _ = StartFrontendAsync();
    }

    private async Task StartFrontendAsync()
    {
        var stage = "FrontendClientCreation";
        try
        {
            _frontendClient = UiFrontendClientFactory.Create();
            _frontendClient.Disconnected += OnFrontendDisconnected;
            stage = "RuntimeConnection";
            await _frontendClient.ConnectAsync().ConfigureAwait(true);
            AppLog.Info("Frontend", "Frontend connected.");
            stage = "BootstrapAcquisition";
            var bootstrap = await _frontendClient.GetBootstrapAsync().ConfigureAwait(true);
            AppLog.Info("Frontend", "Bootstrap acquired.");
            stage = "MainWindowInitialization";
            _mainWindow = new MainWindow(_frontendClient, bootstrap);
            _mainWindow.Closed += OnMainWindowClosed;
            AppLog.Info("Frontend", "MainWindow initialized.");
            if (_activationPending)
                AppLog.Info("Frontend", "Pending UI activation fulfilled.");
            stage = "Activation";
            ActivateOrDeferOnUiThread();
            AppLog.Info("Frontend", "Frontend activated.");
        }
        catch (Exception exception)
        {
            AppLog.Error("Startup", "UI startup failed.", exception, ("Stage", stage));
            await ShutdownAndExitAsync("StartupFailure").ConfigureAwait(true);
        }
    }

    private void OnFrontendDisconnected(object? sender, EventArgs args)
    {
        if (_dispatcherQueue?.TryEnqueue(() => _ = ShutdownAndExitAsync("RuntimeDisconnected")) != true)
            AppLog.Info("Frontend disconnect observed during UI shutdown.");
    }

    private void ActivateOrDeferOnUiThread()
    {
        if (Volatile.Read(ref _shuttingDown) != 0) return;
        if (_mainWindow is null) { _activationPending = true; return; }
        _activationPending = false;
        _mainWindow.AppWindow.Show();
        _mainWindow.Activate();
    }

    private async void OnMainWindowClosed(object sender, WindowEventArgs args)
    {
        await ShutdownAndExitAsync("WindowClosed").ConfigureAwait(true);
    }

    private async Task ShutdownAndExitAsync(string reason)
    {
        if (Interlocked.Exchange(ref _shuttingDown, 1) != 0) return;
        AppLog.Info("Frontend", "UI shutdown requested.", ("Reason", reason));
        if (_frontendClient is not null)
        {
            _frontendClient.Disconnected -= OnFrontendDisconnected;
            await _frontendClient.DisposeAsync().ConfigureAwait(false);
            _frontendClient = null;
        }
        Exit();
    }
}
