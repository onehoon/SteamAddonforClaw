using SteamInputAddonforClaw.Devices.MSI.Claw;
using SteamInputAddonforClaw.Input;

namespace SteamInputAddonforClaw.VirtualOutput.Viiper;

/// <summary>
/// Maps the Addon's physical <see cref="ControllerState"/> into the canonical VIIPER
/// <see cref="SteamDeckDeviceState"/> from the canonical typed VIIPER ABI.
/// </summary>
/// <remarks>
/// Steam Deck has native right-stick and R3 fields, so this mapper writes them directly (RightStick
/// -> RStickX/Y, R3 -> native R3) rather than substituting them into trackpad fields. Trackpad,
/// quaternion, L5/R5, Steam, and QuickAccess fields remain neutral. Fresh PR1 motion snapshots may
/// populate the Steam Deck IMU fields; invalid or unavailable snapshots remain neutral.
/// </remarks>
internal static class SteamDeckDeviceStateMapper
{
    // The current HHC SteamDeckTarget reference scales its 0..255 trigger source to
    // 0..Int16.MaxValue (32767) for the VIIPER Steam Deck target (see docs/Reference Research_Steam
    // Deck VIIPER SteamOutput Input Reports.txt section 7). LTrigger/RTrigger are declared ushort in
    // the canonical ABI, so this mapper reuses that same 0..32767 target range; TriggerScalingTests
    // pins the exact conversion.
    internal const ushort MaxAnalogTrigger = (ushort)short.MaxValue;

    internal static SteamDeckDeviceState Map(
        ControllerState state,
        bool suppressM1 = false,
        bool suppressM2 = false,
        MsiClawMotionState? motion = null)
    {
        var buttons = state.Buttons;

        var result = new SteamDeckDeviceState
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
            R4 = ToByte(IsPressed(state.Auxiliary, AuxiliaryButtonSlot.RightRear) && !suppressM1),
            L4 = ToByte(IsPressed(state.Auxiliary, AuxiliaryButtonSlot.LeftRear) && !suppressM2),

            LTrigger = ScaleTrigger(state.Triggers.Left),
            RTrigger = ScaleTrigger(state.Triggers.Right),

            LStickX = state.LeftStick.X,
            LStickY = state.LeftStick.Y,
            RStickX = state.RightStick.X,
            RStickY = state.RightStick.Y,

            // L5/R5, Steam, QuickAccess, trackpad touch/press/axes/force, stick touch/force, and
            // quaternion remain neutral. IMU is filled below only from a complete valid snapshot.
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
            GyroQuatW = 0,
            GyroQuatX = 0,
            GyroQuatY = 0,
            GyroQuatZ = 0,
        };

        if (IsUsableMotion(motion))
        {
            result.Pitch = EncodeI16(motion!.GyroXDegPerSecond, 16);
            result.Yaw = EncodeI16(-motion.GyroZDegPerSecond, 16);
            result.Roll = EncodeI16(motion.GyroYDegPerSecond, 16);
            result.AccelX = EncodeI16(motion.AccelXG, 16384);
            result.AccelY = EncodeI16(-motion.AccelZG, 16384);
            result.AccelZ = EncodeI16(motion.AccelYG, 16384);
        }

        return result;
    }

    private static bool IsUsableMotion(MsiClawMotionState? motion) =>
        motion is { IsUsableForSteamDeckImu: true }
        && double.IsFinite(motion.GyroXDegPerSecond)
        && double.IsFinite(motion.GyroYDegPerSecond)
        && double.IsFinite(motion.GyroZDegPerSecond)
        && double.IsFinite(motion.AccelXG)
        && double.IsFinite(motion.AccelYG)
        && double.IsFinite(motion.AccelZG);

    private static short EncodeI16(double value, double countsPerUnit)
    {
        if (!double.IsFinite(value)) return 0;

        var counts = value * countsPerUnit;
        if (double.IsPositiveInfinity(counts)) return short.MaxValue;
        if (double.IsNegativeInfinity(counts)) return short.MinValue;

        var rounded = Math.Round(counts, MidpointRounding.AwayFromZero);
        return (short)Math.Clamp(rounded, (double)short.MinValue, (double)short.MaxValue);
    }

    private static byte ToByte(bool value) => value ? (byte)1 : (byte)0;

    private static bool IsPressed(AuxiliaryButtonState state, AuxiliaryButtonSlot slot)
    {
        var index = (int)slot;
        return index >= 0 && index < state.Count && state[index];
    }

    private static ushort ScaleTrigger(byte value) => (ushort)(value * MaxAnalogTrigger / byte.MaxValue);
}
