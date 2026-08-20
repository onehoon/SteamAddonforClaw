using SteamInputAddonforClaw.Devices;
using SteamInputAddonforClaw.Contracts.Frontend;
using SteamInputAddonforClaw.Devices.Abstractions;
using SteamInputAddonforClaw.Diagnostics;
using SteamInputAddonforClaw.Developer;
using SteamInputAddonforClaw.Install;
using SteamInputAddonforClaw.Lifecycle;
using SteamInputAddonforClaw.Profiles;
using SteamInputAddonforClaw.Profiles.Performance;
using SteamInputAddonforClaw.Recovery;
using SteamInputAddonforClaw.Routing;
using SteamInputAddonforClaw.Runtime;
using SteamInputAddonforClaw.Settings;
using SteamInputAddonforClaw.Startup;
using SteamInputAddonforClaw.Status;
using SteamInputAddonforClaw.Steam;
using SteamInputAddonforClaw.VirtualOutput.Viiper;
using SteamInputAddonforClaw.FrontendTransport;
using SteamInputAddonforClaw.GameBar;

namespace SteamInputAddonforClaw.Hosting;

internal enum AddonProcessStartupOutcome
{
    RuntimeReady,
    UpdateRestartScheduled,
    Canceled
}

internal sealed class AddonProcessHost : IAsyncDisposable
{
    private readonly string[]? _updateRestartArguments;
    private readonly CancellationTokenSource _startupCancellationTokenSource = new();
    private AddonStartupComposition? _startupComposition;
    private AddonRuntimeHost? _runtimeHost;
    private AddonProcessStartupOutcome? _startupOutcome;
    private SystemTrayIcon? _systemTrayIcon;
    private NativeTrayHostWindow? _trayHostWindow;
    private int _runtimeInitialized;
    private int _disposed;
    private int _startupStarted;
    private IAddonFrontendControl? _frontendControl;
    private NamedPipeAddonFrontendServer? _frontendServer;
    private readonly FrontendProcessLauncher _frontendLauncher;
    private readonly QamHostProcessController _qamHostController;
    private readonly GameBarForegroundWatcher _gameBarForegroundWatcher;

    // Device/Profile Runtime -- a sibling capability of the routing/OEM1 composition above, not a
    // member of it (work order PR276 sections 0/2/12): CPU Boost must remain fully usable even with
    // Routing/OEM1/Steam/the frontend absent, so it is constructed and reconciled independently
    // here rather than inside AddonRuntimeCompositionFactory/AddonRoutingRuntime.
    private readonly CpuBoostRuntime _cpuBoostRuntime = new(new ProfileStore(AddonDataPaths.ProfilesPath));

    private int _processShutdownStarted;
    private int _runtimeShutdownPrepared;

    internal AddonProcessHost(string[]? updateRestartArguments)
    {
        _updateRestartArguments = updateRestartArguments;
        _frontendLauncher = new FrontendProcessLauncher(AppContext.BaseDirectory, Install.AddonDataPaths.LogDirectory);
        _qamHostController = new QamHostProcessController(AppContext.BaseDirectory, Install.AddonDataPaths.LogDirectory);
        _gameBarForegroundWatcher = new GameBarForegroundWatcher();
    }

    internal bool IsTrayAvailable => _systemTrayIcon?.IsAvailable == true;
    internal IAddonFrontendControl FrontendControl => _frontendControl ?? throw new InvalidOperationException("Frontend control has not been initialized.");

    internal async Task<AddonProcessStartupOutcome> RunStartupAsync()
    {
        if (_startupOutcome is not null) return _startupOutcome.Value;
        if (Interlocked.Exchange(ref _startupStarted, 1) != 0)
            throw new InvalidOperationException("Startup has already been started.");

        AppLog.Info("Startup coordination started.");
        var startupComposition = AddonStartupCompositionFactory.Create(_updateRestartArguments);
        _startupComposition = startupComposition;

        try
        {
            var startupResult = await startupComposition.Coordinator.RunAsync(_startupCancellationTokenSource.Token).ConfigureAwait(false);
            _startupOutcome = startupResult.ShouldStartRuntime
                ? AddonProcessStartupOutcome.RuntimeReady
                : AddonProcessStartupOutcome.UpdateRestartScheduled;
            _startupResult = startupResult;

            // QamHost itself remains BPM-scoped. Prepare only Steam's persistent CEF bootstrap
            // marker here so a normal future Steam/steamwebhelper launch exposes the loopback CDP
            // endpoint without requiring the user to add launch flags manually. Failure is
            // feature-local: controller/routing Runtime startup must continue normally.
            if (startupResult.ShouldStartRuntime)
                _ = SteamCefDebugBootstrap.Ensure();

            return _startupOutcome.Value;
        }
        catch (OperationCanceledException) when (_startupCancellationTokenSource.IsCancellationRequested)
        {
            _startupOutcome = AddonProcessStartupOutcome.Canceled;
            return _startupOutcome.Value;
        }
        catch (Exception exception)
        {
            AppLog.Error("Startup coordination failed.", exception);
            throw;
        }
    }

    private StartupResult? _startupResult;

