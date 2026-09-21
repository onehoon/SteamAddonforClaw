namespace SteamInputAddonforClaw.Devices.MSI.Claw;

internal static class MsiClawModeCommand
{
    internal const byte SwitchMode = 0x24;
    internal const byte ReadGamepadMode = 0x26;
    internal const byte GamepadModeAck = 0x27;
    internal const byte OutboundReportId = 0x0F;
    internal const byte InboundReportId = 0x10;
    internal const byte CommandMarker = 0x3C;

    internal static byte[] Build(MsiClawNativeMode mode) => BuildSwitch(mode switch
    {
        MsiClawNativeMode.XInput => MsiClawGamepadMode.XInput,
        MsiClawNativeMode.DirectInput => MsiClawGamepadMode.DirectInput,
        _ => throw new ArgumentOutOfRangeException(nameof(mode)),
    });

    internal static byte[] BuildSwitch(MsiClawGamepadMode mode)
    {
        Validate(mode);
        var report = new byte[64];
        report[0] = OutboundReportId;
        report[3] = CommandMarker;
        report[4] = SwitchMode;
        report[5] = (byte)mode;
        return report;
    }

    internal static byte[] BuildReadGamepadMode()
    {
        var report = new byte[64];
        report[0] = OutboundReportId;
        report[3] = CommandMarker;
        report[4] = ReadGamepadMode;
        return report;
    }

    internal static bool TryParseGamepadModeAck(ReadOnlySpan<byte> report, out MsiClawGamepadMode mode)
    {
        mode = default;
        if (report.Length < 6 || report[0] != InboundReportId || report[3] != CommandMarker || report[4] != GamepadModeAck)
            return false;
        var value = report[5];
        if (value > (byte)MsiClawGamepadMode.Testing)
            return false;
        mode = (MsiClawGamepadMode)value;
        return true;
    }

    private static void Validate(MsiClawGamepadMode mode)
    {
        if (mode is < MsiClawGamepadMode.Offline or > MsiClawGamepadMode.Testing)
            throw new ArgumentOutOfRangeException(nameof(mode));
    }
}
