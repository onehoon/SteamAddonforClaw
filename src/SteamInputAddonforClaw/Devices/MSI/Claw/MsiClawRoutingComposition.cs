using SteamInputAddonforClaw.CenterM;
using SteamInputAddonforClaw.Contracts.Oem1;
using SteamInputAddonforClaw.Devices.Abstractions;
using SteamInputAddonforClaw.Diagnostics;
using SteamInputAddonforClaw.HidHide;
using SteamInputAddonforClaw.Input;
using SteamInputAddonforClaw.Input.DirectInput;
using SteamInputAddonforClaw.Power;
using SteamInputAddonforClaw.Recovery;
using SteamInputAddonforClaw.Routing;
using SteamInputAddonforClaw.Feedback;
using SteamInputAddonforClaw.Wing;

namespace SteamInputAddonforClaw.Devices.MSI.Claw;

/// <summary>
/// Composes and owns the MSI Claw-specific routing input stages (native-mode session,
/// native-mode stage, physical input source/stage, physical isolation stage) that previously
/// were constructed directly in <c>App.xaml.cs</c>.
/// </summary>
/// <remarks>
/// Also implements <see cref="IHandheldRoutingComposition"/>, the generic view the routing/output
/// layer consumes -- a thin projection over the same already-created objects below, not a
/// second composition. The concrete properties remain for focused MSI implementation/tests;
/// removing any no-longer-needed concrete surface is deferred to later cleanup.
///
/// <para>
/// This class owns <see cref="NativeModeSession"/> and <see cref="PhysicalInputSource"/> --
/// <see cref="DisposeAsync"/> disposes them, in that order, matching the disposal order the
/// caller previously performed itself. <see cref="NativeModeStage"/>,
/// <see cref="PhysicalInputStage"/>, and <see cref="PhysicalIsolationStage"/> hold no resources
/// of their own beyond references to <see cref="NativeModeSession"/>/<see cref="PhysicalInputSource"/>,
/// so they are not separately disposed.
/// </para>
/// </remarks>
internal sealed class MsiClawRoutingComposition : IHandheldRoutingComposition
{
    internal MsiClawNativeModeSessionCoordinator NativeModeSession { get; }
    internal MsiClawNativeModeStage NativeModeStage { get; }
    internal MsiClawInputSource PhysicalInputSource { get; }
    internal MsiClawPhysicalInputStage PhysicalInputStage { get; }
    internal MsiClawPhysicalIsolationStage PhysicalIsolationStage { get; }
    internal MsiClawRumbleSink PhysicalRumbleSink { get; }

    /// <summary>The single authoritative <see cref="CenterM.CenterMHelperOwnership"/> for this MSI
    /// Claw runtime composition (PR1 ownership convergence). Created here (unless caller-injected,
    /// e.g. by tests, or by a future OEM1 production composition seam) and passed to
    /// <see cref="CenterMGuard"/> via constructor injection so routing never constructs a second,
    /// competing production ownership object for the same same-name helper identity. Disposal
    /// ownership follows creation: this composition disposes it only when it created it itself
    /// (see <see cref="_ownsCenterMHelperOwnership"/>) -- a caller-injected instance is the
    /// caller's terminal responsibility, so two owners never race to dispose the same object.</summary>
    internal CenterMHelperOwnership CenterMHelperOwnership { get; }
    internal CenterMMainUiRoutingGuard CenterMGuard { get; }
    internal CenterMMainUiRoutingGuardStage CenterMGuardStage { get; }

    /// <summary>PR2: production-composed OEM1/Center M lifecycle coordinator, sharing the SAME
    /// <see cref="CenterMHelperOwnership"/> as <see cref="CenterMGuard"/> -- never a second,
    /// competing production helper owner. As of the OEM1 production action path
    /// (<see cref="ConfigureOem1ActionPath"/>), production DOES call
    /// <see cref="CenterMOem1LifecycleCoordinator.SetDesiredEnabledAsync"/> with true -- suppression
    /// is armed once WMI observation actually starts, and disarmed again on action-failure fail-open
    /// or shutdown.</summary>
    internal CenterMOem1LifecycleCoordinator CenterMOem1Coordinator { get; }

    /// <summary>PR2: the one small production driver/lifetime owner around
    /// <see cref="CenterMOem1Coordinator"/> -- drives its documented low-rate poll contract, and
    /// participates in suspend/resume/shutdown. See <see cref="CenterMOem1LifecycleRuntime"/>.</summary>
    internal CenterMOem1LifecycleRuntime CenterMOem1Runtime { get; }

    /// <summary>True only when this composition itself constructed <see cref="CenterMHelperOwnership"/>
    /// (no caller-injected instance was supplied) -- the sole condition under which
    /// <see cref="DisposeAsync"/> disposes it. A caller-injected instance (e.g. a future OEM1
    /// composition seam sharing one instance across both consumers) is never disposed here; the
    /// caller that created it remains its one terminal owner.</summary>
    private readonly bool _ownsCenterMHelperOwnership;

    private readonly IReadOnlyList<IRoutingPipelineStage> _stages;
    private readonly IReadOnlyList<IRoutingRuntimeSessionBoundaryParticipant> _sessionBoundaryParticipants;
    private readonly bool _startCenterMOem1LifecycleRuntime;

    /// <summary>The startup hardware-support result (<see cref="Startup.StartupResult.HardwareSupported"/>),
    /// forwarded by <see cref="HandheldRoutingCompositionFactory"/>. It is a HARDWARE gate only --
    /// never a routing/Steam/BPM condition -- and its single effect is that
    /// <see cref="ConfigureOem1ActionPath"/> wires nothing at all on unrecognized hardware: no Event41
    /// WMI observation, no <see cref="CenterMOem1Coordinator"/> enable, no gesture bridge, and no
    /// subscription to the persisted mapping. Persisted OEM1 settings are never read for a decision
    /// nor written here, so a machine with <c>RemappingEnabled = true</c> saved keeps it saved.
    /// Defaults to true because the only production construction site (the factory above) always
    /// passes it explicitly; direct test constructions opt out by passing false.</summary>
    private readonly bool _hardwareSupported;

