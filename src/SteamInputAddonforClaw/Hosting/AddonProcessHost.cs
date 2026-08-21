using SteamInputAddonforClaw.Devices;
using SteamInputAddonforClaw.Devices.MSI.Claw;
using SteamInputAddonforClaw.Contracts.Frontend;
using SteamInputAddonforClaw.Devices.Abstractions;
using SteamInputAddonforClaw.Diagnostics;
using SteamInputAddonforClaw.Profiles.Performance;
using SteamInputAddonforClaw.Developer;
using SteamInputAddonforClaw.Install;
using SteamInputAddonforClaw.Lifecycle;
using SteamInputAddonforClaw.Profiles;
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
    private readonly GameBarForegroundPresentationDelivery _gameBarDelivery;
    private readonly WinGSuppressionGuard _winGSuppressionGuard = new();

    // Device/Profile Runtime -- a sibling capability of the routing/OEM1 composition above, not a
    // member of it (work order PR276 sections 0/2/12): CPU Boost must remain fully usable even with
    // Routing/OEM1/Steam/the frontend absent, so it is constructed and reconciled independently
    // here rather than inside AddonRuntimeCompositionFactory/AddonRoutingRuntime.
    private readonly ProfileStore _profileStore = new(AddonDataPaths.ProfilesPath);
    private readonly ProfileMutationGate _profileMutationGate = new();
    private readonly CpuBoostRuntime _cpuBoostRuntime;
    private TdpRuntime? _tdpRuntime;
    private TdpPowerLifecycleWatcher? _tdpPowerLifecycleWatcher;
    private TdpCenterMRegistryWatcher? _tdpCenterMRegistryWatcher;

    private int _processShutdownStarted;
    private int _runtimeShutdownPrepared;
    private Task? _deferredRuntimeStartup;

    internal AddonProcessHost(string[]? updateRestartArguments)
    {
        _updateRestartArguments = updateRestartArguments;
        _cpuBoostRuntime = new(_profileStore, mutationGate: _profileMutationGate);
        _frontendLauncher = new FrontendProcessLauncher(AppContext.BaseDirectory, Install.AddonDataPaths.LogDirectory);
        _qamHostController = new QamHostProcessController(AppContext.BaseDirectory, Install.AddonDataPaths.LogDirectory);
        _gameBarForegroundWatcher = new GameBarForegroundWatcher();
        _gameBarDelivery = new GameBarForegroundPresentationDelivery(
            foreground => _runtimeHost?.HandleGameBarForegroundChangedAsync(foreground) ?? Task.FromResult(false));
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

        var composition = AddonRuntimeCompositionFactory.Create(
            startupComposition.HandheldDeviceAdapter,
            startupComposition.DeviceRegistry,
            startupComposition.ControllerEnvironmentAssessmentProvider,
            startupComposition.AddonOwnedVirtualDeviceTracker,
            startupComposition.RuntimeRecoveryManager,
            startupComposition.StockCenterMBaseline,
            startupResult.RecoverySafe,
            startupResult.HardwareSupported,
            winGSuppressionGuard: _winGSuppressionGuard,
            bigPictureStateChanged: _qamHostController.OnBigPictureStateChanged,
            routingReconcileCompleted: null);

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
        if (startupResult.EnvironmentMode == ControllerEnvironmentMode.StockCenterM
            && startupResult.HardwareDeviceModel is { } tdpModel
            && MsiClawTdpPolicy.TryResolve(tdpModel, out _))
        {
            _tdpRuntime = new(_profileStore, _profileMutationGate, tdpModel,
                new MsiClawTdpHardware(new MsiClawWmiTdpTransport()));
            _tdpPowerLifecycleWatcher = new(_tdpRuntime, new WindowsTdpPowerNotificationSource());
            _tdpCenterMRegistryWatcher = new(() => _tdpPowerLifecycleWatcher?.ScheduleCenterMReconcile());
        }
        _frontendControl = new SteamInputAddonforClaw.Frontend.InProcessAddonFrontendControl(
            composition.StartupSettings, composition.StatusProvider, _runtimeHost, _runtimeHost.DeveloperTestModeState, composition.StartupRegistrationMessage,
            // Same single startup hardware-support result the routing composition's OEM1 gate above
            // received -- the UI and the runtime can never disagree about whether OEM1 mapping exists.
            oem1MappingAvailable: startupResult.HardwareSupported,
            // Device/Profile CPU Boost is a sibling capability of Routing/OEM1, not a member of the
            // routing composition above -- passed here as the SAME instance ReconcileDeviceProfileStartup()
            // reconciles, so the frontend and the Runtime never observe two different owners.
            cpuBoostRuntime: _cpuBoostRuntime, tdpRuntime: _tdpRuntime);
        var pipeName = FrontendPipeEndpoint.CreateForCurrentUser();
        _frontendServer = new NamedPipeAddonFrontendServer(pipeName, _frontendControl);
        try
        {
            AppLog.Debug("FrontendTransport", "Frontend named-pipe server starting.", ("PipeName", pipeName));
            await _frontendServer.StartAsync().ConfigureAwait(false);
            AppLog.Info("FrontendTransport", "Frontend named-pipe server ready.", ("PipeName", pipeName));
        }
        catch (Exception exception)
        {
            AppLog.Error("FrontendTransport", "Frontend named-pipe server startup failed.", exception,
                ("PipeName", pipeName), ("ExceptionType", exception.GetType().FullName ?? exception.GetType().Name),
                ("HResult", $"0x{exception.HResult:X8}"));
            throw;
        }
        _frontendLauncher.MarkRuntimeReady();
        _startupComposition = null;
    }

    internal void RequestFrontendOpen(FrontendOpenReason reason) => _frontendLauncher.RequestOpen(reason);

    internal void StartPowerObservation() => GetRuntimeHost().StartPowerObservation();

    internal async Task ShutdownRuntimeBeforeMessageLoopExitAsync()
    {
        if (_runtimeHost is not null)
            await _runtimeHost.DisposeAsync().ConfigureAwait(false);
    }

    internal void StartRuntimeEventWatchers()
    {
        // Game Bar foreground presentation remains dormant in production. Install the existing
        // Runtime-owned Win+G hook after all synchronous watcher work (none in this mode).
        _winGSuppressionGuard.Start();
    }

    internal void StartDeferredRuntimeStartup()
    {
        if (_deferredRuntimeStartup is not null) return;
        var cancellationToken = _startupCancellationTokenSource.Token;
        _deferredRuntimeStartup = Task.Run(async () =>
        {
            StartPowerObservation();
            var reconcile = ReconcileAsync(cancellationToken);
            ReconcileDeviceProfileStartup();
            await reconcile.ConfigureAwait(false);
        }, cancellationToken);
    }

    internal Task ReconcileAsync(CancellationToken cancellationToken = default) => GetRuntimeHost().ReconcileAsync(cancellationToken);

    /// <summary>
    /// CPU Boost Device/Profile Runtime startup reconcile -- a sibling capability, deliberately
    /// independent of Routing/OEM1/Steam. Must be called only after the controller Runtime has been
    /// initialized and the initial routing reconcile has been issued, so a slow/local-I/O or
    /// PowrProf call can never delay/block reaching the initial controller Routing reconcile (work
    /// order PR276 sections 0/2/12/14). A failure here must never fail Addon Runtime startup or
    /// affect Routing/OEM1/VIIPER/HidHide.
    /// </summary>
    internal void ReconcileDeviceProfileStartup()
    {
        try
        {
            _cpuBoostRuntime.StartupReconcile();
        }
        catch (Exception exception)
        {
            AppLog.Error("Profiles.CpuBoost", "CPU Boost startup reconcile failed.", exception);
        }

        try
        {
            if (_tdpRuntime is not null && _tdpPowerLifecycleWatcher is not null)
            {
                _tdpPowerLifecycleWatcher.Start();
                _tdpCenterMRegistryWatcher?.Start();
                _tdpPowerLifecycleWatcher.ScheduleStartup();
            }
        }
        catch (Exception exception)
        {
            AppLog.Error("Profiles.Tdp", "TDP startup reconcile failed.", exception);
        }
    }

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
        _gameBarDelivery.StopAccepting();
        _gameBarForegroundWatcher.StateChanged -= OnGameBarForegroundChanged;
        _gameBarForegroundWatcher.Dispose();
        _qamHostController.BeginShutdown();
        _tdpRuntime?.BeginShutdown();
        _tdpCenterMRegistryWatcher?.Dispose();
        _tdpCenterMRegistryWatcher = null;
        _tdpPowerLifecycleWatcher?.Dispose();
        _tdpPowerLifecycleWatcher = null;
        _startupCancellationTokenSource.Cancel();
        PrepareRuntimeForShutdown();
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;

        BeginProcessShutdown();
        await _gameBarDelivery.DrainAsync().ConfigureAwait(false);
        if (_deferredRuntimeStartup is not null)
        {
            try { await _deferredRuntimeStartup.ConfigureAwait(false); }
            catch (OperationCanceledException) when (_startupCancellationTokenSource.IsCancellationRequested) { }
            catch (Exception exception) { AppLog.Error("Startup", "Deferred Runtime startup work failed.", exception); }
            _deferredRuntimeStartup = null;
        }
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
            var runtimeHost = _runtimeHost;
            await runtimeHost.DisposeAsync().ConfigureAwait(false);
            _runtimeHost = null;
            FinalizeWinGGuardAfterRoutingShutdown(_winGSuppressionGuard, runtimeHost.RoutingShutdownSucceeded);
        }
        else
        {
            _winGSuppressionGuard.Dispose();
        }
        if (_tdpRuntime is not null)
        {
            await _tdpRuntime.DisposeAsync().ConfigureAwait(false);
            _tdpRuntime = null;
        }
        await _qamHostController.DisposeAsync().ConfigureAwait(false);
        _startupComposition = null;
    }

    internal static void FinalizeWinGGuardAfterRoutingShutdown(WinGSuppressionGuard guard, bool routingShutdownSucceeded)
    {
        ArgumentNullException.ThrowIfNull(guard);
        if (routingShutdownSucceeded)
            guard.Dispose();
        else
            AppLog.Warn("Wing.Guard", "Win+G hook retained until process exit because routing shutdown did not complete safely.");
    }

    private AddonRuntimeHost GetRuntimeHost() => _runtimeHost ?? throw new InvalidOperationException("Runtime has not been initialized.");

    private void PrepareRuntimeForShutdown()
    {
        if (Interlocked.Exchange(ref _runtimeShutdownPrepared, 1) != 0) return;
        if (_frontendControl is SteamInputAddonforClaw.Frontend.InProcessAddonFrontendControl control)
            control.BeginProcessShutdown();
        _runtimeHost?.PrepareForShutdown();
    }

    private void OnGameBarForegroundChanged(object? sender, EventArgs args) =>
        _gameBarDelivery.Request(_gameBarForegroundWatcher.IsForeground);

    private void RequestGameBarPresentationReconcile() =>
        _gameBarDelivery.Request(_gameBarForegroundWatcher.IsForeground);
}

