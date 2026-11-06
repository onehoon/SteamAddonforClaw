using SteamInputAddonforClaw.Profiles;

namespace SteamInputAddonforClaw.Profiles.Performance;

/// <summary>Model-neutral power-limit ranges consumed by profiles and the TDP runtime.</summary>
internal sealed record TdpPowerLimits(int Pl1MinimumWatts, int Pl1MaximumWatts, int Pl2MinimumWatts, int Pl2MaximumWatts)
{
    internal bool IsValidPl1(int watts) => watts >= Pl1MinimumWatts && watts <= Pl1MaximumWatts;
    internal bool IsValidPl2(int watts) => watts >= Pl2MinimumWatts && watts <= Pl2MaximumWatts;
    internal bool IsValid(TdpPowerPair pair) => IsValidPl1(pair.Pl1Watts) && IsValidPl2(pair.Pl2Watts);
}

/// <summary>Neutral outcome for applying a TDP target to a device-specific power controller.</summary>
internal sealed record TdpControlApplyResult(bool Succeeded, string FailureStage = "None", bool RecoveryAttempted = false, bool RecoverySucceeded = false);

/// <summary>Hardware boundary used by the shared profile TDP runtime.</summary>
internal interface IDeviceTdpControl
{
    void InvalidateCachedPowerLimits(string reason = "Unknown");
    TdpControlApplyResult Apply(TdpPowerPair target);
}