    private Func<string, ValueTask>? _runtimeFaultHandler;

    // PR3: development-only OEM1 production E2E POC action path -- Event41 observation, gesture
    // recognition/bridge, and dispatch. Never constructed until ConfigureOem1ActionPath is called
    // (only the real production composition seam, AddonRoutingRuntime.Create, calls it), so every
    // existing test that constructs this composition directly and never calls it is unaffected.
    private IMsiEventSource? _oem1EventSource;
    private Oem1GestureRecognizer? _oem1GestureRecognizer;
    private Oem1EventGestureBridge? _oem1Bridge;
    private bool _oem1ActionPathConfigured;
    private readonly IMsiEventSource? _testOnlyOem1EventSource;
    private readonly IMsiEventSource? _testOnlyWingEventSource;
    private WingEventGestureBridge? _wingBridge;
    private WingGestureRecognizer? _wingRecognizer;
    private IMsiEventSource? _wingEventSource;
    // Review fix (MAJOR): lets a test drive Oem1GestureRecognizer's single/double-click debounce
    // deterministically (no real-time sleeps) and observe the normal-mapping replacement action
    // without spawning a real process -- never supplied by production composition.
    private readonly IOem1GestureDelay? _testOnlyOem1GestureDelay;
    private readonly IOem1GestureClock? _testOnlyOem1GestureClock;
    private readonly Action? _testOnlyOem1LaunchBigPicture;

    // Review fix (BLOCKER): explicit ownership/join boundary for the two background operations the
    // OEM1 action path can start (startup activation, action-failure fail-open disable) -- both were
    // previously untracked/detached Task.Run work that DisposeAsync had no way to wait for before
    // tearing down the coordinator they call into. _oem1Stopping (set under the same lock) prevents a
    // new fail-open task from being scheduled once teardown has begun.
    //
    // Review fix (BLOCKER): _oem1FailOpenTask is a one-way lifetime latch (null = never failed yet;
    // non-null = a replacement-action failure has occurred and custom OEM1 admission stays fail-open
    // for the rest of this composition's lifetime -- never re-armed). This lets RefreshOem1BridgeAuthority
    // decide "may custom authority activate" with a plain null-check evaluated from INSIDE the bridge's
    // own lock (see Oem1EventGestureBridge.SetCustomAuthority's allowActivation parameter), instead of
    // this composition taking _oem1TaskSync BEFORE calling into the bridge -- which used to invert lock
    // order against OnGestureRecognized's bridge-lock-then-PolicyRequested-callback path and could
    // deadlock a real action failure against a concurrent lifecycle refresh/disposal.
    private readonly object _oem1TaskSync = new();
    private readonly CancellationTokenSource _oem1LifetimeCancellation = new();
    private static readonly TimeSpan Oem1ShutdownJoinTimeout = TimeSpan.FromSeconds(5);
    private Task _oem1ActivationTask = Task.CompletedTask;
    private Task? _oem1FailOpenTask;
    private bool _oem1Stopping;

    /// <summary>The persisted OEM1 mapping source of truth. Read fresh on every gesture (never
    /// cached into the dispatcher) and watched so the global remapping switch can drive the existing
    /// suppression lifecycle. Null until <see cref="ConfigureOem1ActionPath"/> runs.</summary>
    private Settings.IOem1MappingPreference? _oem1MappingPreference;

    /// <summary>Last remapping-switch value actually APPLIED to the lifecycle, not the last value
    /// persisted. Guarded by <see cref="_oem1TaskSync"/>. Its whole purpose is that editing a slot
    /// binding -- by far the common settings change -- raises the same change event but must not
    /// touch suppression ownership at all: only a real On/Off transition schedules lifecycle work.
    /// It is also the extra condition on custom gesture authority, so a revoke takes effect
    /// immediately rather than only once the coordinator's next reconcile observes it.</summary>
    private bool _oem1RemappingEnabled;

    /// <summary>WMI observation is started lazily and exactly once, the first time remapping is
    /// actually enabled -- a user who has the feature switched off must never have Event41
    /// observation started on their behalf, and a later On must still be able to start it. Only ever
    /// touched from inside the serialized <see cref="_oem1ActivationTask"/> chain.</summary>
    private bool _oem1ObservationStarted;