internal sealed class GameBarForegroundPresentationDelivery
{
    private readonly Lock _sync = new();
    private readonly Func<bool, Task<bool>> _apply;
    private bool _desired;
    private bool _running;
    private bool _accepting = true;
    private long _requestVersion;
    private Task? _dispatch;

    internal GameBarForegroundPresentationDelivery(Func<bool, Task<bool>> apply) =>
        _apply = apply ?? throw new ArgumentNullException(nameof(apply));

    internal void Request(bool foreground)
    {
        lock (_sync)
        {
            if (!_accepting) return;
            _desired = foreground;
            _requestVersion++;
            if (_running) return;
            _running = true;
            _dispatch = Task.Run(DispatchAsync);
        }
    }

    internal void StopAccepting()
    {
        lock (_sync) _accepting = false;
    }

    internal async Task DrainAsync()
    {
        Task? dispatch;
        lock (_sync) dispatch = _dispatch;
        if (dispatch is not null) await dispatch.ConfigureAwait(false);
    }

    private async Task DispatchAsync()
    {
        try
        {
            while (true)
            {
                bool desired;
                long observedVersion;
                lock (_sync)
                {
                    if (!_accepting) return;
                    desired = _desired;
                    observedVersion = _requestVersion;
                }

                var applied = false;
                try
                {
                    AppLog.Debug("GameBar", "Game Bar presentation delivery started.", ("Foreground", desired));
                    applied = await _apply(desired).ConfigureAwait(false);
                }
                catch (Exception exception)
                {
                    AppLog.Warn("GameBar", "Game Bar presentation delivery was contained.", exception);
                }

                lock (_sync)
                {
                    if (!_accepting)
                    {
                        _running = false;
                        return;
                    }

                    var requestArrived = _requestVersion != observedVersion;
                    var latestStillSame = _desired == desired;
                    if (!requestArrived || (applied && latestStillSame))
                    {
                        _running = false;
                        return;
                    }
                }
            }
        }
        catch (Exception exception)
        {
            AppLog.Warn("GameBar", "Game Bar presentation dispatcher failed.", exception);
            lock (_sync) _running = false;
        }
    }
}
