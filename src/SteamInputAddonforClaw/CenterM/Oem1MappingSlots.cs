using SteamInputAddonforClaw.Contracts.Oem1;

namespace SteamInputAddonforClaw.CenterM;

/// <summary>
/// The single place the (mapping domain, gesture) pair becomes one of the four
/// <see cref="Oem1BindingSlot"/> values.
/// </summary>
/// <remarks>
/// Kept as a tiny pure function rather than folded into the dispatcher so the domain rule -- "actual
/// canonical Steam Deck output state, nothing else" -- can be asserted directly in tests, and so
/// there is exactly one expression of it for both dispatch and any future surface. It takes the
/// already-decided boolean, never a routing snapshot: this type must not be able to look at
/// <c>Available</c>, routing eligibility, Steam/Big Picture state, or the routing setting even by
/// accident.
/// </remarks>
internal static class Oem1MappingSlots
{
    internal static Oem1BindingSlot Resolve(bool steamOutputActive, Oem1Gesture gesture) =>
        steamOutputActive
            ? gesture == Oem1Gesture.Double ? Oem1BindingSlot.RoutingDouble : Oem1BindingSlot.RoutingSingle
            : gesture == Oem1Gesture.Double ? Oem1BindingSlot.NormalDouble : Oem1BindingSlot.NormalSingle;
}