    internal MsiClawRoutingComposition(
        MsiClawNativeStateManager nativeState,
        RecoveryManager recovery,
        PowerMutationGate powerGate,
        RecoverySafetyState recoverySafety,
        bool hardwareSupported = true,
        Func<IMsiClawRumbleEndpointResolver>? rumbleEndpointResolverFactory = null,
        CenterMHelperOwnership? centerMHelperOwnership = null,
        CenterMMainUiRoutingGuard? centerMGuard = null,
        CenterMOem1LifecycleCoordinator? centerMOem1Coordinator = null,
        CenterMOem1LifecycleRuntime? centerMOem1Runtime = null,
        bool startCenterMOem1LifecycleRuntime = true,
        IMsiEventSource? testOnlyOem1EventSource = null,
        IOem1GestureDelay? testOnlyOem1GestureDelay = null,
        IOem1GestureClock? testOnlyOem1GestureClock = null,
        Action? testOnlyOem1LaunchBigPicture = null,
        IMsiEventSource? testOnlyWingEventSource = null)
    {
        _hardwareSupported = hardwareSupported;
        _testOnlyOem1EventSource = testOnlyOem1EventSource;
        _testOnlyWingEventSource = testOnlyWingEventSource;
        _testOnlyOem1GestureDelay = testOnlyOem1GestureDelay;
        _testOnlyOem1GestureClock = testOnlyOem1GestureClock;
        _testOnlyOem1LaunchBigPicture = testOnlyOem1LaunchBigPicture;
        NativeModeSession = new MsiClawNativeModeSessionCoordinator(
            nativeState,
            recovery,
            powerGate,
            recoverySafety);

        NativeModeStage = new MsiClawNativeModeStage(NativeModeSession);

        PhysicalInputSource = new MsiClawInputSource(() => new VorticeDirectInputDeviceEnumerator(IntPtr.Zero));

        PhysicalInputStage = new MsiClawPhysicalInputStage(
            () => new VorticeDirectInputDeviceEnumerator(IntPtr.Zero),
            PhysicalInputSource);

        PhysicalIsolationStage = new MsiClawPhysicalIsolationStage(
            PhysicalInputStage,
            NativeModeSession,
            recovery,
            new HidHideDriverClient(),
            () => Environment.ProcessPath);

        PhysicalRumbleSink = new MsiClawRumbleSink(PhysicalInputStage, new WindowsMsiClawRumbleTransport(),
            rumbleEndpointResolverFactory?.Invoke() ?? new MsiClawRumbleEndpointResolver());
        PhysicalInputStage.PhysicalSessionRetiring += PhysicalRumbleSink.BeginPhysicalSessionRetirement;
        PhysicalInputStage.PhysicalSessionStarted += PhysicalRumbleSink.BeginPhysicalSession;
        PhysicalInputStage.PhysicalSessionRetired += PhysicalRumbleSink.InvalidatePhysicalSession;
        PhysicalInputSource.TestCompleted += OnPhysicalInputCompleted;

        // Constructed here -- not inside CenterMMainUiRoutingGuard -- so this composition, not the
        // guard, is the shared authority a future OEM1 production composition seam can also receive
        // the SAME instance of. Only used as the guard's default when no guard is caller-injected;
        // a caller-injected guard (tests today) already carries its own ownership instance.
        _ownsCenterMHelperOwnership = centerMHelperOwnership is null;
        CenterMHelperOwnership = centerMHelperOwnership ?? new CenterMHelperOwnership();

        // Phase 2: the same already-owned nativeState instance is reused (never a second
        // MsiClawNativeStateManager) as the guard's read-only native-mode probe, so retirement's
        // XInput verification observes the exact same authority the real NativeMode stage uses.
        var centerMProcesses = new Win32ProcessSnapshotSource();
        CenterMOem1LifecycleCoordinator? oem1Coordinator = centerMOem1Coordinator;
        CenterMGuard = centerMGuard ?? new CenterMMainUiRoutingGuard(
            processSnapshotSource: centerMProcesses,
            helperOwnership: CenterMHelperOwnership,
            retirement: new CenterMMainUiRoutingRetirement(
                new MsiClawCenterMNativeModeProbe(nativeState),
                // Same NativeModeSession instance the real stage below uses -- an early read-only
                // look at the same route authority, so an already-doomed route (fault latch,
                // recovery safety, power gate) never retires the user's real MainUI first.
                new MsiClawCenterMRoutingPreflightProbe(NativeModeSession),
                processSnapshotSource: centerMProcesses),
            persistentHelperOwnerReady: () =>
            {
                var snapshot = oem1Coordinator?.GetSnapshot();
                return snapshot is
                {
                    DesiredEnabled: true,
                    State: CenterMOem1LifecycleState.Armed,
                    HelperProcessId: not null
                };
            },
            releaseSharedHelper: cancellationToken => (oem1Coordinator ?? throw new InvalidOperationException("OEM1 coordinator is unavailable.")).ReleaseRoutingHelperAsync(cancellationToken));
        CenterMGuardStage = new CenterMMainUiRoutingGuardStage(CenterMGuard);

        // PR2: production-compose the already-implemented CenterMOem1LifecycleCoordinator into
        // this same shared CenterMHelperOwnership authority (work order requirement 1) -- never a
        // second, independent production helper owner. Review correction: the supported product
        // model is one Windows user / one interactive session (no Fast User Switching/RDP/multi-
        // session), so this reuses the SAME simple centerMProcesses source the routing guard/
        // retirement path above already uses on that model, rather than adding a session-scoping
        // wrapper that would only protect against an explicitly unsupported scenario. This
        // composition only ever exists for an MSI Claw adapter (HandheldRoutingCompositionFactory),
        // so `() => true` here IS the required explicit MSI Claw environment-eligibility predicate
        // (requirement 9) -- distinct from, and never widening, the coordinator's own fail-open
        // default of false for any caller that omits one.
        CenterMOem1Coordinator = oem1Coordinator ??= new CenterMOem1LifecycleCoordinator(
            publishRootProvider: () => AppContext.BaseDirectory,
            processSnapshotSource: centerMProcesses,
            helperOwnership: CenterMHelperOwnership,
            environmentEligibility: () => true,
            externalHelperDemand: () => CenterMGuard.HasHelperDemand,
            sharedHelperSafetyFault: ReportRuntimeFault);
        // PR3: the two narrow callbacks below let the OEM1 action path (wired later, only by
        // ConfigureOem1ActionPath) refresh custom gesture-bridge authority from the coordinator's own
        // freshly reconciled SuppressionReady snapshot after every tick/resume -- see
        // RefreshOem1BridgeAuthority. Suspend revokes authority immediately (work order Scope 14).
        CenterMOem1Runtime = centerMOem1Runtime ?? new CenterMOem1LifecycleRuntime(
            CenterMOem1Coordinator,
            routingGuardIsArmed: () => CenterMGuard.IsArmed,
            onReconciled: RefreshOem1BridgeAuthority,
            onSuspending: () => _oem1Bridge?.SetCustomAuthority(false));

        _startCenterMOem1LifecycleRuntime = startCenterMOem1LifecycleRuntime;

        // Requirement 3/8: the driver is started as part of normal MSI runtime composition (it is
        // part of the real runtime object lifetime now), but `DesiredEnabled` is never set to true
        // here -- normal production startup never stages/starts the helper via this coordinator.
        // Only the real production OEM1 action path (ConfigureOem1ActionPath) ever calls
        // SetDesiredEnabledAsync(true), and only after WMI observation has actually started.
        if (startCenterMOem1LifecycleRuntime)
            CenterMOem1Runtime.Start();

        _stages = [NativeModeStage, PhysicalInputStage, PhysicalIsolationStage, CenterMGuardStage];
        _sessionBoundaryParticipants = [NativeModeSession];
    }

