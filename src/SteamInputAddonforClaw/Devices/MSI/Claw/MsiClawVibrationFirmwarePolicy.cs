using SteamInputAddonforClaw.Devices.Abstractions;

namespace SteamInputAddonforClaw.Devices.MSI.Claw;

internal static class MsiClawVibrationFirmwarePolicy
{
    // Profile serialization offsets alone do not prove direct firmware-address ownership for a model.
    internal static bool IsDirectMotorProfileAddressVerified(HandheldDeviceModelId modelId) => modelId.Value switch
    {
        "msi.claw.a2vm.7" => false,
        "msi.claw.a2vm.8" => false,
        "msi.claw.cg3em" => false,
        _ => false
    };
}
