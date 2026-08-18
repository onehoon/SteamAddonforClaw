namespace SteamInputAddonforClaw.CenterM;

/// <summary>
/// Selectable OEM1 gesture-to-action bindings. This is the future settings seam: nothing here
/// persists or exposes UI yet, but the binding is not hard-coded into the dispatch logic.
/// </summary>
internal readonly record struct Oem1ActionBindings(
    Oem1Action Single,
    Oem1Action Double)
{
    internal static Oem1ActionBindings Default { get; } =
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
