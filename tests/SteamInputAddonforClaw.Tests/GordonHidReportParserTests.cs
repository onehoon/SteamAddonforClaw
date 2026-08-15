using SteamInputAddonforClaw.Diagnostics.GordonDPad;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class GordonHidReportParserTests
{
    private static byte[] NeutralReport(byte byte9 = 0x00)
    {
        var report = new byte[GordonHidReportParser.ExpectedLength];
        report[0] = GordonHidReportParser.ExpectedReportId;
        report[9] = byte9;
        return report;
    }

    [Theory]
    [InlineData(0x00, 0x00)] // neutral
    [InlineData(0x01, 0x01)] // Up
    [InlineData(0x02, 0x02)] // Right
    [InlineData(0x04, 0x04)] // Left
    [InlineData(0x08, 0x08)] // Down
    [InlineData(0x03, 0x03)] // Up+Right
    [InlineData(0x05, 0x05)] // Up+Left
    [InlineData(0x0A, 0x0A)] // Down+Right
    [InlineData(0x0C, 0x0C)] // Down+Left
    public void Parse_ExtractsExpectedDPadMask(byte byte9, byte expectedMask)
    {
        var result = GordonHidReportParser.Parse(NeutralReport(byte9));

        Assert.True(result.Accepted);
        Assert.Equal(expectedMask, result.DPadMask);
        Assert.Equal(byte9, result.Byte9);
    }

    [Fact]
    public void Parse_UpperNibbleIsExcludedFromDPadMask()
    {
        // Menu/Steam/Options/LGrip occupy the upper nibble of byte9 and must never leak into DPadMask.
        var result = GordonHidReportParser.Parse(NeutralReport(0xF8));

        Assert.Equal(0x08, result.DPadMask);
        Assert.Equal(0xF8, result.Byte9);
    }

    [Fact]
    public void Parse_ShortReportIsRejectedSafely()
    {
        var shortReport = new byte[GordonHidReportParser.ExpectedLength - 1];

        var result = GordonHidReportParser.Parse(shortReport);

        Assert.False(result.Accepted);
        Assert.Equal(GordonHidReportRejectionReason.TooShort, result.RejectionReason);
    }

    [Fact]
    public void Parse_WrongReportIdIsRejectedSafely()
    {
        var report = NeutralReport();
        report[0] = 0x02;

        var result = GordonHidReportParser.Parse(report);

        Assert.False(result.Accepted);
        Assert.Equal(GordonHidReportRejectionReason.UnexpectedReportId, result.RejectionReason);
        Assert.Equal((byte)0x02, result.ReportId);
    }

    [Fact]
    public void Parse_ExposesByte8AndByte10Verbatim()
    {
        var report = NeutralReport();
        report[8] = 0xAB;
        report[10] = 0xCD;

        var result = GordonHidReportParser.Parse(report);

        Assert.Equal((byte)0xAB, result.Byte8);
        Assert.Equal((byte)0xCD, result.Byte10);
    }
}
