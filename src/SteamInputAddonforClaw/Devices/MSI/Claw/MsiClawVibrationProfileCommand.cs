namespace SteamInputAddonforClaw.Devices.MSI.Claw;

internal readonly record struct MsiClawVibrationProfileDiagnosticResponse(
    byte ResponseIndex,
    bool IndexEchoMatched,
    int CandidateLeft,
    int CandidateRight);

internal static class MsiClawVibrationProfileCommand
{
    internal const ushort LeftMotorAddress = 0x0022;
    internal const ushort RightMotorAddress = 0x0023;
    internal const int ReportLength = 64;

    internal static byte[] BuildReadProfile(ushort address)
    {
        ValidateAddress(address);
        return BuildReadProfileRequest(profileIndex: 0x01, address, length: 0x01);
    }

    internal static byte[] BuildDiagnosticReadProfile(byte profileIndex, ushort address, byte length)
    {
        if (profileIndex is not 0x00 and not 0x01)
            throw new ArgumentOutOfRangeException(nameof(profileIndex));
        if (address != LeftMotorAddress)
            throw new ArgumentOutOfRangeException(nameof(address));
        if (length != 0x02)
            throw new ArgumentOutOfRangeException(nameof(length));

        return BuildReadProfileRequest(profileIndex, address, length);
    }

    private static byte[] BuildReadProfileRequest(byte profileIndex, ushort address, byte length)
    {
        var report = new byte[ReportLength];
        report[0] = 0x0F;
        report[3] = 0x3C;
        report[4] = 0x04;
        report[5] = profileIndex;
        report[6] = (byte)(address >> 8);
        report[7] = (byte)address;
        report[8] = length;
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

    internal static byte[] BuildDiagnosticMotorPairWrite(int leftPercent, int rightPercent)
    {
        if (leftPercent is < 0 or > 100)
            throw new ArgumentOutOfRangeException(nameof(leftPercent));
        if (rightPercent is < 0 or > 100)
            throw new ArgumentOutOfRangeException(nameof(rightPercent));

        var report = new byte[ReportLength];
        report[0] = 0x0F;
        report[3] = 0x3C;
        report[4] = 0x21;
        report[5] = 0x01;
        report[6] = 0x00;
        report[7] = 0x22;
        report[8] = 0x02;
        report[9] = (byte)leftPercent;
        report[10] = (byte)rightPercent;
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

    internal static bool TryParseDiagnosticReadProfileResponse(
        ReadOnlySpan<byte> report,
        byte requestedProfileIndex,
        ushort requestedAddress,
        byte requestedLength,
        out MsiClawVibrationProfileDiagnosticResponse response)
    {
        response = default;
        if (requestedProfileIndex is not 0x00 and not 0x01
            || requestedAddress != LeftMotorAddress
            || requestedLength != 0x02
            || report.Length != ReportLength
            || report[0] != 0x10
            || report[1] != 0x00
            || report[2] != 0x00
            || report[3] != 0x3C
            || report[4] != 0x05
            || report[6] != (byte)(requestedAddress >> 8)
            || report[7] != (byte)requestedAddress
            || report[8] != requestedLength
            || report[9] > 100
            || report[10] > 100)
            return false;

        response = new(
            report[5],
            report[5] == requestedProfileIndex,
            report[9],
            report[10]);
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
