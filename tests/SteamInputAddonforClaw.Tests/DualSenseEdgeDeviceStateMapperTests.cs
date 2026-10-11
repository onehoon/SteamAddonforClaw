using SteamInputAddonforClaw.Devices.MSI.Claw;
using SteamInputAddonforClaw.Input;
using SteamInputAddonforClaw.VirtualOutput.Viiper;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class DualSenseEdgeDeviceStateMapperTests
{
    [Fact]
    public void MapsButtonsTriggersAndDpadWithoutUsingUnmappedControls()
    {
        var buttons = new GamepadButtons(
            A: true, B: true, X: true, Y: true,
            DPadUp: true, DPadRight: true, DPadDown: false, DPadLeft: false,
            LeftBumper: true, RightBumper: true, Back: true, Start: true,
            LeftStickClick: true, RightStickClick: true,
            LeftTriggerFull: true, RightTriggerFull: false);
        var source = State(buttons, triggers: new TriggerState(33, 211), auxiliary: new AuxiliaryButtonState([true, true]));

        var mapped = Map(source);

        Assert.Equal(0x10u | 0x20u | 0x40u | 0x80u | 0x100u | 0x200u | 0x400u | 0x2000u | 0x1000u | 0x4000u | 0x8000u | 0x00400000u | 0x00800000u, mapped.Buttons);
        Assert.Equal((byte)0x9, mapped.DPad); // Up + Right diagonal
        Assert.Equal((byte)33, mapped.L2);
        Assert.Equal((byte)211, mapped.R2);
        Assert.Equal((ushort)0, mapped.Touch1X);
        Assert.Equal((byte)0, mapped.Touch1Active);
        Assert.Equal((short)0, mapped.GyroX);
        Assert.Equal((short)0, mapped.AccelZ);
    }

    [Fact]
    public void MapsAllDpadDirectionsAndSuppressesHeldRearButtonsIndependently()
    {
        var buttons = new GamepadButtons(false, false, false, false, true, true, true, true,
            false, false, false, false, false, false, false, false);
        var source = State(buttons, auxiliary: new AuxiliaryButtonState([true, true]));

        var mapped = Map(source, suppressM1: true, suppressM2: false);

        Assert.Equal((byte)0xF, mapped.DPad);
        Assert.Equal(0x00400000u, mapped.Buttons); // M2 -> L4 only
    }

    [Fact]
    public void ConvertsStickBoundariesWithNearestAwayFromZeroAndPreservesQuadrants()
    {
        var source = State(default,
            leftStick: new StickState(short.MinValue, 128),
            rightStick: new StickState(-128, short.MaxValue));

        var mapped = Map(source);

        Assert.Equal((sbyte)-128, mapped.LX);
        Assert.Equal((sbyte)-1, mapped.LY);
        Assert.Equal((sbyte)-1, mapped.RX);
        Assert.Equal((sbyte)-128, mapped.RY);
        Assert.Equal((sbyte)0, Map(State(default)).LX);
    }

    [Fact]
    public void MapsPhysicalUpAndDownToSonyHidVerticalDirectionOnBothSticks()
    {
        var neutral = Map(State(default));
        Assert.Equal((sbyte)0, neutral.LY);
        Assert.Equal((sbyte)0, neutral.RY);

        // Physical UP is positive in Claw state and must become Sony HID 0; DOWN becomes HID 255.
        var leftUpRightDown = Map(State(default,
            leftStick: new StickState(1111, short.MaxValue),
            rightStick: new StickState(-2222, short.MinValue)));
        Assert.Equal((sbyte)4, leftUpRightDown.LX);
        Assert.Equal((sbyte)-128, leftUpRightDown.LY);
        Assert.Equal((sbyte)-9, leftUpRightDown.RX);
        Assert.Equal((sbyte)127, leftUpRightDown.RY);

        var leftDownRightUp = Map(State(default,
            leftStick: new StickState(short.MinValue, short.MinValue),
            rightStick: new StickState(short.MaxValue, short.MaxValue)));
        Assert.Equal((sbyte)-128, leftDownRightUp.LX);
        Assert.Equal((sbyte)127, leftDownRightUp.LY);
        Assert.Equal((sbyte)127, leftDownRightUp.RX);
        Assert.Equal((sbyte)-128, leftDownRightUp.RY);

        var intermediate = Map(State(default,
            leftStick: new StickState(3210, 128),
            rightStick: new StickState(-6543, -128)));
        Assert.Equal((sbyte)13, intermediate.LX);
        Assert.Equal((sbyte)-1, intermediate.LY);
        Assert.Equal((sbyte)-26, intermediate.RX);
        Assert.Equal((sbyte)1, intermediate.RY);

        var xOnly = Map(State(default,
            leftStick: new StickState(4096, 0),
            rightStick: new StickState(-4096, 0)));
        Assert.Equal((sbyte)16, xOnly.LX);
        Assert.Equal((sbyte)0, xOnly.LY);
        Assert.Equal((sbyte)-16, xOnly.RX);
        Assert.Equal((sbyte)0, xOnly.RY);
    }

    [Fact]
    public void MapsFreshMotionToAllSixEdgeFieldsUsingDirectNormalizedAxes()
    {
        var motion = Motion(10, -20, 30, 0.5, -1, 2, receiveTicks: 1000);

        var mapped = Map(State(new GamepadButtons(true, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false)), motion: motion, nowTicks: 1000);

        Assert.Equal((short)164, mapped.GyroX);
        Assert.Equal((short)-328, mapped.GyroY);
        Assert.Equal((short)492, mapped.GyroZ);
        Assert.Equal((short)4096, mapped.AccelX);
        Assert.Equal((short)-8192, mapped.AccelY);
        Assert.Equal((short)16384, mapped.AccelZ);
        Assert.NotEqual(0u, mapped.Buttons); // motion availability does not gate ordinary input
    }

    [Fact]
    public void ClampsMotionAndZerosEveryImuFieldForStaleIncompleteOrNonFiniteSnapshots()
    {
        var tooLarge = Motion(10000, -10000, 1, 10, -10, 1, receiveTicks: 1000);
        var clamped = Map(State(default), motion: tooLarge, nowTicks: 1000);
        Assert.Equal(short.MaxValue, clamped.GyroX);
        Assert.Equal(short.MinValue, clamped.GyroY);
        Assert.Equal(short.MaxValue, clamped.AccelX);
        Assert.Equal(short.MinValue, clamped.AccelY);

        AssertMotionNeutral(Map(State(new GamepadButtons(true, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false)), motion: tooLarge, nowTicks: 10000));
        AssertMotionNeutral(Map(State(default), motion: tooLarge with { HasAccelerometer = false }, nowTicks: 1000));
        AssertMotionNeutral(Map(State(default), motion: tooLarge with { GyroYDegPerSecond = double.NaN }, nowTicks: 1000));
        Assert.NotEqual(0u, Map(State(new GamepadButtons(true, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false)), motion: tooLarge with { GyroXDegPerSecond = double.NaN }, nowTicks: 1000).Buttons);
    }

    private static DSDeviceState Map(ControllerState state, bool suppressM1 = false, bool suppressM2 = false, MsiClawMotionState? motion = null, long nowTicks = 1000) =>
        DualSenseEdgeDeviceStateMapper.Map(state, suppressM1, suppressM2, motion, nowTicks, timestampFrequency: 1000);

    private static ControllerState State(
        GamepadButtons buttons,
        StickState leftStick = default,
        StickState rightStick = default,
        TriggerState triggers = default,
        AuxiliaryButtonState auxiliary = default) =>
        new(buttons, leftStick, rightStick, triggers, auxiliary);

    private static MsiClawMotionState Motion(double gx, double gy, double gz, double ax, double ay, double az, long receiveTicks) => new(
        gx, gy, gz, ax, ay, az,
        receiveTicks, receiveTicks, null, null,
        true, true, "Test", "Test");

    private static void AssertMotionNeutral(DSDeviceState state)
    {
        Assert.Equal((short)0, state.GyroX);
        Assert.Equal((short)0, state.GyroY);
        Assert.Equal((short)0, state.GyroZ);
        Assert.Equal((short)0, state.AccelX);
        Assert.Equal((short)0, state.AccelY);
        Assert.Equal((short)0, state.AccelZ);
    }
}
