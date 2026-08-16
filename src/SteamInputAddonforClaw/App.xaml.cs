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
using SteamInputAddonforClaw.Devices.Abstractions;
using SteamInputAddonforClaw.Devices.MSI.Claw;
using SteamInputAddonforClaw.Devices;
using SteamInputAddonforClaw.Prerequisites;
using SteamInputAddonforClaw.Runtime;
using SteamInputAddonforClaw.Status;
using SteamInputAddonforClaw.Routing;
using SteamInputAddonforClaw.VirtualOutput.Viiper;
using SteamInputAddonforClaw.Power;
using SteamInputAddonforClaw.Developer;

namespace SteamInputAddonforClaw;

public partial class App : Application
{
    private MainWindow? _mainWindow;
    private readonly CancellationTokenSource _startupCancellationTokenSource = new();
    private DispatcherQueue? _dispatcherQueue;
    private bool _showMainWindow;
    private SystemTrayIcon? _systemTrayIcon;
    private RecoveryManager? _recoveryManager;
    private bool _isExplicitExit;
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

    private void StartNormalRuntime(AddonOwnedVirtualDeviceTracker addonOwnedVirtualDeviceTracker, HandheldDeviceRegistry deviceRegistry, IHandheldDeviceAdapter handheldDeviceAdapter, IControllerEnvironmentAssessmentProvider controllerEnvironmentAssessmentProvider, IStockCenterMStartupBaseline? stockCenterMBaseline, ControllerEnvironmentMode environmentMode, ControllerEnvironmentReadiness environmentReadiness, bool recoverySafe)
    {
        AppLog.Info($"Starting runtime. Environment={environmentMode}; Readiness={environmentReadiness}.");
        var settingsStore = new SettingsStore(VelopackAppPaths.SettingsPath);
        var settings = settingsStore.Load();
        AppLog.MinimumLevelOverride = AppSettingsPolicy.ToAppLogLevel(settings.LogLevel);
        var startupRegistration = new WindowsTaskSchedulerStartupManager();
        var startupSettings = new StartupSettingsCoordinator(settings, settingsStore, startupRegistration);
        var steamRuntime = new SteamSessionRuntime(startupSettings);
        var startupRegistrationResult = startupSettings.Repair();

        if (recoverySafe)
        {
            steamRuntime.StartRoutingObservation();
        }
        else
        {
            AppLog.Warn("Recovery", "Steam/controller routing remains stopped because recovery is unsafe.", null, ("Action", "Passive"));
        }

        var recoverySafetyState = new RecoverySafetyState(recoverySafe ? RecoverySafety.Safe : RecoverySafety.Unsafe);
        var powerGate = new PowerMutationGate();
        var statusProvider = new SystemStatusProvider(
            new WindowsDeviceInformationProvider(),
            new WindowsDeviceProbeContextFactory(),
            new HardwareCompatibilityEvaluator(deviceRegistry),
            controllerEnvironmentAssessmentProvider,
            new RuntimePrerequisiteInspector(
                new HidHidePrerequisiteInspector(new HidHideDriverClient()),
                new UsbIpWin2PrerequisiteInspector(new WindowsUsbIpWin2DeviceProbe(new WindowsControllerDeviceEnumerator()), new WindowsUsbIpWin2PackageProbe()),
                new ViiperRuntimeInspector()),
            () => steamRuntime.State,
            () => recoverySafetyState.Current == RecoverySafety.Safe,
            () => addonOwnedVirtualDeviceTracker.HasUncertainOwnership);
        var routingRuntime = AddonRoutingRuntime.Create(
            handheldDeviceAdapter,
            statusProvider,
            addonOwnedVirtualDeviceTracker,
            _recoveryManager!,
            powerGate,
            recoverySafetyState);

        var recoveryManager = _recoveryManager!;
        Func<CancellationToken, Task<bool>> establishBaseline = stockCenterMBaseline is null
            ? _ => Task.FromResult(false)
            : async token => (await stockCenterMBaseline.EstablishAsync(token).ConfigureAwait(false)).Succeeded;

        _runtimeHost = new AddonRuntimeHost(
            steamRuntime,
            routingRuntime,
            powerGate,
            recoverySafetyState,
            recoverySafe,
            () => recoveryManager.HasIncompleteRecovery,
            establishBaseline);
        _runtimeHost.SteamSessionStateChanged += OnRuntimeSteamSessionStateChanged;
        _runtimeHost.StatusRefreshRequested += OnRuntimeStatusRefreshRequested;
        _runtimeHost.StartPowerObservation();
        RoutingRuntimeStatusSnapshot CaptureRoutingRuntimeStatus() => _runtimeHost?.CaptureRoutingStatus() ?? RoutingRuntimeStatusSnapshot.Unavailable;
        _mainWindow = new MainWindow(startupSettings, startupRegistrationResult.Message, _recoveryManager, statusProvider,
            developerTestModeState: _runtimeHost.DeveloperTestModeState, routingRuntimeStatusProvider: CaptureRoutingRuntimeStatus);
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

        _startupCancellationTokenSource.Cancel();
        _runtimeHost?.PrepareForShutdown();

        _systemTrayIcon?.Dispose();
        _systemTrayIcon = null;
        if (_runtimeHost is not null)
        {
            _runtimeHost.DisposeAsync().AsTask().GetAwaiter().GetResult();
            _runtimeHost = null;
        }
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