    IReadOnlyList<IRoutingPipelineStage> IHandheldRoutingComposition.Stages => _stages;

    IControllerStateSnapshotSource IHandheldRoutingComposition.ControllerStateSource => PhysicalInputSource;

    IReadOnlyList<IRoutingRuntimeSessionBoundaryParticipant> IHandheldRoutingComposition.SessionBoundaryParticipants => _sessionBoundaryParticipants;

    IRoutingSafetySession? IHandheldRoutingComposition.SafetySession => NativeModeSession;
    IPhysicalRumbleSink? IHandheldRoutingComposition.PhysicalRumbleSink => PhysicalRumbleSink;

    // PR2: the OEM1 lifecycle driver is the composition's only auxiliary power/resume
    // participant -- exposed generically so AddonRuntimeHost never needs to know it is MSI/CenterM
    // specific (work order requirement 13).
    IPowerSuspendParticipant? IHandheldRoutingComposition.AuxiliaryPowerParticipant => CenterMOem1Runtime;
    IRuntimeResumeParticipant? IHandheldRoutingComposition.AuxiliaryResumeParticipant => CenterMOem1Runtime;

    void IHandheldRoutingComposition.SetRuntimeFaultHandler(Func<string, ValueTask> handler) =>
        _runtimeFaultHandler = handler ?? throw new ArgumentNullException(nameof(handler));

    /// <summary>
    /// PR3: development-only OEM1 production E2E POC. Production-composes the already-existing
    /// Event41 chain (<see cref="WmiMsiEventSource"/> -&gt; <see cref="Oem1EventGestureBridge"/> -&gt;
    /// <see cref="Oem1ActionDispatcher"/>) and requests OEM1 lifecycle enable -- independent of any
    /// routing setting (work order's central architectural rule). <paramref name="captureRoutingStatus"/>
    /// and <paramref name="requestQuickAccessPulse"/> are the only two routing/output-layer facts this
    /// composition ever receives; it never learns anything else about Steam Deck output/VIIPER
    /// ownership. Idempotent no-op on a second call.
    ///
    /// Review fix (BLOCKER): the returned task is the SAME task retained as
    /// <see cref="_oem1ActivationTask"/> -- the OEM1 coordinator and the routing guard share the SAME
    /// <see cref="CenterMHelperOwnership"/>, but only <see cref="CenterMHelperOwnership.Start"/> itself
    /// serializes the final creation call between them, so the production startup boundary
    /// (<see cref="Hosting.AddonProcessHost.InitializeRuntimeAsync"/>) must await this before routing's
    /// own initial reconcile can enter and possibly start the shared helper first. No Task.Run: this
    /// composition seam is already synchronous only up to its first await, so returning the async
    /// method's task directly is enough.
    /// </summary>
    Task IHandheldRoutingComposition.ConfigureOem1ActionPath(
        Func<RoutingRuntimeStatusSnapshot> captureRoutingStatus,
        Action requestQuickAccessPulse,
        Settings.IOem1MappingPreference mappingPreference)
    {
        ArgumentNullException.ThrowIfNull(mappingPreference);
        if (_oem1ActionPathConfigured) return Task.CompletedTask;
        _oem1ActionPathConfigured = true;

        // Hardware availability gate: OEM1 mapping only exists on hardware startup already recognized
        // as a supported MSI Claw. Returning here -- before anything is constructed -- is what makes
        // "unsupported hardware never reaches Event41 observation / coordinator enable / suppression
        // arming" structural rather than a condition each step below has to remember. The persisted
        // Oem1MappingSettings is deliberately neither read nor written on this path.
        if (!_hardwareSupported)
        {
            AppLog.Info("CenterM.Oem1", "OEM1 mapping is unavailable on this hardware; the OEM1 action path is not wired.",
                ("Reason", "HardwareNotSupported"), ("Action", "Passive"));
            return Task.CompletedTask;
        }

        var eventSource = _testOnlyOem1EventSource ?? new WmiMsiEventSource();
        var recognizer = new Oem1GestureRecognizer(
            doubleClickEnabled: () =>
            {
                var mapping = mappingPreference.Oem1Mapping;
                var routingActuallyActive = captureRoutingStatus().SteamOutputActive;
                var doubleSlot = Oem1MappingSlots.Resolve(routingActuallyActive, Oem1Gesture.Double);
                var binding = mapping.Resolve(doubleSlot);
                var enabled = Oem1ActionCapabilities.Supports(binding.Action, doubleSlot)
                    && binding.Action != Oem1Action.None;
                AppLog.Debug("CenterM.Oem1", "OEM1 gesture policy",
                    ("Domain", routingActuallyActive ? "Routing" : "Normal"),
                    ("DoubleSlot", doubleSlot), ("DoubleAction", binding.Action),
                    ("DoubleEnabled", enabled), ("DoubleWindowMs", enabled ? 200 : 0));
                return enabled;
            },
            doubleClickWindow: TimeSpan.FromMilliseconds(200),
            delay: _testOnlyOem1GestureDelay, clock: _testOnlyOem1GestureClock);
        var bridge = new Oem1EventGestureBridge(eventSource, recognizer);
        var dispatcher = new Oem1ActionDispatcher(
            // Captured fresh per gesture, exactly like routing status: a mapping the user changed a
            // moment ago must take effect on the very next press with no re-wiring, and neither the
            // dispatcher nor this composition may hold a stale copy of it.
            captureMapping: () => mappingPreference.Oem1Mapping,
            captureRoutingStatus: captureRoutingStatus,
            requestQuickAccessPulse: requestQuickAccessPulse,
            launchBigPicture: _testOnlyOem1LaunchBigPicture ?? Oem1BigPictureLauncher.Launch);

        bridge.PolicyRequested += request =>
        {
            if (!dispatcher.Dispatch(request))
                OnOem1ActionFailed();
        };
        bridge.RecognitionFailed += OnOem1ActionFailed;

        _oem1EventSource = eventSource;
        _oem1GestureRecognizer = recognizer;
        _oem1Bridge = bridge;
        _oem1MappingPreference = mappingPreference;
        mappingPreference.Oem1MappingChanged += OnOem1MappingChanged;

        if (!_startCenterMOem1LifecycleRuntime)
            return Task.CompletedTask;

        // Scope 7: the returned task is the one OWNED activation (see _oem1TaskSync) so DisposeAsync
        // can join it before the coordinator it calls into is disposed, AND the production startup
        // boundary can await the SAME task before routing/power observation begins. When remapping is
        // switched off this still resolves normally -- it simply never enables anything.
        var enabled = ComputeOem1CanArm(mappingPreference);
        lock (_oem1TaskSync)
        {
            _oem1RemappingEnabled = enabled;
            return _oem1ActivationTask = StartInitialOem1ActivationAsync(enabled);
        }
    }

