using SteamInputAddonforClaw.CenterM;
using SteamInputAddonforClaw.Devices.Abstractions;
using SteamInputAddonforClaw.Diagnostics;
using SteamInputAddonforClaw.HidHide;
using SteamInputAddonforClaw.Input;
using SteamInputAddonforClaw.Input.DirectInput;
using SteamInputAddonforClaw.Power;
using SteamInputAddonforClaw.Recovery;
using SteamInputAddonforClaw.Routing;
using SteamInputAddonforClaw.Feedback;

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
    private readonly object _oem1TaskSync = new();
    private Task _oem1ActivationTask = Task.CompletedTask;
    private Task _oem1FailOpenTask = Task.CompletedTask;
    private bool _oem1Stopping;

    internal MsiClawRoutingComposition(
        MsiClawNativeStateManager nativeState,
        RecoveryManager recovery,
        PowerMutationGate powerGate,
        RecoverySafetyState recoverySafety,
        Func<IMsiClawRumbleEndpointResolver>? rumbleEndpointResolverFactory = null,
        CenterMHelperOwnership? centerMHelperOwnership = null,
        CenterMMainUiRoutingGuard? centerMGuard = null,
        CenterMOem1LifecycleCoordinator? centerMOem1Coordinator = null,
        CenterMOem1LifecycleRuntime? centerMOem1Runtime = null,
        bool startCenterMOem1LifecycleRuntime = true,
        IMsiEventSource? testOnlyOem1EventSource = null,
        IOem1GestureDelay? testOnlyOem1GestureDelay = null,
        IOem1GestureClock? testOnlyOem1GestureClock = null,
        Action? testOnlyOem1LaunchBigPicture = null)
    {
        _testOnlyOem1EventSource = testOnlyOem1EventSource;
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
        CenterMGuard = centerMGuard ?? new CenterMMainUiRoutingGuard(
            processSnapshotSource: centerMProcesses,
            helperOwnership: CenterMHelperOwnership,
            retirement: new CenterMMainUiRoutingRetirement(
                new MsiClawCenterMNativeModeProbe(nativeState),
                // Same NativeModeSession instance the real stage below uses -- an early read-only
                // look at the same route authority, so an already-doomed route (fault latch,
                // recovery safety, power gate) never retires the user's real MainUI first.
                new MsiClawCenterMRoutingPreflightProbe(NativeModeSession),
                processSnapshotSource: centerMProcesses));
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
        CenterMOem1Coordinator = centerMOem1Coordinator ?? new CenterMOem1LifecycleCoordinator(
            publishRootProvider: () => AppContext.BaseDirectory,
            processSnapshotSource: centerMProcesses,
            helperOwnership: CenterMHelperOwnership,
            environmentEligibility: () => true);
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
        Action requestQuickAccessPulse)
    {
        if (_oem1ActionPathConfigured) return Task.CompletedTask;
        _oem1ActionPathConfigured = true;

        var eventSource = _testOnlyOem1EventSource ?? new WmiMsiEventSource();
        var recognizer = new Oem1GestureRecognizer(doubleClickEnabled: true, doubleClickWindow: TimeSpan.FromMilliseconds(400),
            delay: _testOnlyOem1GestureDelay, clock: _testOnlyOem1GestureClock);
        var bridge = new Oem1EventGestureBridge(eventSource, recognizer);
        var dispatcher = new Oem1ActionDispatcher(
            normalBindings: Oem1ActionBindings.NormalDefault,
            routingActiveBindings: Oem1ActionBindings.RoutingActiveDefault,
            captureRoutingStatus: captureRoutingStatus,
            requestQuickAccessPulse: requestQuickAccessPulse,
            launchBigPicture: _testOnlyOem1LaunchBigPicture ?? Oem1BigPictureLauncher.Launch);

        bridge.PolicyRequested += request =>
        {
            if (!dispatcher.Dispatch(request))
                OnOem1ActionFailed();
        };

        _oem1EventSource = eventSource;
        _oem1GestureRecognizer = recognizer;
        _oem1Bridge = bridge;

        if (!_startCenterMOem1LifecycleRuntime)
            return Task.CompletedTask;

        // Scope 8: WMI startup failure must remain feature-local -- never arm custom suppression,
        // leave native Center M available, continue the Addon runtime otherwise unaffected.
        if (!eventSource.Start())
        {
            AppLog.Warn("CenterM.Oem1", "OEM1 WMI observation failed to start; custom OEM1 suppression will not be armed, native Center M remains available.", null);
            return Task.CompletedTask;
        }

        // Scope 7: request lifecycle enable only after WMI observation has actually started and the
        // gesture/action wiring above is already subscribed. Explicitly OWNED (see _oem1TaskSync) so
        // DisposeAsync can join it before the coordinator it calls into is disposed, AND now also
        // returned to the caller so the production startup boundary can await the SAME task before
        // routing/power observation begins.
        lock (_oem1TaskSync)
            return _oem1ActivationTask = EnableOem1Async();
    }

    private async Task EnableOem1Async()
    {
        try
        {
            await CenterMOem1Coordinator.SetDesiredEnabledAsync(true).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            AppLog.Error("CenterM.Oem1", "Requesting OEM1 lifecycle enable failed.", exception);
        }
        RefreshOem1BridgeAuthority();
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

        bridge.SetCustomAuthority(ready);
    }

    /// <summary>
    /// Scope 9: an actual replacement-action execution failure (e.g. the Big Picture launch backend
    /// throwing) is different from routing simply being unavailable/inactive -- revoke custom gesture
    /// authority and request the existing coordinator disable/fail-open, restoring native Center M.
    /// </summary>
    private void OnOem1ActionFailed()
    {
        _oem1Bridge?.SetCustomAuthority(false);

        lock (_oem1TaskSync)
        {
            // Once teardown has begun, no new fail-open task may start -- DisposeAsync is (or is
            // about to be) the sole owner draining outstanding OEM1 background work before the
            // coordinator it targets is disposed. A fail-open already in flight is left to finish
            // naturally; a second one is unnecessary (SetDesiredEnabledAsync(false) is idempotent-ish
            // via the coordinator's own gate) and would just create a second untracked reference.
            if (_oem1Stopping || !_oem1FailOpenTask.IsCompleted)
                return;

            _oem1FailOpenTask = DisableOem1AfterActionFailureAsync();
        }
    }

    private async Task DisableOem1AfterActionFailureAsync()
    {
        try
        {
            await CenterMOem1Coordinator.SetDesiredEnabledAsync(false).ConfigureAwait(false);
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
        // Review fix (MAJOR): close custom OEM1 gesture admission and dispose the event/gesture/
        // bridge path as the VERY FIRST step of composition disposal -- before any other awaited
        // teardown below. The bridge race fix (Oem1EventGestureBridge.OnGestureRecognized) only
        // guarantees no policy delivery can begin AFTER SetCustomAuthority(false)/Dispose() returns;
        // it does nothing to stop a physical OEM1 press from starting a NEW BPM/QAM action while an
        // earlier awaited step (e.g. NativeModeSession/PhysicalInputSource teardown) is still in
        // flight. The stated shutdown contract is "close custom gesture admission first, then tear
        // down the rest" -- so this boundary must not wait behind any of that unrelated teardown.
        _oem1Bridge?.SetCustomAuthority(false);

        // Review fix (BLOCKER): close admission to new fail-open scheduling and capture whatever
        // activation/fail-open work is currently owned/in-flight BEFORE disposing the bridge/event
        // source -- both background operations call into the coordinator this method eventually
        // disposes below, so neither may still be capable of entering it once DisposeAsync returns.
        Task oem1ActivationTask, oem1FailOpenTask;
        lock (_oem1TaskSync)
        {
            _oem1Stopping = true;
            oem1ActivationTask = _oem1ActivationTask;
            oem1FailOpenTask = _oem1FailOpenTask;
        }

        _oem1Bridge?.Dispose();
        _oem1EventSource?.Dispose();
        _oem1GestureRecognizer?.Dispose();

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

        try { await oem1ActivationTask.ConfigureAwait(false); }
        catch (Exception exception) { AppLog.Error("CenterM.Oem1", "OEM1 startup activation did not complete cleanly during shutdown.", exception); }
        try { await oem1FailOpenTask.ConfigureAwait(false); }
        catch (Exception exception) { AppLog.Error("CenterM.Oem1", "OEM1 action-failure fail-open did not complete cleanly during shutdown.", exception); }

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
    }
}
