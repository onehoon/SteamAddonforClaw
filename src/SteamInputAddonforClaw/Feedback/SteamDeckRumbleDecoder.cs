using System.Buffers.Binary;

namespace SteamInputAddonforClaw.Feedback;

internal enum SteamDeckFeedbackCommand
{
    Rumble,
    Haptic,
    HapticPulse,
    Unsupported,
    Unknown,
    Malformed
}

internal readonly record struct SteamDeckFeedbackDecodeResult(
    SteamDeckFeedbackCommand Command,
    TwoMotorRumble Rumble,
    int? PulseDurationMilliseconds = null,
    byte? Intensity = null,
    int? Gain = null,
    ushort? PulsePeriod = null,
    ushort? PulseCount = null,
    byte? Strength8 = null,
    byte? RumbleType = null,
    ushort? RumbleIntensity = null,
    sbyte? RumbleLeftGain = null,
    sbyte? RumbleRightGain = null,
    SteamDeckHapticMetadata? Haptic = null)
{
    internal bool IsSupported => Command is SteamDeckFeedbackCommand.Rumble or SteamDeckFeedbackCommand.Haptic or SteamDeckFeedbackCommand.HapticPulse;
}

internal readonly record struct SteamDeckHapticMetadata(
    byte DeclaredPayloadLength,
    byte Side,
    byte CommandType,
    byte UiIntensity,
    sbyte DbGain,
    bool IsModernSdlLayout,
    ushort? Frequency = null,
    short? DurationMilliseconds = null,
    ushort? NoiseIntensity = null,
    ushort? LfoFrequency = null,
    byte? LfoDepth = null,
    byte? RandomToneGain = null,
    byte? ScriptId = null,
    ushort? SweepStartFrequency = null,
    ushort? SweepEndFrequency = null);

internal static class SteamDeckRumbleDecoder
{
    private const byte RumbleOpcode = 0xEB;
    private const byte HapticOpcode = 0xEA;
    private const byte HapticPulseOpcode = 0x8F;
    private const int MinimumReportLength = 11;

    internal static SteamDeckFeedbackDecodeResult Decode(ReadOnlySpan<byte> report)
    {
        if (report.Length < 1) return new(SteamDeckFeedbackCommand.Malformed, TwoMotorRumble.Stopped);
        var opcode = report[0];
        if (opcode == RumbleOpcode)
        {
            // VIIPER normalizes the optional leading report-id before callback delivery:
            // [0xEB, 9, rumbleType, intensity(2), left(2), right(2), leftGain, rightGain].
            if (report.Length < MinimumReportLength || report[1] != 9)
                return new(SteamDeckFeedbackCommand.Malformed, TwoMotorRumble.Stopped);
            var rumbleType = report[2];
            var intensity = (ushort)(report[3] | report[4] << 8);
            var left = (ushort)(report[5] | report[6] << 8);
            var right = (ushort)(report[7] | report[8] << 8);
            var leftGain = unchecked((sbyte)report[9]);
            var rightGain = unchecked((sbyte)report[10]);
            return new(SteamDeckFeedbackCommand.Rumble, new TwoMotorRumble(left, right),
                RumbleType: rumbleType,
                RumbleIntensity: intensity,
                RumbleLeftGain: leftGain,
                RumbleRightGain: rightGain);
        }

        if (opcode == HapticOpcode)
        {
            if (report.Length < 6) return new(SteamDeckFeedbackCommand.Malformed, TwoMotorRumble.Stopped);
            var declaredPayloadLength = report[1];
            if (declaredPayloadLength == 19 && report.Length < 21)
                return new(SteamDeckFeedbackCommand.Malformed, TwoMotorRumble.Stopped);
            var side = report[2];
            var commandType = report[3];
            var intensity = report[4];
            var gain = unchecked((sbyte)report[5]);
            var isModernSdlLayout = declaredPayloadLength == 19;
            var haptic = new SteamDeckHapticMetadata(
                declaredPayloadLength,
                side,
                commandType,
                intensity,
                gain,
                isModernSdlLayout,
                isModernSdlLayout ? BinaryPrimitives.ReadUInt16LittleEndian(report[6..8]) : null,
                isModernSdlLayout ? BinaryPrimitives.ReadInt16LittleEndian(report[8..10]) : null,
                isModernSdlLayout ? BinaryPrimitives.ReadUInt16LittleEndian(report[10..12]) : null,
                isModernSdlLayout ? BinaryPrimitives.ReadUInt16LittleEndian(report[12..14]) : null,
                isModernSdlLayout ? report[14] : null,
                isModernSdlLayout ? report[15] : null,
                isModernSdlLayout ? report[16] : null,
                isModernSdlLayout ? BinaryPrimitives.ReadUInt16LittleEndian(report[17..19]) : null,
                isModernSdlLayout ? BinaryPrimitives.ReadUInt16LittleEndian(report[19..21]) : null);
            // This is the existing MSI Claw translation heuristic, not Valve protocol strength.
            var strength8 = ComputeClawHapticFallbackStrength8(intensity, gain);
            var strength16 = (ushort)(strength8 * 257);
            return new(SteamDeckFeedbackCommand.Haptic, new TwoMotorRumble(strength16, strength16), null, intensity, gain, Strength8: strength8, Haptic: haptic);
        }

        if (opcode == HapticPulseOpcode)
        {
            if (report.Length < 10) return new(SteamDeckFeedbackCommand.Malformed, TwoMotorRumble.Stopped);
            var period = (ushort)(report[5] | report[6] << 8);
            var count = (ushort)(report[7] | report[8] << 8);
            var gain = report[9];
            var strength8 = (byte)Math.Min(255, count * 16 + gain);
            var duration = Math.Max(1, (int)Math.Ceiling(period * (long)count / 1000.0));
            var strength16 = (ushort)(strength8 * 257);
            return new(SteamDeckFeedbackCommand.HapticPulse, new TwoMotorRumble(strength16, strength16), duration, Gain: gain, PulsePeriod: period, PulseCount: count, Strength8: strength8);
        }

        return new(opcode is 0xEA or 0x8F or 0xB6 or 0xB7 or 0xB8 or 0xB9 ?
            SteamDeckFeedbackCommand.Unsupported : SteamDeckFeedbackCommand.Unknown, TwoMotorRumble.Stopped);
    }

    private static byte ComputeClawHapticFallbackStrength8(byte rawIntensity, sbyte rawGain) =>
        (byte)Math.Clamp(rawIntensity + rawGain * 8, 0, 255);
}