    Task IHandheldRoutingComposition.ConfigureWingActionPath(
        Func<SteamInputAddonforClaw.Wing.WingRouteAuthoritySnapshot> captureAuthority,
        Func<bool> tryRequestSteamPulse)
    {
        if (!_hardwareSupported || _wingBridge is not null) return Task.CompletedTask;
        var source = _testOnlyWingEventSource ?? new WmiMsiEventSource();
        var mapping = WingMapping.Default;
        var recognizer = new WingGestureRecognizer(() => mapping.Double.Action != WingAction.None);
        var dispatcher = new WingActionDispatcher(() => mapping, tryRequestSteamPulse);
        _wingEventSource = source;
        _wingRecognizer = recognizer;
        _wingBridge = new WingEventGestureBridge(source, recognizer, captureAuthority, dispatcher);
        if (!source.Start())
        {
            AppLog.Warn("Wing.Event", "WING Event88 observation unavailable; routing remains functional.");
            _wingBridge.Dispose();
            _wingBridge = null;
        }
        return Task.CompletedTask;
    }

    private async Task StartInitialOem1ActivationAsync(bool enabled)
    {
        // Keep synchronous WMI/helper work out of composition creation. The task remains owned
        // and is joined by Routing at its helper-acquisition boundary and by disposal.
        await Task.Yield();
        await ApplyOem1RemappingEnabledAsync(enabled, _oem1LifetimeCancellation.Token).ConfigureAwait(false);
    }

    /// <summary>Whether OEM1 desired-enabled should be requested, based solely on the persisted
    /// remapping switch. Shared by the startup path
    /// (<see cref="IHandheldRoutingComposition.ConfigureOem1ActionPath"/>) and the mapping-change path
    /// (<see cref="OnOem1MappingChanged"/>) so both apply the exact same gate. The rest of the arming
    /// decision (environment eligibility, Launcher/Server readiness, same-name topology, exact helper
    /// ownership) is owned entirely by <see cref="CenterMOem1LifecycleCoordinator"/>.</summary>
    private static bool ComputeOem1CanArm(Settings.IOem1MappingPreference preference) =>
        preference.Oem1Mapping.RemappingEnabled;

    /// <summary>Requests the given desired-enabled value be applied through the SAME serialized
    /// activation chain <see cref="OnOem1MappingChanged"/> already uses for every enable/disable
    /// transition, and returns the (possibly already-completed) task representing that request so a
    /// caller that needs to observe completion -- unlike the fire-and-forget mapping-change
    /// notification -- can await it. A no-op (returns the current task unchanged) when nothing needs
    /// to change, the composition is stopping, or a fail-open latch has already revoked custom
    /// authority.</summary>
    private Task RequestOem1EnabledTransitionAsync(bool enabled)
    {
        lock (_oem1TaskSync)
        {
            if (_oem1Stopping || !_startCenterMOem1LifecycleRuntime) return _oem1ActivationTask;
            if (enabled && _oem1FailOpenTask is not null) return _oem1ActivationTask;
            if (_oem1RemappingEnabled == enabled) return _oem1ActivationTask;

            _oem1RemappingEnabled = enabled;
            var previous = _oem1ActivationTask;
            return _oem1ActivationTask = ContinueOem1RemappingAsync(previous, enabled);
        }
    }

    /// <summary>
    /// The global "Center M Button Remapping" switch, applied to the EXISTING suppression lifecycle
    /// owner -- this composition never becomes a second one. On means the coordinator is asked to
    /// enable (arming suppression once its own prerequisites are met); off means it is asked to
    /// disable, restoring native MSI Center M behaviour. Persisted mappings are untouched either way.
    /// </summary>
    private async Task ApplyOem1RemappingEnabledAsync(bool enabled, CancellationToken cancellationToken = default)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            AppLog.Info("CenterM.Oem1", enabled ? "OEM1 activation started." : "OEM1 activation skipped; mapping is OFF.",
                ("Enabled", enabled), ("RoutingGuardArmed", CenterMGuard.IsArmed));
            // Scope 8: WMI startup failure must remain feature-local -- never arm custom suppression,
            // leave native Center M available, continue the Addon runtime otherwise unaffected.
            if (enabled && !TryStartOem1Observation())
                return;

