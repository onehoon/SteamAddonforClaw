using SteamInputAddonforClaw.Devices.MSI.Claw;
using SteamInputAddonforClaw.Input;

namespace SteamInputAddonforClaw.VirtualOutput.Viiper;

/// <summary>Maps the normalized Claw gamepad and motion snapshot to VIIPER's DualSense state.</summary>
internal static class DualSenseEdgeDeviceStateMapper
{
    private const uint Square = 0x10;
    private const uint Cross = 0x20;
    private const uint Circle = 0x40;
    private const uint Triangle = 0x80;
    private const uint L1 = 0x100;
    private const uint R1 = 0x200;
    private const uint L2 = 0x400;
    private const uint R2 = 0x800;
    private const uint Create = 0x1000;
    private const uint Options = 0x2000;
    private const uint L3 = 0x4000;
    private const uint R3 = 0x8000;
    private const uint L4 = 0x00400000;
    private const uint R4 = 0x00800000;

    internal static DSDeviceState Map(
        ControllerState state,
        bool suppressM1,
        bool suppressM2,
        MsiClawMotionState? motion,
        long nowTicks,
        long timestampFrequency)
    {
        var buttons = state.Buttons;
        uint buttonMask = 0;
        if (buttons.X) buttonMask |= Square;
        if (buttons.A) buttonMask |= Cross;
        if (buttons.B) buttonMask |= Circle;
        if (buttons.Y) buttonMask |= Triangle;
        if (buttons.LeftBumper) buttonMask |= L1;
        if (buttons.RightBumper) buttonMask |= R1;
        if (buttons.LeftTriggerFull) buttonMask |= L2;
        if (buttons.RightTriggerFull) buttonMask |= R2;
        if (buttons.Start) buttonMask |= Options;
        if (buttons.Back) buttonMask |= Create;
        if (buttons.LeftStickClick) buttonMask |= L3;
        if (buttons.RightStickClick) buttonMask |= R3;
        if (IsPressed(state.Auxiliary, AuxiliaryButtonSlot.LeftRear) && !suppressM2) buttonMask |= L4;
        if (IsPressed(state.Auxiliary, AuxiliaryButtonSlot.RightRear) && !suppressM1) buttonMask |= R4;

        byte dpad = 0;
        if (buttons.DPadUp) dpad |= 0x1;
        if (buttons.DPadDown) dpad |= 0x2;
        if (buttons.DPadLeft) dpad |= 0x4;
        if (buttons.DPadRight) dpad |= 0x8;

        var result = new DSDeviceState
        {
            LX = ToSByte(state.LeftStick.X),
            // Claw reports physical UP as positive Y; Sony encodes UP as HID 0 and DOWN as 255.
            LY = ToSByte(state.LeftStick.Y, invert: true),
            RX = ToSByte(state.RightStick.X),
            RY = ToSByte(state.RightStick.Y, invert: true),
            Buttons = buttonMask,
            DPad = dpad,
            L2 = state.Triggers.Left,
            R2 = state.Triggers.Right,
            // Touch, system, mute, and Edge function-key inputs are intentionally neutral in PR-A.
            Touch1X = 0,
            Touch1Y = 0,
            Touch1Active = 0,
            Touch2X = 0,
            Touch2Y = 0,
            Touch2Active = 0,
            GyroX = 0,
            GyroY = 0,
            GyroZ = 0,
            AccelX = 0,
            AccelY = 0,
            AccelZ = 0
        };

        if (motion is null || timestampFrequency <= 0) return result;
        var fresh = motion.WithFreshness(nowTicks, timestampFrequency);
        if (!fresh.IsUsableForSteamDeckImu ||
            !double.IsFinite(fresh.GyroXDegPerSecond) ||
            !double.IsFinite(fresh.GyroYDegPerSecond) ||
            !double.IsFinite(fresh.GyroZDegPerSecond) ||
            !double.IsFinite(fresh.AccelXG) ||
            !double.IsFinite(fresh.AccelYG) ||
            !double.IsFinite(fresh.AccelZG))
            return result;

        // Initial PR-A reference mapping is direct: normalized source +X/+Y/+Z maps to Edge
        // +X/+Y/+Z for both sensors. Physical reference-pose verification remains a hardware check.
        result.GyroX = EncodeI16(fresh.GyroXDegPerSecond, 16.384);
        result.GyroY = EncodeI16(fresh.GyroYDegPerSecond, 16.384);
        result.GyroZ = EncodeI16(fresh.GyroZDegPerSecond, 16.384);
        result.AccelX = EncodeI16(fresh.AccelXG, 8192);
        result.AccelY = EncodeI16(fresh.AccelYG, 8192);
        result.AccelZ = EncodeI16(fresh.AccelZG, 8192);
        return result;
    }

    private static sbyte ToSByte(short value, bool invert = false)
    {
        var signed = invert ? -(int)value : (int)value;
        var rounded = Math.Round(signed / 256d, MidpointRounding.AwayFromZero);
        return (sbyte)Math.Clamp(rounded, sbyte.MinValue, sbyte.MaxValue);
    }

    private static short EncodeI16(double value, double countsPerUnit)
    {
        var counts = value * countsPerUnit;
        if (double.IsPositiveInfinity(counts)) return short.MaxValue;
        if (double.IsNegativeInfinity(counts)) return short.MinValue;
        var rounded = Math.Round(counts, MidpointRounding.AwayFromZero);
        return (short)Math.Clamp(rounded, (double)short.MinValue, (double)short.MaxValue);
    }

    private static bool IsPressed(AuxiliaryButtonState state, AuxiliaryButtonSlot slot)
    {
        var index = (int)slot;
        return index >= 0 && index < state.Count && state[index];
    }
}
