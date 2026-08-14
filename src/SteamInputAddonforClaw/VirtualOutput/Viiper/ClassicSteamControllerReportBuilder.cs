using System.Buffers.Binary;
using SteamInputAddonforClaw.Input;

namespace SteamInputAddonforClaw.VirtualOutput.Viiper;

internal readonly record struct ClassicSteamControllerInput(GamepadButtons Buttons, StickState LeftStick, StickState RightPad, TriggerState Triggers, bool LeftGrip, bool RightGrip, bool RightPadPress, bool RightPadTouch)
{
    internal ClassicSteamControllerInput(bool leftGrip, bool rightGrip) : this(default, default, default, default, leftGrip, rightGrip, default, default) { }
}

internal static class ClassicSteamControllerRightPadPolicy
{
    internal const short TouchThreshold = 500;

    internal static bool IsTouched(StickState pad, bool press) =>
        press || Math.Abs((int)pad.X) > TouchThreshold || Math.Abs((int)pad.Y) > TouchThreshold;
}

internal static class ClassicSteamControllerInputMapper
{
    internal static ClassicSteamControllerInput Map(ControllerState state)
    {
        var rightPad = state.RightStick;
        var press = state.Buttons.RightStickClick;
        var touch = ClassicSteamControllerRightPadPolicy.IsTouched(rightPad, press);
        return new(state.Buttons, state.LeftStick, rightPad, state.Triggers, state.Auxiliary[(int)AuxiliaryButtonSlot.LeftRear], state.Auxiliary[(int)AuxiliaryButtonSlot.RightRear], press, touch);
    }
}

internal static class ClassicSteamControllerReportBuilder
{
    internal const int ReportLength = 64;
    internal static void Write(Span<byte> report, uint frame, ClassicSteamControllerInput input)
    {
        if (report.Length != ReportLength) throw new ArgumentException("A Classic Steam Controller report must be exactly 64 bytes.", nameof(report));
        report.Clear(); report[0] = 1; report[2] = 1; report[3] = 0x3C; BinaryPrimitives.WriteUInt32LittleEndian(report[4..8], frame);
        var b = input.Buttons; report[8] = (byte)((b.A ? 0x80 : 0) | (b.X ? 0x40 : 0) | (b.B ? 0x20 : 0) | (b.Y ? 0x10 : 0) | (b.LeftBumper ? 8 : 0) | (b.RightBumper ? 4 : 0) | (b.LeftTriggerFull ? 2 : 0) | (b.RightTriggerFull ? 1 : 0));
        report[9] = (byte)((b.DPadUp ? 1 : 0) | (b.DPadRight ? 2 : 0) | (b.DPadLeft ? 4 : 0) | (b.DPadDown ? 8 : 0) | (b.Back ? 0x10 : 0) | (b.Start ? 0x40 : 0) | (input.LeftGrip ? 0x80 : 0));
        report[10] = (byte)((input.RightGrip ? 1 : 0) | (input.RightPadPress ? 4 : 0) | (input.RightPadTouch ? 0x10 : 0) | (b.LeftStickClick ? 0x40 : 0));
        var leftRaw = EffectiveRawTrigger(input.Triggers.Left);
        var rightRaw = EffectiveRawTrigger(input.Triggers.Right);
        report[11] = RawTriggerToByte(leftRaw); report[12] = RawTriggerToByte(rightRaw); WriteStick(report[16..20], input.LeftStick); WriteStick(report[20..24], input.RightPad); WriteRawTrigger(report[24..26], leftRaw); WriteRawTrigger(report[26..28], rightRaw); WriteRawTrigger(report[50..52], leftRaw); WriteRawTrigger(report[52..54], rightRaw); WriteStick(report[54..58], input.LeftStick); BinaryPrimitives.WriteUInt16LittleEndian(report[40..42], 0x4000); BinaryPrimitives.WriteUInt16LittleEndian(report[62..64], 3000);
    }
    private static void WriteStick(Span<byte> target, StickState value) { BinaryPrimitives.WriteInt16LittleEndian(target, value.X); BinaryPrimitives.WriteInt16LittleEndian(target[2..], value.Y); }
    // Analog magnitude always comes from the trigger axis, never from the digital full-pull
    // button bit -- the MSI Claw's TriggerFull button can latch true while the analog axis is
    // still near the start of travel, and forcing 26000 here destroyed that travel.
    private static int EffectiveRawTrigger(byte value) => value * 26000 / 255;
    private static byte RawTriggerToByte(int raw) => (byte)Math.Clamp(raw * 255 / 26000, 0, 255);
    private static void WriteRawTrigger(Span<byte> target, int value) => BinaryPrimitives.WriteUInt16LittleEndian(target, (ushort)value);
}
