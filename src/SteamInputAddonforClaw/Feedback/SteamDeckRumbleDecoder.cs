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
    byte? Strength8 = null)
{
    internal bool IsSupported => Command is SteamDeckFeedbackCommand.Rumble or SteamDeckFeedbackCommand.Haptic or SteamDeckFeedbackCommand.HapticPulse;
}

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
            var left = (ushort)(report[5] | report[6] << 8);
            var right = (ushort)(report[7] | report[8] << 8);
            return new(SteamDeckFeedbackCommand.Rumble, new TwoMotorRumble(left, right));
        }

        if (opcode == HapticOpcode)
        {
            if (report.Length < 6) return new(SteamDeckFeedbackCommand.Malformed, TwoMotorRumble.Stopped);
            var intensity = report[4];
            var gain = (sbyte)report[5];
            var strength8 = (byte)Math.Clamp(intensity + gain * 8, 0, 255);
            var strength16 = (ushort)(strength8 * 257);
            return new(SteamDeckFeedbackCommand.Haptic, new TwoMotorRumble(strength16, strength16), null, intensity, gain, Strength8: strength8);
        }

        if (opcode == HapticPulseOpcode)
        {
            if (report.Length < 10) return new(SteamDeckFeedbackCommand.Malformed, TwoMotorRumble.Stopped);
            var period = (ushort)(report[5] | report[6] << 8);
            var count = (ushort)(report[7] | report[8] << 8);
            var gain = report[9];
            var strength8 = (byte)Math.Min(255, count * 16 + gain);
            var duration = Math.Max(1, (int)Math.Ceiling(period * count / 1000.0));
            var strength16 = (ushort)(strength8 * 257);
            return new(SteamDeckFeedbackCommand.HapticPulse, new TwoMotorRumble(strength16, strength16), duration, Gain: gain, PulsePeriod: period, PulseCount: count, Strength8: strength8);
        }

        return new(opcode is 0xEA or 0x8F or 0xB6 or 0xB7 or 0xB8 or 0xB9 ?
            SteamDeckFeedbackCommand.Unsupported : SteamDeckFeedbackCommand.Unknown, TwoMotorRumble.Stopped);
    }
}
