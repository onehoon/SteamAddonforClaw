using Microsoft.UI.Xaml;
using Microsoft.UI.Dispatching;
using SteamInputAddonforClaw.Controllers.Detection;
using SteamInputAddonforClaw.Install;
using SteamInputAddonforClaw.Settings;
using SteamInputAddonforClaw.Steam;
using SteamInputAddonforClaw.Startup;
using SteamInputAddonforClaw.Lifecycle;
using SteamInputAddonforClaw.Diagnostics;
using System.Diagnostics;

namespace SteamInputAddonforClaw;

public partial class App : Application
{
    private MainWindow? _mainWindow;
    private SteamRunningAppIdRegistrySource? _runningAppIdSource;
    private SteamSessionWatcher? _steamSessionWatcher;
    private readonly CancellationTokenSource _startupCancellationTokenSource = new();
    private DispatcherQueue? _dispatcherQueue;
    private bool _showMainWindow;
    private SystemTrayIcon? _systemTrayIcon;
    private bool _isExplicitExit;

    public App(string[]? arguments = null)
    {
        _showMainWindow = ApplicationLifecyclePolicy.ShouldShowMainWindow(arguments ?? []);
        AppLog.Info($"Application launch mode: {(_showMainWindow ? "manual" : "background")}.");
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
        _ = StartAsync();
    }

    private async Task StartAsync()
    {
        AppLog.Info("Startup coordination started.");
        var classifier = new ControllerDeviceClassifier();
        var deviceEnumerator = new WindowsControllerDeviceEnumerator();
        var coordinator = new StartupCoordinator(
            new SilentUpdateGate(),
            new ClawTweaksEnvironmentDetector(deviceEnumerator),
            new ControllerEnvironmentWaiter(deviceEnumerator, classifier));

        try
        {
            var startupResult = await coordinator.RunAsync(_startupCancellationTokenSource.Token).ConfigureAwait(false);
            if (!startupResult.ShouldStartRuntime)
            {
                AppLog.Info("Startup scheduled an update restart.");
                _dispatcherQueue?.TryEnqueue(ExitAfterScheduledUpdate);
                return;
            }

            _dispatcherQueue?.TryEnqueue(() => StartNormalRuntime(classifier, startupResult.EnvironmentMode, startupResult.EnvironmentReadiness));
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

    private void StartNormalRuntime(ControllerDeviceClassifier classifier, ControllerEnvironmentMode environmentMode, ControllerEnvironmentReadiness environmentReadiness)
    {
        AppLog.Info($"Starting runtime. Environment={environmentMode}; Readiness={environmentReadiness}.");
        _runningAppIdSource = new SteamRunningAppIdRegistrySource();
        _steamSessionWatcher = new SteamSessionWatcher(_runningAppIdSource);
        _steamSessionWatcher.StateChanged += OnSteamSessionStateChanged;

        var settingsStore = new SettingsStore(VelopackAppPaths.SettingsPath);
        var settings = settingsStore.Load();
        var startupRegistration = new WindowsTaskSchedulerStartupManager();
        var startupSettings = new StartupSettingsCoordinator(settings, settingsStore, startupRegistration);
        var startupRegistrationResult = startupSettings.Repair();

        _mainWindow = new MainWindow(startupSettings, startupRegistrationResult.Message);
        _mainWindow.Closed += OnMainWindowClosed;
        _mainWindow.AppWindow.Closing += OnMainWindowClosing;

        var controllerDetector = new ExternalControllerDetector(
            new WindowsControllerDeviceEnumerator(),
            classifier);
        var externalAssessment = controllerDetector.Detect();
        AppLog.Info($"External controller assessment: {externalAssessment.Status}.");
        _mainWindow.UpdateExternalControllerAssessment(externalAssessment.Status == ExternalControllerAssessmentStatus.ExternalPresent
            ? externalAssessment
            : environmentMode == ControllerEnvironmentMode.HHCManaged || environmentReadiness != ControllerEnvironmentReadiness.Stable
                ? new ExternalControllerAssessment(ExternalControllerAssessmentStatus.Indeterminate, 0, [])
                : externalAssessment);

        _steamSessionWatcher.Start();
        _mainWindow.UpdateSteamSessionState(_steamSessionWatcher.State);
        try
        {
            _systemTrayIcon = new SystemTrayIcon(WinRT.Interop.WindowNative.GetWindowHandle(_mainWindow), ShowMainWindow, ExitApplication);
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"System tray initialization failed; showing the main window. {exception}");
            _showMainWindow = true;
        }
        if (_showMainWindow)
        {
            _mainWindow.Activate();
            AppLog.Info("Main window activated.");
        }

    }

    private void OnSteamSessionStateChanged(object? sender, EventArgs e)
    {
        if (sender is SteamSessionWatcher watcher)
        {
            _mainWindow?.UpdateSteamSessionState(watcher.State);
        }
    }

    private void OnMainWindowClosed(object sender, WindowEventArgs args)
    {
        if (!_isExplicitExit)
        {
            return;
        }

        _startupCancellationTokenSource.Cancel();

        if (_steamSessionWatcher is not null)
        {
            _steamSessionWatcher.StateChanged -= OnSteamSessionStateChanged;
            _steamSessionWatcher.Dispose();
            _steamSessionWatcher = null;
        }

        _runningAppIdSource?.Dispose();
        _runningAppIdSource = null;
        _systemTrayIcon?.Dispose();
        _systemTrayIcon = null;
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

        _isExplicitExit = true;
    }

    private void ShowMainWindow()
    {
        _dispatcherQueue?.TryEnqueue(() =>
        {
            _mainWindow?.AppWindow.Show();
            _mainWindow?.Activate();
            AppLog.Info("Main window restored from tray.");
        });
    }

    private void ExitApplication()
    {
        _dispatcherQueue?.TryEnqueue(() =>
        {
            _isExplicitExit = true;
            AppLog.Info("Explicit application exit requested.");
            _mainWindow?.Close();
            Exit();
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
