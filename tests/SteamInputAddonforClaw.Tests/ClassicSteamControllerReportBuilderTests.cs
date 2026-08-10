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
        Assert.Equal(64, report.Length);
        Assert.Equal(new byte[] { 0x01, 0x00, 0x01, 0x3C, 0x00, 0x00, 0x00, 0x00 }, report[..8]);
        Assert.Equal(0x00, report[9]);
        Assert.Equal(0x00, report[10]);
        Assert.Equal(0x00, report[40]);
        Assert.Equal(0x40, report[41]);
        Assert.Equal(0xB8, report[62]);
        Assert.Equal(0x0B, report[63]);
    }

    [Theory]
    [InlineData(true, false, 0x80, 0x00)]
    [InlineData(false, true, 0x00, 0x01)]
    [InlineData(true, true, 0x80, 0x01)]
    public void Grip_bits_are_independent(bool leftGrip, bool rightGrip, byte expectedByte9, byte expectedByte10)
    {
        var report = new byte[64];
        ClassicSteamControllerReportBuilder.Write(report, 0x78563412, new(leftGrip, rightGrip));
        Assert.Equal(new byte[] { 0x12, 0x34, 0x56, 0x78 }, report[4..8]);
        Assert.Equal(expectedByte9, report[9]);
        Assert.Equal(expectedByte10, report[10]);
    }
}
