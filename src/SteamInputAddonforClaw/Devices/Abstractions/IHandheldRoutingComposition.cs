using SteamInputAddonforClaw.Input;
using SteamInputAddonforClaw.Power;
using SteamInputAddonforClaw.Routing;
using SteamInputAddonforClaw.Feedback;

namespace SteamInputAddonforClaw.Devices.Abstractions;

/// <summary>
/// What a device-specific handheld routing composition supplies to the generic routing/output
/// layer: the device/backend-specific pipeline stages that must run before the generic output
/// stage, the normalized <see cref="ControllerState"/> source generic output consumes, and the
/// routing session-boundary participants the runtime coordinator already understands.
/// </summary>
/// <remarks>
/// Deliberately minimal. <see cref="Stages"/> is a plain list rather than named
/// native-mode/physical-input/physical-isolation properties -- that would encode today's MSI
/// topology into a supposedly generic contract. A future backend without native-mode or physical
/// isolation is not required to fake those concepts; it can return however many stages it
/// actually needs. <see cref="SafetySession"/> exposes an optional routing-safety capability
/// (recovery session id, activity/recovery-boundary state, fail-closed) for backends that have
/// one -- a backend without a meaningful safety session may return null.
///
/// <para>
/// The composition owns the backend resources represented by these views. <see cref="Stages"/>,
/// <see cref="ControllerStateSource"/>, <see cref="SessionBoundaryParticipants"/>,
/// <see cref="SafetySession"/>, and <see cref="PhysicalRumbleSink"/> are borrowed references into
/// resources the composition created and
/// must not be disposed independently by consumers. <see cref="IAsyncDisposable.DisposeAsync"/>
/// releases those backend-owned resources; callers must dispose the composition only after
/// routing and power orchestration built on top of it have already been stopped.
/// </para>
/// </remarks>
internal interface IHandheldRoutingComposition : IAsyncDisposable
{
    IReadOnlyList<IRoutingPipelineStage> Stages { get; }

    IControllerStateSnapshotSource ControllerStateSource { get; }

    IReadOnlyList<IRoutingRuntimeSessionBoundaryParticipant> SessionBoundaryParticipants { get; }

    IRoutingSafetySession? SafetySession { get; }

    /// <summary>Optional borrowed physical-feedback capability; null is valid for a backend without rumble.</summary>
    IPhysicalRumbleSink? PhysicalRumbleSink { get; }

    /// <summary>
    /// Registers the callback the composition must invoke when it detects a fatal backend runtime
    /// fault -- currently, unexpected loss of an owned physical-input session -- that invalidates
    /// the currently active routing pipeline. The reported reason is a short stable string, not a
    /// user-facing message. A backend with nothing to report may simply never invoke it.
    /// </summary>
    void SetRuntimeFaultHandler(Func<string, bool, ValueTask> handler);

    /// <summary>
    /// Optional additional power-suspend participant this composition owns, beyond the routing
    /// pipeline's own safety session -- e.g. an MSI-specific OEM1/Center M lifecycle driver. Null
    /// (the default) for a backend with nothing extra to quiesce. Deliberately generic: the
    /// device-specific detail stays entirely inside the composition that supplies it.
    /// </summary>
    IPowerSuspendParticipant? AuxiliaryPowerParticipant => null;

    /// <summary>
    /// Optional additional resume-reconcile participant this composition owns (see
    /// <see cref="IRuntimeResumeParticipant"/>). Null (the default) for a backend with nothing
    /// extra to reconcile after resume.
    /// </summary>
    IRuntimeResumeParticipant? AuxiliaryResumeParticipant => null;

    Task<RoutingStageOperationResult> PauseOwnedRouteForSuspendAsync(CancellationToken cancellationToken) =>
        Task.FromResult(RoutingStageOperationResult.Success("NoOwnedRoute"));

    Task<RoutingStageOperationResult> ReconcileOwnedRouteStateAsync(CancellationToken cancellationToken) =>
        Task.FromResult(RoutingStageOperationResult.Success("NoOwnedRoute"));

    Task PrepareRoutingEntryAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    Task CompleteRoutingExitAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>
    /// Optional OEM1 production action-path wiring hook (PR3 development E2E POC). The generic
    /// routing/output layer is the only place that owns the canonical Steam Deck output stage's QAM
    /// pulse primitive and the fresh routing-status capture -- this passes just those two narrow
    /// callbacks down to a composition that has its own OEM1 feature (e.g. MSI Center M), so it can
    /// wire its dispatcher without the routing/output layer ever learning it is MSI/CenterM specific.
    /// A backend with no OEM1 feature never overrides this (the default is a no-op). Never called more
    /// than once per composition instance in production.
    ///
    /// <para>
    /// Returns the (possibly still in-flight) OEM1 lifecycle-enable activation as a single owned
    /// task, so the production startup boundary can await it before routing/power observation is
    /// allowed to begin -- see <see cref="Devices.MSI.Claw.MsiClawRoutingComposition"/>'s
    /// implementation for why: the OEM1 coordinator and the routing guard share the SAME underlying
    /// helper ownership, so the OEM1 activation decision must be settled before the routing guard's
    /// own arm transaction can start.
    /// </para>
    ///
    /// <para>
    /// <paramref name="mappingPreference"/> is the persisted OEM1 mapping source of truth (global
    /// remapping switch + the four slot bindings). It is read fresh per gesture and its change event
    /// drives the remapping switch onto the composition's existing suppression lifecycle -- it is
    /// deliberately NOT a routing input and carries nothing about Steam routing.
    /// </para>
    /// </summary>
    Task ConfigureOem1ActionPath(
        Func<RoutingRuntimeStatusSnapshot> captureRoutingStatus,
        Action requestQuickAccessPulse,
        Settings.IOem1MappingPreference mappingPreference) => Task.CompletedTask;

    Task ConfigureWingActionPath(
        Func<SteamInputAddonforClaw.Wing.WingRouteAuthoritySnapshot> captureAuthority,
        Func<bool> tryRequestSteamPulse,
        Settings.IWingMappingPreference mappingPreference) => Task.CompletedTask;
}
