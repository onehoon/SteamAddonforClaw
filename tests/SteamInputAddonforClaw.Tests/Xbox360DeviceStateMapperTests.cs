using SteamInputAddonforClaw.Contracts.BackButtons;
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

    [Theory]
    [InlineData(Xbox360BackButtonTarget.A, Xbox360ButtonBits.A)]
    [InlineData(Xbox360BackButtonTarget.B, Xbox360ButtonBits.B)]
    [InlineData(Xbox360BackButtonTarget.X, Xbox360ButtonBits.X)]
    [InlineData(Xbox360BackButtonTarget.Y, Xbox360ButtonBits.Y)]
    [InlineData(Xbox360BackButtonTarget.DPadUp, Xbox360ButtonBits.DPadUp)]
    [InlineData(Xbox360BackButtonTarget.DPadRight, Xbox360ButtonBits.DPadRight)]
    [InlineData(Xbox360BackButtonTarget.DPadDown, Xbox360ButtonBits.DPadDown)]
    [InlineData(Xbox360BackButtonTarget.DPadLeft, Xbox360ButtonBits.DPadLeft)]
    [InlineData(Xbox360BackButtonTarget.LeftBumper, Xbox360ButtonBits.LeftShoulder)]
    [InlineData(Xbox360BackButtonTarget.RightBumper, Xbox360ButtonBits.RightShoulder)]
    [InlineData(Xbox360BackButtonTarget.LeftStickClick, Xbox360ButtonBits.LeftThumb)]
    [InlineData(Xbox360BackButtonTarget.RightStickClick, Xbox360ButtonBits.RightThumb)]
    [InlineData(Xbox360BackButtonTarget.View, Xbox360ButtonBits.Back)]
    [InlineData(Xbox360BackButtonTarget.Menu, Xbox360ButtonBits.Start)]
    [InlineData(Xbox360BackButtonTarget.XboxGuide, Xbox360ButtonBits.Guide)]
    public void M1_maps_each_digital_target_to_the_exact_Xbox360_bit(Xbox360BackButtonTarget target, uint expectedBit)
    {
        var mapped = Xbox360DeviceStateMapper.Map(
            State(auxiliary: new AuxiliaryButtonState([false, true])),
            new BackButtonMappingSettings(target, Xbox360BackButtonTarget.Disabled));

        Assert.Equal(expectedBit, mapped.Buttons);
    }

    [Fact]
    public void M2_maps_to_right_bumper_without_changing_M1_or_physical_controls()
    {
        var mapped = Xbox360DeviceStateMapper.Map(
            State(buttons: Button("A"), auxiliary: new AuxiliaryButtonState([true, false])),
            new BackButtonMappingSettings(Xbox360BackButtonTarget.Disabled, Xbox360BackButtonTarget.RightBumper));

        Assert.Equal(Xbox360ButtonBits.A | Xbox360ButtonBits.RightShoulder, mapped.Buttons);
    }

    [Fact]
    public void Trigger_targets_are_full_pull_only_while_the_rear_button_is_pressed()
    {
        var mapping = new BackButtonMappingSettings(Xbox360BackButtonTarget.LeftTrigger, Xbox360BackButtonTarget.RightTrigger);

        var held = Xbox360DeviceStateMapper.Map(
            State(triggers: new TriggerState(91, 37), auxiliary: new AuxiliaryButtonState([false, true])), mapping);
        var m2Held = Xbox360DeviceStateMapper.Map(
            State(triggers: new TriggerState(91, 37), auxiliary: new AuxiliaryButtonState([true, false])), mapping);
        var released = Xbox360DeviceStateMapper.Map(
            State(triggers: new TriggerState(91, 37), auxiliary: new AuxiliaryButtonState([false, false])), mapping);

        Assert.Equal((byte)255, held.LT);
        Assert.Equal((byte)37, held.RT);
        Assert.Equal((byte)91, m2Held.LT);
        Assert.Equal((byte)255, m2Held.RT);
        Assert.Equal((byte)91, released.LT);
        Assert.Equal((byte)37, released.RT);
    }

    [Fact]
    public void Physical_and_rear_button_targets_are_additive_and_same_target_is_or_combined()
    {
        var mapping = new BackButtonMappingSettings(Xbox360BackButtonTarget.A, Xbox360BackButtonTarget.A);
        var physicalAOnly = Xbox360DeviceStateMapper.Map(
            State(buttons: Button("A"), auxiliary: new AuxiliaryButtonState([false, false])), mapping);
        var m1Only = Xbox360DeviceStateMapper.Map(
            State(auxiliary: new AuxiliaryButtonState([false, true])), mapping);
        var m2Only = Xbox360DeviceStateMapper.Map(
            State(auxiliary: new AuxiliaryButtonState([true, false])), mapping);
        var bothReleased = Xbox360DeviceStateMapper.Map(
            State(auxiliary: new AuxiliaryButtonState([false, false])), mapping);

        Assert.Equal(Xbox360ButtonBits.A, physicalAOnly.Buttons);
        Assert.Equal(Xbox360ButtonBits.A, m1Only.Buttons);
        Assert.Equal(Xbox360ButtonBits.A, m2Only.Buttons);
        Assert.Equal(0u, bothReleased.Buttons);
    }

    [Fact]
    public void Suppressing_M1_removes_only_the_rear_contribution_and_preserves_physical_A()
    {
        var mapped = Xbox360DeviceStateMapper.Map(
            State(buttons: Button("A"), auxiliary: new AuxiliaryButtonState([false, true])),
            new BackButtonMappingSettings(Xbox360BackButtonTarget.A, Xbox360BackButtonTarget.Disabled),
            suppressM1: true);

        Assert.Equal(Xbox360ButtonBits.A, mapped.Buttons);
    }

    [Fact]
    public void Suppression_is_independent_for_M1_and_M2()
    {
        var mapped = Xbox360DeviceStateMapper.Map(
            State(auxiliary: new AuxiliaryButtonState([true, true])),
            new BackButtonMappingSettings(Xbox360BackButtonTarget.A, Xbox360BackButtonTarget.RightBumper),
            suppressM1: true);

        Assert.Equal(Xbox360ButtonBits.RightShoulder, mapped.Buttons);
    }

    [Fact]
    public void Suppressing_a_trigger_target_preserves_physical_analog_travel()
    {
        var mapped = Xbox360DeviceStateMapper.Map(
            State(triggers: new TriggerState(91, 37), auxiliary: new AuxiliaryButtonState([false, true])),
            new BackButtonMappingSettings(Xbox360BackButtonTarget.LeftTrigger, Xbox360BackButtonTarget.Disabled),
            suppressM1: true);

        Assert.Equal((byte)91, mapped.LT);
        Assert.Equal((byte)37, mapped.RT);
    }

    [Fact]
    public void Default_controller_state_is_safe_with_a_non_default_mapping()
    {
        var mapped = Xbox360DeviceStateMapper.Map(
            default,
            new BackButtonMappingSettings(Xbox360BackButtonTarget.A, Xbox360BackButtonTarget.RightTrigger));

        Assert.Equal(0u, mapped.Buttons);
        Assert.Equal((byte)0, mapped.LT);
        Assert.Equal((byte)0, mapped.RT);
    }

    [Fact]
    public void Unknown_target_fails_closed_without_throwing_or_output()
    {
        var mapped = Xbox360DeviceStateMapper.Map(
            State(auxiliary: new AuxiliaryButtonState([false, true])),
            new BackButtonMappingSettings((Xbox360BackButtonTarget)999, Xbox360BackButtonTarget.Disabled));

        Assert.Equal(0u, mapped.Buttons);
        Assert.Equal((byte)0, mapped.LT);
        Assert.Equal((byte)0, mapped.RT);
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
