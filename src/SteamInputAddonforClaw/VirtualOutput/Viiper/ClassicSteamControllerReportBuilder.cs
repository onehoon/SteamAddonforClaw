using System.Buffers.Binary;
using SteamInputAddonforClaw.Input;

namespace SteamInputAddonforClaw.VirtualOutput.Viiper;

internal readonly record struct ClassicSteamControllerInput(GamepadButtons Buttons, StickState LeftStick, StickState RightPad, TriggerState Triggers, bool LeftGrip, bool RightGrip)
{
    internal ClassicSteamControllerInput(bool leftGrip, bool rightGrip) : this(default, default, default, default, leftGrip, rightGrip) { }
}

internal static class ClassicSteamControllerInputMapper
{
    internal static ClassicSteamControllerInput Map(ControllerState state) => new(state.Buttons, state.LeftStick, state.RightStick, state.Triggers, state.Auxiliary[1], state.Auxiliary[0]);
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
        var active = input.RightPad != default || b.RightStickClick; report[10] = (byte)((input.RightGrip ? 1 : 0) | (b.RightStickClick ? 4 : 0) | (active ? 0x10 : 0) | (b.LeftStickClick ? 0x40 : 0));
        report[11] = input.Triggers.Left; report[12] = input.Triggers.Right; WriteStick(report[16..20], input.LeftStick); WriteStick(report[20..24], input.RightPad); WriteRawTrigger(report[24..26], input.Triggers.Left); WriteRawTrigger(report[26..28], input.Triggers.Right); WriteStick(report[54..58], input.LeftStick); BinaryPrimitives.WriteUInt16LittleEndian(report[40..42], 0x4000); BinaryPrimitives.WriteUInt16LittleEndian(report[62..64], 3000);
    }
    private static void WriteStick(Span<byte> target, StickState value) { BinaryPrimitives.WriteInt16LittleEndian(target, value.X); BinaryPrimitives.WriteInt16LittleEndian(target[2..], value.Y); }
    private static void WriteRawTrigger(Span<byte> target, byte value) => BinaryPrimitives.WriteInt16LittleEndian(target, (short)(value * 26000 / 255));
}
