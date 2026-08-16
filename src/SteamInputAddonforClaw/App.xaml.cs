using Microsoft.UI.Xaml;
using Microsoft.UI.Dispatching;
using SteamInputAddonforClaw.Steam;
using SteamInputAddonforClaw.Startup;
using SteamInputAddonforClaw.Lifecycle;
using SteamInputAddonforClaw.Diagnostics;
using System.Diagnostics;
using SteamInputAddonforClaw.Recovery;
using SteamInputAddonforClaw.Devices.Abstractions;
using SteamInputAddonforClaw.Devices;
using SteamInputAddonforClaw.Runtime;
using SteamInputAddonforClaw.Status;
using SteamInputAddonforClaw.Routing;
using SteamInputAddonforClaw.VirtualOutput.Viiper;

namespace SteamInputAddonforClaw;

public partial class App : Application
{
    private MainWindow? _mainWindow;
    private readonly CancellationTokenSource _startupCancellationTokenSource = new();
    private DispatcherQueue? _dispatcherQueue;
    private bool _showMainWindow;
    private SystemTrayIcon? _systemTrayIcon;
    private NativeTrayHostWindow? _trayHostWindow;
    private RecoveryManager? _recoveryManager;
    private bool _isExplicitExit;
    private int _shutdownStarted;
    private readonly SingleInstanceGate _singleInstanceGate;
    private AddonRuntimeHost? _runtimeHost;

    public App()
        : this(arguments: null, Program.CurrentSingleInstanceGate ?? throw new InvalidOperationException("The single-instance gate was not initialized."))
    {
    }

    internal App(string[]? arguments, SingleInstanceGate singleInstanceGate)
    {
        _singleInstanceGate = singleInstanceGate;
        _showMainWindow = ApplicationLifecyclePolicy.ShouldShowMainWindow(arguments ?? []);
        AppLog.Info($"Application launch mode: {(_showMainWindow ? "manual" : "background")}.");
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
        _singleInstanceGate.RegisterActivation(ShowMainWindow);
        _ = StartAsync();
    }

    private async Task StartAsync()
    {
        AppLog.Info("Startup coordination started.");
        var startupComposition = AddonStartupCompositionFactory.Create(_showMainWindow ? null : ["--background"]);
        _recoveryManager = startupComposition.RuntimeRecoveryManager;

        try
        {
            var startupResult = await startupComposition.Coordinator.RunAsync(_startupCancellationTokenSource.Token).ConfigureAwait(false);
            if (!startupResult.ShouldStartRuntime)
            {
                AppLog.Info("Startup scheduled an update restart.");
                _dispatcherQueue?.TryEnqueue(ExitAfterScheduledUpdate);
                return;
            }

            _dispatcherQueue?.TryEnqueue(() => StartNormalRuntime(startupComposition.AddonOwnedVirtualDeviceTracker, startupComposition.DeviceRegistry, startupComposition.HandheldDeviceAdapter, startupComposition.ControllerEnvironmentAssessmentProvider, startupComposition.StockCenterMBaseline, startupResult.EnvironmentMode, startupResult.EnvironmentReadiness, startupResult.RecoverySafe));
        }
        catch (OperationCanceledException) when (_startupCancellationTokenSource.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            AppLog.Error("Startup coordination failed.", exception);
            throw;
        }
    }

    private void StartNormalRuntime(AddonOwnedVirtualDeviceTracker addonOwnedVirtualDeviceTracker, HandheldDeviceRegistry deviceRegistry, IHandheldDeviceAdapter handheldDeviceAdapter, IControllerEnvironmentAssessmentProvider controllerEnvironmentAssessmentProvider, IStockCenterMStartupBaseline? stockCenterMBaseline, ControllerEnvironmentMode environmentMode, ControllerEnvironmentReadiness environmentReadiness, bool recoverySafe)
    {
        AppLog.Info($"Starting runtime. Environment={environmentMode}; Readiness={environmentReadiness}.");
        var composition = AddonRuntimeCompositionFactory.Create(
            handheldDeviceAdapter,
            deviceRegistry,
            controllerEnvironmentAssessmentProvider,
            addonOwnedVirtualDeviceTracker,
            _recoveryManager!,
            stockCenterMBaseline,
            recoverySafe);

        _runtimeHost = composition.RuntimeHost;
        _runtimeHost.SteamSessionStateChanged += OnRuntimeSteamSessionStateChanged;
        _runtimeHost.StatusRefreshRequested += OnRuntimeStatusRefreshRequested;
        _runtimeHost.StartPowerObservation();
        RoutingRuntimeStatusSnapshot CaptureRoutingRuntimeStatus() => _runtimeHost?.CaptureRoutingStatus() ?? RoutingRuntimeStatusSnapshot.Unavailable;
        _mainWindow = new MainWindow(composition.StartupSettings, composition.StartupRegistrationMessage, _recoveryManager, composition.StatusProvider,
            developerTestModeState: _runtimeHost.DeveloperTestModeState, routingRuntimeStatusProvider: CaptureRoutingRuntimeStatus);
        _mainWindow.Closed += OnMainWindowClosed;
        _mainWindow.AppWindow.Closing += OnMainWindowClosing;

        try
        {
            _trayHostWindow = new NativeTrayHostWindow();
            _systemTrayIcon = new SystemTrayIcon(_trayHostWindow.Handle, ShowMainWindow, RestartApplication, ExitApplication, GetUserTerminationDecision);
        }
        catch (Exception exception)
        {
            _systemTrayIcon?.Dispose();
            _systemTrayIcon = null;
            _trayHostWindow?.Dispose();
            _trayHostWindow = null;
            Debug.WriteLine($"System tray initialization failed; showing the main window. {exception}");
            _showMainWindow = true;
        }
        if (_showMainWindow)
        {
            _mainWindow.Activate();
            AppLog.Info("Main window activated.");
        }
        _ = _runtimeHost.ReconcileAsync();

    }

