using SteamInputAddonforClaw.Input;

namespace SteamInputAddonforClaw.VirtualOutput.Viiper;

/// <summary>
/// Maps the Addon's physical <see cref="ControllerState"/> into the canonical VIIPER
/// <see cref="Xbox360DeviceState"/> (VIIPER main@e10b5f02945b1322f33c33468e583546600ba000).
/// </summary>
/// <remarks>
/// Preparation-only primitive: nothing in the Addon consumes this mapper's output yet. No
/// Xbox360 device is created, attached, or driven by this change -- see
/// docs/VIIPER_MIGRATION_TODO.md for the deferred session/publisher/output-stage work.
///
/// <para>
/// <see cref="ControllerState"/> is already normalized into the XInput semantic convention (up =
/// positive Y, down = negative Y) -- the same convention HHC, ClawTweaks, and DS4Windows all
/// converge on before their respective X360 output boundary. This mapper therefore preserves
/// <c>LeftStick.Y</c>/<c>RightStick.Y</c> directly; it must never negate or otherwise transform
/// them. Doing so would apply a second, incorrect Y-axis inversion.
/// </para>
///
/// MSI-specific auxiliary controls (M1/M2) have no Xbox 360 equivalent and are not mapped; Guide
/// is never set, since the Addon exposes no Guide-equivalent control.
/// </remarks>
internal static class Xbox360DeviceStateMapper
{
    internal static Xbox360DeviceState Map(ControllerState state)
    {
        var buttons = state.Buttons;

        uint bits = 0;
        if (buttons.DPadUp) bits |= Xbox360ButtonBits.DPadUp;
        if (buttons.DPadDown) bits |= Xbox360ButtonBits.DPadDown;
        if (buttons.DPadLeft) bits |= Xbox360ButtonBits.DPadLeft;
        if (buttons.DPadRight) bits |= Xbox360ButtonBits.DPadRight;
        if (buttons.Start) bits |= Xbox360ButtonBits.Start;
        if (buttons.Back) bits |= Xbox360ButtonBits.Back;
        if (buttons.LeftStickClick) bits |= Xbox360ButtonBits.LeftThumb;
        if (buttons.RightStickClick) bits |= Xbox360ButtonBits.RightThumb;
        if (buttons.LeftBumper) bits |= Xbox360ButtonBits.LeftShoulder;
        if (buttons.RightBumper) bits |= Xbox360ButtonBits.RightShoulder;
        if (buttons.A) bits |= Xbox360ButtonBits.A;
        if (buttons.B) bits |= Xbox360ButtonBits.B;
        if (buttons.X) bits |= Xbox360ButtonBits.X;
        if (buttons.Y) bits |= Xbox360ButtonBits.Y;

        return new Xbox360DeviceState
        {
            Buttons = bits,

            LT = state.Triggers.Left,
            RT = state.Triggers.Right,

            // Preserve the already-normalized XInput-convention Y directly -- see remarks above.
            // Do not negate or otherwise transform LY/RY here.
            LX = state.LeftStick.X,
            LY = state.LeftStick.Y,
            RX = state.RightStick.X,
            RY = state.RightStick.Y,

            // Reserved0..Reserved5 are left unassigned: the struct's default is already all
            // zeros, and there is no source data to derive them from.
        };
    }
}
