using SteamInputAddonforClaw.Devices.Abstractions;

namespace SteamInputAddonforClaw.Devices.MSI.Claw;

internal static class MsiClawDeviceModels
{
    public static readonly HandheldDeviceModelDescriptor Claw8ExAiPlus = new(
        new HandheldDeviceModelId("msi.claw.cg3em"),
        new HandheldDeviceId("msi.claw"),
        "MSI Claw 8 EX AI+ CG3EM",
        "MS-1T91");
}

internal sealed class MsiClawDeviceModelResolver : IHandheldDeviceModelResolver
{
    public HandheldDeviceModelResolution Resolve(DeviceProbeContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var boardProduct = context.BaseBoardProduct?.Trim();
        if (string.IsNullOrWhiteSpace(boardProduct))
            return new(HandheldDeviceModelResolutionStatus.Indeterminate, null, "BaseBoardProductUnavailable");
        if (string.Equals(boardProduct, MsiClawDeviceModels.Claw8ExAiPlus.HardwareModelId, StringComparison.OrdinalIgnoreCase))
            return new(HandheldDeviceModelResolutionStatus.Matched, MsiClawDeviceModels.Claw8ExAiPlus, "MsiClawModelMatched");
        return new(HandheldDeviceModelResolutionStatus.Unsupported, null, "MsiClawModelUnsupported");
    }
}
