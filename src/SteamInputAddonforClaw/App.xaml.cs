using Microsoft.UI.Xaml;
using SteamInputAddonforClaw.Controllers.Detection;
using SteamInputAddonforClaw.Install;
using SteamInputAddonforClaw.Settings;
using SteamInputAddonforClaw.Steam;
using SteamInputAddonforClaw.Updates;

namespace SteamInputAddonforClaw;

public partial class App : Application
{
    private MainWindow? _mainWindow;
    private SteamRunningAppIdRegistrySource? _runningAppIdSource;
    private SteamSessionWatcher? _steamSessionWatcher;
    private readonly CancellationTokenSource _updateCancellationTokenSource = new();
    private bool _isClosing;

    public App()
    {
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
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
            new ControllerDeviceClassifier());
        _mainWindow.UpdateExternalControllerAssessment(controllerDetector.Detect());

        _steamSessionWatcher.Start();
        _mainWindow.UpdateSteamSessionState(_steamSessionWatcher.State);
        _mainWindow.Activate();

        _ = CheckForUpdatesInBackgroundAsync();
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
        _isClosing = true;
        _updateCancellationTokenSource.Cancel();

        if (_steamSessionWatcher is not null)
        {
            _steamSessionWatcher.StateChanged -= OnSteamSessionStateChanged;
            _steamSessionWatcher.Dispose();
            _steamSessionWatcher = null;
        }

        _runningAppIdSource?.Dispose();
        _runningAppIdSource = null;
    }

    private async Task CheckForUpdatesInBackgroundAsync()
    {
        try
        {
            var updateScheduled = await new SilentUpdateService(new VelopackUpdateClient())
                .CheckDownloadAndScheduleAsync(_updateCancellationTokenSource.Token)
                .ConfigureAwait(false);

            if (updateScheduled && !_isClosing)
            {
                _mainWindow?.DispatcherQueue.TryEnqueue(() =>
                {
                    if (!_isClosing)
                    {
                        _mainWindow?.Close();
                    }
                });
            }
        }
        catch (OperationCanceledException) when (_updateCancellationTokenSource.IsCancellationRequested)
        {
        }
        catch (Exception)
        {
            // Update failures are intentionally ignored so the running version remains usable.
        }
    }
}
