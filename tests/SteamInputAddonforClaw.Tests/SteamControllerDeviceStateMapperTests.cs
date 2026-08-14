using System.Reflection;
using SteamInputAddonforClaw.Input;
using SteamInputAddonforClaw.VirtualOutput.Viiper;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class SteamControllerDeviceStateMapperTests
{
    [Fact]
    public void Neutral_state_leaves_all_unsupported_fields_zero()
    {
        var mapped = SteamControllerDeviceStateMapper.Map(State());

        Assert.Equal(0, mapped.A);
        Assert.Equal(0, mapped.X);
        Assert.Equal(0, mapped.B);
        Assert.Equal(0, mapped.Y);
        Assert.Equal(0, mapped.L1);
        Assert.Equal(0, mapped.R1);
        Assert.Equal(0, mapped.L2);
        Assert.Equal(0, mapped.R2);
        Assert.Equal(0, mapped.Menu);
        Assert.Equal(0, mapped.Options);
        Assert.Equal(0, mapped.DPadUp);
        Assert.Equal(0, mapped.DPadRight);
        Assert.Equal(0, mapped.DPadDown);
        Assert.Equal(0, mapped.DPadLeft);
        Assert.Equal(0, mapped.L3);
        Assert.Equal(0, mapped.LGrip);
        Assert.Equal(0, mapped.RGrip);
        Assert.Equal(0, mapped.RPadTouch);
        Assert.Equal(0, mapped.RPadPress);
        Assert.Equal(0, mapped.RPadX);
        Assert.Equal(0, mapped.RPadY);
        Assert.Equal(0, mapped.LStickX);
        Assert.Equal(0, mapped.LStickY);
        Assert.Equal(0, mapped.LTrigger);
        Assert.Equal(0, mapped.RTrigger);
        Assert.Equal(0, mapped.Steam);
        Assert.Equal(0, mapped.LPadTouch);
        Assert.Equal(0, mapped.LPadPress);
        Assert.Equal(0, mapped.LPadAndStick);
        Assert.Equal(0, mapped.LPadX);
        Assert.Equal(0, mapped.LPadY);
        Assert.Equal(0, mapped.AccelX);
        Assert.Equal(0, mapped.AccelY);
        Assert.Equal(0, mapped.AccelZ);
        Assert.Equal(0, mapped.GyroX);
        Assert.Equal(0, mapped.GyroY);
        Assert.Equal(0, mapped.GyroZ);
        Assert.Equal(0, mapped.GyroQuatW);
        Assert.Equal(0, mapped.GyroQuatX);
        Assert.Equal(0, mapped.GyroQuatY);
        Assert.Equal(0, mapped.GyroQuatZ);
        Assert.Equal(0, mapped.BatteryMilliVolts);
    }

    [Theory]
    [InlineData("A")]
    [InlineData("X")]
    [InlineData("B")]
    [InlineData("Y")]
    public void Face_buttons_map_individually(string button)
    {
        var mapped = SteamControllerDeviceStateMapper.Map(State(buttons: Button(button)));
        Assert.Equal((byte)1, typeof(SteamControllerDeviceState).GetField(button, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(mapped));
    }

    [Fact]
    public void Dpad_and_shoulders_map_without_reordering()
    {
        var mapped = SteamControllerDeviceStateMapper.Map(State(buttons: new GamepadButtons(false, false, false, false, true, true, true, true, true, true, false, false, false, false, false, false)));

        Assert.Equal(1, mapped.DPadUp);
        Assert.Equal(1, mapped.DPadRight);
        Assert.Equal(1, mapped.DPadDown);
        Assert.Equal(1, mapped.DPadLeft);
        Assert.Equal(1, mapped.L1);
        Assert.Equal(1, mapped.R1);
    }

    [Theory]
    [InlineData("DPadUp")]
    [InlineData("DPadRight")]
    [InlineData("DPadDown")]
    [InlineData("DPadLeft")]
    public void Each_dpad_source_maps_only_to_its_matching_field(string direction)
    {
        var mapped = SteamControllerDeviceStateMapper.Map(State(buttons: Dpad(direction)));

        foreach (var field in new[] { "DPadUp", "DPadRight", "DPadDown", "DPadLeft" })
        {
            var actual = (byte)typeof(SteamControllerDeviceState).GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(mapped)!;
            Assert.Equal(field == direction ? (byte)1 : (byte)0, actual);
        }
    }

    [Fact]
    public void Dpad_diagonal_preserves_the_two_source_directions_only()
    {
        var mapped = SteamControllerDeviceStateMapper.Map(State(buttons: new GamepadButtons(false, false, false, false, true, true, false, false, false, false, false, false, false, false, false, false)));

        Assert.Equal(1, mapped.DPadUp);
        Assert.Equal(1, mapped.DPadRight);
        Assert.Equal(0, mapped.DPadDown);
        Assert.Equal(0, mapped.DPadLeft);
    }

    [Theory]
    [InlineData("LeftBumper")]
    [InlineData("RightBumper")]
    public void Each_shoulder_source_maps_only_to_its_matching_field(string shoulder)
    {
        var mapped = SteamControllerDeviceStateMapper.Map(State(buttons: Shoulder(shoulder)));

        Assert.Equal(shoulder == "LeftBumper" ? (byte)1 : (byte)0, mapped.L1);
        Assert.Equal(shoulder == "RightBumper" ? (byte)1 : (byte)0, mapped.R1);
    }

    [Fact]
    public void Back_and_start_map_to_menu_and_options_while_steam_stays_neutral()
    {
        var mapped = SteamControllerDeviceStateMapper.Map(State(buttons: new GamepadButtons(false, false, false, false, false, false, false, false, false, false, true, true, false, false, false, false)));

        Assert.Equal(1, mapped.Menu);
        Assert.Equal(1, mapped.Options);
        Assert.Equal(0, mapped.Steam);
    }

    [Fact]
    public void Stick_clicks_map_to_l3_and_right_pad_press_touch()
    {
        var mapped = SteamControllerDeviceStateMapper.Map(State(buttons: new GamepadButtons(false, false, false, false, false, false, false, false, false, false, false, false, true, true, false, false)));

        Assert.Equal(1, mapped.L3);
        Assert.Equal(1, mapped.RPadPress);
        Assert.Equal(1, mapped.RPadTouch);
    }

    [Theory]
    [InlineData((short)0, (short)0, false)]
    [InlineData((short)1, (short)-1, false)]
    [InlineData((short)500, (short)-500, false)]
    [InlineData((short)501, (short)0, true)]
    [InlineData((short)-501, (short)0, true)]
    [InlineData(short.MinValue, (short)0, true)]
    [InlineData(short.MaxValue, (short)0, true)]
    public void Right_pad_coordinates_are_preserved_and_touch_uses_per_axis_threshold(short x, short y, bool touched)
    {
        var mapped = SteamControllerDeviceStateMapper.Map(State(rightStick: new StickState(x, y)));

        Assert.Equal(x, mapped.RPadX);
        Assert.Equal(y, mapped.RPadY);
        Assert.Equal(touched ? 1 : 0, mapped.RPadTouch);
        Assert.Equal(0, mapped.RPadPress);
    }

    [Fact]
    public void Right_pad_press_forces_touch_at_center()
    {
        var mapped = SteamControllerDeviceStateMapper.Map(State(buttons: new GamepadButtons(false, false, false, false, false, false, false, false, false, false, false, false, false, true, false, false)));

        Assert.Equal(1, mapped.RPadPress);
        Assert.Equal(1, mapped.RPadTouch);
    }

    [Theory]
    [InlineData(false, false, 0, 0)]
    [InlineData(true, false, 1, 0)]
    [InlineData(false, true, 0, 1)]
    [InlineData(true, true, 1, 1)]
    public void M2_maps_to_left_grip_and_M1_maps_to_right_grip(bool m2, bool m1, byte left, byte right)
    {
        var mapped = SteamControllerDeviceStateMapper.Map(State(auxiliary: new AuxiliaryButtonState(new[] { m2, m1 })));

        Assert.Equal(left, mapped.LGrip);
        Assert.Equal(right, mapped.RGrip);
    }

    [Theory]
    [InlineData((byte)0, (ushort)0)]
    [InlineData((byte)128, (ushort)13050)]
    [InlineData((byte)254, (ushort)25898)]
    [InlineData((byte)255, (ushort)26000)]
    public void Trigger_scaling_uses_integer_floor_behavior(byte value, ushort expected)
    {
        var mapped = SteamControllerDeviceStateMapper.Map(State(triggers: new TriggerState(value, value)));

        Assert.Equal(expected, mapped.LTrigger);
        Assert.Equal(expected, mapped.RTrigger);
    }

    [Theory]
    [InlineData((byte)0, false, (ushort)0, (byte)0)]
    [InlineData((byte)0, true, (ushort)0, (byte)1)]
    [InlineData((byte)64, true, (ushort)6525, (byte)1)]
    [InlineData((byte)255, false, (ushort)26000, (byte)0)]
    [InlineData((byte)255, true, (ushort)26000, (byte)1)]
    public void Explicit_trigger_full_pull_does_not_change_analog_magnitude(byte value, bool full, ushort expectedRaw, byte expectedDigital)
    {
        var buttons = new GamepadButtons(false, false, false, false, false, false, false, false, false, false, false, false, false, false, full, full);
        var mapped = SteamControllerDeviceStateMapper.Map(State(buttons: buttons, triggers: new TriggerState(value, value)));

        Assert.Equal(expectedRaw, mapped.LTrigger);
        Assert.Equal(expectedRaw, mapped.RTrigger);
        Assert.Equal(expectedDigital, mapped.L2);
        Assert.Equal(expectedDigital, mapped.R2);
    }

    [Fact]
    public void Left_and_right_trigger_full_pull_flags_are_independent()
    {
        var buttons = new GamepadButtons(false, false, false, false, false, false, false, false, false, false, false, false, false, false, true, false);
        var mapped = SteamControllerDeviceStateMapper.Map(State(buttons: buttons, triggers: new TriggerState(64, 128)));

        Assert.Equal(1, mapped.L2);
        Assert.Equal(0, mapped.R2);
        Assert.Equal((ushort)(64 * 26000 / 255), mapped.LTrigger);
        Assert.Equal((ushort)(128 * 26000 / 255), mapped.RTrigger);
    }

    [Fact]
    public void Mapper_has_no_frame_ownership_or_frame_field()
    {
        Assert.Null(typeof(SteamControllerDeviceStateMapper).GetField("_frame", BindingFlags.Static | BindingFlags.NonPublic));
        Assert.Null(typeof(SteamControllerDeviceState).GetField("Frame", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic));
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

    private static GamepadButtons Shoulder(string name) => name switch
    {
        "LeftBumper" => new(false, false, false, false, false, false, false, false, true, false, false, false, false, false, false, false),
        "RightBumper" => new(false, false, false, false, false, false, false, false, false, true, false, false, false, false, false, false),
        _ => throw new ArgumentOutOfRangeException(nameof(name), name, null)
    };
}
