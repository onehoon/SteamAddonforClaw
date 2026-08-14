using SteamInputAddonforClaw.Input;

namespace SteamInputAddonforClaw.VirtualOutput.Viiper;

internal static class SteamControllerDeviceStateMapper
{
    private const int MaxAnalogTrigger = 26000;

    internal static SteamControllerDeviceState Map(ControllerState state)
    {
        var buttons = state.Buttons;
        var rightPad = state.RightStick;
        var rightPadPress = buttons.RightStickClick;

        return new SteamControllerDeviceState
        {
            A = ToByte(buttons.A),
            X = ToByte(buttons.X),
            B = ToByte(buttons.B),
            Y = ToByte(buttons.Y),
            L1 = ToByte(buttons.LeftBumper),
            R1 = ToByte(buttons.RightBumper),
            L2 = ToByte(buttons.LeftTriggerFull),
            R2 = ToByte(buttons.RightTriggerFull),
            Menu = ToByte(buttons.Back),
            Options = ToByte(buttons.Start),
            DPadDown = ToByte(buttons.DPadDown),
            DPadLeft = ToByte(buttons.DPadLeft),
            DPadRight = ToByte(buttons.DPadRight),
            DPadUp = ToByte(buttons.DPadUp),
            L3 = ToByte(buttons.LeftStickClick),
            LGrip = ToByte(state.Auxiliary[(int)AuxiliaryButtonSlot.LeftRear]),
            RGrip = ToByte(state.Auxiliary[(int)AuxiliaryButtonSlot.RightRear]),
            RPadTouch = ToByte(ClassicSteamControllerRightPadPolicy.IsTouched(rightPad, rightPadPress)),
            RPadPress = ToByte(rightPadPress),
            RPadX = rightPad.X,
            RPadY = rightPad.Y,
            LTrigger = ScaleTrigger(state.Triggers.Left),
            RTrigger = ScaleTrigger(state.Triggers.Right),
            LStickX = state.LeftStick.X,
            LStickY = state.LeftStick.Y
        };
    }

    private static byte ToByte(bool value) => value ? (byte)1 : (byte)0;

    private static ushort ScaleTrigger(byte value) => (ushort)(value * MaxAnalogTrigger / byte.MaxValue);
}
