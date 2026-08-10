using SteamInputAddonforClaw.Input;
using SteamInputAddonforClaw.Input.DirectInput;

namespace SteamInputAddonforClaw.Devices.MSI.Claw;

internal static class MsiClawControllerStateMapper
{
    internal static bool IsKnownInvalidInitialState(DirectInputState input) => input.RotationX == 32767 && input.RotationY == 32767 && input.RotationZ == 32767;

    internal static bool TryMap(DirectInputState input, out ControllerState state)
    {
        if (input.Buttons.Count < MsiClawHardware.RequiredDirectInputButtonCount || IsKnownInvalidInitialState(input)) { state = default; return false; }
        var pov = input.PointOfViewControllers.Count == 0 ? -1 : input.PointOfViewControllers[0];
        var up = pov is 0 or 4500 or 31500; var right = pov is 4500 or 9000 or 13500;
        var down = pov is 13500 or 18000 or 22500; var left = pov is 22500 or 27000 or 31500;
        Span<bool> auxiliary = stackalloc bool[MsiClawControls.Catalog.Count];
        auxiliary[MsiClawControls.Catalog.GetIndex(MsiClawControls.M1)] = input.Buttons[15];
        auxiliary[MsiClawControls.Catalog.GetIndex(MsiClawControls.M2)] = input.Buttons[16];
        if (input.X is null && input.Y is null && input.Z is null && input.RotationX is null && input.RotationY is null && input.RotationZ is null)
        { state = new ControllerState(new AuxiliaryButtonState(auxiliary)); return true; }
        state = new ControllerState(
            new GamepadButtons(input.Buttons[1], input.Buttons[2], input.Buttons[0], input.Buttons[3], up, right, down, left, input.Buttons[4], input.Buttons[5], input.Buttons[8], input.Buttons[9], input.Buttons[10], input.Buttons[11], input.Buttons[6], input.Buttons[7]),
            new(NormalizeAxis(input.X ?? 32767), NormalizeAxis(input.Y ?? 32767, true)), new(NormalizeAxis(input.Z ?? 32767), NormalizeAxis(input.RotationZ ?? 32767, true)), new(NormalizeByte(input.RotationX ?? 0), NormalizeByte(input.RotationY ?? 0)), new AuxiliaryButtonState(auxiliary));
        return true;
    }
    private static short NormalizeAxis(int value, bool invert = false)
    { var raw = Math.Clamp(value, 0, ushort.MaxValue); var normalized = invert ? short.MaxValue - raw : raw + short.MinValue; return (short)Math.Clamp(normalized, short.MinValue, short.MaxValue); }
    private static byte NormalizeByte(int value) => (byte)Math.Clamp((int)Math.Round(value * 255.0 / 65535.0), 0, 255);
}
