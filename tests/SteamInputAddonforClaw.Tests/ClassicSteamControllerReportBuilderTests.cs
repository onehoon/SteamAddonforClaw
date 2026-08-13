using SteamInputAddonforClaw.Input;
using SteamInputAddonforClaw.VirtualOutput.Viiper;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class ClassicSteamControllerReportBuilderTests
{
    [Fact]
    public void Neutral_report_matches_the_pinned_VIIPER_wire_contract()
    {
        var report = new byte[64];
        ClassicSteamControllerReportBuilder.Write(report, 0, new(false, false));
        Assert.Equal(NeutralGolden, report);
    }

    [Theory]
    [InlineData(short.MinValue, (short)0)]
    [InlineData((short)0, short.MinValue)]
    [InlineData(short.MinValue, short.MinValue)]
    [InlineData(short.MaxValue, short.MaxValue)]
    public void FullDeflectionRightStickDoesNotThrow(short x, short y)
    {
        var report = new byte[64];
        var input = new ClassicSteamControllerInput(default, default, new StickState(x, y), default, false, false);

        var exception = Record.Exception(() => ClassicSteamControllerReportBuilder.Write(report, 0, input));

        Assert.Null(exception);
    }

    [Fact]
    public void RightStickAtShortMinValueIsReportedAsActive()
    {
        var report = new byte[64];
        var input = new ClassicSteamControllerInput(default, default, new StickState(short.MinValue, short.MinValue), default, false, false);

        ClassicSteamControllerReportBuilder.Write(report, 0, input);

        Assert.Equal(0x10, report[10] & 0x10);
    }

    [Theory]
    [MemberData(nameof(GripVectors))]
    public void Grip_reports_match_complete_golden_vectors(bool leftGrip, bool rightGrip, byte[] expected)
    {
        var report = new byte[64];
        ClassicSteamControllerReportBuilder.Write(report, 0x78563412, new(leftGrip, rightGrip));
        Assert.Equal(expected, report);
    }

    public static IEnumerable<object[]> GripVectors =>
    [
        [true, false, Golden(0x80, 0x00, 0x78563412)],
        [false, true, Golden(0x00, 0x01, 0x78563412)],
        [true, true, Golden(0x80, 0x01, 0x78563412)]
    ];

    private static readonly byte[] NeutralGolden = Golden(0x00, 0x00, 0);
    private static byte[] Golden(byte byte9, byte byte10, uint frame)
    {
        var report = new byte[64];
        report[0] = 0x01; report[1] = 0x00; report[2] = 0x01; report[3] = 0x3C;
        report[4] = (byte)frame; report[5] = (byte)(frame >> 8); report[6] = (byte)(frame >> 16); report[7] = (byte)(frame >> 24);
        report[9] = byte9; report[10] = byte10; report[40] = 0x00; report[41] = 0x40; report[62] = 0xB8; report[63] = 0x0B;
        return report;
    }
}
