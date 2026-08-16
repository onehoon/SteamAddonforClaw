using SteamInputAddonforClaw.Input;
using SteamInputAddonforClaw.VirtualOutput.Viiper;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class Xbox360DeviceStateMapperTests
{
    [Theory]
    [InlineData("A", Xbox360ButtonBits.A)]
    [InlineData("B", Xbox360ButtonBits.B)]
    [InlineData("X", Xbox360ButtonBits.X)]
    [InlineData("Y", Xbox360ButtonBits.Y)]
    [InlineData("DPadUp", Xbox360ButtonBits.DPadUp)]
    [InlineData("DPadDown", Xbox360ButtonBits.DPadDown)]
    [InlineData("DPadLeft", Xbox360ButtonBits.DPadLeft)]
    [InlineData("DPadRight", Xbox360ButtonBits.DPadRight)]
    [InlineData("LeftBumper", Xbox360ButtonBits.LeftShoulder)]
    [InlineData("RightBumper", Xbox360ButtonBits.RightShoulder)]
    [InlineData("LeftStickClick", Xbox360ButtonBits.LeftThumb)]
    [InlineData("RightStickClick", Xbox360ButtonBits.RightThumb)]
    [InlineData("Start", Xbox360ButtonBits.Start)]
    [InlineData("Back", Xbox360ButtonBits.Back)]
    public void Each_standard_button_maps_to_exactly_its_own_bit(string button, uint expectedBit)
    {
        var mapped = Xbox360DeviceStateMapper.Map(State(buttons: Button(button)));

        Assert.Equal(expectedBit, mapped.Buttons);
    }

    [Fact]
    public void Dpad_up_and_right_combine_without_affecting_other_bits()
    {
        var buttons = new GamepadButtons(false, false, false, false, true, true, false, false, false, false, false, false, false, false, false, false);
        var mapped = Xbox360DeviceStateMapper.Map(State(buttons: buttons));

        Assert.Equal(Xbox360ButtonBits.DPadUp | Xbox360ButtonBits.DPadRight, mapped.Buttons);
    }

    [Fact]
    public void Dpad_down_and_left_combine_without_affecting_other_bits()
    {
        var buttons = new GamepadButtons(false, false, false, false, false, false, true, true, false, false, false, false, false, false, false, false);
        var mapped = Xbox360DeviceStateMapper.Map(State(buttons: buttons));

        Assert.Equal(Xbox360ButtonBits.DPadDown | Xbox360ButtonBits.DPadLeft, mapped.Buttons);
    }

    [Fact]
    public void Multiple_simultaneous_buttons_produce_the_OR_combination_of_their_bits()
    {
        // A + Start + LeftBumper + DPadUp, in field-declaration order: A, B, X, Y, DPadUp,
        // DPadRight, DPadDown, DPadLeft, LeftBumper, RightBumper, Back, Start, LeftStickClick,
        // RightStickClick, LeftTriggerFull, RightTriggerFull.
        var buttons = new GamepadButtons(true, false, false, false, true, false, false, false, true, false, false, true, false, false, false, false);
        var mapped = Xbox360DeviceStateMapper.Map(State(buttons: buttons));

        var expected = Xbox360ButtonBits.A | Xbox360ButtonBits.DPadUp | Xbox360ButtonBits.LeftShoulder | Xbox360ButtonBits.Start;
        Assert.Equal(expected, mapped.Buttons);
    }

    [Fact]
    public void Neutral_state_leaves_Buttons_and_Guide_zero()
    {
        var mapped = Xbox360DeviceStateMapper.Map(State());

        Assert.Equal(0u, mapped.Buttons);
        Assert.Equal(0u, mapped.Buttons & Xbox360ButtonBits.Guide);
    }

    [Theory]
    [InlineData((byte)0)]
    [InlineData((byte)128)]
    [InlineData((byte)255)]
    public void Triggers_are_preserved_unchanged(byte value)
    {
        var mapped = Xbox360DeviceStateMapper.Map(State(triggers: new TriggerState(value, value)));

        Assert.Equal(value, mapped.LT);
        Assert.Equal(value, mapped.RT);
    }

    [Fact]
    public void Left_and_right_trigger_values_are_not_swapped()
    {
        // Equal-value cases above can't catch LT/RT being accidentally cross-wired; this proves
        // each side lands in its own field.
        var mapped = Xbox360DeviceStateMapper.Map(State(triggers: new TriggerState(17, 231)));

        Assert.Equal((byte)17, mapped.LT);
        Assert.Equal((byte)231, mapped.RT);
    }

    [Fact]
    public void Digital_full_pull_trigger_flags_do_not_set_any_button_bit()
    {
        // LeftTriggerFull/RightTriggerFull exist on GamepadButtons but have no Xbox 360 button
        // equivalent -- they must not leak into Buttons (Guide included).
        var buttons = new GamepadButtons(false, false, false, false, false, false, false, false, false, false, false, false, false, false, true, true);
        var mapped = Xbox360DeviceStateMapper.Map(State(buttons: buttons));

        Assert.Equal(0u, mapped.Buttons);
        Assert.Equal(0u, mapped.Buttons & Xbox360ButtonBits.Guide);
    }

    [Theory]
    [InlineData((short)0)]
    public void Stick_axes_are_preserved_unchanged_at_zero(short value)
    {
        var mapped = Xbox360DeviceStateMapper.Map(State(leftStick: new StickState(value, value), rightStick: new StickState(value, value)));

        Assert.Equal(value, mapped.LX);
        Assert.Equal(value, mapped.LY);
        Assert.Equal(value, mapped.RX);
        Assert.Equal(value, mapped.RY);
    }

    [Fact]
    public void Stick_axes_are_preserved_unchanged_at_extremes()
    {
        var mapped = Xbox360DeviceStateMapper.Map(State(
            leftStick: new StickState(short.MinValue, short.MaxValue),
            rightStick: new StickState(short.MaxValue, short.MinValue)));

        Assert.Equal(short.MinValue, mapped.LX);
        Assert.Equal(short.MaxValue, mapped.LY);
        Assert.Equal(short.MaxValue, mapped.RX);
        Assert.Equal(short.MinValue, mapped.RY);
    }

    [Fact]
    public void Left_stick_up_preserves_the_same_positive_Y_value()
    {
        var mapped = Xbox360DeviceStateMapper.Map(State(leftStick: new StickState(0, 12000)));

        Assert.Equal((short)12000, mapped.LY);
    }

    [Fact]
    public void Left_stick_down_preserves_the_same_negative_Y_value()
    {
        var mapped = Xbox360DeviceStateMapper.Map(State(leftStick: new StickState(0, -12000)));

        Assert.Equal((short)-12000, mapped.LY);
    }

    [Fact]
    public void Right_stick_up_preserves_the_same_positive_Y_value()
    {
        var mapped = Xbox360DeviceStateMapper.Map(State(rightStick: new StickState(0, 12000)));

        Assert.Equal((short)12000, mapped.RY);
    }

    [Fact]
    public void Right_stick_down_preserves_the_same_negative_Y_value()
    {
        var mapped = Xbox360DeviceStateMapper.Map(State(rightStick: new StickState(0, -12000)));

        Assert.Equal((short)-12000, mapped.RY);
    }

    [Fact]
    public void MSI_auxiliary_buttons_do_not_affect_the_Xbox360_result()
    {
        var withAuxiliary = Xbox360DeviceStateMapper.Map(State(auxiliary: new AuxiliaryButtonState(new[] { true, true })));
        var neutral = Xbox360DeviceStateMapper.Map(State());

        Assert.Equal(neutral.Buttons, withAuxiliary.Buttons);
        Assert.Equal(0u, withAuxiliary.Buttons);
    }

    [Fact]
    public void Reserved_is_always_six_zero_bytes()
    {
        var buttons = new GamepadButtons(true, true, true, true, true, true, true, true, true, true, true, true, true, true, true, true);
        var mapped = Xbox360DeviceStateMapper.Map(State(
            buttons: buttons,
            leftStick: new StickState(short.MaxValue, short.MinValue),
            rightStick: new StickState(short.MinValue, short.MaxValue),
            triggers: new TriggerState(255, 255),
            auxiliary: new AuxiliaryButtonState(new[] { true, true })));

        Assert.Equal(0, mapped.Reserved0);
        Assert.Equal(0, mapped.Reserved1);
        Assert.Equal(0, mapped.Reserved2);
        Assert.Equal(0, mapped.Reserved3);
        Assert.Equal(0, mapped.Reserved4);
        Assert.Equal(0, mapped.Reserved5);
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
        "B" => new(false, true, false, false, false, false, false, false, false, false, false, false, false, false, false, false),
        "X" => new(false, false, true, false, false, false, false, false, false, false, false, false, false, false, false, false),
        "Y" => new(false, false, false, true, false, false, false, false, false, false, false, false, false, false, false, false),
        "DPadUp" => new(false, false, false, false, true, false, false, false, false, false, false, false, false, false, false, false),
        "DPadRight" => new(false, false, false, false, false, true, false, false, false, false, false, false, false, false, false, false),
        "DPadDown" => new(false, false, false, false, false, false, true, false, false, false, false, false, false, false, false, false),
        "DPadLeft" => new(false, false, false, false, false, false, false, true, false, false, false, false, false, false, false, false),
        "LeftBumper" => new(false, false, false, false, false, false, false, false, true, false, false, false, false, false, false, false),
        "RightBumper" => new(false, false, false, false, false, false, false, false, false, true, false, false, false, false, false, false),
        "Back" => new(false, false, false, false, false, false, false, false, false, false, true, false, false, false, false, false),
        "Start" => new(false, false, false, false, false, false, false, false, false, false, false, true, false, false, false, false),
        "LeftStickClick" => new(false, false, false, false, false, false, false, false, false, false, false, false, true, false, false, false),
        "RightStickClick" => new(false, false, false, false, false, false, false, false, false, false, false, false, false, true, false, false),
        _ => throw new ArgumentOutOfRangeException(nameof(name), name, null)
    };
}
