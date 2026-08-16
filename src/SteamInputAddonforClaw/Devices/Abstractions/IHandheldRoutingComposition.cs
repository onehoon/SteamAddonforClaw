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
/// actually needs. Likewise this type carries no lifecycle capabilities (recovery session id,
/// fail-closed, disposal, ...) -- those are handled separately.
/// </remarks>
internal interface IHandheldRoutingComposition
{
    IReadOnlyList<IRoutingPipelineStage> Stages { get; }

    IControllerStateSnapshotSource ControllerStateSource { get; }

    IReadOnlyList<IRoutingRuntimeSessionBoundaryParticipant> SessionBoundaryParticipants { get; }
}
