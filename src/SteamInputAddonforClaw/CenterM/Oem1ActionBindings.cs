namespace SteamInputAddonforClaw.CenterM;

/// <summary>
/// Selectable OEM1 gesture-to-action bindings. This is the future settings seam: nothing here
/// persists or exposes UI yet, but the binding is not hard-coded into the dispatch logic.
/// </summary>
/// <remarks>
/// Two independent binding domains exist, deliberately kept as two separate instances of this same
/// minimal struct rather than one combined table: <see cref="NormalDefault"/> is used whenever
/// canonical Steam Deck routing is not actually active right now, and <see cref="RoutingActiveDefault"/>
/// is used only while it is. <see cref="Oem1ActionDispatcher"/> selects the domain first (by
/// capturing routing state fresh at dispatch time), then resolves the gesture within that domain --
/// it never resolves one action and asks the action itself whether routing is active.
/// </remarks>
internal readonly record struct Oem1ActionBindings(
    Oem1Action Single,
    Oem1Action Double)
{
    /// <summary>Normal-mapping POC default: works whether or not routing is enabled/available/active.</summary>
    internal static Oem1ActionBindings NormalDefault { get; } =
        new(
            Single: Oem1Action.SteamBigPicture,
            Double: Oem1Action.None);

    /// <summary>Routing-active-mapping POC default: selected only while canonical Steam Deck routing
    /// is actually active right now.</summary>
    internal static Oem1ActionBindings RoutingActiveDefault { get; } =
        new(
            Single: Oem1Action.SteamQuickAccess,
            Double: Oem1Action.None);

    internal Oem1Action Resolve(Oem1Gesture gesture) =>
        gesture switch
        {
            Oem1Gesture.Single => Single,
            Oem1Gesture.Double => Double,
            _ => Oem1Action.None
        };
}
