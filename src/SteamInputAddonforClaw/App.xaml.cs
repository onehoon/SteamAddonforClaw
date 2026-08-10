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
using SteamInputAddonforClaw.VirtualOutput.Viiper;
using SteamInputAddonforClaw.Power;
using SteamInputAddonforClaw.Developer;

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
    private DeveloperTestModeState? _developerTestModeState;
    private EffectiveSteamSessionSource? _effectiveSteamSessionSource;
    private PowerTransitionWatcher? _powerWatcher;
    private PowerTransitionCoordinator? _powerCoordinator;
    private ViiperSteamControllerPocCoordinator? _viiperPoc;
    private MsiClawNativeModeSessionCoordinator? _msiClawNativeModeSession;

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
        var addonOwnedVirtualDeviceTracker = new AddonOwnedVirtualDeviceTracker();
        var classifier = new ControllerDeviceClassifier(msiClawAdapter.InternalControllerMatcher, addonOwnedVirtualDeviceTracker);
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

            _dispatcherQueue?.TryEnqueue(() => StartNormalRuntime(classifier, addonOwnedVirtualDeviceTracker, deviceRegistry, msiClawAdapter, startupResult.EnvironmentMode, startupResult.EnvironmentReadiness, startupResult.RecoverySafe));
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

    private void StartNormalRuntime(ControllerDeviceClassifier classifier, AddonOwnedVirtualDeviceTracker addonOwnedVirtualDeviceTracker, HandheldDeviceRegistry deviceRegistry, MsiClawDeviceAdapter msiClawAdapter, ControllerEnvironmentMode environmentMode, ControllerEnvironmentReadiness environmentReadiness, bool recoverySafe)
    {
        AppLog.Info($"Starting runtime. Environment={environmentMode}; Readiness={environmentReadiness}.");
        ClawTweaksCompatibilitySnapshotLogger.LogAtStartup(new WindowsControllerDeviceEnumerator());
        _runningAppIdSource = new SteamRunningAppIdRegistrySource();
        _steamSessionWatcher = new SteamSessionWatcher(_runningAppIdSource);
        _developerTestModeState = new DeveloperTestModeState();
        _effectiveSteamSessionSource = new EffectiveSteamSessionSource(_steamSessionWatcher, _developerTestModeState);
        _effectiveSteamSessionSource.StateChanged += OnEffectiveSteamSessionStateChanged;

        var settingsStore = new SettingsStore(VelopackAppPaths.SettingsPath);
        var settings = settingsStore.Load();
        var startupRegistration = new WindowsTaskSchedulerStartupManager();
        var startupSettings = new StartupSettingsCoordinator(settings, settingsStore, startupRegistration);
        var startupRegistrationResult = startupSettings.Repair();

        if (recoverySafe)
        {
            _steamSessionWatcher.Start();
            _effectiveSteamSessionSource.Refresh();
            _routingSessionStateMachine.ObserveSteamSessionState(_effectiveSteamSessionSource.State);
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

        var recoverySafetyState = new RecoverySafetyState(recoverySafe ? RecoverySafety.Safe : RecoverySafety.Unsafe);
        var powerGate = new PowerMutationGate();
        var statusProvider = new SystemStatusProvider(
            new WindowsDeviceInformationProvider(),
            new WindowsDeviceProbeContextFactory(),
            new HardwareCompatibilityEvaluator(deviceRegistry),
            [
                new MsiCenterMSoftwareStatusProvider(),
                new ClawTweaksSoftwareStatusProvider(new ClawTweaksInstallationProbe(), new ClawTweaksRuntimeDetector()),
                new HandheldCompanionSoftwareStatusProvider(new HandheldCompanionRuntimeDetector())
            ],
            new RuntimePrerequisiteInspector(
                new HidHidePrerequisiteInspector(new HidHideDriverClient()),
                new UsbIpWin2PrerequisiteInspector(new WindowsUsbIpWin2DeviceProbe(new WindowsControllerDeviceEnumerator())),
                new ViiperRuntimeInspector()),
            () => _effectiveSteamSessionSource?.State ?? SteamSessionState.FromRunningAppId(0),
            CaptureExternalControllerAssessment,
            () => recoverySafetyState.Current == RecoverySafety.Safe,
            routingSessionStateMachine: _routingSessionStateMachine);
        _viiperPoc = new ViiperSteamControllerPocCoordinator(statusProvider, new WindowsControllerDeviceEnumerator(), addonOwnedVirtualDeviceTracker, Path.Combine(AppContext.BaseDirectory, "Dependencies", "Viiper", "libVIIPER.dll"), powerGate: powerGate);
        var nativeState = msiClawAdapter.NativeState as MsiClawNativeStateManager;
        _msiClawNativeModeSession = nativeState is null ? null : new MsiClawNativeModeSessionCoordinator(nativeState, _recoveryManager!, powerGate);
        var powerParticipants = _msiClawNativeModeSession is null
            ? new IPowerTransitionParticipant[] { _viiperPoc }
            : new IPowerTransitionParticipant[] { _viiperPoc, _msiClawNativeModeSession };
        _powerCoordinator = new PowerTransitionCoordinator(powerGate, recoverySafetyState, async token =>
        {
            if (_recoveryManager is null) return false;
            var result = await _recoveryManager.RecoverIncompleteSessionAsync(token).ConfigureAwait(false);
            return result.Status is RecoveryStatus.Success or RecoveryStatus.NoRecoveryNeeded;
        }, powerParticipants, async token =>
        {
            if (_msiClawNativeModeSession is null || _effectiveSteamSessionSource is null) return true;
            return await _msiClawNativeModeSession.ReconcileEffectiveSessionAsync(_effectiveSteamSessionSource.State, token).ConfigureAwait(false);
        });
        _powerWatcher = new PowerTransitionWatcher(new WindowsSuspendResumeNotificationSource(), powerGate, _powerCoordinator, _viiperPoc.CancelLifecycle);
        if (!_powerWatcher.Start()) AppLog.Error("Power.Notify", "Suspend/resume notification registration failed.", new InvalidOperationException("PowerRegisterSuspendResumeNotification failed."));
        else if (recoverySafetyState.Current == RecoverySafety.Safe) powerGate.OpenAfterRecovery();
        if (_msiClawNativeModeSession is not null) _ = _msiClawNativeModeSession.ObserveAsync(_effectiveSteamSessionSource.State);
        _mainWindow = new MainWindow(startupSettings, startupRegistrationResult.Message, _recoveryManager, statusProvider, viiperSteamControllerPocCoordinator: _viiperPoc, developerTestModeState: _developerTestModeState);
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

    private void OnEffectiveSteamSessionStateChanged(object? sender, SteamSessionStateChangedEventArgs args)
    {
        _routingSessionStateMachine.ObserveSteamSessionState(args.Current);
        _mainWindow?.UpdateSteamSessionState(args.Current);
        if (_msiClawNativeModeSession is not null) _ = _msiClawNativeModeSession.ObserveAsync(args.Current);
    }

    private void OnMainWindowClosed(object sender, WindowEventArgs args)
    {
        if (!_isExplicitExit)
        {
            return;
        }

        _startupCancellationTokenSource.Cancel();

        if (_effectiveSteamSessionSource is not null)
        {
            _effectiveSteamSessionSource.StateChanged -= OnEffectiveSteamSessionStateChanged;
            _effectiveSteamSessionSource.Dispose();
            _effectiveSteamSessionSource = null;
        }
        if (_steamSessionWatcher is not null)
        {
            _steamSessionWatcher.Dispose();
            _steamSessionWatcher = null;
        }

        _runningAppIdSource?.Dispose();
        _runningAppIdSource = null;
        _systemTrayIcon?.Dispose();
        _systemTrayIcon = null;
        _powerWatcher?.Dispose();
        _powerWatcher = null;
        if (_powerCoordinator is not null) _powerCoordinator.DisposeAsync().AsTask().GetAwaiter().GetResult();
        _powerCoordinator = null;
        if (_viiperPoc is not null) _viiperPoc.DisposeAsync().AsTask().GetAwaiter().GetResult();
        _viiperPoc = null;
        if (_msiClawNativeModeSession is not null) _msiClawNativeModeSession.DisposeAsync().AsTask().GetAwaiter().GetResult();
        _msiClawNativeModeSession = null;
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
