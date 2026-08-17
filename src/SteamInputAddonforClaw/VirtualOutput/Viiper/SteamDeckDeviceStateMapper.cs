using SteamInputAddonforClaw.Input;

namespace SteamInputAddonforClaw.VirtualOutput.Viiper;

/// <summary>
/// Maps the Addon's physical <see cref="ControllerState"/> into the canonical VIIPER
/// <see cref="SteamDeckDeviceState"/> from the canonical typed VIIPER ABI.
/// </summary>
/// <remarks>
/// Steam Deck has native right-stick and R3 fields, so this mapper writes them directly (RightStick
/// -> RStickX/Y, R3 -> native R3) rather than substituting them into trackpad fields. Trackpad,
/// IMU/quaternion, L5/R5, Steam, and QuickAccess fields are intentionally left neutral until their
/// separate feature tracks are implemented and hardware-validated -- see
/// docs/VIIPER_MIGRATION_TODO.md SD5/SD6.
/// </remarks>
internal static class SteamDeckDeviceStateMapper
{
    // The current HHC SteamDeckTarget reference scales its 0..255 trigger source to
    // 0..Int16.MaxValue (32767) for the VIIPER Steam Deck target (see docs/Reference Research_Steam
    // Deck VIIPER SteamOutput Input Reports.txt section 7). LTrigger/RTrigger are declared ushort in
    // the canonical ABI, so this mapper reuses that same 0..32767 target range; TriggerScalingTests
    // pins the exact conversion.
    internal const ushort MaxAnalogTrigger = (ushort)short.MaxValue;

    internal static SteamDeckDeviceState Map(ControllerState state)
    {
        var buttons = state.Buttons;

        return new SteamDeckDeviceState
        {
            A = ToByte(buttons.A),
            X = ToByte(buttons.X),
            B = ToByte(buttons.B),
            Y = ToByte(buttons.Y),

            L1 = ToByte(buttons.LeftBumper),
            R1 = ToByte(buttons.RightBumper),

            // Digital full-pull remains independent of analog travel -- do not derive this from
            // analog saturation. See docs/VIIPER_INTEGRATION.md section 6 and
            // VIIPER_MIGRATION_TODO.md SD2.
            L2Digital = ToByte(buttons.LeftTriggerFull),
            R2Digital = ToByte(buttons.RightTriggerFull),

            DPadDown = ToByte(buttons.DPadDown),
            DPadLeft = ToByte(buttons.DPadLeft),
            DPadRight = ToByte(buttons.DPadRight),
            DPadUp = ToByte(buttons.DPadUp),

            L3 = ToByte(buttons.LeftStickClick),
            R3 = ToByte(buttons.RightStickClick),

            // Steam Deck Menu = MENU / Start
            // Steam Deck Options = VIEW / Back
            Menu = ToByte(buttons.Start),
            Options = ToByte(buttons.Back),

            // M1 = right rear -> R4, M2 = left rear -> L4 (see AuxiliaryButtonSlot).
            R4 = ToByte(state.Auxiliary[(int)AuxiliaryButtonSlot.RightRear]),
            L4 = ToByte(state.Auxiliary[(int)AuxiliaryButtonSlot.LeftRear]),

            LTrigger = ScaleTrigger(state.Triggers.Left),
            RTrigger = ScaleTrigger(state.Triggers.Right),

            LStickX = state.LeftStick.X,
            LStickY = state.LeftStick.Y,
            RStickX = state.RightStick.X,
            RStickY = state.RightStick.Y,

            // Neutral for the first SD2 smoke test: L5/R5, Steam, QuickAccess, all trackpad
            // touch/press/axes/force, stick touch/force, IMU, and quaternion. Left as struct
            // defaults (0/false) below -- listed here for reviewability, not because they need an
            // explicit assignment.
            L5 = 0,
            R5 = 0,
            Steam = 0,
            QuickAccess = 0,
            RPadTouch = 0,
            LPadTouch = 0,
            RPadPress = 0,
            LPadPress = 0,
            RStickTouch = 0,
            LStickTouch = 0,
            LPadX = 0,
            LPadY = 0,
            RPadX = 0,
            RPadY = 0,
            LPadForce = 0,
            RPadForce = 0,
            LStickForce = 0,
            RStickForce = 0,
            AccelX = 0,
            AccelY = 0,
            AccelZ = 0,
            Pitch = 0,
            Yaw = 0,
            Roll = 0,
            GyroQuatW = 0,
            GyroQuatX = 0,
            GyroQuatY = 0,
            GyroQuatZ = 0,
        };
    }

    private static byte ToByte(bool value) => value ? (byte)1 : (byte)0;

    private static ushort ScaleTrigger(byte value) => (ushort)(value * MaxAnalogTrigger / byte.MaxValue);
}
