namespace SteamInputAddonforClaw.Devices.MSI.Claw;

internal static class MsiClawVibrationProfileCommand
{
    internal const int ReportLength = 64;

    internal static byte[] BuildMotorPairWrite(int leftPercent, int rightPercent)
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
}
