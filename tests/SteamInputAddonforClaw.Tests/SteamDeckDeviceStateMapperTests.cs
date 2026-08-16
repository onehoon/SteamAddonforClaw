using SteamInputAddonforClaw.Input;
using SteamInputAddonforClaw.VirtualOutput.Viiper;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class SteamDeckDeviceStateMapperTests
{
    [Fact]
    public void Neutral_state_leaves_unused_deck_fields_zero()
    {
        var mapped = SteamDeckDeviceStateMapper.Map(State());

        Assert.Equal(0, mapped.L5);
        Assert.Equal(0, mapped.R5);
        Assert.Equal(0, mapped.Steam);
        Assert.Equal(0, mapped.QuickAccess);
        Assert.Equal(0, mapped.RPadTouch);
        Assert.Equal(0, mapped.LPadTouch);
        Assert.Equal(0, mapped.RPadPress);
        Assert.Equal(0, mapped.LPadPress);
        Assert.Equal(0, mapped.RStickTouch);
        Assert.Equal(0, mapped.LStickTouch);
        Assert.Equal(0, mapped.LPadX);
        Assert.Equal(0, mapped.LPadY);
        Assert.Equal(0, mapped.RPadX);
        Assert.Equal(0, mapped.RPadY);
        Assert.Equal(0, mapped.LPadForce);
        Assert.Equal(0, mapped.RPadForce);
        Assert.Equal(0, mapped.LStickForce);
        Assert.Equal(0, mapped.RStickForce);
        Assert.Equal(0, mapped.AccelX);
        Assert.Equal(0, mapped.AccelY);
        Assert.Equal(0, mapped.AccelZ);
        Assert.Equal(0, mapped.Pitch);
        Assert.Equal(0, mapped.Yaw);
        Assert.Equal(0, mapped.Roll);
        Assert.Equal(0, mapped.GyroQuatW);
        Assert.Equal(0, mapped.GyroQuatX);
        Assert.Equal(0, mapped.GyroQuatY);
        Assert.Equal(0, mapped.GyroQuatZ);
    }

    [Theory]
    [InlineData("A")]
    [InlineData("X")]
    [InlineData("B")]
    [InlineData("Y")]
    public void Face_buttons_map_individually(string button)
    {
        var mapped = SteamDeckDeviceStateMapper.Map(State(buttons: Button(button)));
        var actual = button switch
        {
            "A" => mapped.A,
            "X" => mapped.X,
            "B" => mapped.B,
            "Y" => mapped.Y,
            _ => throw new ArgumentOutOfRangeException(nameof(button))
        };
        Assert.Equal((byte)1, actual);
    }

    [Theory]
    [InlineData("DPadUp")]
    [InlineData("DPadRight")]
    [InlineData("DPadDown")]
    [InlineData("DPadLeft")]
    public void Each_dpad_source_maps_only_to_its_matching_field(string direction)
    {
        var mapped = SteamDeckDeviceStateMapper.Map(State(buttons: Dpad(direction)));

        Assert.Equal(direction == "DPadUp" ? 1 : 0, mapped.DPadUp);
        Assert.Equal(direction == "DPadRight" ? 1 : 0, mapped.DPadRight);
        Assert.Equal(direction == "DPadDown" ? 1 : 0, mapped.DPadDown);
        Assert.Equal(direction == "DPadLeft" ? 1 : 0, mapped.DPadLeft);
    }

    [Fact]
    public void Bumpers_map_to_L1_R1()
    {
        var buttons = new GamepadButtons(false, false, false, false, false, false, false, false, true, true, false, false, false, false, false, false);
        var mapped = SteamDeckDeviceStateMapper.Map(State(buttons: buttons));

        Assert.Equal(1, mapped.L1);
        Assert.Equal(1, mapped.R1);
    }

    [Fact]
    public void Left_and_right_stick_clicks_map_to_native_L3_and_R3()
    {
        var buttons = new GamepadButtons(false, false, false, false, false, false, false, false, false, false, false, false, true, true, false, false);
        var mapped = SteamDeckDeviceStateMapper.Map(State(buttons: buttons));

        Assert.Equal(1, mapped.L3);
        Assert.Equal(1, mapped.R3);
        // Steam Deck has a native R3 field -- R3 must never be routed to RPadPress.
        Assert.Equal(0, mapped.RPadPress);
    }

    [Fact]
    public void Start_maps_to_SteamDeck_Menu()
    {
        // Independent of Back -- a prior version of this mapper had Start and Back swapped, and a
        // test that set both simultaneously could not have caught that regression.
        var buttons = new GamepadButtons(false, false, false, false, false, false, false, false, false, false, false, true, false, false, false, false);
        var mapped = SteamDeckDeviceStateMapper.Map(State(buttons: buttons));

        Assert.Equal(1, mapped.Menu);
        Assert.Equal(0, mapped.Options);
    }

    [Fact]
    public void Back_maps_to_SteamDeck_Options()
    {
        // Independent of Start -- see Start_maps_to_SteamDeck_Menu.
        var buttons = new GamepadButtons(false, false, false, false, false, false, false, false, false, false, true, false, false, false, false, false);
        var mapped = SteamDeckDeviceStateMapper.Map(State(buttons: buttons));

        Assert.Equal(0, mapped.Menu);
        Assert.Equal(1, mapped.Options);
    }

    [Theory]
    [InlineData(false, false, 0, 0)]
    [InlineData(true, false, 1, 0)]
    [InlineData(false, true, 0, 1)]
    [InlineData(true, true, 1, 1)]
    public void M2_maps_to_L4_and_M1_maps_to_R4(bool m2, bool m1, byte left, byte right)
    {
        var mapped = SteamDeckDeviceStateMapper.Map(State(auxiliary: new AuxiliaryButtonState(new[] { m2, m1 })));

        Assert.Equal(left, mapped.L4);
        Assert.Equal(right, mapped.R4);
    }

    [Fact]
    public void Left_stick_maps_to_LStickXY_and_right_stick_maps_to_native_RStickXY()
    {
        var mapped = SteamDeckDeviceStateMapper.Map(State(leftStick: new StickState(111, -222), rightStick: new StickState(333, -444)));

        Assert.Equal((short)111, mapped.LStickX);
        Assert.Equal((short)-222, mapped.LStickY);
        Assert.Equal((short)333, mapped.RStickX);
        Assert.Equal((short)-444, mapped.RStickY);
        // Steam Deck has native right-stick fields -- the right stick must never be routed through
        // RPadX/Y.
        Assert.Equal(0, mapped.RPadX);
        Assert.Equal(0, mapped.RPadY);
    }

    [Theory]
    [InlineData((byte)0, (ushort)0)]
    [InlineData((byte)128, (ushort)16447)]
    [InlineData((byte)255, (ushort)32767)]
    public void Analog_trigger_scales_0_255_to_0_32767(byte value, ushort expected)
    {
        var mapped = SteamDeckDeviceStateMapper.Map(State(triggers: new TriggerState(value, value)));

        Assert.Equal(expected, mapped.LTrigger);
        Assert.Equal(expected, mapped.RTrigger);
    }

    [Theory]
    [InlineData((byte)0, false, (ushort)0, (byte)0)]
    [InlineData((byte)0, true, (ushort)0, (byte)1)]
    [InlineData((byte)128, true, (ushort)16447, (byte)1)]
    [InlineData((byte)255, false, (ushort)32767, (byte)0)]
    public void Digital_full_pull_is_independent_of_analog_magnitude(byte value, bool full, ushort expectedRaw, byte expectedDigital)
    {
        var buttons = new GamepadButtons(false, false, false, false, false, false, false, false, false, false, false, false, false, false, full, full);
        var mapped = SteamDeckDeviceStateMapper.Map(State(buttons: buttons, triggers: new TriggerState(value, value)));

        Assert.Equal(expectedRaw, mapped.LTrigger);
        Assert.Equal(expectedRaw, mapped.RTrigger);
        Assert.Equal(expectedDigital, mapped.L2Digital);
        Assert.Equal(expectedDigital, mapped.R2Digital);
    }

    [Fact]
    public void Combined_midpoint_analog_and_full_pull_produce_both_independently()
    {
        var buttons = new GamepadButtons(false, false, false, false, false, false, false, false, false, false, false, false, false, false, true, false);
        var mapped = SteamDeckDeviceStateMapper.Map(State(buttons: buttons, triggers: new TriggerState(128, 0)));

        Assert.Equal((ushort)16447, mapped.LTrigger);
        Assert.Equal((byte)1, mapped.L2Digital);
        Assert.Equal((ushort)0, mapped.RTrigger);
        Assert.Equal((byte)0, mapped.R2Digital);
    }

    [Fact]
    public void Left_and_right_trigger_full_pull_flags_are_independent()
    {
        var buttons = new GamepadButtons(false, false, false, false, false, false, false, false, false, false, false, false, false, false, true, false);
        var mapped = SteamDeckDeviceStateMapper.Map(State(buttons: buttons, triggers: new TriggerState(64, 128)));

        Assert.Equal(1, mapped.L2Digital);
        Assert.Equal(0, mapped.R2Digital);
    }

    private static ControllerState State(
        GamepadButtons? buttons = null,
        StickState? leftStick = null,
        StickState? rightStick = null,
        TriggerState? triggers = null,
        AuxiliaryButtonState? auxiliary = null) =>
        new(buttons ?? default, leftStick ?? default, rightStick ?? default, triggers ?? default, auxiliary ?? new AuxiliaryButtonState(new[] { false, false }));

    private static GamepadButtons Button(string name) => name switch
    {
        "A" => new(true, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false),
        "X" => new(false, false, true, false, false, false, false, false, false, false, false, false, false, false, false, false),
        "B" => new(false, true, false, false, false, false, false, false, false, false, false, false, false, false, false, false),
        "Y" => new(false, false, false, true, false, false, false, false, false, false, false, false, false, false, false, false),
        _ => throw new ArgumentOutOfRangeException(nameof(name), name, null)
    };

    private static GamepadButtons Dpad(string name) => name switch
    {
        "DPadUp" => new(false, false, false, false, true, false, false, false, false, false, false, false, false, false, false, false),
        "DPadRight" => new(false, false, false, false, false, true, false, false, false, false, false, false, false, false, false, false),
        "DPadDown" => new(false, false, false, false, false, false, true, false, false, false, false, false, false, false, false, false),
        "DPadLeft" => new(false, false, false, false, false, false, false, true, false, false, false, false, false, false, false, false),
        _ => throw new ArgumentOutOfRangeException(nameof(name), name, null)
    };
}
