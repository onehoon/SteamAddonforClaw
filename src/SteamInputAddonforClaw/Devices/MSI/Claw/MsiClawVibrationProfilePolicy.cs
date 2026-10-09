using SteamInputAddonforClaw.Devices.Abstractions;

namespace SteamInputAddonforClaw.Devices.MSI.Claw;

internal static class MsiClawVibrationProfilePolicy
{
    internal static bool IsProductionPairWriteVerified(HandheldDeviceModelId modelId) =>
        modelId.Value is "msi.claw.cg3em" or "msi.claw.a2vm.8";

    internal static string GetProductionUnavailableReason(HandheldDeviceModelId modelId) =>
        modelId.Value == "msi.claw.a2vm.7"
            ? "Vibration strength is unavailable on A2VM 7 because its profile-write mapping has not been verified; no profile write was issued."
            : "Saved vibration strength is not yet applied on this model because its profile-write mapping has not been verified; no profile write was issued.";
}