            await CenterMOem1Coordinator.SetDesiredEnabledAsync(enabled, cancellationToken).ConfigureAwait(false);
            AppLog.Info("CenterM.Oem1", "OEM1 activation completed.",
                ("Enabled", enabled), ("HelperProcessId", CenterMHelperOwnership.ProcessId),
                ("RoutingGuardArmed", CenterMGuard.IsArmed));
        }
        catch (Exception exception)
        {
            AppLog.Error("CenterM.Oem1", "Applying the OEM1 remapping switch to the suppression lifecycle failed.", exception, ("Enabled", enabled));
        }
        RefreshOem1BridgeAuthority();
    }

    /// <summary>Starts Event41 observation at most once, and only on behalf of a user who actually
    /// has remapping switched on. Only ever called from inside the serialized activation chain.</summary>
    private bool TryStartOem1Observation()
    {
        if (_oem1EventSource is not { } eventSource) return false;
        if (_oem1ObservationStarted) return true;

        if (!eventSource.Start())
        {
            AppLog.Warn("CenterM.Oem1", "OEM1 WMI observation failed to start; custom OEM1 suppression will not be armed, native Center M remains available.", null);
            return false;
        }

        _oem1ObservationStarted = true;
        return true;
    }

    /// <summary>
    /// Reacts to a persisted OEM1 mapping change. Only an actual remapping On/Off transition does
    /// anything here: a slot-binding edit is picked up by the dispatcher's per-gesture capture and
    /// must never disturb suppression ownership, the helper, or the gesture bridge.
    /// </summary>
    /// <remarks>
    /// Lifecycle work is CHAINED onto <see cref="_oem1ActivationTask"/> rather than started
    /// concurrently, so rapid toggling can never run two conflicting enable/disable requests against
    /// the coordinator at once, and <see cref="DisposeAsync"/> keeps exactly one task to join. A
    /// composition whose fail-open latch has already fired stays fail-open for the rest of its
    /// lifetime -- re-enabling from here would re-arm suppression while custom gesture authority is
    /// permanently revoked, i.e. a dead Center M button, which is strictly worse than native
    /// behaviour.
    /// </remarks>
    private void OnOem1MappingChanged(object? sender, EventArgs args)
    {
        if (_oem1MappingPreference is not { } preference) return;
        RequestOem1EnabledTransitionAsync(ComputeOem1CanArm(preference));
    }

    private async Task ContinueOem1RemappingAsync(Task previous, bool enabled)
    {
        // The predecessor's own failures are already logged where they happen; this chain only needs
        // it to have FINISHED, so a faulted predecessor must not abort the newly requested change.
        try { await previous.ConfigureAwait(false); }
        catch { /* observed above */ }

        await ApplyOem1RemappingEnabledAsync(enabled, _oem1LifetimeCancellation.Token).ConfigureAwait(false);
    }

    /// <summary>Test-only observability: awaits the fire-and-forget startup activation
    /// (<c>SetDesiredEnabledAsync(true)</c> + the resulting bridge-authority refresh) triggered by
    /// <see cref="IHandheldRoutingComposition.ConfigureOem1ActionPath"/>, so a test can deterministically
    /// observe the outcome instead of racing a background task. Never touched by production code.</summary>
    internal Task TestOnly_Oem1ActivationTask { get { lock (_oem1TaskSync) return _oem1ActivationTask; } }

    /// <summary>Test-only observability seam onto the wired OEM1 gesture bridge -- lets a test assert
    /// custom authority state directly without a real WMI/hardware event. Never touched by production
    /// code.</summary>
    internal Oem1EventGestureBridge? TestOnly_Oem1Bridge => _oem1Bridge;

    /// <summary>Test-only seam: invokes the exact same authority-refresh path the production driver's
    /// onReconciled callback calls, so a test can force a refresh to race an action-failure fail-open
    /// deterministically. Never touched by production code.</summary>
    internal void TestOnly_RefreshOem1BridgeAuthority() => RefreshOem1BridgeAuthority();

    /// <summary>
    /// Scope 10/13: the existing <see cref="CenterMOem1LifecycleCoordinator"/> remains the single
    /// authority for whether OEM1 suppression is currently trusted -- this only mirrors its own
    /// <see cref="CenterMOem1LifecycleSnapshot.SuppressionReady"/> onto the gesture bridge's custom
    /// authority, called after every coordinator reconcile tick/resume (never on a timer of its own).
    /// Bridge authority never depends on routing.
    /// </summary>
    private void RefreshOem1BridgeAuthority()
    {
        if (_oem1Bridge is not { } bridge)
            return;

        bool ready;
        try
        {
            ready = CenterMOem1Coordinator.GetSnapshot().SuppressionReady;
        }
        catch (ObjectDisposedException)
        {
            return;
        }

        // Test-only synchronization seam: lets a test pause a stale-but-otherwise-successful
        // readiness read here, before the bridge call below, so it can prove a concurrent
        // OnOem1ActionFailed (a real Event41 -> bridge -> dispatcher failure) never deadlocks
        // against it and never gets undone by it. Never touched by production code.
        TestOnly_BeforeOem1BridgeAuthorityPublish?.Invoke();

        // Review fix (BLOCKER): lock-order inversion. Taking _oem1TaskSync here BEFORE calling into
        // the bridge would invert the lock order against OnGestureRecognized, which deliberately holds
        // the bridge's _recognizerOperationGate while invoking PolicyRequested (i.e. while a real
        // action failure can reach OnOem1ActionFailed). Instead, the "may custom authority actually
        // activate" decision is passed down as a predicate the bridge evaluates from INSIDE its own
        // lock -- _oem1TaskSync is only ever acquired after entering the bridge, never before, so the
        // lock order is always bridge -> _oem1TaskSync, everywhere.
        // _oem1RemappingEnabled is evaluated inside the same predicate for the same reason the
        // fail-open latch is: a user who just switched remapping off must stop having their Center M
        // presses intercepted immediately, not only once the coordinator's next reconcile happens to
        // publish SuppressionReady == false.
        bridge.SetCustomAuthority(ready, allowActivation: () =>
        {
            lock (_oem1TaskSync)
                return !_oem1Stopping && _oem1FailOpenTask is null && _oem1RemappingEnabled;
        });
    }

    /// <summary>See the invocation site inside <see cref="RefreshOem1BridgeAuthority"/>. Never touched
    /// by production code.</summary>
    internal Action? TestOnly_BeforeOem1BridgeAuthorityPublish;

    /// <summary>
    /// Scope 9: an actual replacement-action execution failure (e.g. the Big Picture launch backend
    /// throwing) is different from routing simply being unavailable/inactive -- revoke custom gesture
    /// authority and request the existing coordinator disable/fail-open, restoring native Center M.
    /// </summary>
    private void OnOem1ActionFailed()
    {
        // Re-entrant on the bridge's own _recognizerOperationGate when called synchronously from
        // within a PolicyRequested subscriber (the real production path) -- see
        // Oem1EventGestureBridge.OnGestureRecognized's doc comment. Must run BEFORE this method takes
        // _oem1TaskSync below, preserving the same bridge -> _oem1TaskSync lock order
        // RefreshOem1BridgeAuthority uses, so a real action failure can never deadlock against a
        // concurrent lifecycle refresh/disposal.
        _oem1Bridge?.SetCustomAuthority(false);

        lock (_oem1TaskSync)
        {
            // _oem1FailOpenTask is a one-way lifetime latch (see field doc comment): once set, custom
            // OEM1 admission stays fail-open for the rest of this composition's lifetime. Once teardown
            // has begun, no new fail-open task may start either -- DisposeAsync is (or is about to be)
            // the sole owner draining outstanding OEM1 background work before the coordinator it
            // targets is disposed.
            if (_oem1Stopping || _oem1FailOpenTask is not null)
                return;

            _oem1FailOpenTask = DisableOem1AfterActionFailureAsync(_oem1LifetimeCancellation.Token);
        }
    }

    private async Task DisableOem1AfterActionFailureAsync(CancellationToken cancellationToken)
    {
        try
        {
            // Replacement-action failure is feature-local. Revoke OEM1 authority and let the
            // existing shared-demand policy keep Routing alive until its own teardown.
            await CenterMOem1Coordinator.SetDesiredEnabledAsync(false, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            AppLog.Error("CenterM.Oem1", "Disabling OEM1 lifecycle after a replacement-action failure did not complete.", exception);
        }
    }

    /// <summary>
    /// Forwards the physical-input source's completion summary to the registered runtime-fault
    /// handler when it represents an unexpected termination of a session routing currently owns
    /// (<see cref="MsiClawPhysicalInputFaultPolicy"/>). Expected stops -- normal pipeline rollback
    /// (<see cref="MsiClawInputStopReason.Stopped"/>) and any stop before
    /// <see cref="MsiClawPhysicalInputStage"/> has committed ownership -- are not reported.
    /// Internal (rather than private) so the decision/dispatch wiring can be exercised directly in
    /// tests without a real DirectInput device.
    /// </summary>
    internal void OnPhysicalInputCompleted(object? sender, MsiClawInputTestSummary summary)
    {
        if (!MsiClawPhysicalInputFaultPolicy.IsFatal(summary.StopReason, PhysicalInputStage.CurrentIdentity is not null))
            return;

        AppLog.Warn("Routing.Runtime", "Owned physical-input session terminated unexpectedly; requesting routing fail-close.", null,
            ("Event", "PhysicalInputSessionLost"), ("StopReason", summary.StopReason), ("Action", "FailClosed"));
        ReportRuntimeFault(MsiClawPhysicalInputFaultPolicy.PhysicalInputSessionLostReason);
    }

    /// <summary>
    /// Internal (rather than private) so the dispatch/exception-containment behavior can be
    /// exercised directly in tests without needing committed physical-input ownership, which
    /// requires real DirectInput hardware.
    /// </summary>
    internal void ReportRuntimeFault(string reason)
    {
        if (_runtimeFaultHandler is not { } handler)
            return;

        // Do not synchronously await the handler here: it eventually rolls back this same physical
        // input stage, which awaits the polling task raising this very event -- a self-await
        // deadlock. Detach onto a background task instead, matching the existing Steam output
        // fault-forwarding pattern (CanonicalSteamDeckOutputStage.ReportOutputFault).
        _ = Task.Run(() => RunRuntimeFaultHandlerAsync(handler, reason));
    }

    private static async Task RunRuntimeFaultHandlerAsync(Func<string, ValueTask> handler, string reason)
    {
        try
        {
            await handler(reason).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            AppLog.Error("Routing.Runtime", "Backend runtime fault handler failed.", exception, ("Reason", reason));
        }
    }

    public async ValueTask DisposeAsync()
    {
        _wingBridge?.Dispose();
        _wingBridge = null;
        _wingRecognizer = null;
        _wingEventSource = null;
        // Review fix (MAJOR): close custom OEM1 gesture admission and dispose the event/gesture/
        // bridge path as the VERY FIRST step of composition disposal -- before any other awaited
        // teardown below. The bridge race fix (Oem1EventGestureBridge.OnGestureRecognized) only
        // guarantees no policy delivery can begin AFTER SetCustomAuthority(false)/Dispose() returns;
        // it does nothing to stop a physical OEM1 press from starting a NEW BPM/QAM action while an
        // earlier awaited step (e.g. NativeModeSession/PhysicalInputSource teardown) is still in
        // flight. The stated shutdown contract is "close custom gesture admission first, then tear
        // down the rest" -- so this boundary must not wait behind any of that unrelated teardown.
        //
        // Review fix (BLOCKER): permanently close/drain the bridge BEFORE taking _oem1TaskSync, never
        // after -- disposing the bridge first (which internally revokes authority as part of the same
        // operation) guarantees no new PolicyRequested callback can begin once Dispose() returns, so
        // capturing the owned tasks under _oem1TaskSync afterward is safe without ever reversing the
        // bridge -> _oem1TaskSync lock order RefreshOem1BridgeAuthority/OnOem1ActionFailed rely on.
        // Unsubscribed alongside the bridge teardown, not later: once admission is closed there is
        // nothing left for a settings change to drive, and a live subscription would keep this
        // composition reachable from the process-lifetime settings coordinator.
        if (_oem1MappingPreference is { } mappingPreference)
            mappingPreference.Oem1MappingChanged -= OnOem1MappingChanged;

        _oem1Bridge?.Dispose();
        _oem1GestureRecognizer?.Dispose();

        // Captures whatever activation/fail-open work is currently owned/in-flight -- both background
        // operations call into the coordinator this method eventually disposes, so neither may still
        // be capable of entering it once DisposeAsync returns.
        Task oem1ActivationTask, oem1FailOpenTask;
        lock (_oem1TaskSync)
        {
            _oem1Stopping = true;
            _oem1LifetimeCancellation.Cancel();
            oem1ActivationTask = _oem1ActivationTask;
            oem1FailOpenTask = _oem1FailOpenTask ?? Task.CompletedTask;
        }

        PhysicalInputSource.TestCompleted -= OnPhysicalInputCompleted;
        PhysicalInputStage.PhysicalSessionRetired -= PhysicalRumbleSink.InvalidatePhysicalSession;
        PhysicalInputStage.PhysicalSessionRetiring -= PhysicalRumbleSink.BeginPhysicalSessionRetirement;
        PhysicalInputStage.PhysicalSessionStarted -= PhysicalRumbleSink.BeginPhysicalSession;
        PhysicalRumbleSink.Dispose();

        // Normal routing rollback already disarms the guard (last in
        // RoutingPipelineStageOrder.Rollback, after native/physical restoration) long before
        // disposal is ever reached -- this composition is only disposed after routing has already
        // been shut down. This is a last-resort fallback for whatever normal rollback did NOT
        // resolve, so it preserves the same ordering invariant (relative to each other): never
        // release Center M launch protection before native-mode/physical restoration has at least
        // been attempted. OEM1 admission above is closed first regardless, per the shutdown contract.
        await NativeModeSession.DisposeAsync().ConfigureAwait(false);
        await PhysicalInputSource.DisposeAsync().ConfigureAwait(false);

        var activationDrained = true;
        try { await oem1ActivationTask.WaitAsync(Oem1ShutdownJoinTimeout).ConfigureAwait(false); }
        catch (TimeoutException exception)
        {
            activationDrained = false;
            AppLog.Error("CenterM.Oem1", "OEM1 startup activation did not drain during shutdown; retaining its coordinator and helper authority.", exception);
        }
        catch (OperationCanceledException) when (_oem1LifetimeCancellation.IsCancellationRequested) { }
        catch (Exception exception) { AppLog.Error("CenterM.Oem1", "OEM1 startup activation did not complete cleanly during shutdown.", exception); }
        var failOpenDrained = true;
        try { await oem1FailOpenTask.WaitAsync(Oem1ShutdownJoinTimeout).ConfigureAwait(false); }
        catch (TimeoutException exception)
        {
            failOpenDrained = false;
            AppLog.Error("CenterM.Oem1", "OEM1 action-failure fail-open did not drain during shutdown; retaining its coordinator and helper authority.", exception);
        }
        catch (OperationCanceledException) when (_oem1LifetimeCancellation.IsCancellationRequested) { }
        catch (Exception exception) { AppLog.Error("CenterM.Oem1", "OEM1 action-failure fail-open did not complete cleanly during shutdown.", exception); }

        if (!activationDrained || !failOpenDrained)
            return;

        // WmiMsiEventSource serializes Start/Dispose under its own lock. Dispose the source only
        // after the owned activation/fail-open tasks have drained, so a slow Start cannot block
        // shutdown before the cancellation and bounded-join policy has taken effect.
        _oem1EventSource?.Dispose();

        // Shutdown ordering (work order requirement 14): stop the OEM1 periodic driver and dispose
        // the coordinator BEFORE the routing guard's own terminal cleanup below -- a timer callback
        // must never still be capable of entering coordinator methods once this composition starts
        // tearing down the shared helper authority. Safe even though DesiredEnabled has always been
        // false outside the PR3 production action path: the coordinator's own DisposeAsync is a no-op
        // stop when it never owned anything.
        await CenterMOem1Runtime.DisposeAsync().ConfigureAwait(false);

        // Terminal cleanup, not the normal Disarm path: bounded final Stop retries, and -- if the
        // exact helper handle is still unresolved after those -- hands it to the process-level
        // CenterMOrphanedHelperRegistry rather than letting this composition (and the guard's own
        // ability to retry) become unreachable with the only exact ownership still outstanding.
        // This only acts if the guard itself started the helper -- a borrowed helper is left
        // untouched for its external owner (PR1 ownership convergence).
        await CenterMGuard.DisposeAsync().ConfigureAwait(false);

        // Sole final disposer of the shared authority, but only when THIS composition created it
        // (PR1 ownership convergence, requirement 8): a caller-injected instance (a future OEM1
        // composition seam sharing one instance across both consumers) is never disposed here --
        // its creator remains the one terminal owner, so two terminal-cleanup paths never race the
        // same exact-handle ownership. When this composition IS the owner, disposing is a no-op if
        // nothing is owned (never started, already stopped by the guard above).
        if (_ownsCenterMHelperOwnership)
            CenterMHelperOwnership.Dispose();

        _oem1LifetimeCancellation.Dispose();
    }
}
