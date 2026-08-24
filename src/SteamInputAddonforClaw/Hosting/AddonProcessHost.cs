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
using SteamInputAddonforClaw.Profiles.Display;
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
    UnsupportedHardware,
    IndeterminateHardware,
    Canceled
}

internal sealed class AddonProcessHost : IAsyncDisposable
{
    private readonly string[]? _updateRestartArguments;
    private readonly Func<AddonStartupComposition, StartupResult, AddonRuntimeComposition>? _runtimeCompositionFactory;
    private readonly Func<string>? _frontendPipeNameFactory;
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
    private NamedPipeAddonFrontendServer? _qamFrontendServer;
    private readonly FrontendProcessLauncher _frontendLauncher;
    private readonly QamHostProcessController _qamHostController;
    private readonly GameBarForegroundWatcher _gameBarForegroundWatcher;
    private readonly GameBarForegroundPresentationDelivery _gameBarDelivery;
    private readonly WinGSuppressionGuard _winGSuppressionGuard = new();

    // Device/Profile Runtime -- a sibling capability of the routing/OEM1 composition above, not a
    // member of it (work order PR276 sections 0/2/12): CPU Boost must remain fully usable even with
    // Routing/OEM1/Steam/the frontend absent, so it is constructed and reconciled independently
    // here rather than inside AddonRuntimeCompositionFactory/AddonRoutingRuntime.
    private readonly ProfileStore _profileStore;
    private readonly ProfileMutationGate _profileMutationGate = new();
    private readonly CpuBoostRuntime _cpuBoostRuntime;
    private readonly PowerModeRuntime _powerModeRuntime;
    private readonly GameProfileMutations _gameProfileMutations;
    private readonly GameDisplayResolutionRuntime _displayResolutionRuntime;
    private readonly IntelFrameLimiterRuntime _intelFpsRuntime;
    private WindowsIntelFpsPowerNotificationSource? _intelFpsPowerSource;
    private TdpRuntime? _tdpRuntime;
    private HelperMsiClawTdpTransport? _tdpTransport;
    private TdpPowerLifecycleWatcher? _tdpPowerLifecycleWatcher;
    private TdpCenterMRegistryWatcher? _tdpCenterMRegistryWatcher;

    private int _processShutdownStarted;
    private int _runtimeShutdownPrepared;
    private Task? _deferredRuntimeStartup;

    internal AddonProcessHost(string[]? updateRestartArguments,
        Func<AddonStartupComposition, StartupResult, AddonRuntimeComposition>? testRuntimeCompositionFactory = null,
        string? testOnlyDataRoot = null,
        Func<string>? testFrontendPipeNameFactory = null,
        Func<string?, IIntelFrameLimiter>? testIntelFrameLimiterFactory = null)
    {
        _updateRestartArguments = updateRestartArguments;
        _runtimeCompositionFactory = testRuntimeCompositionFactory;
        _frontendPipeNameFactory = testFrontendPipeNameFactory;
        var profilePath = testOnlyDataRoot is null
            ? AddonDataPaths.ProfilesPath
            : Path.Combine(testOnlyDataRoot, "profiles.json");
        var logDirectory = testOnlyDataRoot is null
            ? Install.AddonDataPaths.LogDirectory
            : Path.Combine(testOnlyDataRoot, "logs");
        _profileStore = new(profilePath);
        _cpuBoostRuntime = new(_profileStore, mutationGate: _profileMutationGate);
        _powerModeRuntime = new(_profileStore, mutationGate: _profileMutationGate);
        _gameProfileMutations = new(_profileStore, _profileMutationGate);
        _displayResolutionRuntime = new(_profileStore, _profileMutationGate, testOnlyDataRoot);
        var fpsMarker = testOnlyDataRoot is null ? AddonDataPaths.IntelFpsLimitOwnershipPath : Path.Combine(testOnlyDataRoot, "intel-fps-limit-ownership.json");
        var fpsLimiter = testIntelFrameLimiterFactory?.Invoke(fpsMarker) ?? (testOnlyDataRoot is null ? new IntelFrameLimiter(fpsMarker) : new UnavailableIntelFrameLimiter());
        _intelFpsRuntime = new(_profileStore, _profileMutationGate, fpsLimiter, marker: fpsMarker);
        _frontendLauncher = new FrontendProcessLauncher(AppContext.BaseDirectory, logDirectory);
        _qamHostController = new QamHostProcessController(AppContext.BaseDirectory, logDirectory);
        _gameBarForegroundWatcher = new GameBarForegroundWatcher();
        _gameBarDelivery = new GameBarForegroundPresentationDelivery(
            foreground => _runtimeHost?.HandleGameBarForegroundChangedAsync(foreground) ?? Task.FromResult(false));
    }

