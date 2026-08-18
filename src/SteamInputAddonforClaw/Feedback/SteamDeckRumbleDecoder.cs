namespace SteamInputAddonforClaw.Feedback;

internal enum SteamDeckFeedbackCommand
{
    Rumble,
    Unsupported,
    Unknown,
    Malformed
}

internal readonly record struct SteamDeckFeedbackDecodeResult(
    SteamDeckFeedbackCommand Command,
    TwoMotorRumble Rumble)
{
    internal bool IsSupported => Command == SteamDeckFeedbackCommand.Rumble;
}

internal static class SteamDeckRumbleDecoder
{
    private const byte RumbleOpcode = 0xEB;
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

        return new(opcode is 0xEA or 0x8F or 0xB6 or 0xB7 or 0xB8 or 0xB9 ?
            SteamDeckFeedbackCommand.Unsupported : SteamDeckFeedbackCommand.Unknown, TwoMotorRumble.Stopped);
    }
}
