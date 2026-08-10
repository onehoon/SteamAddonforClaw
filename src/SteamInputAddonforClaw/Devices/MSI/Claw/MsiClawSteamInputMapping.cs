using SteamInputAddonforClaw.Devices.Abstractions;

namespace SteamInputAddonforClaw.Devices.MSI.Claw;

public enum SteamGripSide
{
    Left,
    Right
}

internal static class MsiClawSteamInputMapping
{
    internal static SteamGripSide GetGrip(AuxiliaryControlId control) =>
        control == MsiClawControls.M2 ? SteamGripSide.Left :
        control == MsiClawControls.M1 ? SteamGripSide.Right :
        throw new ArgumentException($"Unknown MSI Claw control: {control}", nameof(control));
}
