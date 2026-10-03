namespace SteamInputAddonforClaw.Devices.MSI.Claw;

internal static class MsiClawVibrationProfileCommand
{
    internal const ushort LeftMotorAddress = 0x0022;
    internal const ushort RightMotorAddress = 0x0023;
    internal const int ReportLength = 64;

    internal static byte[] BuildReadProfile(ushort address)
    {
        ValidateAddress(address);
        var report = new byte[ReportLength];
        report[0] = 0x0F;
        report[3] = 0x3C;
        report[4] = 0x04;
        report[5] = 0x01;
        report[6] = (byte)(address >> 8);
        report[7] = (byte)address;
        report[8] = 0x01;
        return report;
    }

    internal static byte[] BuildWriteProfile(ushort address, int percent)
    {
        ValidateAddress(address);
        if (percent is < 0 or > 100)
            throw new ArgumentOutOfRangeException(nameof(percent));

        var report = new byte[ReportLength];
        report[0] = 0x0F;
        report[3] = 0x3C;
        report[4] = 0x21;
        report[5] = 0x01;
        report[6] = (byte)(address >> 8);
        report[7] = (byte)address;
        report[8] = 0x01;
        report[9] = (byte)percent;
        return report;
    }

    internal static byte[] BuildSyncToRom()
    {
        var report = new byte[ReportLength];
        report[0] = 0x0F;
        report[3] = 0x3C;
        report[4] = 0x22;
        return report;
    }

    internal static bool TryParseReadProfileResponse(
        ReadOnlySpan<byte> report,
        ushort requestedAddress,
        out int percent)
    {
        percent = default;
        if (!IsSupportedAddress(requestedAddress)
            || report.Length != ReportLength
            || report[0] != 0x10
            || report[1] != 0x00
            || report[2] != 0x00
            || report[3] != 0x3C
            || report[4] != 0x05
            || report[5] != 0x01
            || report[6] != (byte)(requestedAddress >> 8)
            || report[7] != (byte)requestedAddress
            || report[8] != 0x01
            || report[9] > 100)
            return false;

        percent = report[9];
        return true;
    }

    private static void ValidateAddress(ushort address)
    {
        if (!IsSupportedAddress(address))
            throw new ArgumentOutOfRangeException(nameof(address));
    }

    private static bool IsSupportedAddress(ushort address) =>
        address is LeftMotorAddress or RightMotorAddress;
}
