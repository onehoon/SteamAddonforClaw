using SteamInputAddonforClaw.Devices.Abstractions;
using SteamInputAddonforClaw.Profiles;

namespace SteamInputAddonforClaw.Devices.MSI.Claw;

/// <summary>Model-specific TDP limits and the Center M-compatible Shift selector.</summary>
internal sealed record MsiClawTdpPolicy(
    int Pl1MinimumWatts,
    int Pl1MaximumWatts,
    int Pl2MinimumWatts,
    int Pl2MaximumWatts,
    int ManualCompatibleShiftSelector)
{
    public bool IsValidPl1(int watts) => watts >= Pl1MinimumWatts && watts <= Pl1MaximumWatts;

    public bool IsValidPl2(int watts) => watts >= Pl2MinimumWatts && watts <= Pl2MaximumWatts;

    public bool IsValid(TdpPowerPair pair) => IsValidPl1(pair.Pl1Watts) && IsValidPl2(pair.Pl2Watts);

    public static bool TryResolve(HandheldDeviceModelId modelId, out MsiClawTdpPolicy policy)
    {
        if (modelId == MsiClawDeviceModels.Claw7AiPlusA2vm.Id ||
            modelId == MsiClawDeviceModels.Claw8AiPlusA2vm.Id)
        {
            policy = new(8, 30, 8, 37, 0);
            return true;
        }

        if (modelId == MsiClawDeviceModels.Claw8ExAiPlus.Id)
        {
            policy = new(8, 35, 8, 45, 6);
            return true;
        }

        policy = null!;
        return false;
    }
}
