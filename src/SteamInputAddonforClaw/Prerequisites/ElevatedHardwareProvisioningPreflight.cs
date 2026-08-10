using SteamInputAddonforClaw.Devices;

namespace SteamInputAddonforClaw.Prerequisites;

internal sealed record ElevatedHardwareProvisioningPreflightResult(
    HardwareCompatibilityAssessment Hardware,
    ProvisioningStorageAssessment? Storage)
{
    public bool AllowsProvisioning => Hardware.Status == HardwareCompatibilityStatus.Supported && Storage?.Status == ProvisioningStorageStatus.Trusted;
}

internal static class ElevatedHardwareProvisioningPreflight
{
    public static ElevatedHardwareProvisioningPreflightResult Evaluate(
        IWindowsDeviceProbeContextFactory probeContextFactory,
        IHardwareCompatibilityEvaluator hardwareCompatibilityEvaluator,
        Func<ProvisioningStorageAssessment> establishTrustedStorage)
    {
        ArgumentNullException.ThrowIfNull(probeContextFactory);
        ArgumentNullException.ThrowIfNull(hardwareCompatibilityEvaluator);
        ArgumentNullException.ThrowIfNull(establishTrustedStorage);

        var hardware = hardwareCompatibilityEvaluator.Evaluate(probeContextFactory.Capture());
        if (hardware.Status != HardwareCompatibilityStatus.Supported)
            return new(hardware, null);

        return new(hardware, establishTrustedStorage());
    }
}
