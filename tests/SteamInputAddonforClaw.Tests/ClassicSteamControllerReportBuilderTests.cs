using System.Buffers.Binary;
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
    [InlineData((byte)0)]
    [InlineData((byte)64)]
    [InlineData((byte)128)]
    [InlineData((byte)254)]
    [InlineData((byte)255)]
    public void LeftTriggerAnalogTravelIsPreservedAcrossAllReportFields(byte value)
    {
        var report = new byte[64];
        var input = TriggerInput(left: value, leftFull: false, right: 0, rightFull: false);

        ClassicSteamControllerReportBuilder.Write(report, 0, input);

        AssertTriggerFields(report, offsetByte: 11, offsetWide1: 24, offsetWide2: 50, value);
    }

    [Theory]
    [InlineData((byte)0)]
    [InlineData((byte)64)]
    [InlineData((byte)128)]
    [InlineData((byte)254)]
    [InlineData((byte)255)]
    public void RightTriggerAnalogTravelIsPreservedAcrossAllReportFields(byte value)
    {
        var report = new byte[64];
        var input = TriggerInput(left: 0, leftFull: false, right: value, rightFull: false);

        ClassicSteamControllerReportBuilder.Write(report, 0, input);

        AssertTriggerFields(report, offsetByte: 12, offsetWide1: 26, offsetWide2: 52, value);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LeftTriggerFullButtonDoesNotOverrideAnalogMagnitude(bool triggerFull)
    {
        // The MSI Claw's digital "trigger full" button can latch true while the analog axis is
        // still mid-travel; the analog fields must reflect only TriggerState.Left, never the
        // digital flag. Only the digital button bit (report[8] bit 1) may differ.
        const byte value = 64;
        var withoutFull = new byte[64];
        var withFull = new byte[64];
        ClassicSteamControllerReportBuilder.Write(withoutFull, 0, TriggerInput(left: value, leftFull: false, right: 0, rightFull: false));
        ClassicSteamControllerReportBuilder.Write(withFull, 0, TriggerInput(left: value, leftFull: triggerFull, right: 0, rightFull: false));

        Assert.Equal(withoutFull[11], withFull[11]);
        Assert.Equal(withoutFull[24..26].ToArray(), withFull[24..26].ToArray());
        Assert.Equal(withoutFull[50..52].ToArray(), withFull[50..52].ToArray());
        Assert.Equal(triggerFull ? 2 : 0, withFull[8] & 2);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RightTriggerFullButtonDoesNotOverrideAnalogMagnitude(bool triggerFull)
    {
        const byte value = 64;
        var withoutFull = new byte[64];
        var withFull = new byte[64];
        ClassicSteamControllerReportBuilder.Write(withoutFull, 0, TriggerInput(left: 0, leftFull: false, right: value, rightFull: false));
        ClassicSteamControllerReportBuilder.Write(withFull, 0, TriggerInput(left: 0, leftFull: false, right: value, rightFull: triggerFull));

        Assert.Equal(withoutFull[12], withFull[12]);
        Assert.Equal(withoutFull[26..28].ToArray(), withFull[26..28].ToArray());
        Assert.Equal(withoutFull[52..54].ToArray(), withFull[52..54].ToArray());
        Assert.Equal(triggerFull ? 1 : 0, withFull[8] & 1);
    }

    private static void AssertTriggerFields(byte[] report, int offsetByte, int offsetWide1, int offsetWide2, byte value)
    {
        var expectedRaw = value * 26000 / 255;
        var expectedByte = (byte)Math.Clamp(expectedRaw * 255 / 26000, 0, 255);
        Assert.Equal(expectedByte, report[offsetByte]);
        Assert.Equal((ushort)expectedRaw, BinaryPrimitives.ReadUInt16LittleEndian(report.AsSpan(offsetWide1, 2)));
        Assert.Equal((ushort)expectedRaw, BinaryPrimitives.ReadUInt16LittleEndian(report.AsSpan(offsetWide2, 2)));
    }

    private static ClassicSteamControllerInput TriggerInput(byte left, bool leftFull, byte right, bool rightFull) =>
        new(new GamepadButtons(false, false, false, false, false, false, false, false, false, false, false, false, false, false, leftFull, rightFull),
            default, default, new TriggerState(left, right), false, false, false, false);

    [Theory]
    [InlineData(short.MinValue, (short)0)]
    [InlineData((short)0, short.MinValue)]
    [InlineData(short.MinValue, short.MinValue)]
    [InlineData(short.MaxValue, short.MaxValue)]
    public void FullDeflectionRightStickDoesNotThrow(short x, short y)
    {
        var report = new byte[64];
        var input = ClassicSteamControllerInputMapper.Map(new ControllerState(default, default, new StickState(x, y), default, new([false, false])));

        var exception = Record.Exception(() => ClassicSteamControllerReportBuilder.Write(report, 0, input));

        Assert.Null(exception);
    }

    [Fact]
    public void RightStickAtShortMinValueIsReportedAsActive()
    {
        var report = new byte[64];
        var input = ClassicSteamControllerInputMapper.Map(new ControllerState(default, default, new StickState(short.MinValue, short.MinValue), default, new([false, false])));

        ClassicSteamControllerReportBuilder.Write(report, 0, input);

        Assert.Equal(0x10, report[10] & 0x10);
    }

    [Fact]
    public void BuilderDoesNotDeriveTouchOrPressFromRightPadCoordinatesItself()
    {
        // RightPadTouch/RightPadPress must come entirely from the already-decided logical
        // input; the builder only serializes them. A RightPad coordinate past the mapper's
        // touch threshold must NOT flip byte 10 bits here if the logical flags say untouched --
        // guards against the threshold check regressing back into the report builder.
        var report = new byte[64];
        var input = new ClassicSteamControllerInput(default, default, new StickState(501, 0), default, false, false, false, false);

        ClassicSteamControllerReportBuilder.Write(report, 0, input);

        Assert.Equal(501, BitConverter.ToInt16(report, 20));
        Assert.Equal(0, report[10] & 0x10);
        Assert.Equal(0, report[10] & 0x04);
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
