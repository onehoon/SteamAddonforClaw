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
using SteamInputAddonforClaw.Recovery;
using SteamInputAddonforClaw.HidHide;
using SteamInputAddonforClaw.Devices.MSI.Claw;
using SteamInputAddonforClaw.Devices;
using SteamInputAddonforClaw.Prerequisites;
using SteamInputAddonforClaw.Status;
using SteamInputAddonforClaw.Routing;

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
    private RecoveryManager? _recoveryManager;
    private bool _isExplicitExit;
    private readonly SingleInstanceGate _singleInstanceGate;
    private readonly RoutingSessionStateMachine _routingSessionStateMachine = new();

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
        var deviceEnumerator = new WindowsControllerDeviceEnumerator();
        var msiClawAdapter = new MsiClawDeviceAdapter(deviceEnumerator);
        var classifier = new ControllerDeviceClassifier(msiClawAdapter.InternalControllerMatcher);
        var deviceRegistry = new HandheldDeviceRegistry([msiClawAdapter]);
        _recoveryManager = new RecoveryManager(new RecoveryJournalStore(VelopackAppPaths.RecoveryJournalPath), deviceRegistry, new HidHideDriverClient());
        var coordinator = new StartupCoordinator(
            new SilentUpdateGate(_showMainWindow ? null : ["--background"]),
            new ClawTweaksEnvironmentDetector(deviceEnumerator),
            new ControllerEnvironmentWaiter(deviceEnumerator, classifier),
            recoveryManager: _recoveryManager);

        try
        {
            var startupResult = await coordinator.RunAsync(_startupCancellationTokenSource.Token).ConfigureAwait(false);
            if (!startupResult.ShouldStartRuntime)
            {
                AppLog.Info("Startup scheduled an update restart.");
                _dispatcherQueue?.TryEnqueue(ExitAfterScheduledUpdate);
                return;
            }

            var prerequisiteAssessment = new RuntimePrerequisiteInspector(
                new HidHidePrerequisiteInspector(new HidHideDriverClient()),
                new UsbIpWin2PrerequisiteInspector(new WindowsUsbIpWin2DeviceProbe(deviceEnumerator)),
                new ViiperRuntimeInspector()).Inspect();
            AppLog.Info("Prerequisite", "Prerequisite assessment completed.",
                ("HidHide", prerequisiteAssessment.HidHide.Status),
                ("HidHideReason", prerequisiteAssessment.HidHide.Reason),
                ("UsbIpWin2", prerequisiteAssessment.UsbIpWin2.Status),
                ("UsbIpWin2Reason", prerequisiteAssessment.UsbIpWin2.Reason),
                ("Viiper", prerequisiteAssessment.Viiper.Status),
                ("ViiperReason", prerequisiteAssessment.Viiper.Reason),
                ("RoutingReady", prerequisiteAssessment.IsRoutingReady));

            _dispatcherQueue?.TryEnqueue(() => StartNormalRuntime(classifier, startupResult.EnvironmentMode, startupResult.EnvironmentReadiness, startupResult.RecoverySafe));
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

    private void StartNormalRuntime(ControllerDeviceClassifier classifier, ControllerEnvironmentMode environmentMode, ControllerEnvironmentReadiness environmentReadiness, bool recoverySafe)
    {
        AppLog.Info($"Starting runtime. Environment={environmentMode}; Readiness={environmentReadiness}.");
        ClawTweaksCompatibilitySnapshotLogger.LogAtStartup(new WindowsControllerDeviceEnumerator());
        _runningAppIdSource = new SteamRunningAppIdRegistrySource();
        _steamSessionWatcher = new SteamSessionWatcher(_runningAppIdSource);
        _steamSessionWatcher.StateChanged += OnSteamSessionStateChanged;

        var settingsStore = new SettingsStore(VelopackAppPaths.SettingsPath);
        var settings = settingsStore.Load();
        var startupRegistration = new WindowsTaskSchedulerStartupManager();
        var startupSettings = new StartupSettingsCoordinator(settings, settingsStore, startupRegistration);
        var startupRegistrationResult = startupSettings.Repair();

        if (recoverySafe)
        {
            _steamSessionWatcher.Start();
            _routingSessionStateMachine.ObserveSteamSessionState(_steamSessionWatcher.State);
        }
        else
        {
            AppLog.Warn("Recovery", "Steam/controller routing remains stopped because recovery is unsafe.", null, ("Action", "Passive"));
        }

        var controllerDetector = new ExternalControllerDetector(
            new WindowsControllerDeviceEnumerator(),
            classifier);

        ExternalControllerAssessment CaptureExternalControllerAssessment()
        {
            var assessment = controllerDetector.Detect();
            return ExternalControllerAssessmentPolicy.ApplyEnvironmentSafety(
                assessment,
                environmentMode,
                environmentReadiness);
        }

        var statusProvider = new SystemStatusProvider(
            new WindowsDeviceInformationProvider(),
            [
                new MsiCenterMSoftwareStatusProvider(),
                new ClawTweaksSoftwareStatusProvider(new ClawTweaksInstallationProbe(), new ClawTweaksRuntimeDetector()),
                new HandheldCompanionSoftwareStatusProvider(new HandheldCompanionRuntimeDetector())
            ],
            new RuntimePrerequisiteInspector(
                new HidHidePrerequisiteInspector(new HidHideDriverClient()),
                new UsbIpWin2PrerequisiteInspector(new WindowsUsbIpWin2DeviceProbe(new WindowsControllerDeviceEnumerator())),
                new ViiperRuntimeInspector()),
            () => _steamSessionWatcher?.State ?? SteamSessionState.FromRunningAppId(0),
            CaptureExternalControllerAssessment,
            () => recoverySafe,
            routingSessionStateMachine: _routingSessionStateMachine);
        var hidHideProvisioner = new HidHideProvisioner(
            new HidHidePrerequisiteInspector(new HidHideDriverClient()),
            new WindowsHidHidePackageProbe(),
            new HidHideProvisioningReceiptStore(VelopackAppPaths.HidHideProvisioningReceiptPath),
            new ElevatedProcessRunner(),
            installerPathProvider: null,
            installerIntegrityValidator: null,
            safetyStateProvider: new SystemStatusHidHideProvisioningSafetyStateProvider(statusProvider));
        _mainWindow = new MainWindow(startupSettings, startupRegistrationResult.Message, _recoveryManager, statusProvider, hidHideProvisioner: hidHideProvisioner);
        _mainWindow.Closed += OnMainWindowClosed;
        _mainWindow.AppWindow.Closing += OnMainWindowClosing;

        try
        {
            _systemTrayIcon = new SystemTrayIcon(WinRT.Interop.WindowNative.GetWindowHandle(_mainWindow), ShowMainWindow, RestartApplication, ExitApplication);
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
            _routingSessionStateMachine.ObserveSteamSessionState(watcher.State);
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
            _isExplicitExit = true;
            AppLog.Info("Explicit application exit requested.");
            _mainWindow?.Close();
            Exit();
        });
    }

    private void RestartApplication()
    {
        _dispatcherQueue?.TryEnqueue(() =>
        {
            try
            {
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
