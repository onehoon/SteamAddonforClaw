using SteamInputAddonforClaw.Devices.MSI.Claw;
using SteamInputAddonforClaw.Input.DirectInput;
using SteamInputAddonforClaw.VirtualOutput.Viiper;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class FullNonGyroPipelineTests
{
    [Fact]
    public void Maps_full_non_gyro_state_to_deck()
    {
        var buttons = new bool[17];
        buttons[1] = true; // A
        buttons[4] = true; // LeftBumper -> L1
        buttons[9] = true; // Start -> Options
        buttons[10] = true; // LeftStickClick -> L3
        buttons[11] = true; // RightStickClick -> R3
        buttons[15] = true; // M1 -> R4
        buttons[16] = true; // M2 -> L4
        var raw = new DirectInputState(buttons, 0, 65535, 65535, 32768, 65535, 0, [4500]);

        Assert.True(MsiClawControllerStateMapper.TryMap(raw, out var state));
        var deck = SteamDeckDeviceStateMapper.Map(state);

        Assert.Equal(1, deck.A);
        Assert.Equal(1, deck.L1);
        Assert.Equal(1, deck.Options);
        Assert.Equal(1, deck.L3);
        Assert.Equal(1, deck.R3);
        Assert.Equal(1, deck.DPadUp);
        Assert.Equal(1, deck.DPadRight);
        Assert.Equal(1, deck.R4);
        Assert.Equal(1, deck.L4);
        Assert.Equal(short.MinValue, deck.LStickX);
        Assert.Equal(short.MinValue, deck.LStickY);
        Assert.Equal(short.MaxValue, deck.RStickX);
        Assert.Equal(short.MaxValue, deck.RStickY);
        Assert.Equal((ushort)32767, deck.RTrigger);
    }

    [Fact]
    public void Rejects_known_invalid_initial_state()
    {
        var raw = new DirectInputState(new bool[17], 32767, 32767, 32767, 32767, 32767, 32767, [-1]);
        Assert.True(MsiClawControllerStateMapper.IsKnownInvalidInitialState(raw));
        Assert.False(MsiClawControllerStateMapper.TryMap(raw, out _));
    }

    [Theory]
    [InlineData(true, false, 1, 0)]
    [InlineData(false, true, 0, 1)]
    public void Maps_M1_to_R4_and_M2_to_L4(bool m1, bool m2, byte expectedR4, byte expectedL4)
    {
        var buttons = new bool[17]; buttons[15] = m1; buttons[16] = m2;
        Assert.True(MsiClawControllerStateMapper.TryMap(new DirectInputState(buttons, 32768, 32768, 32768, 0, 0, 0, [-1]), out var state));

        var deck = SteamDeckDeviceStateMapper.Map(state);

        Assert.Equal(expectedR4, deck.R4);
        Assert.Equal(expectedL4, deck.L4);
    }

    [Theory]
    [InlineData(0, short.MaxValue)] [InlineData(65535, short.MinValue)]
    public void Inverted_axis_endpoints_are_clamped(int raw, short expected)
    {
        Assert.True(MsiClawControllerStateMapper.TryMap(new DirectInputState(new bool[17], 32768, raw, 32768, 0, 0, 0, [-1]), out var state));
        Assert.Equal(expected, state.LeftStick.Y);
    }

    [Theory]
    [InlineData(-1, false, false, false, false)] [InlineData(0, true, false, false, false)] [InlineData(22500, false, false, true, true)] [InlineData(31500, true, false, false, true)]
    public void Maps_pov_to_independent_direction_bits(int pov, bool up, bool right, bool down, bool left)
    {
        Assert.True(MsiClawControllerStateMapper.TryMap(new DirectInputState(new bool[17], 32767, 32767, 32767, 0, 0, 0, [pov]), out var state));
        Assert.Equal(up, state.Buttons.DPadUp); Assert.Equal(right, state.Buttons.DPadRight); Assert.Equal(down, state.Buttons.DPadDown); Assert.Equal(left, state.Buttons.DPadLeft);
    }
}
