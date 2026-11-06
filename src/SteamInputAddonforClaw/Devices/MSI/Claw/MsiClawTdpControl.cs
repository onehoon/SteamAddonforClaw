using SteamInputAddonforClaw.Devices.Abstractions;
using SteamInputAddonforClaw.Profiles;
using SteamInputAddonforClaw.Profiles.Performance;

namespace SteamInputAddonforClaw.Devices.MSI.Claw;

/// <summary>Binds the model-neutral runtime contract to MSI Claw's existing policy and ACPI protocol.</summary>
internal sealed class MsiClawTdpControl(HandheldDeviceModelId modelId, MsiClawTdpHardware hardware) : IDeviceTdpControl
{
    private readonly MsiClawTdpHardware _hardware = hardware ?? throw new ArgumentNullException(nameof(hardware));

    internal static bool TryCreate(HandheldDeviceModelId modelId, MsiClawTdpHardware hardware,
        out MsiClawTdpControl control, out TdpPowerLimits limits)
    {
        if (MsiClawTdpPolicy.TryResolve(modelId, out var policy))
        {
            limits = policy.ToPowerLimits();
            control = new(modelId, hardware);
            return true;
        }

        limits = null!;
        control = null!;
        return false;
    }

    public void InvalidateCachedPowerLimits(string reason = "Unknown")
        => _hardware.InvalidateCachedPowerLimits(reason);

    public TdpControlApplyResult Apply(TdpPowerPair target)
    {
        var result = _hardware.Apply(modelId, target);
        return new(result.Succeeded, result.FailureStage.ToString(), result.RecoveryAttempted, result.RecoverySucceeded);
    }
}
