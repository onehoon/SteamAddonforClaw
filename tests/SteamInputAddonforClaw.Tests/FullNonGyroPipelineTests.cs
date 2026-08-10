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
    [InlineData(-1, false, false, false, false)] [InlineData(0, true, false, false, false)] [InlineData(22500, false, false, true, true)] [InlineData(31500, true, false, false, true)]
    public void Maps_pov_to_independent_direction_bits(int pov, bool up, bool right, bool down, bool left)
    {
        Assert.True(MsiClawControllerStateMapper.TryMap(new DirectInputState(new bool[17], 32767, 32767, 32767, 0, 0, 0, [pov]), out var state));
        Assert.Equal(up, state.Buttons.DPadUp); Assert.Equal(right, state.Buttons.DPadRight); Assert.Equal(down, state.Buttons.DPadDown); Assert.Equal(left, state.Buttons.DPadLeft);
    }
}
