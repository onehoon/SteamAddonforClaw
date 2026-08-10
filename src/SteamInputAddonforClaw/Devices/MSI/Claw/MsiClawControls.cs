using SteamInputAddonforClaw.Devices.Abstractions;

namespace SteamInputAddonforClaw.Devices.MSI.Claw;

internal static class MsiClawControls
{
    public static readonly AuxiliaryControlId M1 = new("msi.claw.m1");
    public static readonly AuxiliaryControlId M2 = new("msi.claw.m2");

    public static AuxiliaryControlCatalog Catalog { get; } = new(
    [
        new(M2, "M2", ControlSurface.Rear, ControlSide.Left, 1),
        new(M1, "M1", ControlSurface.Rear, ControlSide.Right, 2)
    ]);
}