    internal async Task InitializeRuntimeAsync()
    {
        if (_startupOutcome != AddonProcessStartupOutcome.RuntimeReady)
            throw new InvalidOperationException("Runtime initialization requires a successful startup.");
        if (Interlocked.Exchange(ref _runtimeInitialized, 1) != 0)
            throw new InvalidOperationException("Runtime has already been initialized.");

        var startupComposition = _startupComposition ?? throw new InvalidOperationException("Startup composition is unavailable.");
        var startupResult = _startupResult ?? throw new InvalidOperationException("Startup result is unavailable.");
        AppLog.Info($"Starting runtime. Environment={startupResult.EnvironmentMode}; Readiness={startupResult.EnvironmentReadiness}.");

        // CPU Boost Device/Profile Runtime startup reconcile -- a sibling capability, deliberately
        // independent of Routing/OEM1/Steam and run unconditionally here rather than gated on any
        // of the routing-specific state below (work order PR276 sections 0/2/12/14). A failure here
        // must never fail Addon Runtime startup or affect Routing/OEM1/VIIPER/HidHide.
        try
        {
            _cpuBoostRuntime.StartupReconcile();
        }
        catch (Exception exception)
        {
            AppLog.Error("Profiles.CpuBoost", "CPU Boost startup reconcile failed.", exception);
        }
        var composition = AddonRuntimeCompositionFactory.Create(
            startupComposition.HandheldDeviceAdapter,
            startupComposition.DeviceRegistry,
            startupComposition.ControllerEnvironmentAssessmentProvider,
            startupComposition.AddonOwnedVirtualDeviceTracker,
            startupComposition.RuntimeRecoveryManager,
            startupComposition.StockCenterMBaseline,
            startupResult.RecoverySafe,
            startupResult.HardwareSupported,
            _qamHostController.OnBigPictureStateChanged);

        // Review fix (BLOCKER): the OEM1 coordinator and the routing guard share the SAME underlying
        // helper ownership, but only their exact-handle Start() call itself serializes between them.
        // This must be awaited BEFORE StartPowerObservation()/the initial ReconcileAsync() (called by
        // RuntimeProcessApplication only after this method returns) can let routing enter and possibly
        // start the shared helper first -- otherwise both owners could race toward Start(), or routing
        // could win first while OEM1 later observes and re-arms around an operational helper the guard
        // still believes it exclusively owns.
        try
        {
            await composition.Oem1ActivationTask.ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            AppLog.Error("OEM1 startup activation did not complete cleanly.", exception);
        }

        _runtimeHost = composition.RuntimeHost;
        _frontendControl = new SteamInputAddonforClaw.Frontend.InProcessAddonFrontendControl(
            composition.StartupSettings, composition.StatusProvider, _runtimeHost, _runtimeHost.DeveloperTestModeState, composition.StartupRegistrationMessage,
            // Same single startup hardware-support result the routing composition's OEM1 gate above
            // received -- the UI and the runtime can never disagree about whether OEM1 mapping exists.
            oem1MappingAvailable: startupResult.HardwareSupported);
        _frontendServer = new NamedPipeAddonFrontendServer(
            FrontendPipeEndpoint.CreateForCurrentUser(),
            _frontendControl);
        await _frontendServer.StartAsync().ConfigureAwait(false);
        _frontendLauncher.MarkRuntimeReady();
        _startupComposition = null;
    }

    internal void RequestFrontendOpen(FrontendOpenReason reason) => _frontendLauncher.RequestOpen(reason);

    internal void StartPowerObservation() => GetRuntimeHost().StartPowerObservation();

    internal void StartRuntimeEventWatchers() => _gameBarForegroundWatcher.Start();

    internal Task ReconcileAsync(CancellationToken cancellationToken = default) => GetRuntimeHost().ReconcileAsync(cancellationToken);

    internal UserTerminationDecision EvaluateUserTermination() =>
        _runtimeHost?.EvaluateUserTermination() ?? new(true, UserTerminationBlockReason.None);

    internal bool TryInitializeTray(Action restart, Action exit)
    {
        try
        {
            _trayHostWindow = new NativeTrayHostWindow();
            _systemTrayIcon = new SystemTrayIcon(_trayHostWindow.Handle, () => RequestFrontendOpen(FrontendOpenReason.Tray), restart, exit, EvaluateUserTermination);
            return true;
        }
        catch (Exception exception)
        {
            _systemTrayIcon?.Dispose();
            _systemTrayIcon = null;
            _trayHostWindow?.Dispose();
            _trayHostWindow = null;
            AppLog.Error("Tray", "Tray initialization failed in headless Runtime mode.", exception);
            return false;
        }
    }

    internal void BeginProcessShutdown()
    {
        if (Interlocked.Exchange(ref _processShutdownStarted, 1) != 0) return;
        _frontendLauncher.StopAcceptingRequests();
        _gameBarForegroundWatcher.Dispose();
        _qamHostController.BeginShutdown();
        _startupCancellationTokenSource.Cancel();
        PrepareRuntimeForShutdown();
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;

        BeginProcessShutdown();
        if (_frontendServer is not null)
        {
            await _frontendServer.DisposeAsync().ConfigureAwait(false);
            _frontendServer = null;
        }
        PrepareRuntimeForShutdown();
        _systemTrayIcon?.Dispose();
        _systemTrayIcon = null;
        _trayHostWindow?.Dispose();
        _trayHostWindow = null;
        if (_runtimeHost is not null)
        {
            await _runtimeHost.DisposeAsync().ConfigureAwait(false);
            _runtimeHost = null;
        }
        await _qamHostController.DisposeAsync().ConfigureAwait(false);
        _startupComposition = null;
    }

    private AddonRuntimeHost GetRuntimeHost() => _runtimeHost ?? throw new InvalidOperationException("Runtime has not been initialized.");

    private void PrepareRuntimeForShutdown()
    {
        if (Interlocked.Exchange(ref _runtimeShutdownPrepared, 1) != 0) return;
        if (_frontendControl is SteamInputAddonforClaw.Frontend.InProcessAddonFrontendControl control)
            control.BeginProcessShutdown();
        _runtimeHost?.PrepareForShutdown();
    }
}
