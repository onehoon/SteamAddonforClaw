using Microsoft.UI.Xaml;
using Microsoft.UI.Dispatching;
using SteamInputAddonforClaw.FrontendTransport;
using SteamInputAddonforClaw.UI.Lifecycle;
using SteamInputAddonforClaw.UI.Frontend;
using SteamInputAddonforClaw.Views;
using System.Reflection;

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
            // Temporary external WinUI startup diagnostic. Remove after the failing
            // XAML component/resource path is identified.
            ProbeXamlComponent("StatusPage", static () => new StatusPage());
            ProbeXamlComponent("HowToUsePage", static () => new HowToUsePage());
            ProbeXamlComponent("ControllerPage", static () => new ControllerPage());
            ProbeXamlComponent("SettingsPage", static () => new SettingsPage());
            ProbeXamlComponent("DeveloperPage", static () => new DeveloperPage());
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
            LogXamlFailure("MainWindow", exception, ("Stage", stage));
            await ShutdownAndExitAsync("StartupFailure").ConfigureAwait(true);
        }
    }

    private static void ProbeXamlComponent(string component, Func<object> create)
    {
        AppLog.Info("XamlProbe", "Component probe started.", ("Component", component));
        try
        {
            _ = create();
            AppLog.Info("XamlProbe", "Component probe succeeded.", ("Component", component));
        }
        catch (Exception exception)
        {
            LogXamlFailure(component, exception);
        }
    }

    private static void LogXamlFailure(string component, Exception exception, params (string Key, object? Value)[] fields)
    {
        var assembly = typeof(App).Assembly;
        var context = new (string Key, object? Value)[]
        {
            ("Component", component),
            ("ExceptionType", exception.GetType().FullName ?? exception.GetType().Name),
            ("HResult", $"0x{exception.HResult:X8}"),
            ("BaseDirectory", AppContext.BaseDirectory),
            ("AssemblyLocation", assembly.Location),
            ("AssemblyName", assembly.GetName().Name ?? string.Empty),
            ("AssemblyVersion", assembly.GetName().Version?.ToString() ?? string.Empty)
        };
        AppLog.Error("XamlProbe", component == "MainWindow" ? "MainWindow XAML initialization failed." : "Component probe failed.", exception,
            [.. context, .. fields]);
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
