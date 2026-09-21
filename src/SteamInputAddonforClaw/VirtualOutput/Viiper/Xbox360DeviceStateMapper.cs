using SteamInputAddonforClaw.Contracts.BackButtons;
using SteamInputAddonforClaw.Input;

namespace SteamInputAddonforClaw.VirtualOutput.Viiper;

/// <summary>
/// Maps the Addon's physical <see cref="ControllerState"/> into the canonical VIIPER
/// <see cref="Xbox360DeviceState"/> (VIIPER main@e10b5f02945b1322f33c33468e583546600ba000).
/// </summary>
/// <remarks>
/// The mapper is consumed by the production Xbox360 publisher during temporary Game Bar
/// presentation. Xbox360 remains detached/unpublished when that presentation is inactive -- see
/// docs/VIIPER_MIGRATION_TODO.md for the current SD7 contract.
///
/// <para>
/// <see cref="ControllerState"/> is already normalized into the XInput semantic convention (up =
/// positive Y, down = negative Y) -- the same convention HHC, ClawTweaks, and DS4Windows all
/// converge on before their respective X360 output boundary. This mapper therefore preserves
/// <c>LeftStick.Y</c>/<c>RightStick.Y</c> directly; it must never negate or otherwise transform
/// them. Doing so would apply a second, incorrect Y-axis inversion.
/// </para>
///
/// MSI-specific auxiliary controls (M1/M2) are projected only when the caller supplies an explicit
/// Xbox360 back-button mapping. The default overload keeps them disabled. Guide can be set by the
/// explicit XboxGuide target, but no timing or native Guide action is synthesized here.
/// </remarks>
internal static class Xbox360DeviceStateMapper
{
    internal static Xbox360DeviceState Map(ControllerState state)
        => Map(state, BackButtonMappingSettings.Default);

    internal static Xbox360DeviceState Map(
        ControllerState state,
        BackButtonMappingSettings mapping)
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

        var leftTrigger = state.Triggers.Left;
        var rightTrigger = state.Triggers.Right;
        ApplyBackButtonTarget(mapping.M1, IsPressed(state.Auxiliary, AuxiliaryButtonSlot.RightRear), ref bits, ref leftTrigger, ref rightTrigger);
        ApplyBackButtonTarget(mapping.M2, IsPressed(state.Auxiliary, AuxiliaryButtonSlot.LeftRear), ref bits, ref leftTrigger, ref rightTrigger);

        return new Xbox360DeviceState
        {
            Buttons = bits,

            LT = leftTrigger,
            RT = rightTrigger,

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

    private static bool IsPressed(AuxiliaryButtonState state, AuxiliaryButtonSlot slot)
    {
        var index = (int)slot;
        return index >= 0 && index < state.Count && state[index];
    }

    private static void ApplyBackButtonTarget(
        Xbox360BackButtonTarget target,
        bool pressed,
        ref uint bits,
        ref byte leftTrigger,
        ref byte rightTrigger)
    {
        if (!pressed) return;

        switch (target)
        {
            case Xbox360BackButtonTarget.A: bits |= Xbox360ButtonBits.A; break;
            case Xbox360BackButtonTarget.B: bits |= Xbox360ButtonBits.B; break;
            case Xbox360BackButtonTarget.X: bits |= Xbox360ButtonBits.X; break;
            case Xbox360BackButtonTarget.Y: bits |= Xbox360ButtonBits.Y; break;
            case Xbox360BackButtonTarget.DPadUp: bits |= Xbox360ButtonBits.DPadUp; break;
            case Xbox360BackButtonTarget.DPadRight: bits |= Xbox360ButtonBits.DPadRight; break;
            case Xbox360BackButtonTarget.DPadDown: bits |= Xbox360ButtonBits.DPadDown; break;
            case Xbox360BackButtonTarget.DPadLeft: bits |= Xbox360ButtonBits.DPadLeft; break;
            case Xbox360BackButtonTarget.LeftBumper: bits |= Xbox360ButtonBits.LeftShoulder; break;
            case Xbox360BackButtonTarget.RightBumper: bits |= Xbox360ButtonBits.RightShoulder; break;
            case Xbox360BackButtonTarget.LeftTrigger: leftTrigger = byte.MaxValue; break;
            case Xbox360BackButtonTarget.RightTrigger: rightTrigger = byte.MaxValue; break;
            case Xbox360BackButtonTarget.LeftStickClick: bits |= Xbox360ButtonBits.LeftThumb; break;
            case Xbox360BackButtonTarget.RightStickClick: bits |= Xbox360ButtonBits.RightThumb; break;
            case Xbox360BackButtonTarget.View: bits |= Xbox360ButtonBits.Back; break;
            case Xbox360BackButtonTarget.Menu: bits |= Xbox360ButtonBits.Start; break;
            case Xbox360BackButtonTarget.XboxGuide: bits |= Xbox360ButtonBits.Guide; break;
            case Xbox360BackButtonTarget.Disabled:
            default:
                // Persisted/frontend validation owns user-input validation. The output boundary
                // remains fail-closed for a future or corrupted enum value.
                break;
        }
    }
}