    private void OnRuntimeSteamSessionStateChanged(object? sender, SteamSessionStateChangedEventArgs args) =>
        _mainWindow?.UpdateSteamSessionState(args.Current);

    private void OnRuntimeStatusRefreshRequested(object? sender, EventArgs args) =>
        _mainWindow?.RequestStatusRefresh();

    private void OnMainWindowClosed(object sender, WindowEventArgs args)
    {
        if (!_isExplicitExit)
        {
            return;
        }

        ShutdownApplicationOnce();
    }

    private void ShutdownApplicationOnce()
    {
        if (Interlocked.Exchange(ref _shutdownStarted, 1) != 0)
        {
            return;
        }

        _startupCancellationTokenSource.Cancel();
        _runtimeHost?.PrepareForShutdown();

        _systemTrayIcon?.Dispose();
        _systemTrayIcon = null;
        _trayHostWindow?.Dispose();
        _trayHostWindow = null;
        if (_runtimeHost is not null)
        {
            _runtimeHost.DisposeAsync().AsTask().GetAwaiter().GetResult();
            _runtimeHost = null;
        }
        AppLog.Info("Runtime cleanup completed.");
    }

    private void OnMainWindowClosing(Microsoft.UI.Windowing.AppWindow sender, Microsoft.UI.Windowing.AppWindowClosingEventArgs args)
    {
        if (ApplicationLifecyclePolicy.OnWindowClose(_isExplicitExit) == ApplicationCloseAction.HideWindow && _systemTrayIcon?.IsAvailable == true)
        {
            args.Cancel = true;
            _mainWindow?.AppWindow.Hide();
            AppLog.Info("Main window hidden by close request.");
            return;
        }

        if (!_isExplicitExit && _systemTrayIcon?.IsAvailable != true)
        {
            var termination = GetUserTerminationDecision();
            if (!termination.CanTerminate)
            {
                args.Cancel = true;
                AppLog.Info("Lifecycle", "Window close blocked by active routing ownership.", ("Allowed", false), ("Reason", termination.Reason));
                return;
            }
        }

        _isExplicitExit = true;
    }

    private UserTerminationDecision GetUserTerminationDecision() =>
        _runtimeHost?.EvaluateUserTermination() ?? new(true, UserTerminationBlockReason.None);

    private void ShowMainWindow()
    {
        _dispatcherQueue?.TryEnqueue(() =>
        {
            _showMainWindow = true;
            _mainWindow?.AppWindow.Show();
            _mainWindow?.Activate();
            AppLog.Info("Main window restored from tray.");
        });
    }

    private void ExitApplication()
    {
        _dispatcherQueue?.TryEnqueue(() =>
        {
            var termination = GetUserTerminationDecision();
            if (!termination.CanTerminate)
            {
                AppLog.Info("Lifecycle", "Exit request blocked by active routing ownership.", ("Allowed", false), ("Reason", termination.Reason));
                return;
            }
            _isExplicitExit = true;
            AppLog.Info("Explicit application exit requested.");
            _mainWindow?.Close();
            ShutdownApplicationOnce();
            Exit();
        });
    }

    private void RestartApplication()
    {
        _dispatcherQueue?.TryEnqueue(() =>
        {
            try
            {
                var termination = GetUserTerminationDecision();
                if (!termination.CanTerminate)
                {
                    AppLog.Info("Lifecycle", "Restart request blocked by active routing ownership.", ("Allowed", false), ("Reason", termination.Reason));
                    return;
                }
                var executablePath = Environment.ProcessPath ?? throw new InvalidOperationException("The current executable path is unavailable.");
                var restartInfo = new ProcessStartInfo(executablePath)
                {
                    UseShellExecute = false
                };

                foreach (var argument in Environment.GetCommandLineArgs().Skip(1).Where(argument => !string.Equals(argument, "--restart", StringComparison.OrdinalIgnoreCase)))
                {
                    restartInfo.ArgumentList.Add(argument);
                }

                restartInfo.ArgumentList.Add("--restart");
                AppLog.Info("App", "Application restart requested.", ("ExecutablePath", executablePath), ("Background", restartInfo.ArgumentList.Contains("--background")), ("Restart", true));
                var process = Process.Start(restartInfo);
                AppLog.Info("App", "Application restart process started.", ("ProcessId", process?.Id), ("Started", process is not null));
                _isExplicitExit = true;
                _mainWindow?.Close();
                ShutdownApplicationOnce();
                Exit();
            }
            catch (Exception exception)
            {
                AppLog.Error("App", "Application restart could not be started.", exception);
            }
        });
    }

    private void ExitAfterScheduledUpdate()
    {
        _isExplicitExit = true;
        AppLog.Info("Update shutdown requested.");
        _startupCancellationTokenSource.Cancel();
        Exit();
    }
}
