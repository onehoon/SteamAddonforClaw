using SteamInputAddonforClaw.Devices.Abstractions;

namespace SteamInputAddonforClaw.Devices.MSI.Claw;

internal static class MsiClawDeviceModels
{
    internal static readonly HandheldDeviceId FamilyId = new("msi.claw");

    private static readonly HandheldModelFeatures ExistingIntelClawFeatures = new(
        SupportsTdp: true,
        SupportsBatteryChargeLimit: true,
        SupportsControllerVibrationStrength: true,
        SupportsControllerLed: true,
        SupportsMotion: true,
        SupportsIntelFpsLimit: true,
        SupportsIntelGpuMinimumClock: true);

    public static readonly HandheldDeviceModelDescriptor Claw7AiPlusA2vm = new(
        new HandheldDeviceModelId("msi.claw.a2vm.7"),
        FamilyId,
        "MSI Claw 7 AI+ A2VM",
        "MS-1T42")
    {
        Features = ExistingIntelClawFeatures
    };

    public static readonly HandheldDeviceModelDescriptor Claw8AiPlusA2vm = new(
        new HandheldDeviceModelId("msi.claw.a2vm.8"),
        FamilyId,
        "MSI Claw 8 AI+ A2VM",
        "MS-1T52")
    {
        Features = ExistingIntelClawFeatures
    };

    public static readonly HandheldDeviceModelDescriptor Claw8ExAiPlus = new(
        new HandheldDeviceModelId("msi.claw.cg3em"),
        FamilyId,
        "MSI Claw 8 EX AI+ CG3EM",
        "MS-1T91")
    {
        Features = ExistingIntelClawFeatures
    };

    // Exact, known-model BaseBoard contract only -- no fuzzy matching, no Contains, no
    // inference from product-name/CPU/GPU text. Each MSI Claw board maps to exactly one
    // model and reuses the same shared non-gyro controller pipeline.
    public static readonly IReadOnlyList<HandheldDeviceModelDescriptor> All =
    [
        Claw7AiPlusA2vm,
        Claw8AiPlusA2vm,
        Claw8ExAiPlus
    ];

    internal static bool TryGetFeatures(
        HandheldDeviceId familyId,
        HandheldDeviceModelId modelId,
        out HandheldModelFeatures features)
    {
        var model = All.FirstOrDefault(candidate => candidate.FamilyId == familyId && candidate.Id == modelId);
        features = model?.Features ?? HandheldModelFeatures.None;
        return model is not null;
    }
}

internal sealed class MsiClawDeviceModelResolver : IHandheldDeviceModelResolver
{
    public HandheldDeviceModelResolution Resolve(DeviceProbeContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var boardProduct = context.BaseBoardProduct?.Trim();
        if (string.IsNullOrWhiteSpace(boardProduct))
            return new(HandheldDeviceModelResolutionStatus.Indeterminate, null, "BaseBoardProductUnavailable");
        foreach (var model in MsiClawDeviceModels.All)
        {
            if (string.Equals(boardProduct, model.HardwareModelId, StringComparison.OrdinalIgnoreCase))
                return new(HandheldDeviceModelResolutionStatus.Matched, model, "MsiClawModelMatched");
        }
        return new(HandheldDeviceModelResolutionStatus.Unsupported, null, "MsiClawModelUnsupported");
    }
}
