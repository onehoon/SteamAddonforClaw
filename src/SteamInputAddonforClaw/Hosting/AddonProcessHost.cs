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
using SteamInputAddonforClaw.CenterMStartup;

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
    // One shared MSI Center M startup reader: the Device-page frontend feature, the mandatory
    // Runtime termination policy, and the mandatory launch-at-startup predicate all read from it
    // (PR2.5 work order section 8) rather than constructing independent Center M readers.
    private CenterMStartupControl? _centerMStartupControl;
    // PR12: the shared authority-transition owner also exposes the Runtime-owned uninstall-preparation
    // stock-restoration operation.
    private SteamInputAddonforClaw.CenterMStartup.ICenterMRebootAuthorityTransition? _centerMAuthorityTransition;
    private NamedPipeAddonFrontendServer? _frontendServer;
    private NamedPipeAddonFrontendServer? _qamFrontendServer;
    private readonly FrontendProcessLauncher _frontendLauncher;
    private readonly QamHostProcessController _qamHostController;
    private readonly OverlayProcessController _overlayController;
    // OQ3-A: one narrow cross-surface ordering gate so a normal user request cannot run the two
    // opposite Main UI <-> Overlay visibility transitions at the same time. Not a surface manager.
    private readonly SemaphoreSlim _visibleSurfaceTransition = new(1, 1);
    private static readonly TimeSpan MainUiCloseTimeout = TimeSpan.FromSeconds(6);
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
    private Task? _overlayStartup;
    // PR5: the process-lifetime Full PID1902 physical owner. Non-null only after an exact Disabled
    // boot; owns one live DirectInput session which PR6 consumes.
    private SteamInputAddonforClaw.Devices.MSI.Claw.IMsiClawAddonPhysicalOwnership? _physicalOwnership;
    // PR6: the process-lifetime Full-1902 virtual-presentation owner (one canonical VIIPER runtime +
    // exactly one attached typed device + its publisher). Retired before _physicalOwnership.
    private SteamInputAddonforClaw.Devices.MSI.Claw.IMsiClawAddonPresentation? _presentationOwnership;
    // PR7: the most recently requested runtime X360 <-> SteamDeck presentation reconcile. Serialized
    // inside the presentation owner's own gate; tracked only so controlled teardown can await it.
    private Task _presentationReconcile = Task.CompletedTask;
    // PR8: the one in-flight owned-DirectInput recovery, scheduled event-driven from an unexpected
    // owned-session completion. Serialized inside the physical owner's own gate; tracked only so
    // controlled teardown drains it BEFORE the presentation reconcile it may itself request.
    private Task _ownedControllerRecovery = Task.CompletedTask;
    // PR10 review [P1]: a single "a Device Arrival landed while a recovery was in flight" bit, so the
    // only real arrival signal is never lost to coalescing. Consumed for exactly one follow-up.
    private int _pendingOwnedControllerArrival;
    // PR10 review [P1]: once an owned-session completion reports unproven DirectInput cleanup, refuse
    // ALL further owned-controller recovery (including Device Arrival) for the rest of this Runtime
    // lifetime -- native resources may still be retained. A Runtime restart resets it.
    private int _ownedControllerRecoveryBlockedByCleanup;
    // PR10: one Runtime-owned, event-driven Windows Device Arrival observer. Non-null once a physical
    // owner has actually committed; it only wakes the existing recovery entrypoint, which re-proves
    // the strong MSI Claw identity itself. Disposed at BeginProcessShutdown before recovery drains.
    private Controllers.Detection.WindowsDeviceArrivalWatcher? _deviceArrivalWatcher;

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
        _overlayController = new OverlayProcessController(AppContext.BaseDirectory, logDirectory);
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

            // QamHost itself remains GamepadUI-session scoped. Prepare only Steam's persistent CEF bootstrap
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

        // PR4: reuse the ONE Center M startup control constructed by the startup composition -- the
        // same instance the authority branch read -- for the mandatory policy, PR3 transition, and
        // Device-page capture. No second Center M startup writer/manager is created.
        _centerMStartupControl = startupComposition.CenterMStartupControl;

        var composition = _runtimeCompositionFactory?.Invoke(startupComposition, startupResult)
            ?? AddonRuntimeCompositionFactory.Create(
                startupComposition.HandheldDeviceAdapter,
                startupComposition.DeviceRegistry,
                startupComposition.ControllerEnvironmentAssessmentProvider,
                startupComposition.RuntimeRecoveryManager,
                startupComposition.StockCenterMBaseline,
                startupResult.RecoverySafe,
                startupResult.HardwareSupported,
                startupResult.LegacyRoutingAllowed,
                winGSuppressionGuard: _winGSuppressionGuard,
                // PR7: forward the raw BPM bool to QAM unchanged, then request a Full-1902 runtime
                // presentation reconcile (BPM is half of the X360 <-> SteamDeck policy).
                bigPictureStateChanged: OnBigPictureStateChanged,
                routingReconcileCompleted: null,
                // PR2.5: while Center M startup config is exactly Disabled, launch-at-startup is a
                // mandatory-ON policy the Repair()/setter enforce -- not a user preference.
                isLaunchAtWindowsStartupRequired: IsControllerRuntimeMandatory);

        // Frontend transport and tray readiness are independent of OEM1 activation. Routing still
        // awaits this task at its helper-acquisition boundary, so removing this process-wide await
        // does not reintroduce a shared-helper Start race.
        AppLog.Info("CenterM.Oem1", "OEM1 activation pending; Frontend transport will initialize independently.");

        _runtimeHost = composition.RuntimeHost;
        _cpuBoostRuntime.SetActualAppIdSource(() => _runtimeHost?.ActualRunningAppId ?? 0);
        _powerModeRuntime.SetActualAppIdSource(() => _runtimeHost?.ActualRunningAppId ?? 0);
        _intelFpsRuntime.SetActualAppIdSource(() => _runtimeHost?.ActualRunningAppId ?? 0);
        _runtimeHost.ActualRunningAppIdChanged += OnActualRunningAppIdChanged;
        _runtimeHost.PowerResumeObserved += OnPowerResumeObserved;
        _qamHostController.OnActualRunningAppIdChanged(_runtimeHost.ActualRunningAppId);
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
        // PR3: the reboot-bound Center M controller-authority transition. Composes the already-merged
        // narrow owners -- the shared CenterMStartupControl, the composition's single
        // StartupSettingsCoordinator, and the persistent PR2 HidHide baseline (production-wired here
        // for the first time) -- plus the RAW lower-level Runtime safety decision (not
        // AddonProcessHost.EvaluateUserTermination, whose ControllerAuthorityMandatory outer rule
        // must never block the official Enable-and-Restart release path).
        var authorityHidHideBaseline = new SteamInputAddonforClaw.HidHide.AddonControllerHidHideBaseline(
            new SteamInputAddonforClaw.HidHide.HidHideDriverClient(),
            Environment.ProcessPath ?? throw new InvalidOperationException("The current executable path is unavailable."));
        var centerMAuthorityTransition = new SteamInputAddonforClaw.CenterMStartup.CenterMRebootAuthorityTransition(
            _centerMStartupControl,
            composition.StartupSettings,
            authorityHidHideBaseline,
            _runtimeHost.EvaluateUserTermination,
            () => IsConflictingControllerEnvironment(startupComposition.ControllerEnvironmentAssessmentProvider),
            async token =>
            {
                var status = await composition.StatusProvider.CaptureAsync(token).ConfigureAwait(false);
                return (status.Prerequisites, status.RecoverySafe);
            },
            // PR5/PR6: late-bound -- the owners are created after this transition owner, only for a
            // Disabled boot. PR6 section 17: the virtual presentation is retired and canonical VIIPER
            // is torn down BEFORE PR5 physical release; a virtual-release failure prevents everything
            // downstream (DirectInput stop, PID1901 restore, HidHide clear, Center M roots, restart).
            async token =>
            {
                if (_presentationOwnership is { } presentation && !await presentation.ReleaseForCenterMEnableAsync(token).ConfigureAwait(false))
                    return new SteamInputAddonforClaw.Devices.MSI.Claw.PhysicalOwnershipReleaseResult(false, "VirtualPresentationReleaseFailed", null);
                return _physicalOwnership is { } owner
                    ? await owner.ReleaseForCenterMEnableAsync(token).ConfigureAwait(false)
                    : SteamInputAddonforClaw.Devices.MSI.Claw.PhysicalOwnershipReleaseResult.NothingOwned;
            },
            // PR12 section 6/7: reuse the composition's existing StockCenterMStartupBaseline (the one
            // built from the shared MsiClawNativeStateManager). A machine with no MSI Claw fails
            // closed here rather than assuming stock.
            startupComposition.StockCenterMBaseline is { } stockBaseline
                ? stockBaseline.EstablishAsync
                : _ => Task.FromResult(new SteamInputAddonforClaw.Startup.StockCenterMStartupBaselineResult(false, false, "StockBaselineUnavailable")),
            // PR12 section 8: the one safely provable persisted Addon-owned primary PID1902 target.
            () => authorityHidHideBaseline.TryGetSingleExistingOwnedTarget(
                SteamInputAddonforClaw.Devices.MSI.Claw.MsiClawHardware.IsPrimaryDirectInputHidCollectionInstanceId),
            // PR12 section 11: startup-task removal routed through the existing registration owner.
            () => composition.StartupSettings.ChangeLaunchAtWindowsStartup(false),
            new SteamInputAddonforClaw.CenterMStartup.WindowsRestartRequester());
        _centerMAuthorityTransition = centerMAuthorityTransition;
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
            // MSI Center M startup Enable/Disable (work order PR1). The one shared reader -- also
            // consulted by the mandatory Runtime termination / launch-at-startup policy (PR2.5).
            centerMStartup: _centerMStartupControl,
            centerMAuthorityTransition: centerMAuthorityTransition);
        var pipeName = _frontendPipeNameFactory?.Invoke() ?? FrontendPipeEndpoint.CreateForCurrentUser();
        _frontendServer = new NamedPipeAddonFrontendServer(pipeName, _frontendControl);
        var qamPipeName = FrontendPipeEndpoint.CreateQamForCurrentUser();

        // PR6 section 16: run the whole Disabled-mode controller startup sequence -- VIIPER init ->
        // PR5 physical acquire -> first presentation attach -- BEFORE the frontend transport accepts
        // external requests, so a user cannot request Enable-and-Restart mid-commit. Failure still
        // continues to frontend startup so the repair/Enable path stays available. Bounded by the
        // existing native/PnP/DirectInput operations; other Center M states add no delay.
        await TryStartDisabledModeControllerAsync(startupComposition, startupResult).ConfigureAwait(false);

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
        _overlayStartup = StartOverlayWarmupAsync();

        _startupComposition = null;
    }

    /// <summary>PR5/PR6: the whole Disabled-mode controller startup sequence. Runs only for an exact
    /// Center M Disabled boot. Order (work order PR6 section 5.1): construct PR5 physical owner ->
    /// init canonical VIIPER -> require VIIPER Ready -> PR5 AcquireAsync -> require Owned + live input
    /// -> fresh Steam/BPM snapshot -> attach exactly one X360/SteamDeck presentation. Any failure
    /// keeps the mandatory Runtime/tray/frontend alive; PID1902 is never rolled back here.</summary>
    private async Task TryStartDisabledModeControllerAsync(AddonStartupComposition startupComposition, StartupResult startupResult)
    {
        if (startupResult.CenterMStartupState != FrontendCenterMStartupState.Disabled)
            return;

        // Construct the narrow PR5 owner on ANY exact Disabled boot -- even a Blocked one -- so
        // Enable-and-Restart can always release existing PID1902 / persisted PR5 HidHide ownership.
        var owner = CreatePhysicalOwnership(startupComposition);
        if (owner is null)
            return;
        _physicalOwnership = owner;

        if (startupResult.DisabledBootAdmission?.IsReady != true)
        {
            AppLog.Info("ControllerOwnership", "Physical acquisition not started; Disabled-boot admission is not Ready. Release seam stays available.",
                ("Admission", startupResult.DisabledBootAdmission?.Outcome.ToString() ?? "None"));
            return;
        }

        try
        {
            // PR6 section 5: canonical VIIPER must be positively Ready before any new PID takeover.
            var viiper = LoadAndInitializeCanonicalViiper();
            var presentation = new Devices.MSI.Claw.MsiClawAddonPresentation(viiper);
            _presentationOwnership = presentation;
            AppLog.Info("ControllerPresentation", "Canonical VIIPER runtime initialized.", ("Event", "ViiperRuntimeInitialized"),
                ("State", presentation.ViiperState?.ToString() ?? "Unavailable"));
            if (presentation.ViiperState != VirtualOutput.Viiper.CanonicalViiperRuntimeState.Ready)
            {
                AppLog.Warn("ControllerPresentation", "Canonical VIIPER is not Ready; no new physical takeover this boot.", null,
                    ("State", presentation.ViiperState?.ToString() ?? "Unavailable"));
                return;
            }

            var acquired = await owner.AcquireAsync(_startupCancellationTokenSource.Token).ConfigureAwait(false);
            AppLog.Info("ControllerOwnership", "Physical ownership acquisition completed.",
                ("Result", acquired.Outcome), ("Reason", acquired.Reason), ("ModeWriteIssued", acquired.ModeWriteIssued), ("HiddenTarget", acquired.HiddenTarget ?? "None"));
            if (!acquired.IsOwned)
            {
                await presentation.ReleaseForCenterMEnableAsync(_startupCancellationTokenSource.Token).ConfigureAwait(false);
                return;
            }

            var source = owner.LiveInputSource;
            if (source is null || !source.IsRunning)
            {
                AppLog.Warn("ControllerPresentation", "PR5 live input source is not running; no presentation attach.", null);
                await presentation.ReleaseForCenterMEnableAsync(_startupCancellationTokenSource.Token).ConfigureAwait(false);
                return;
            }

            // PR10: the owner has committed a real strong identity + exact hidden target, so a later
            // physical disappearance can be recovered on a real Device Arrival even after the bounded
            // PR8/PR9 attempt has already failed closed.
            StartControllerDeviceArrivalWatcher();

            var snapshot = _runtimeHost!.CapturePresentationSnapshot();
            var presentationResult = await presentation.AttachInitialAsync(source, snapshot, _startupCancellationTokenSource.Token).ConfigureAwait(false);
            AppLog.Info("ControllerPresentation", "First presentation attach completed.",
                ("Succeeded", presentationResult.Succeeded), ("Presentation", presentationResult.Presentation?.ToString() ?? "None"), ("Reason", presentationResult.Reason));
        }
        catch (OperationCanceledException) when (_startupCancellationTokenSource.IsCancellationRequested) { }
        catch (Exception exception)
        {
            AppLog.Error("ControllerOwnership", "Disabled-mode controller startup threw; Runtime remains available.", exception);
        }
    }

    private VirtualOutput.Viiper.CanonicalViiperRuntime? LoadAndInitializeCanonicalViiper()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Dependencies", "Viiper", "libVIIPER.dll");
        var native = Routing.AddonRoutingRuntime.TryLoadViiper(path);
        return native is null ? null : VirtualOutput.Viiper.CanonicalViiperRuntime.TryInitialize(native, "127.0.0.1:3242");
    }

    private Devices.MSI.Claw.IMsiClawAddonPhysicalOwnership? CreatePhysicalOwnership(AddonStartupComposition startupComposition)
    {
        if (startupComposition.HandheldDeviceAdapter.NativeState is not Devices.MSI.Claw.MsiClawNativeStateManager nativeState)
        {
            AppLog.Warn("ControllerOwnership", "Physical ownership unavailable; MSI Claw native-state manager is unavailable.", null);
            return null;
        }

        var controllerDevices = new Controllers.Detection.WindowsControllerDeviceEnumerator();
        var directInputInputSource = new Devices.MSI.Claw.MsiClawInputSource(() => new Input.DirectInput.VorticeDirectInputDeviceEnumerator(IntPtr.Zero));
        // PR8 section 7: the one Full-1902 owned-input completion signal. MsiClawInputSource already
        // neutralizes LatestState and cleans up the dead session before raising this, so the callback
        // only decides whether an unexpected owned-session loss should request recovery.
        directInputInputSource.TestCompleted += OnOwnedControllerPhysicalInputCompleted;
        var hidHideBaseline = new SteamInputAddonforClaw.HidHide.AddonControllerHidHideBaseline(
            new SteamInputAddonforClaw.HidHide.HidHideDriverClient(),
            Environment.ProcessPath ?? throw new InvalidOperationException("The current executable path is unavailable."));

        return new Devices.MSI.Claw.MsiClawAddonPhysicalOwnership(
            () => _centerMStartupControl!.Capture().State,
            token => nativeState.CaptureStableCurrentSnapshotAsync(token, allowTransientDeviceNotFound: true),
            (target, identity, token) => nativeState.SwitchModeAsync(target, identity, token),
            () =>
            {
                using var enumerator = new Input.DirectInput.VorticeDirectInputDeviceEnumerator(IntPtr.Zero);
                return enumerator.EnumerateGameControllers();
            },
            instanceId => controllerDevices.EnumeratePresentDevices().FirstOrDefault(device =>
                string.Equals(device.InstanceId, instanceId, StringComparison.OrdinalIgnoreCase)),
            directInputInputSource,
            target => hidHideBaseline.ApplyDisabledModeBaseline([target]),
            () => hidHideBaseline.TryGetSingleExistingOwnedTarget(
                Devices.MSI.Claw.MsiClawHardware.IsPrimaryDirectInputHidCollectionInstanceId));
    }

    /// <summary>PR8 section 7: decide whether an owned DirectInput session completion is an unexpected
    /// loss that should request recovery. Runs on the input polling worker -- it must only classify and
    /// schedule, never do native/PnP/DirectInput work synchronously.</summary>
    private void OnOwnedControllerPhysicalInputCompleted(object? sender, Devices.MSI.Claw.MsiClawInputTestSummary summary)
    {
        // 7.1: a normal explicit Stop/Dispose/Enable teardown is expected -- never a recovery.
        if (summary.StopReason == Devices.MSI.Claw.MsiClawInputStopReason.Stopped) return;
        // 15: no new recovery may be scheduled once controlled shutdown has begun.
        if (Volatile.Read(ref _processShutdownStarted) != 0) return;

        var physical = _physicalOwnership;
        if (physical is null) return;
        // 7.2: PR5 has not committed the owned live source yet (or it was already released) -- the
        // startup acquisition itself reports failure; do not stack a runtime recovery behind it.
        var source = physical.LiveInputSource;
        if (source is null) return;
        // The source recovered/stayed healthy between finalization and this callback.
        if (source.IsRunning) return;

        AppLog.Warn("ControllerOwnership", "Owned physical DirectInput session terminated unexpectedly.", null,
            ("Event", "OwnedPhysicalInputLost"), ("StopReason", summary.StopReason),
            ("ReadFailures", summary.ReadFailures), ("CleanupSucceeded", summary.CleanupSucceeded));

        // 7.4: the dead DirectInput device/enumerator cleanup is not proven -- do not acquire another
        // session on top of possibly-retained native resources. Fail closed; publisher stays neutral.
        // This is a native-resource safety boundary for the REST of this Runtime lifetime, not just
        // this one completion -- a later Device Arrival must not be allowed to bypass it (review [P1]).
        if (!summary.CleanupSucceeded)
        {
            Interlocked.Exchange(ref _ownedControllerRecoveryBlockedByCleanup, 1);
            AppLog.Warn("ControllerOwnership", "Owned physical input recovery blocked; dead DirectInput session cleanup is unproven.", null,
                ("Event", "OwnedPhysicalRecoveryBlocked"), ("Reason", "CleanupUnproven"));
            return;
        }

        RequestOwnedControllerRecovery(physical, "UnexpectedDirectInputCompletion");
    }

    private void StartControllerDeviceArrivalWatcher()
    {
        if (_deviceArrivalWatcher is not null) return;
        var watcher = new Controllers.Detection.WindowsDeviceArrivalWatcher();
        watcher.DeviceArrived += OnControllerDeviceArrived;
        _deviceArrivalWatcher = watcher;
        watcher.Start(); // logs DeviceArrivalWatcherStarted / DeviceArrivalWatcherUnavailable; no polling fallback
    }

    /// <summary>PR10 section 7: a Windows Device Arrival is only a wake-up. Do almost no work here --
    /// the physical owner re-proves the strong MSI Claw identity, exact target, and Center M authority
    /// itself, so an unrelated USB/BT/network arrival can never gain controller authority.</summary>
    private void OnControllerDeviceArrived()
    {
        if (Volatile.Read(ref _processShutdownStarted) != 0) return;
        // 7.4 continued: an unproven prior DirectInput cleanup blocks recovery for the rest of this
        // Runtime lifetime -- a Device Arrival is only a trigger and must never bypass that rule.
        if (Volatile.Read(ref _ownedControllerRecoveryBlockedByCleanup) != 0)
        {
            AppLog.Warn("ControllerOwnership", "Device-arrival recovery ignored because prior DirectInput cleanup is unproven.", null,
                ("Event", "OwnedPhysicalRecoveryBlocked"), ("Reason", "CleanupUnproven"));
            return;
        }
        var physical = _physicalOwnership;
        if (physical is null) return;
        // A live owned session is healthy -- an unrelated arrival must not cause native/HidHide/DI work.
        if (physical.LiveInputSource is { IsRunning: true }) return;

        AppLog.Info("ControllerOwnership", "Controller device arrival observed.", ("Event", "ControllerDeviceArrivalObserved"));
        RequestOwnedControllerRecovery(physical, "DeviceArrival");
    }

    /// <summary>The one owned-controller recovery scheduling seam, shared by the unexpected
    /// DirectInput completion (PR8) and the PR10 Device Arrival trigger. The physical owner's own
    /// gate remains the serialization authority.</summary>
    private void RequestOwnedControllerRecovery(Devices.MSI.Claw.IMsiClawAddonPhysicalOwnership physical, string trigger)
    {
        if (Volatile.Read(ref _processShutdownStarted) != 0) return;

        // Coalesce concurrent triggers to one in-flight attempt. A real Device Arrival that lands
        // while an attempt is still inside its bounded settle window must NOT be dropped (PR10
        // section 8.2): retain a single pending-arrival bit and consume it for exactly one follow-up
        // once the current attempt finishes, if the source is still down. No epoch/manager.
        if (!_ownedControllerRecovery.IsCompleted)
        {
            if (trigger == "DeviceArrival")
                Interlocked.Exchange(ref _pendingOwnedControllerArrival, 1);
            return;
        }

        AppLog.Info("ControllerOwnership", "Owned physical input recovery requested.",
            ("Event", "OwnedPhysicalRecoveryRequested"), ("Trigger", trigger));
        _ownedControllerRecovery = RecoverOwnedControllerPhysicalInputAsync(physical, trigger, _startupCancellationTokenSource.Token);
    }

    private async Task RecoverOwnedControllerPhysicalInputAsync(
        Devices.MSI.Claw.IMsiClawAddonPhysicalOwnership physical,
        string trigger,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await physical.RecoverLostInputAsync(cancellationToken).ConfigureAwait(false);
            AppLog.Info("ControllerOwnership", "Owned physical input recovery completed.",
                ("Trigger", trigger), ("Result", result.Outcome), ("Reason", result.Reason), ("HiddenTarget", result.HiddenTarget ?? "None"));
            // 11: raw Steam/BPM state may have changed while input was down and PR7 correctly refused
            // forward mutation on a non-running source. Re-run the existing reconcile exactly once.
            if (result.IsOwned && result.Reason != "RecoveryNotNeeded")
                RequestControllerPresentationReconcile("PhysicalInputRecovered");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception exception)
        {
            AppLog.Error("ControllerOwnership", "Owned physical input recovery threw; Runtime remains available.", exception,
                ("Trigger", trigger));
        }
        finally
        {
            // A Device Arrival was observed while this attempt was in flight -- run exactly one
            // follow-up if the owned source is still not running and cleanup was never found unproven
            // in the meantime (review [P1]: the cleanup gate must not be bypassable via a queued arrival).
            if (Volatile.Read(ref _processShutdownStarted) == 0
                && Volatile.Read(ref _ownedControllerRecoveryBlockedByCleanup) == 0
                && Interlocked.Exchange(ref _pendingOwnedControllerArrival, 0) != 0
                && physical.LiveInputSource is not { IsRunning: true })
            {
                AppLog.Info("ControllerOwnership", "Owned physical input recovery requested.",
                    ("Event", "OwnedPhysicalRecoveryRequested"), ("Trigger", "DeferredDeviceArrival"));
                _ownedControllerRecovery = RecoverOwnedControllerPhysicalInputAsync(physical, "DeferredDeviceArrival", _startupCancellationTokenSource.Token);
            }
        }
    }

    private async Task StartOverlayWarmupAsync()
    {
        try
        {
            await _overlayController.StartAsync(_startupCancellationTokenSource.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (_startupCancellationTokenSource.IsCancellationRequested) { }
        catch (Exception exception)
        {
            AppLog.Warn("Overlay", "Overlay POC startup failed; continuing without Overlay.", exception);
        }
    }

    // OQ3-A: Main UI and Overlay are mutually exclusive visible surfaces. A Main UI open request
    // first hides/retires the Overlay; an Overlay Show request first asks the Main UI to run its
    // normal close path and waits for the .Frontend connection to disconnect.
    internal void RequestFrontendOpen(FrontendOpenReason reason) => _ = CoordinateFrontendOpenAsync(reason);

    internal void ToggleOverlayForPoc() => _ = CoordinateOverlayToggleAsync();

    private async Task CoordinateFrontendOpenAsync(FrontendOpenReason reason)
    {
        await _visibleSurfaceTransition.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_overlayController.IsVisible)
            {
                AppLog.Info("UiSurface", "Overlay Hide requested before Main UI open.", ("Reason", reason));
                await _overlayController.EnsureHiddenAsync().ConfigureAwait(false);
                if (_overlayController.IsVisible)
                    AppLog.Warn("UiSurface", "Main UI open proceeding though the Overlay still reports visible.", null, ("Reason", reason));
                else
                    AppLog.Info("UiSurface", "Overlay retired before Main UI open.", ("Reason", reason));
            }
        }
        catch (Exception exception)
        {
            AppLog.Warn("UiSurface", "Overlay retirement before Main UI open failed; requesting open anyway.", exception, ("Reason", reason));
        }
        finally { _visibleSurfaceTransition.Release(); }

        _frontendLauncher.RequestOpen(reason);
    }

    private async Task CoordinateOverlayToggleAsync()
    {
        await _visibleSurfaceTransition.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_overlayController.IsVisible)
            {
                await _overlayController.EnsureHiddenAsync().ConfigureAwait(false);
                return;
            }

            var server = _frontendServer;
            if (server is not null)
            {
                AppLog.Info("UiSurface", "Main UI close requested before Overlay Show.");
                bool retired;
                try
                {
                    retired = await server.RequestClientCloseAsync(MainUiCloseTimeout, CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception exception)
                {
                    AppLog.Warn("UiSurface", "Overlay Show blocked because the Main UI close request failed.", exception);
                    return;
                }
                if (!retired)
                {
                    AppLog.Warn("UiSurface", "Overlay Show blocked because Main UI did not retire.", null);
                    return;
                }
                AppLog.Info("UiSurface", "Main UI retired before Overlay Show.");
            }

            await _overlayController.ShowAsync().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            AppLog.Warn("UiSurface", "Overlay visible-surface coordination failed.", exception);
        }
        finally { _visibleSurfaceTransition.Release(); }
    }

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
        UserTerminationComposition.Compose(
            _runtimeHost?.EvaluateUserTermination() ?? new(true, UserTerminationBlockReason.None),
            IsControllerRuntimeMandatory());

    /// <summary>The mandatory-controller-authority fact for a user action, read fresh from the shared
    /// Center M startup reader (never cached from process startup). A read that cannot prove an
    /// exactly-Disabled configuration is not treated as Addon-owned authority (PR2.5 section 14).</summary>
    private bool IsControllerRuntimeMandatory()
    {
        try
        {
            return MandatoryControllerRuntimePolicy.IsMandatory(
                _centerMStartupControl?.Capture().State ?? FrontendCenterMStartupState.Unavailable);
        }
        catch (Exception exception)
        {
            AppLog.Warn("Lifecycle", "MSI Center M startup state read for the mandatory Runtime policy failed; not classifying mandatory.", exception);
            return false;
        }
    }

    /// <summary>A fresh check (evaluated at transition-request time, not startup) that no other
    /// controller manager may coexist with Addon controller authority (work order PR3 section
    /// 6.2/13). This is the one-shot ENTRY admission for the Disable path, so it fails closed:
    /// entering exclusive Addon authority is allowed only when the existing detector positively
    /// proves <see cref="Status.ControllerManagerKind.None"/>. An unresolved (<c>Indeterminate</c>)
    /// read or a throwing assessment blocks and lets the user retry. Reuses the existing environment
    /// detector; adds no new scanner. The Enable-and-Restart release path never consults this.</summary>
    internal static bool IsConflictingControllerEnvironment(Status.IControllerEnvironmentAssessmentProvider provider)
    {
        try
        {
            return provider.Capture().Manager.Kind != Status.ControllerManagerKind.None;
        }
        catch (Exception exception)
        {
            AppLog.Warn("CenterM.Authority", "Controller-environment admission could not be verified; blocking Addon authority entry.", exception);
            return true;
        }
    }

    internal bool TryInitializeTray(Action restart, Action exit)
    {
        try
        {
            _trayHostWindow = new NativeTrayHostWindow();
            _systemTrayIcon = new SystemTrayIcon(_trayHostWindow.Handle, () => RequestFrontendOpen(FrontendOpenReason.Tray), restart, exit, EvaluateUserTermination, ToggleOverlayForPoc);
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

    /// <summary>PR12 section 17: the one narrow Runtime-owned operation a future safe-uninstall entry
    /// requests BEFORE any file removal. Leaves the machine verified stock-safe (MSI authority
    /// restored + mandatory Addon startup task removed) or fails closed. Issues no Windows restart.</summary>
    internal async Task<SteamInputAddonforClaw.CenterMStartup.StockUninstallPrepareResult> PrepareForUninstallAsync()
    {
        if (_centerMAuthorityTransition is not { } transition)
            return SteamInputAddonforClaw.CenterMStartup.StockUninstallPrepareResult.Fail("AuthorityTransitionUnavailable");
        try
        {
            return await transition.PrepareForUninstallAsync(_startupCancellationTokenSource.Token).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            AppLog.Error("Uninstall", "Stock uninstall preparation threw; the Runtime remains stock-unsafe.", exception);
            return SteamInputAddonforClaw.CenterMStartup.StockUninstallPrepareResult.Fail("PrepareThrew:" + exception.GetType().Name);
        }
    }

    internal void BeginProcessShutdown()
    {
        if (Interlocked.Exchange(ref _processShutdownStarted, 1) != 0) return;
        _frontendLauncher.StopAcceptingRequests();
        _overlayController.BeginShutdown();
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
        // PR10 section 15: stop the Device Arrival watcher before recovery drains -- no WMI callback
        // may reach OnControllerDeviceArrived after this, and _processShutdownStarted is already set
        // so no new arrival-triggered recovery can be scheduled.
        _deviceArrivalWatcher?.Dispose();
        _deviceArrivalWatcher = null;
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
        if (_overlayStartup is not null)
        {
            try { await _overlayStartup.ConfigureAwait(false); }
            catch (Exception exception) { AppLog.Warn("Overlay", "Overlay warm-up task failed during shutdown.", exception); }
            _overlayStartup = null;
        }
        // PR8 section 15: drain the in-flight owned-DirectInput recovery BEFORE the presentation
        // reconcile, because a successful recovery requests a "PhysicalInputRecovered" reconcile as
        // its final action. BeginProcessShutdown already blocks any new recovery from being scheduled.
        try { await _ownedControllerRecovery.ConfigureAwait(false); }
        catch (Exception exception) { AppLog.Warn("ControllerOwnership", "Owned physical input recovery failed during shutdown.", exception); }
        // PR7 section 19.1: no new reconcile can be scheduled after BeginProcessShutdown cancelled
        // the token; drain the last in-flight one before tearing down the presentation owner.
        try { await _presentationReconcile.ConfigureAwait(false); }
        catch (Exception exception) { AppLog.Warn("ControllerPresentation", "Runtime presentation reconcile failed during shutdown.", exception); }
        if (_presentationOwnership is not null)
        {
            // PR6 section 18: retire the virtual presentation + tear down canonical VIIPER BEFORE the
            // physical DirectInput handle. PID1902 / HidHide are durable and untouched.
            try { await _presentationOwnership.DisposeAsync().ConfigureAwait(false); }
            catch (Exception exception) { AppLog.Warn("ControllerPresentation", "Presentation teardown failed during shutdown.", exception); }
            _presentationOwnership = null;
        }
        if (_physicalOwnership is not null)
        {
            // PR5 section 17: release the process-owned DirectInput session only. PID1902 and the
            // persistent HidHide target are durable state and are deliberately left intact.
            try { await _physicalOwnership.DisposeAsync().ConfigureAwait(false); }
            catch (Exception exception) { AppLog.Warn("ControllerOwnership", "Physical ownership teardown failed during shutdown.", exception); }
            _physicalOwnership = null;
        }
        await _overlayController.DisposeAsync().ConfigureAwait(false);
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
        // OQ3-A: disposed last, after the frontend server and Overlay controller are gone, so any
        // in-flight visible-surface coordination has already unwound and released the gate.
        try { _visibleSurfaceTransition.Dispose(); } catch (ObjectDisposedException) { }
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
        {
            _runtimeHost.ActualRunningAppIdChanged -= OnActualRunningAppIdChanged;
            _runtimeHost.PowerResumeObserved -= OnPowerResumeObserved;
        }
        if (_frontendControl is SteamInputAddonforClaw.Frontend.InProcessAddonFrontendControl control)
            control.BeginProcessShutdown();
        try { _displayResolutionRuntime.Shutdown(); } catch (Exception exception) { AppLog.Error("Profiles.Display", "Display resolution shutdown restore failed.", exception); }
        _runtimeHost?.PrepareForShutdown();
    }

    private void OnActualRunningAppIdChanged(uint appId)
    {
        _qamHostController.OnActualRunningAppIdChanged(appId);

        // PR7 section 8: request the Full-1902 X360 <-> SteamDeck reconcile up front, so it does not
        // wait behind the unrelated CPU Boost / Power Mode / Resolution / TDP / FPS profile work
        // below. The switch itself runs asynchronously, serialized by the presentation owner's gate.
        RequestControllerPresentationReconcile("RunningAppIdChanged");

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

    private void OnBigPictureStateChanged(bool active)
    {
        _qamHostController.OnBigPictureStateChanged(active);
        RequestControllerPresentationReconcile("BigPictureChanged");
    }

    /// <summary>PR7: schedule one asynchronous Full-1902 presentation reconcile. Event-driven only --
    /// no timer, no polling. The desired X360/SteamDeck kind is captured fresh AFTER the presentation
    /// owner's gate is acquired, so overlapping RunningAppID/BPM events never apply stale state.</summary>
    private void RequestControllerPresentationReconcile(string trigger)
    {
        if (Volatile.Read(ref _processShutdownStarted) != 0) return;
        var presentation = _presentationOwnership;
        var physical = _physicalOwnership;
        if (presentation is null || physical is null) return;

        AppLog.Debug("ControllerPresentation", "Runtime presentation reconcile requested.", ("Event", "PresentationReconcileRequested"), ("Trigger", trigger));
        _presentationReconcile = ReconcileControllerPresentationAsync(presentation, physical, trigger, _startupCancellationTokenSource.Token);
    }

    private async Task ReconcileControllerPresentationAsync(
        Devices.MSI.Claw.IMsiClawAddonPresentation presentation,
        Devices.MSI.Claw.IMsiClawAddonPhysicalOwnership physical,
        string trigger,
        CancellationToken cancellationToken)
    {
        try
        {
            var source = physical.LiveInputSource;
            if (source is null)
            {
                AppLog.Info("ControllerPresentation", "Runtime presentation reconcile skipped; no live PR5 input source.", ("Trigger", trigger));
                return;
            }
            var result = await presentation.ReconcileDesiredPresentationAsync(
                source,
                () => _runtimeHost!.CapturePresentationSnapshot(),
                cancellationToken).ConfigureAwait(false);
            AppLog.Info("ControllerPresentation", "Runtime presentation reconcile completed.",
                ("Trigger", trigger), ("Outcome", result.Outcome), ("Presentation", result.Presentation?.ToString() ?? "None"), ("Reason", result.Reason));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception exception)
        {
            AppLog.Error("ControllerPresentation", "Runtime presentation reconcile threw; Runtime remains available.", exception, ("Trigger", trigger));
        }
    }

    private void OnPowerResumeObserved()
    {
        if (Volatile.Read(ref _processShutdownStarted) != 0) return;
        _ = ReconcilePerformanceAfterResumeAsync(
            _startupCancellationTokenSource.Token,
            static (delay, cancellationToken) => Task.Delay(delay, cancellationToken),
            () => _runtimeHost?.ActualRunningAppId ?? 0,
            appId => _cpuBoostRuntime.Reconcile(appId),
            appId => _powerModeRuntime.Reconcile(appId));
    }

    internal static async Task ReconcilePerformanceAfterResumeAsync(
        CancellationToken cancellationToken,
        Func<TimeSpan, CancellationToken, Task> delay,
        Func<uint> actualAppIdSource,
        Action<uint> reconcileCpuBoost,
        Action<uint> reconcilePowerMode)
    {
        try
        {
            await delay(TimeSpan.FromMilliseconds(2500), cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return;
        }

        var appId = actualAppIdSource();
        try
        {
            reconcileCpuBoost(appId);
        }
        catch (Exception exception)
        {
            AppLog.Error("Profiles.CpuBoost", "CPU Boost resume reconcile failed.", exception);
        }

        try
        {
            reconcilePowerMode(appId);
        }
        catch (Exception exception)
        {
            AppLog.Error("Profiles.PowerMode", "Power Mode resume reconcile failed.", exception);
        }
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
