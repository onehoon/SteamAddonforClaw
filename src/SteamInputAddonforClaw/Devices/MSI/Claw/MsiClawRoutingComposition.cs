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
    /// e.g. by tests) and passed to <see cref="CenterMGuard"/> via constructor injection so routing
    /// never constructs a second, competing production ownership object for the same same-name
    /// helper identity. A future OEM1 production composition seam can receive this SAME instance.
    /// This composition is the sole final disposer -- <see cref="DisposeAsync"/> disposes it after
    /// <see cref="CenterMGuard"/> has already released its own routing-time resources.</summary>
    internal CenterMHelperOwnership CenterMHelperOwnership { get; }
    internal CenterMMainUiRoutingGuard CenterMGuard { get; }
    internal CenterMMainUiRoutingGuardStage CenterMGuardStage { get; }

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
        CenterMMainUiRoutingGuard? centerMGuard = null)
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
        CenterMHelperOwnership = centerMHelperOwnership ?? new CenterMHelperOwnership();
        CenterMGuard = centerMGuard ?? new CenterMMainUiRoutingGuard(helperOwnership: CenterMHelperOwnership);
        CenterMGuardStage = new CenterMMainUiRoutingGuardStage(CenterMGuard);

        _stages = [NativeModeStage, PhysicalInputStage, PhysicalIsolationStage, CenterMGuardStage];
        _sessionBoundaryParticipants = [NativeModeSession];
    }

    IReadOnlyList<IRoutingPipelineStage> IHandheldRoutingComposition.Stages => _stages;

    IControllerStateSnapshotSource IHandheldRoutingComposition.ControllerStateSource => PhysicalInputSource;

    IReadOnlyList<IRoutingRuntimeSessionBoundaryParticipant> IHandheldRoutingComposition.SessionBoundaryParticipants => _sessionBoundaryParticipants;

    IRoutingSafetySession? IHandheldRoutingComposition.SafetySession => NativeModeSession;
    IPhysicalRumbleSink? IHandheldRoutingComposition.PhysicalRumbleSink => PhysicalRumbleSink;

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

        // Terminal cleanup, not the normal Disarm path: bounded final Stop retries, and -- if the
        // exact helper handle is still unresolved after those -- hands it to the process-level
        // CenterMOrphanedHelperRegistry rather than letting this composition (and the guard's own
        // ability to retry) become unreachable with the only exact ownership still outstanding.
        // This only acts if the guard itself started the helper -- a borrowed helper is left
        // untouched for its external owner (PR1 ownership convergence).
        await CenterMGuard.DisposeAsync().ConfigureAwait(false);

        // Sole final disposer of the shared authority this composition created (PR1 ownership
        // convergence, requirement 8): a no-op if nothing is owned (never started, already stopped
        // by the guard above, or still borrowed by an external owner in a future PR).
        CenterMHelperOwnership.Dispose();
    }
}
