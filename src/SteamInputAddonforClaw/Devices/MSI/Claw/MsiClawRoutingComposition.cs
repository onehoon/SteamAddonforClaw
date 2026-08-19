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
    /// competing production helper owner. Production activation stays off: normal composition never
    /// calls <see cref="CenterMOem1LifecycleCoordinator.SetDesiredEnabledAsync"/> with true. Tests
    /// may do so explicitly through this property to exercise the full production composition
    /// seam.</summary>
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
    private Func<string, ValueTask>? _runtimeFaultHandler;

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
        bool startCenterMOem1LifecycleRuntime = true)
    {
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
        CenterMOem1Runtime = centerMOem1Runtime ?? new CenterMOem1LifecycleRuntime(
            CenterMOem1Coordinator,
            routingGuardIsArmed: () => CenterMGuard.IsArmed);

        // Requirement 3/8: the driver is started as part of normal MSI runtime composition (it is
        // part of the real runtime object lifetime now), but `DesiredEnabled` is never set to true
        // here -- normal production startup never stages/starts the helper via this coordinator.
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
        PhysicalInputSource.TestCompleted -= OnPhysicalInputCompleted;
        PhysicalInputStage.PhysicalSessionRetired -= PhysicalRumbleSink.InvalidatePhysicalSession;
        PhysicalInputStage.PhysicalSessionRetiring -= PhysicalRumbleSink.BeginPhysicalSessionRetirement;
        PhysicalInputStage.PhysicalSessionStarted -= PhysicalRumbleSink.BeginPhysicalSession;
        PhysicalRumbleSink.Dispose();

        // Normal routing rollback already disarms the guard (last in
        // RoutingPipelineStageOrder.Rollback, after native/physical restoration) long before
        // disposal is ever reached -- this composition is only disposed after routing has already
        // been shut down. This is a last-resort fallback for whatever normal rollback did NOT
        // resolve, so it preserves the same ordering invariant: never release Center M launch
        // protection before native-mode/physical restoration has at least been attempted.
        await NativeModeSession.DisposeAsync().ConfigureAwait(false);
        await PhysicalInputSource.DisposeAsync().ConfigureAwait(false);

        // Shutdown ordering (work order requirement 14): stop the OEM1 periodic driver and dispose
        // the coordinator BEFORE the routing guard's own terminal cleanup below -- a timer callback
        // must never still be capable of entering coordinator methods once this composition starts
        // tearing down the shared helper authority. Safe even though DesiredEnabled has always been
        // false in this PR: the coordinator's own DisposeAsync is a no-op stop when it never owned
        // anything.
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
