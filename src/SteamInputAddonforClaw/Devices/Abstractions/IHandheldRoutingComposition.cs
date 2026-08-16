using SteamInputAddonforClaw.Input;
using SteamInputAddonforClaw.Routing;

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
/// one, but this composition does not own or aggregate backend disposal in this phase -- a
/// backend without a meaningful safety session may return null.
/// </remarks>
internal interface IHandheldRoutingComposition
{
    IReadOnlyList<IRoutingPipelineStage> Stages { get; }

    IControllerStateSnapshotSource ControllerStateSource { get; }

    IReadOnlyList<IRoutingRuntimeSessionBoundaryParticipant> SessionBoundaryParticipants { get; }

    IRoutingSafetySession? SafetySession { get; }
}