    internal bool IsTrayAvailable => _systemTrayIcon?.IsAvailable == true;
    internal IAddonFrontendControl FrontendControl => _frontendControl ?? throw new InvalidOperationException("Frontend control has not been initialized.");

    internal void TestOnly_SetStartupForInitialization(AddonStartupComposition composition, StartupResult result)
    {
        _startupComposition = composition;
        _startupResult = result;
        _startupOutcome = AddonProcessStartupOutcome.RuntimeReady;
    }

    internal async Task<AddonProcessStartupOutcome> RunStartupAsync()
    {
        if (_startupOutcome is not null) return _startupOutcome.Value;
        if (Interlocked.Exchange(ref _startupStarted, 1) != 0)
            throw new InvalidOperationException("Startup has already been started.");

        try
        {
            // Display recovery is independent of controller hardware compatibility. Restore any
            // outstanding Addon-owned mode before startup can exit for an unsupported or
            // indeterminate device result.
            _displayResolutionRuntime.StartupRecover();
        }
        catch (Exception exception)
        {
            AppLog.Error("Profiles.Display", "Display resolution startup recovery failed.", exception);
        }

        // FPS ownership recovery is independent of hardware compatibility. Only initialize
        // IGCL on this early path when the Addon left explicit ownership evidence behind;
        // ordinary startup remains free of native driver work until the deferred profile phase.
        try
        {
            if (_intelFpsRuntime.HasPendingOwnership)
            {
                _intelFpsRuntime.Initialize();
                _intelFpsRuntime.StartupRecover();
            }
        }
        catch (Exception exception)
        {
            AppLog.Error("Profiles.IntelFps", "Stale Intel FPS startup recovery failed.", exception);
        }

        AppLog.Info("Startup coordination started.");
        var startupComposition = AddonStartupCompositionFactory.Create(_updateRestartArguments);
        _startupComposition = startupComposition;

        try
        {
            var startupResult = await startupComposition.Coordinator.RunAsync(_startupCancellationTokenSource.Token).ConfigureAwait(false);
            _startupResult = startupResult;

            if (startupResult.HardwareStatus is HardwareCompatibilityStatus.Unsupported or HardwareCompatibilityStatus.Indeterminate)
            {
                var unsupported = startupResult.HardwareStatus == HardwareCompatibilityStatus.Unsupported;
                NativeStartupWarning.Show(unsupported
                    ? "This device is not supported by Steam Addon for Claw."
                    : "This device could not be identified. Steam Addon for Claw will exit without making any changes.");
                _startupOutcome = unsupported
                    ? AddonProcessStartupOutcome.UnsupportedHardware
                    : AddonProcessStartupOutcome.IndeterminateHardware;
                return _startupOutcome.Value;
            }

            _startupOutcome = startupResult.ShouldStartRuntime
                ? AddonProcessStartupOutcome.RuntimeReady
                : AddonProcessStartupOutcome.UpdateRestartScheduled;

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

        var composition = _runtimeCompositionFactory?.Invoke(startupComposition, startupResult)
            ?? AddonRuntimeCompositionFactory.Create(
                startupComposition.HandheldDeviceAdapter,
                startupComposition.DeviceRegistry,
                startupComposition.ControllerEnvironmentAssessmentProvider,
                startupComposition.RuntimeRecoveryManager,
                startupComposition.StockCenterMBaseline,
                startupResult.RecoverySafe,
                startupResult.HardwareSupported,
                winGSuppressionGuard: _winGSuppressionGuard,
                bigPictureStateChanged: _qamHostController.OnBigPictureStateChanged,
                routingReconcileCompleted: null);

        // Frontend transport and tray readiness are independent of OEM1 activation. Routing still
        // awaits this task at its helper-acquisition boundary, so removing this process-wide await
        // does not reintroduce a shared-helper Start race.
        AppLog.Info("CenterM.Oem1", "OEM1 activation pending; Frontend transport will initialize independently.");

        _runtimeHost = composition.RuntimeHost;
        _cpuBoostRuntime.SetActualAppIdSource(() => _runtimeHost?.ActualRunningAppId ?? 0);
        _powerModeRuntime.SetActualAppIdSource(() => _runtimeHost?.ActualRunningAppId ?? 0);
        _intelFpsRuntime.SetActualAppIdSource(() => _runtimeHost?.ActualRunningAppId ?? 0);
        _runtimeHost.ActualRunningAppIdChanged += OnActualRunningAppIdChanged;
        if (startupResult.EnvironmentMode == ControllerEnvironmentMode.StockCenterM
            && startupResult.HardwareDeviceModel is { } tdpModel
            && MsiClawTdpPolicy.TryResolve(tdpModel, out _))
        {
            _gameProfileMutations.SetModelId(tdpModel);
            _tdpTransport = new();
            _tdpRuntime = new(_profileStore, _profileMutationGate, tdpModel,
                new MsiClawTdpHardware(_tdpTransport));
            _tdpRuntime.SetActualAppIdSource(() => _runtimeHost?.ActualRunningAppId ?? 0);
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
            cpuBoostRuntime: _cpuBoostRuntime, tdpRuntime: _tdpRuntime, gameProfileMutations: _gameProfileMutations,
            actualRunningAppIdSource: () => _runtimeHost?.ActualRunningAppId ?? 0, displayResolutionRuntime: _displayResolutionRuntime, powerModeRuntime: _powerModeRuntime,
            intelFpsRuntime: _intelFpsRuntime, fanProbeTransport: _tdpTransport,
            trustedHidHideApplicationPaths: composition.TrustedHidHideApplicationPaths);
        var pipeName = _frontendPipeNameFactory?.Invoke() ?? FrontendPipeEndpoint.CreateForCurrentUser();
        _frontendServer = new NamedPipeAddonFrontendServer(pipeName, _frontendControl);
        var qamPipeName = FrontendPipeEndpoint.CreateQamForCurrentUser();
        try
        {
            AppLog.Debug("FrontendTransport", "Frontend named-pipe server starting.", ("PipeName", pipeName));
            await _frontendServer.StartAsync().ConfigureAwait(false);
            _frontendLauncher.MarkRuntimeReady();
            AppLog.Info("FrontendTransport", "Frontend named-pipe server ready.", ("PipeName", pipeName));
        }
        catch (Exception exception)
        {
            AppLog.Error("FrontendTransport", "Frontend named-pipe server startup failed.", exception,
                ("PipeName", pipeName), ("ExceptionType", exception.GetType().FullName ?? exception.GetType().Name),
                ("HResult", $"0x{exception.HResult:X8}"));
            throw;
        }
        try
        {
            _qamFrontendServer = new NamedPipeAddonFrontendServer(qamPipeName, _frontendControl);
            AppLog.Debug("FrontendTransport", "QAM frontend named-pipe server starting.", ("PipeName", qamPipeName));
            await _qamFrontendServer.StartAsync().ConfigureAwait(false);
            AppLog.Info("FrontendTransport", "QAM frontend named-pipe server ready.", ("PipeName", qamPipeName));
        }
        catch (Exception exception)
        {
            AppLog.Warn("FrontendTransport", "QAM frontend named-pipe server unavailable; continuing without QAM bridge.", exception,
                ("PipeName", qamPipeName), ("ExceptionType", exception.GetType().FullName ?? exception.GetType().Name));
            if (_qamFrontendServer is not null)
                await _qamFrontendServer.DisposeAsync().ConfigureAwait(false);
            _qamFrontendServer = null;
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
            _displayResolutionRuntime.Reconcile(_runtimeHost?.ActualRunningAppId ?? 0);
        }
        catch (Exception exception) { AppLog.Error("Profiles.Display", "Display resolution startup reconcile failed.", exception); }
        try
        {
            _cpuBoostRuntime.StartupReconcile(_runtimeHost?.ActualRunningAppId ?? 0);
        }
        catch (Exception exception)
        {
            AppLog.Error("Profiles.CpuBoost", "CPU Boost startup reconcile failed.", exception);
        }
        try { _powerModeRuntime.StartupReconcile(_runtimeHost?.ActualRunningAppId ?? 0); }
        catch (Exception exception) { AppLog.Error("Profiles.PowerMode", "Power Mode startup reconcile failed.", exception); }
        try
        {
            _intelFpsRuntime.Initialize();
            _intelFpsRuntime.StartupRecover();
            _intelFpsRuntime.StartupReconcile(_runtimeHost?.ActualRunningAppId ?? 0);
            _intelFpsPowerSource = new WindowsIntelFpsPowerNotificationSource();
            if (!_intelFpsPowerSource.TryRegister()) AppLog.Warn("Profiles.IntelFps", "AC/DC power notification registration failed.");
            _intelFpsPowerSource.Changed += OnIntelFpsPowerSourceChanged;
        }
        catch (Exception exception) { AppLog.Error("Profiles.IntelFps", "Intel FPS startup reconcile failed.", exception); }

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
        _intelFpsPowerSource?.Dispose(); _intelFpsPowerSource = null;
        _intelFpsRuntime.BeginShutdown();
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
        if (_qamFrontendServer is not null)
        {
            await _qamFrontendServer.DisposeAsync().ConfigureAwait(false);
            _qamFrontendServer = null;
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
        if (_tdpTransport is not null)
        {
            await _tdpTransport.DisposeAsync().ConfigureAwait(false);
            _tdpTransport = null;
        }
        _intelFpsRuntime.Dispose();
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
        if (_runtimeHost is not null)
            _runtimeHost.ActualRunningAppIdChanged -= OnActualRunningAppIdChanged;
        if (_frontendControl is SteamInputAddonforClaw.Frontend.InProcessAddonFrontendControl control)
            control.BeginProcessShutdown();
        try { _displayResolutionRuntime.Shutdown(); } catch (Exception exception) { AppLog.Error("Profiles.Display", "Display resolution shutdown restore failed.", exception); }
        _runtimeHost?.PrepareForShutdown();
    }

    private void OnActualRunningAppIdChanged(uint appId)
    {
        try
        {
            _cpuBoostRuntime.Reconcile(appId);
        }
        catch (Exception exception)
        {
            AppLog.Error("Profiles.CpuBoost", "CPU Boost game-profile reconcile failed after Actual RunningAppID changed.", exception,
                ("RunningAppID", appId));
        }
        try { _powerModeRuntime.Reconcile(appId); }
        catch (Exception exception) { AppLog.Error("Profiles.PowerMode", "Power Mode game-profile reconcile failed after Actual RunningAppID changed.", exception); }

        try
        {
            _displayResolutionRuntime.Reconcile(appId);
        }
        catch (Exception exception)
        {
            AppLog.Error("Profiles.Display", "Display resolution reconcile failed after Actual RunningAppID changed.", exception,
                ("RunningAppID", appId));
        }

        try
        {
            _tdpRuntime?.ReconcileCurrent(forceApply: true, invalidateHardwareCache: false, "ActualRunningAppIdChanged");
        }
        catch (Exception exception)
        {
            AppLog.Error("Profiles.Tdp", "TDP game-profile reconcile failed after Actual RunningAppID changed.", exception,
                ("RunningAppID", appId));
        }
        try { _intelFpsRuntime.Reconcile(appId, "ActualRunningAppIdChanged"); }
        catch (Exception exception) { AppLog.Error("Profiles.IntelFps", "FPS game-profile reconcile failed after Actual RunningAppID changed.", exception, ("RunningAppID", appId)); }
    }

    private void OnIntelFpsPowerSourceChanged() => _ = Task.Run(() => _intelFpsRuntime.Reconcile(_runtimeHost?.ActualRunningAppId ?? 0, "PowerSourceChanged"));

    private void OnGameBarForegroundChanged(object? sender, EventArgs args) =>
        _gameBarDelivery.Request(_gameBarForegroundWatcher.IsForeground);

    private void RequestGameBarPresentationReconcile() =>
        _gameBarDelivery.Request(_gameBarForegroundWatcher.IsForeground);
}

internal static class NativeStartupWarning
{
    private const uint MbOk = 0x00000000;
    private const uint MbIconWarning = 0x00000030;

    internal static void Show(string message) => MessageBoxW(0, message, "Steam Addon for Claw", MbOk | MbIconWarning);

    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern int MessageBoxW(nint hWnd, string lpText, string lpCaption, uint uType);
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
