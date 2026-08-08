using Microsoft.UI.Xaml;
using Microsoft.UI.Dispatching;
using SteamInputAddonforClaw.Controllers.Detection;
using SteamInputAddonforClaw.Install;
using SteamInputAddonforClaw.Settings;
using SteamInputAddonforClaw.Steam;
using SteamInputAddonforClaw.Startup;

namespace SteamInputAddonforClaw;

public partial class App : Application
{
    private MainWindow? _mainWindow;
    private SteamRunningAppIdRegistrySource? _runningAppIdSource;
    private SteamSessionWatcher? _steamSessionWatcher;
    private readonly CancellationTokenSource _startupCancellationTokenSource = new();
    private DispatcherQueue? _dispatcherQueue;

    public App()
    {
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
        _ = StartAsync();
    }

    private async Task StartAsync()
    {
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
                _dispatcherQueue?.TryEnqueue(ExitAfterScheduledUpdate);
                return;
            }

            _dispatcherQueue?.TryEnqueue(() => StartNormalRuntime(classifier, startupResult.EnvironmentMode, startupResult.EnvironmentReadiness));
        }
        catch (OperationCanceledException) when (_startupCancellationTokenSource.IsCancellationRequested)
        {
        }
    }

    private void StartNormalRuntime(ControllerDeviceClassifier classifier, ControllerEnvironmentMode environmentMode, ControllerEnvironmentReadiness environmentReadiness)
    {
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

        var controllerDetector = new ExternalControllerDetector(
            new WindowsControllerDeviceEnumerator(),
            classifier);
        var externalAssessment = controllerDetector.Detect();
        _mainWindow.UpdateExternalControllerAssessment(externalAssessment.Status == ExternalControllerAssessmentStatus.ExternalPresent
            ? externalAssessment
            : environmentMode == ControllerEnvironmentMode.HHCManaged || environmentReadiness != ControllerEnvironmentReadiness.Stable
                ? new ExternalControllerAssessment(ExternalControllerAssessmentStatus.Indeterminate, 0, [])
                : externalAssessment);

        _steamSessionWatcher.Start();
        _mainWindow.UpdateSteamSessionState(_steamSessionWatcher.State);
        _mainWindow.Activate();

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
        _startupCancellationTokenSource.Cancel();

        if (_steamSessionWatcher is not null)
        {
            _steamSessionWatcher.StateChanged -= OnSteamSessionStateChanged;
            _steamSessionWatcher.Dispose();
            _steamSessionWatcher = null;
        }

        _runningAppIdSource?.Dispose();
        _runningAppIdSource = null;
    }

    private void ExitAfterScheduledUpdate()
    {
        _startupCancellationTokenSource.Cancel();
        Exit();
    }
}
