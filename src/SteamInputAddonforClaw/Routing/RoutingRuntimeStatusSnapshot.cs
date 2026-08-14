namespace SteamInputAddonforClaw.Routing;

/// <summary>
/// Immutable, read-only presentation snapshot of the routing runtime's actual state — as opposed
/// to RoutingDecision, which only expresses eligibility. Used by Status UI to distinguish "routing
/// is eligible" from "routing has actually entered and the Steam output stage is live".
/// </summary>
internal readonly record struct RoutingRuntimeStatusSnapshot(
    bool Available,
    RoutingOperationalState OperationalState,
    bool SteamOutputActive,
    bool NativeDirectInputActive)
{
    internal static RoutingRuntimeStatusSnapshot Unavailable { get; } =
        new(
            Available: false,
            OperationalState: RoutingOperationalState.Passive,
            SteamOutputActive: false,
            NativeDirectInputActive: false);
}
