using System.Buffers.Binary;

namespace SteamInputAddonforClaw.VirtualOutput.Viiper;

internal readonly record struct ClassicSteamControllerInput(bool LeftGrip, bool RightGrip);

internal static class ClassicSteamControllerReportBuilder
{
    internal const int ReportLength = 64;
    internal const byte ReportId = 0x01;
    internal const byte PayloadLength = 0x3C;
    internal const byte LeftGripMask = 0x80;
    internal const byte RightGripMask = 0x01;
    internal const ushort DefaultQuaternionW = 0x4000;
    internal const ushort DefaultBatteryMilliVolts = 3000;

    internal static void Write(Span<byte> report, uint frame, ClassicSteamControllerInput input)
    {
        if (report.Length != ReportLength) throw new ArgumentException("A Classic Steam Controller report must be exactly 64 bytes.", nameof(report));

        report.Clear();
        report[0] = ReportId;
        report[1] = 0x00;
        report[2] = ReportId;
        report[3] = PayloadLength;
        BinaryPrimitives.WriteUInt32LittleEndian(report[4..8], frame);
        if (input.LeftGrip) report[9] |= LeftGripMask;
        if (input.RightGrip) report[10] |= RightGripMask;
        BinaryPrimitives.WriteUInt16LittleEndian(report[40..42], DefaultQuaternionW);
        BinaryPrimitives.WriteUInt16LittleEndian(report[62..64], DefaultBatteryMilliVolts);
    }
}
