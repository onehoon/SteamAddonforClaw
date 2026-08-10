using SteamInputAddonforClaw.Devices.MSI.Claw;
using SteamInputAddonforClaw.Input.DirectInput;
using SteamInputAddonforClaw.VirtualOutput.Viiper;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class FullNonGyroPipelineTests
{
    [Fact]
    public void Maps_full_non_gyro_state_and_grips()
    {
        var buttons = new bool[17]; buttons[1] = true; buttons[4] = true; buttons[9] = true; buttons[10] = true; buttons[11] = true; buttons[15] = true; buttons[16] = true;
        var raw = new DirectInputState(buttons, 0, 65535, 65535, 32768, 65535, 0, [4500]);
        Assert.True(MsiClawControllerStateMapper.TryMap(raw, out var state));
        Assert.True(state.Buttons.A && state.Buttons.LeftBumper && state.Buttons.Start && state.Buttons.LeftStickClick && state.Buttons.RightStickClick);
        Assert.True(state.Buttons.DPadUp && state.Buttons.DPadRight && state.Auxiliary[0] && state.Auxiliary[1]);
        Assert.Equal(short.MinValue, state.LeftStick.X); Assert.Equal(-32767, state.LeftStick.Y);
        Assert.Equal(byte.MaxValue, state.Triggers.Right);
        var steam = ClassicSteamControllerInputMapper.Map(state); var report = new byte[64]; ClassicSteamControllerReportBuilder.Write(report, 7, steam);
        Assert.Equal(0x80 | 0x08, report[8]); Assert.Equal(0x40 | 0x80 | 0x03, report[9]); Assert.Equal(0x01 | 0x04 | 0x10 | 0x40, report[10]); Assert.Equal(26000, BitConverter.ToInt16(report, 26));
    }

    [Fact]
    public void Rejects_known_invalid_initial_state()
    {
        var raw = new DirectInputState(new bool[17], 32767, 32767, 32767, 32767, 32767, 32767, [-1]);
        Assert.True(MsiClawControllerStateMapper.IsKnownInvalidInitialState(raw));
        Assert.False(MsiClawControllerStateMapper.TryMap(raw, out _));
    }

    [Theory]
    [InlineData(true, false, 0x80, 0x00)]
    [InlineData(false, true, 0x00, 0x01)]
    public void Maps_M2_to_left_grip_and_M1_to_right_grip(bool m2, bool m1, byte expectedByte9, byte expectedByte10)
    {
        var buttons = new bool[17]; buttons[15] = m1; buttons[16] = m2;
        Assert.True(MsiClawControllerStateMapper.TryMap(new DirectInputState(buttons, 32768, 32768, 32768, 0, 0, 0, [-1]), out var state));
        var report = new byte[64]; ClassicSteamControllerReportBuilder.Write(report, 0, ClassicSteamControllerInputMapper.Map(state));
        Assert.Equal(expectedByte9, report[9] & 0x80); Assert.Equal(expectedByte10, report[10] & 0x01);
    }

    [Theory]
    [InlineData(0, short.MaxValue)] [InlineData(65535, -32767)]
    public void Inverted_axis_endpoints_are_clamped(int raw, short expected)
    {
        Assert.True(MsiClawControllerStateMapper.TryMap(new DirectInputState(new bool[17], 32768, raw, 32768, 0, 0, 0, [-1]), out var state));
        Assert.Equal(expected, state.LeftStick.Y);
    }

    [Fact]
    public void Center_offset_does_not_activate_right_pad_and_duplicate_trigger_fields_match()
    {
        var input = new ClassicSteamControllerInput(default, default, new(1, -1), new(128, 64), false, false);
        var report = new byte[64]; ClassicSteamControllerReportBuilder.Write(report, 0, input);
        Assert.Equal(0, report[10] & 0x10);
        Assert.Equal(report[24..26], report[50..52]); Assert.Equal(report[26..28], report[52..54]);
    }

    [Theory]
    [InlineData(-1, false, false, false, false)] [InlineData(0, true, false, false, false)] [InlineData(22500, false, false, true, true)] [InlineData(31500, true, false, false, true)]
    public void Maps_pov_to_independent_direction_bits(int pov, bool up, bool right, bool down, bool left)
    {
        Assert.True(MsiClawControllerStateMapper.TryMap(new DirectInputState(new bool[17], 32767, 32767, 32767, 0, 0, 0, [pov]), out var state));
        Assert.Equal(up, state.Buttons.DPadUp); Assert.Equal(right, state.Buttons.DPadRight); Assert.Equal(down, state.Buttons.DPadDown); Assert.Equal(left, state.Buttons.DPadLeft);
    }
}
