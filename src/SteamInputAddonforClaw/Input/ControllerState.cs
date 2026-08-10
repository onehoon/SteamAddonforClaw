namespace SteamInputAddonforClaw.Input;

public readonly record struct GamepadButtons(bool A, bool B, bool X, bool Y, bool DPadUp, bool DPadRight, bool DPadDown, bool DPadLeft, bool LeftBumper, bool RightBumper, bool Back, bool Start, bool LeftStickClick, bool RightStickClick, bool LeftTriggerFull, bool RightTriggerFull);
public readonly record struct StickState(short X, short Y);
public readonly record struct TriggerState(byte Left, byte Right);

public readonly record struct ControllerState(GamepadButtons Buttons, StickState LeftStick, StickState RightStick, TriggerState Triggers, AuxiliaryButtonState Auxiliary)
{
    public ControllerState(AuxiliaryButtonState auxiliary) : this(default, default, default, default, auxiliary) { }
}
