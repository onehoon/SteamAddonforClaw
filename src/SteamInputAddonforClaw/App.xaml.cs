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
using SteamInputAddonforClaw.Input.DirectInput;
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
    private SteamBigPictureWatcher? _steamBigPictureWatcher;
    private readonly CancellationTokenSource _startupCancellationTokenSource = new();
    private DispatcherQueue? _dispatcherQueue;
    private bool _showMainWindow;
    private SystemTrayIcon? _systemTrayIcon;
    private RecoveryManager? _recoveryManager;
    private bool _isExplicitExit;
    private readonly SingleInstanceGate _singleInstanceGate;
    private DeveloperTestModeState? _developerTestModeState;
    private EffectiveSteamSessionSource? _effectiveSteamSessionSource;
    private readonly ResumeFreshReconcileSuppression _resumeFreshReconcileSuppression = new();
    private readonly DiagnosticSessionTracker _diagnosticSessions = new();
    private PowerTransitionWatcher? _powerWatcher;
    private PowerTransitionCoordinator? _powerCoordinator;
    private MsiClawNativeModeSessionCoordinator? _msiClawNativeModeSession;
    private MsiClawInputSource? _physicalInputSource;
    private RoutingPipelineRuntimeCoordinator? _routingRuntimeCoordinator;
    private UserTerminationGuard? _userTerminationGuard;

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
        var controllerSoftwareProviders = new IControllerSoftwareStatusProvider[]
        {
            new MsiCenterMSoftwareStatusProvider(),
            new ClawTweaksSoftwareStatusProvider(new ClawTweaksInstallationProbe(), new ClawTweaksRuntimeDetector()),
            new HandheldCompanionSoftwareStatusProvider(new HandheldCompanionRuntimeDetector())
        };
        var controllerEnvironmentAssessmentProvider = new ControllerEnvironmentAssessmentProvider(controllerSoftwareProviders);
        var recoveryJournalStore = new RecoveryJournalStore(VelopackAppPaths.RecoveryJournalPath);
        _recoveryManager = new RecoveryManager(recoveryJournalStore);
        var nativeState = msiClawAdapter.NativeState as MsiClawNativeStateManager;
        var stockCenterMBaseline = nativeState is null ? null : new StockCenterMStartupBaseline(nativeState);
        var coordinator = new StartupCoordinator(
            new SilentUpdateGate(_showMainWindow ? null : ["--background"]),
            controllerEnvironmentAssessmentProvider,
            new ControllerEnvironmentWaiter(deviceEnumerator, classifier),
            recoveryJournalStore: recoveryJournalStore,
            stockCenterMBaseline: stockCenterMBaseline,
            hidHideRecoveryCleaner: new StartupHidHideRecoveryCleaner(new HidHideDriverClient()),
            probeContextFactory: new WindowsDeviceProbeContextFactory(new WindowsDeviceIdentitySource(), deviceEnumerator),
            hardwareCompatibilityEvaluator: new HardwareCompatibilityEvaluator(deviceRegistry));

        try
        {
            var startupResult = await coordinator.RunAsync(_startupCancellationTokenSource.Token).ConfigureAwait(false);
            if (!startupResult.ShouldStartRuntime)
            {
                AppLog.Info("Startup scheduled an update restart.");
                _dispatcherQueue?.TryEnqueue(ExitAfterScheduledUpdate);
                return;
            }

            _dispatcherQueue?.TryEnqueue(() => StartNormalRuntime(addonOwnedVirtualDeviceTracker, deviceRegistry, msiClawAdapter, controllerEnvironmentAssessmentProvider, stockCenterMBaseline, startupResult.EnvironmentMode, startupResult.EnvironmentReadiness, startupResult.RecoverySafe));
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

    private void StartNormalRuntime(AddonOwnedVirtualDeviceTracker addonOwnedVirtualDeviceTracker, HandheldDeviceRegistry deviceRegistry, MsiClawDeviceAdapter msiClawAdapter, IControllerEnvironmentAssessmentProvider controllerEnvironmentAssessmentProvider, IStockCenterMStartupBaseline? stockCenterMBaseline, ControllerEnvironmentMode environmentMode, ControllerEnvironmentReadiness environmentReadiness, bool recoverySafe)
    {
        AppLog.Info($"Starting runtime. Environment={environmentMode}; Readiness={environmentReadiness}.");
        var settingsStore = new SettingsStore(VelopackAppPaths.SettingsPath);
        var settings = settingsStore.Load();
        AppLog.MinimumLevelOverride = AppSettingsPolicy.ToAppLogLevel(settings.LogLevel);
        var startupRegistration = new WindowsTaskSchedulerStartupManager();
        var startupSettings = new StartupSettingsCoordinator(settings, settingsStore, startupRegistration);
        _runningAppIdSource = new SteamRunningAppIdRegistrySource();
        _steamSessionWatcher = new SteamSessionWatcher(_runningAppIdSource);
        _steamBigPictureWatcher = new SteamBigPictureWatcher();
        _developerTestModeState = new DeveloperTestModeState();
        _steamBigPictureWatcher.Start();
        _effectiveSteamSessionSource = new EffectiveSteamSessionSource(_steamSessionWatcher, _steamBigPictureWatcher, _developerTestModeState, startupSettings);
        _effectiveSteamSessionSource.StateChanged += OnEffectiveSteamSessionStateChanged;
        var startupRegistrationResult = startupSettings.Repair();

        if (recoverySafe)
        {
            _steamSessionWatcher.Start();
            _effectiveSteamSessionSource.Refresh();
        }
        else
        {
            AppLog.Warn("Recovery", "Steam/controller routing remains stopped because recovery is unsafe.", null, ("Action", "Passive"));
        }

        var recoverySafetyState = new RecoverySafetyState(recoverySafe ? RecoverySafety.Safe : RecoverySafety.Unsafe);
        var powerGate = new PowerMutationGate();
        var nativeState = msiClawAdapter.NativeState as MsiClawNativeStateManager;
        var statusProvider = new SystemStatusProvider(
            new WindowsDeviceInformationProvider(),
            new WindowsDeviceProbeContextFactory(),
            new HardwareCompatibilityEvaluator(deviceRegistry),
            controllerEnvironmentAssessmentProvider,
            new RuntimePrerequisiteInspector(
                new HidHidePrerequisiteInspector(new HidHideDriverClient()),
                new UsbIpWin2PrerequisiteInspector(new WindowsUsbIpWin2DeviceProbe(new WindowsControllerDeviceEnumerator()), new WindowsUsbIpWin2PackageProbe()),
                new ViiperRuntimeInspector()),
            () => _effectiveSteamSessionSource?.State ?? SteamSessionState.FromRunningAppId(0),
            () => recoverySafetyState.Current == RecoverySafety.Safe,
            () => addonOwnedVirtualDeviceTracker.HasUncertainOwnership);
        _msiClawNativeModeSession = nativeState is null ? null : new MsiClawNativeModeSessionCoordinator(
            nativeState,
            _recoveryManager!,
            powerGate,
            recoverySafetyState);
        if (_msiClawNativeModeSession is not null)
        {
            var nativeModeStage = new MsiClawNativeModeStage(_msiClawNativeModeSession);
            _physicalInputSource = new MsiClawInputSource(() => new VorticeDirectInputDeviceEnumerator(IntPtr.Zero));
            var physicalInputStage = new MsiClawPhysicalInputStage(() => new VorticeDirectInputDeviceEnumerator(IntPtr.Zero), _physicalInputSource);
            var physicalIsolationStage = new MsiClawPhysicalIsolationStage(
                physicalInputStage,
                _msiClawNativeModeSession,
                _recoveryManager!,
                new HidHideDriverClient(),
                () => Environment.ProcessPath);
            var canonicalViiperPath = Path.Combine(AppContext.BaseDirectory, "Dependencies", "Viiper", "libVIIPER.dll");
            // Developer/test-only routing seam for SD2: the production Steam routing path remains
            // Gordon by default (docs/VIIPER_MIGRATION_TODO.md SD2/SD4). Setting this internal,
            // non-persisted environment variable lets a developer run the parallel Steam Deck path
            // (28DE:1205) for the first real MSI Claw hardware smoke test (SD3) without adding a
            // public "Gordon vs Steam Deck" user-facing setting. This is deliberately not wired
            // through DeveloperTestModeState/the Developer page UI.
            var useSteamDeckOutput = Environment.GetEnvironmentVariable("STEAMINPUT_ADDON_DEV_STEAMDECK_OUTPUT") == "1";
            IRoutingPipelineStage steamOutputStage;
            Action attachOutputFaultHandler;
            if (useSteamDeckOutput)
            {
                var deckStage = new CanonicalSteamDeckOutputStage(
                    () => new CanonicalSteamDeckSession(CanonicalViiperNativeApi.Load(canonicalViiperPath)),
                    new WindowsControllerDeviceEnumerator(),
                    new SteamDeckVirtualDeviceIdentityResolver(new SteamDeckVirtualDeviceIdentityPolicy()),
                    addonOwnedVirtualDeviceTracker,
                    _recoveryManager!,
                    () => _msiClawNativeModeSession?.CurrentRecoverySessionId,
                    new HidHideDriverClient(), _physicalInputSource);
                steamOutputStage = deckStage;
                attachOutputFaultHandler = () => deckStage.SetOutputFaultHandler(async () => { await _routingRuntimeCoordinator!.FailClosedAsync().ConfigureAwait(false); });
            }
            else
            {
                var gordonStage = new ClassicSteamControllerOutputStage(
                    () => new CanonicalSteamControllerSession(CanonicalViiperNativeApi.Load(canonicalViiperPath)),
                    new WindowsControllerDeviceEnumerator(),
                    new ViiperVirtualDeviceIdentityResolver(new ViiperVirtualDeviceIdentityPolicy()),
                    addonOwnedVirtualDeviceTracker,
                    _recoveryManager!,
                    () => _msiClawNativeModeSession?.CurrentRecoverySessionId,
                    new HidHideDriverClient(), _physicalInputSource);
                steamOutputStage = gordonStage;
                attachOutputFaultHandler = () => gordonStage.SetOutputFaultHandler(async () => { await _routingRuntimeCoordinator!.FailClosedAsync().ConfigureAwait(false); });
            }
            var pipelineExecutor = new RoutingPipelineExecutor([nativeModeStage, physicalInputStage, physicalIsolationStage, steamOutputStage]);
            var pipelineSessionCoordinator = new RoutingPipelineSessionCoordinator(pipelineExecutor);
            _routingRuntimeCoordinator = new RoutingPipelineRuntimeCoordinator(
                statusProvider,
                pipelineSessionCoordinator,
                [_msiClawNativeModeSession]);
            attachOutputFaultHandler();
        }
        _userTerminationGuard = new UserTerminationGuard(
            () => _routingRuntimeCoordinator?.CaptureTerminationSnapshot() ?? default,
            () => _msiClawNativeModeSession?.IsActive == true,
            () => _msiClawNativeModeSession?.HasOwnedRecoveryBoundary == true,
            () => recoverySafetyState.Current == RecoverySafety.Safe && _recoveryManager?.HasIncompleteRecovery == true);
        var powerParticipants = new List<IPowerSuspendParticipant>();
        if (_routingRuntimeCoordinator is not null) powerParticipants.Add(_routingRuntimeCoordinator);
        _powerCoordinator = new PowerTransitionCoordinator(powerGate, recoverySafetyState, powerParticipants, async token =>
        {
            if (_routingRuntimeCoordinator is null) return true;
            _resumeFreshReconcileSuppression.Begin();
            _resumeFreshReconcileSuppression.ExecuteExplicitRefresh(() =>
            {
                _steamSessionWatcher?.Refresh();
                _effectiveSteamSessionSource?.Refresh();
            });
            return await RoutingReconcileStatusRefresh.RunResumeFreshAsync(
                freshReconcile: token => _routingRuntimeCoordinator.ReconcileFreshAfterResumeAsync(token).AsTask(),
                completeSuppression: _resumeFreshReconcileSuppression.Complete,
                deferredReconcile: () => ReconcileRoutingAsync(),
                requestStatusRefresh: () => _mainWindow?.RequestStatusRefresh(),
                cancellationToken: token).ConfigureAwait(false);
        }, recoveryEnabled: recoverySafe,
        hasIncompleteRecovery: () => _recoveryManager?.HasIncompleteRecovery == true,
        establishBaseline: async token =>
        {
            if (stockCenterMBaseline is null) return false;
            return (await stockCenterMBaseline.EstablishAsync(token).ConfigureAwait(false)).Succeeded;
        },
        hasResidualRoutingCleanup: () => _routingRuntimeCoordinator?.HasResidualSessionState == true,
        retryResidualRoutingCleanup: async token =>
        {
            if (_routingRuntimeCoordinator is null) return true;
            return await _routingRuntimeCoordinator.RetryResidualCleanupForResumeAsync(token).ConfigureAwait(false);
        });
        _powerWatcher = new PowerTransitionWatcher(new WindowsSuspendResumeNotificationSource(), powerGate, _powerCoordinator,
            () => _routingRuntimeCoordinator?.CancelInFlightTransition());
        if (!_powerWatcher.Start()) AppLog.Error("Power.Notify", "Suspend/resume notification registration failed.", new InvalidOperationException("PowerRegisterSuspendResumeNotification failed."));
        else if (recoverySafetyState.Current == RecoverySafety.Safe) powerGate.OpenAfterRecovery();
        RoutingRuntimeStatusSnapshot CaptureRoutingRuntimeStatus() => _routingRuntimeCoordinator is null
            ? RoutingRuntimeStatusSnapshot.Unavailable
            : new(
                Available: true,
                OperationalState: _routingRuntimeCoordinator.CurrentOperationalState,
                SteamOutputActive: _routingRuntimeCoordinator.ActiveSessionHasSteamOutputEnabled,
                NativeDirectInputActive: _msiClawNativeModeSession?.IsActive == true);
        _mainWindow = new MainWindow(startupSettings, startupRegistrationResult.Message, _recoveryManager, statusProvider,
            developerTestModeState: _developerTestModeState, routingRuntimeStatusProvider: CaptureRoutingRuntimeStatus);
        _mainWindow.Closed += OnMainWindowClosed;
        _mainWindow.AppWindow.Closing += OnMainWindowClosing;

        try
        {
            _systemTrayIcon = new SystemTrayIcon(WinRT.Interop.WindowNative.GetWindowHandle(_mainWindow), ShowMainWindow, RestartApplication, ExitApplication, GetUserTerminationDecision);
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
        _ = ReconcileRoutingAsync();

    }

    private void OnEffectiveSteamSessionStateChanged(object? sender, SteamSessionStateChangedEventArgs args)
    {
        _diagnosticSessions.Observe(_runningAppIdSource?.GetRunningAppId() ?? 0, args.Current.RunningAppId, args.Current.Source.ToString());
        _mainWindow?.UpdateSteamSessionState(args.Current);
        if (!_resumeFreshReconcileSuppression.TrySuppressStateChange()) _ = ReconcileRoutingAsync();
    }

    private async Task ReconcileRoutingAsync(CancellationToken cancellationToken = default)
    {
        var runtime = _routingRuntimeCoordinator;
        if (runtime is null) return;
        await RoutingReconcileStatusRefresh.RunAsync(async () =>
        {
            try
            {
                var result = await runtime.ReconcileAsync(cancellationToken).ConfigureAwait(false);
                if (!result.Succeeded)
                    AppLog.Warn("Routing.Runtime", "Canonical routing reconciliation did not complete successfully.", null,
                        ("Action", result.Action), ("State", result.State), ("Reason", result.Reason));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
            catch (Exception exception)
            {
                AppLog.Warn("Routing.Runtime", "Canonical routing reconciliation failed; routing is being failed closed.", exception);
                try
                {
                    if (_msiClawNativeModeSession is not null)
                        await _msiClawNativeModeSession.LatchRoutingFaultAsync("CanonicalRoutingReconciliationFailed", CancellationToken.None).ConfigureAwait(false);
                    var rollback = await runtime.FailClosedAsync().ConfigureAwait(false);
                    if (!rollback.Succeeded)
                        AppLog.Error("Routing.Runtime", "Pipeline fail-close rollback did not complete.", new InvalidOperationException(rollback.Reason));
                }
                catch (Exception rollbackException)
                {
                    AppLog.Error("Routing.Runtime", "Pipeline fail-close rollback threw an exception.", rollbackException);
                }
            }
        }, () => _mainWindow?.RequestStatusRefresh()).ConfigureAwait(false);
    }

    private void OnMainWindowClosed(object sender, WindowEventArgs args)
    {
        if (!_isExplicitExit)
        {
            return;
        }

        _startupCancellationTokenSource.Cancel();
        _diagnosticSessions.Complete();

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
        _steamBigPictureWatcher?.Dispose();
        _steamBigPictureWatcher = null;

        _runningAppIdSource?.Dispose();
        _runningAppIdSource = null;
        _systemTrayIcon?.Dispose();
        _systemTrayIcon = null;
        _powerWatcher?.Dispose();
        _powerWatcher = null;
        if (_routingRuntimeCoordinator is not null)
        {
            try
            {
                var shutdown = _routingRuntimeCoordinator.ShutdownAsync().AsTask().GetAwaiter().GetResult();
                if (!shutdown.Succeeded && _msiClawNativeModeSession is not null)
                    _msiClawNativeModeSession.FailClosedAsync("ApplicationShutdownRoutingRollbackFailed", CancellationToken.None).GetAwaiter().GetResult();
            }
            catch (Exception exception)
            {
                AppLog.Error("Routing.Runtime", "Routing pipeline shutdown failed; attempting NativeMode fail-close.", exception);
                if (_msiClawNativeModeSession is not null)
                {
                    try { _msiClawNativeModeSession.FailClosedAsync("ApplicationShutdownRoutingRollbackFailed", CancellationToken.None).GetAwaiter().GetResult(); }
                    catch (Exception failClosedException) { AppLog.Error("NativeMode", "NativeMode shutdown fail-close failed.", failClosedException); }
                }
            }
            _routingRuntimeCoordinator = null;
        }
        if (_powerCoordinator is not null) _powerCoordinator.DisposeAsync().AsTask().GetAwaiter().GetResult();
        _powerCoordinator = null;
        if (_msiClawNativeModeSession is not null) _msiClawNativeModeSession.DisposeAsync().AsTask().GetAwaiter().GetResult();
        _msiClawNativeModeSession = null;
        if (_physicalInputSource is not null) _physicalInputSource.DisposeAsync().AsTask().GetAwaiter().GetResult();
        _physicalInputSource = null;
        AppLog.Info("Runtime cleanup completed.");
        // Shutdown ownership lives solely in Program.Main's `finally` (runs once Application.Start
        // returns, i.e. after this method), so it drains exactly this entry plus everything queued
        // before it -- without also blocking here for up to its own separate timeout.
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
        _userTerminationGuard?.Evaluate() ?? new(true, UserTerminationBlockReason.None);

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

internal sealed class ResumeFreshReconcileSuppression
{
    private readonly Lock _sync = new();
    private int _owned;
    private int _pending;

    public void Begin()
    {
        lock (_sync)
        {
            _pending = 0;
            _owned = 1;
        }
    }

    public bool TrySuppressStateChange()
    {
        lock (_sync)
        {
            if (_owned == 0) return false;
            _pending = 1;
            return true;
        }
    }

    public void ExecuteExplicitRefresh(Action refresh)
    {
        lock (_sync)
        {
            refresh();
            _pending = 0;
        }
    }

    public bool Complete(bool freshSucceeded)
    {
        lock (_sync)
        {
            _owned = 0;
            var pending = _pending != 0;
            _pending = 0;
            return freshSucceeded && pending;
        }
    }
}
