using SteamInputAddonforClaw.Devices.Abstractions;

namespace SteamInputAddonforClaw.Devices.MSI.Claw;

internal static class MsiClawVibrationProfilePolicy
{
    internal static bool IsProductionPairWriteVerified(HandheldDeviceModelId modelId) =>
        modelId.Value == "msi.claw.cg3em";
}
