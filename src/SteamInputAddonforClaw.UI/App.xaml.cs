using Microsoft.UI.Xaml;
using Microsoft.UI.Dispatching;
using SteamInputAddonforClaw.FrontendTransport;
using SteamInputAddonforClaw.UI.Lifecycle;
using SteamInputAddonforClaw.UI.Frontend;
using SteamInputAddonforClaw.Contracts.Frontend;
using Microsoft.UI.Xaml.Markup;

namespace SteamInputAddonforClaw.UI;

public partial class App : Application
{
    private readonly UiSingleInstanceGate? _singleInstanceGate;
    private NamedPipeAddonFrontendClient? _frontendClient;
    private MainWindow? _mainWindow;
    private DispatcherQueue? _dispatcherQueue;
    private UiShutdownCoordinator? _shutdownCoordinator;
    private bool _activationPending;
    // OQ3-A: a Runtime CloseRequested that arrives before MainWindow exists is honored once the
    // window is constructed, without activating it first. Recorded synchronously on the read-loop
    // thread so the startup continuation cannot construct/activate the window ahead of it.
    private int _pendingClose;
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
        _shutdownCoordinator = new UiShutdownCoordinator(DisposeFrontendAsync, RequestExitOnUiThread, TimeSpan.FromSeconds(5));
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
            _frontendClient.CloseRequested += OnFrontendCloseRequested;
            stage = "RuntimeConnection";
            await _frontendClient.ConnectAsync().ConfigureAwait(true);
            AppLog.Info("Frontend", "Frontend connected.");
            stage = "BootstrapAcquisition";
            var bootstrap = await _frontendClient.GetBootstrapAsync().ConfigureAwait(true);
            AppLog.Info("Frontend", "Bootstrap acquired.");
            stage = "MainWindowInitialization";
            _mainWindow = CreateMainWindowWithDiagnostics(_frontendClient, bootstrap);
            _mainWindow.Closed += OnMainWindowClosed;
            AppLog.Info("Frontend", "MainWindow initialized.");
            if (Volatile.Read(ref _pendingClose) != 0)
            {
                AppLog.Info("Frontend", "Pending Runtime close honored before activation.");
                _mainWindow.Close();
                return;
            }
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

    private static MainWindow CreateMainWindowWithDiagnostics(
        NamedPipeAddonFrontendClient frontendClient,
        FrontendBootstrapSnapshot bootstrap)
    {
        try { return new MainWindow(frontendClient, bootstrap); }
        catch (Exception exception) when (ContainsXamlParseException(exception))
        {
            LogXamlFailure("MainWindow", exception);
            throw;
        }
    }

    private static bool ContainsXamlParseException(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is XamlParseException) return true;
        }
        return false;
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
        AppLog.Info("Frontend", "Runtime disconnect observed.",
            ("ShuttingDown", Volatile.Read(ref _shuttingDown)),
            ("HasDispatcher", _dispatcherQueue is not null),
            ("HasFrontendClient", _frontendClient is not null));
        if (_dispatcherQueue?.TryEnqueue(() => _ = ShutdownAndExitAsync("RuntimeDisconnected")) != true)
        {
            AppLog.Error("Frontend", "Runtime disconnect shutdown dispatch failed; UI exit will continue.",
                new InvalidOperationException("UI dispatcher was unavailable."));
            RequestExitOnUiThread();
        }
    }

    private void OnFrontendCloseRequested(object? sender, EventArgs args)
    {
        // Record the pending-close fact synchronously on the read-loop thread: the startup
        // continuation runs on the same dispatcher and must not construct/activate MainWindow ahead
        // of an already-arrived CloseRequested (OQ3-A startup contract).
        Interlocked.Exchange(ref _pendingClose, 1);
        AppLog.Info("Frontend", "Runtime close requested.", ("HasDispatcher", _dispatcherQueue is not null), ("HasMainWindow", _mainWindow is not null));
        if (_dispatcherQueue?.TryEnqueue(CloseForRuntimeRequestOnUiThread) == true) return;
        AppLog.Error("Frontend", "Runtime close request dispatch failed; leaving UI alive for the Runtime-side timeout.",
            new InvalidOperationException("UI dispatcher was unavailable."));
    }

    private void CloseForRuntimeRequestOnUiThread()
    {
        if (Volatile.Read(ref _shuttingDown) != 0) return;
        // A not-yet-constructed window is handled by the pending-close check after construction.
        if (_mainWindow is not null) _mainWindow.Close();
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
        if (_mainWindow is not null)
        {
            await _mainWindow.CloseVibrationTestForUiShutdownAsync().ConfigureAwait(true);
            await _mainWindow.CloseClawSensorProbeForUiShutdownAsync().ConfigureAwait(true);
        }
        await ShutdownAndExitAsync("WindowClosed").ConfigureAwait(true);
    }

    private async Task ShutdownAndExitAsync(string reason)
    {
        if (Interlocked.Exchange(ref _shuttingDown, 1) != 0) return;
        AppLog.Info("Frontend", "UI shutdown requested.", ("Reason", reason));
        if (_shutdownCoordinator is not null)
        {
            await _shutdownCoordinator.ShutdownAsync().ConfigureAwait(true);
            return;
        }
        await DisposeFrontendAsync().ConfigureAwait(true);
        RequestExitOnUiThread();
    }

    private async Task DisposeFrontendAsync()
    {
        try
        {
            if (_frontendClient is not null)
            {
                _frontendClient.Disconnected -= OnFrontendDisconnected;
                _frontendClient.CloseRequested -= OnFrontendCloseRequested;
                AppLog.Info("Frontend", "Frontend client disposal started.");
                await _frontendClient.DisposeAsync().ConfigureAwait(false);
                _frontendClient = null;
                AppLog.Info("Frontend", "Frontend client disposal completed.");
            }
        }
        catch (Exception exception)
        {
            AppLog.Error("Frontend", "Frontend client disposal failed; UI exit will continue.", exception);
        }
    }

    private void RequestExitOnUiThread()
    {
        AppLog.Info("Frontend", "UI exit dispatch requested.");
        new UiExitDispatcher(
            () => _dispatcherQueue?.HasThreadAccess == true,
            () => _dispatcherQueue?.TryEnqueue(() =>
            {
                AppLog.Info("Frontend", "UI Application.Exit executing.");
                Exit();
            }) == true,
            () => { AppLog.Info("Frontend", "UI Application.Exit executing."); Exit(); },
            () =>
            {
                AppLog.Error("Frontend", "UI exit dispatch failed; terminating process without XAML API.",
                    new InvalidOperationException("UI dispatcher was unavailable."));
                Environment.Exit(0);
            }).RequestExit();
    }
}
