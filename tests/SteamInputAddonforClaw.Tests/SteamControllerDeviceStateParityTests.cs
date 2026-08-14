using System.Buffers.Binary;
using SteamInputAddonforClaw.Input;
using SteamInputAddonforClaw.VirtualOutput.Viiper;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class SteamControllerDeviceStateParityTests
{
    [Fact]
    public void Common_mapping_fields_match_the_legacy_raw_report_path()
    {
        var state = new ControllerState(
            new GamepadButtons(true, true, true, true, true, true, true, true, true, true, true, true, true, true, true, true),
            new StickState(-1234, 2345),
            new StickState(501, -502),
            new TriggerState(128, 254),
            new AuxiliaryButtonState(new[] { true, false }));

        var typed = SteamControllerDeviceStateMapper.Map(state);
        var report = LegacyReport(state);

        Assert.Equal(typed.A, Bit(report, 8, 0x80));
        Assert.Equal(typed.X, Bit(report, 8, 0x40));
        Assert.Equal(typed.B, Bit(report, 8, 0x20));
        Assert.Equal(typed.Y, Bit(report, 8, 0x10));
        Assert.Equal(typed.L1, Bit(report, 8, 0x08));
        Assert.Equal(typed.R1, Bit(report, 8, 0x04));
        Assert.Equal(typed.Menu, Bit(report, 9, 0x10));
        Assert.Equal(typed.Options, Bit(report, 9, 0x40));
        Assert.Equal(typed.DPadUp, Bit(report, 9, 0x01));
        Assert.Equal(typed.DPadRight, Bit(report, 9, 0x02));
        Assert.Equal(typed.DPadDown, Bit(report, 9, 0x08));
        Assert.Equal(typed.DPadLeft, Bit(report, 9, 0x04));
        Assert.Equal(typed.L3, Bit(report, 10, 0x40));
        Assert.Equal(typed.LGrip, Bit(report, 9, 0x80));
        Assert.Equal(typed.RGrip, Bit(report, 10, 0x01));
        Assert.Equal(typed.RPadPress, Bit(report, 10, 0x04));
        Assert.Equal(typed.RPadTouch, Bit(report, 10, 0x10));
        Assert.Equal(typed.RPadX, BinaryPrimitives.ReadInt16LittleEndian(report.AsSpan(20, 2)));
        Assert.Equal(typed.RPadY, BinaryPrimitives.ReadInt16LittleEndian(report.AsSpan(22, 2)));
        Assert.Equal(typed.LStickX, BinaryPrimitives.ReadInt16LittleEndian(report.AsSpan(16, 2)));
        Assert.Equal(typed.LStickY, BinaryPrimitives.ReadInt16LittleEndian(report.AsSpan(18, 2)));
        Assert.Equal(typed.LStickX, BinaryPrimitives.ReadInt16LittleEndian(report.AsSpan(54, 2)));
        Assert.Equal(typed.LStickY, BinaryPrimitives.ReadInt16LittleEndian(report.AsSpan(56, 2)));
        Assert.Equal(typed.LTrigger, BinaryPrimitives.ReadUInt16LittleEndian(report.AsSpan(24, 2)));
        Assert.Equal(typed.RTrigger, BinaryPrimitives.ReadUInt16LittleEndian(report.AsSpan(26, 2)));
    }

    [Theory]
    [InlineData((byte)0, false)]
    [InlineData((byte)128, false)]
    [InlineData((byte)128, true)]
    [InlineData((byte)255, false)]
    [InlineData((byte)255, true)]
    public void Trigger_vectors_classify_common_magnitude_and_intentional_full_pull_difference(byte value, bool explicitFull)
    {
        var buttons = new GamepadButtons(false, false, false, false, false, false, false, false, false, false, false, false, false, false, explicitFull, explicitFull);
        var state = new ControllerState(buttons, default, default, new TriggerState(value, value), new AuxiliaryButtonState(new[] { false, false }));
        var typed = SteamControllerDeviceStateMapper.Map(state);
        var report = LegacyReport(state);

        Assert.Equal(typed.LTrigger, BinaryPrimitives.ReadUInt16LittleEndian(report.AsSpan(24, 2)));
        Assert.Equal(typed.RTrigger, BinaryPrimitives.ReadUInt16LittleEndian(report.AsSpan(26, 2)));

        var legacyLeftFull = Bit(report, 8, 0x02);
        var legacyRightFull = Bit(report, 8, 0x01);
        var canonicalLeftFull = typed.L2 != 0 || typed.LTrigger >= 26000;
        var canonicalRightFull = typed.R2 != 0 || typed.RTrigger >= 26000;

        if (value == 255 && !explicitFull)
        {
            Assert.Equal((byte)0, legacyLeftFull);
            Assert.Equal((byte)0, legacyRightFull);
            Assert.True(canonicalLeftFull);
            Assert.True(canonicalRightFull);
        }
        else
        {
            Assert.Equal(legacyLeftFull, canonicalLeftFull ? (byte)1 : (byte)0);
            Assert.Equal(legacyRightFull, canonicalRightFull ? (byte)1 : (byte)0);
        }
    }

    [Fact]
    public void Zero_typed_quaternion_and_battery_match_legacy_wire_defaults()
    {
        var state = new ControllerState(default, new StickState(100, -200), default, default, new AuxiliaryButtonState(new[] { false, false }));
        var typed = SteamControllerDeviceStateMapper.Map(state);
        var report = LegacyReport(state);

        Assert.Equal(0, typed.GyroQuatW);
        Assert.Equal(0x4000, BinaryPrimitives.ReadInt16LittleEndian(report.AsSpan(40, 2)));
        Assert.Equal(0, typed.BatteryMilliVolts);
        Assert.Equal((ushort)3000, BinaryPrimitives.ReadUInt16LittleEndian(report.AsSpan(62, 2)));
    }

    [Theory]
    [InlineData((short)0, (short)0)]
    [InlineData((short)501, (short)-501)]
    [InlineData(short.MinValue, short.MaxValue)]
    public void Right_pad_and_grip_vectors_preserve_common_legacy_semantics(short x, short y)
    {
        var state = new ControllerState(default, default, new StickState(x, y), default, new AuxiliaryButtonState(new[] { true, true }));
        var typed = SteamControllerDeviceStateMapper.Map(state);
        var report = LegacyReport(state);

        Assert.Equal(typed.RPadX, BinaryPrimitives.ReadInt16LittleEndian(report.AsSpan(20, 2)));
        Assert.Equal(typed.RPadY, BinaryPrimitives.ReadInt16LittleEndian(report.AsSpan(22, 2)));
        Assert.Equal(typed.RPadTouch, Bit(report, 10, 0x10));
        Assert.Equal(typed.RPadPress, Bit(report, 10, 0x04));
        Assert.Equal(1, typed.LGrip);
        Assert.Equal(1, typed.RGrip);
    }

    private static byte[] LegacyReport(ControllerState state)
    {
        var report = new byte[ClassicSteamControllerReportBuilder.ReportLength];
        ClassicSteamControllerReportBuilder.Write(report, 123, ClassicSteamControllerInputMapper.Map(state));
        return report;
    }

    private static byte Bit(byte[] report, int offset, byte mask) => (byte)((report[offset] & mask) == 0 ? 0 : 1);
}
