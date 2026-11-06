using SteamInputAddonforClaw.Devices.Abstractions;
using SteamInputAddonforClaw.Devices.MSI.Claw;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class HandheldModelFeaturesTests
{
    [Fact]
    public void Only_the_three_existing_Intel_Claw_models_resolve_the_explicit_feature_set()
    {
        Assert.Equal(3, MsiClawDeviceModels.All.Count);

        foreach (var model in MsiClawDeviceModels.All)
        {
            Assert.True(MsiClawDeviceModels.TryGetFeatures(model.FamilyId, model.Id, out var features));
            Assert.Equal(model.Features, features);
            Assert.True(features.SupportsTdp);
            Assert.True(features.SupportsBatteryChargeLimit);
            Assert.True(features.SupportsControllerVibrationStrength);
            Assert.True(features.SupportsControllerLed);
            Assert.True(features.SupportsMotion);
            Assert.True(features.SupportsIntelFpsLimit);
            Assert.True(features.SupportsIntelGpuMinimumClock);
        }
    }

    [Fact]
    public void Missing_or_mismatched_model_evidence_resolves_to_no_features()
    {
        var knownModel = MsiClawDeviceModels.Claw8ExAiPlus.Id;
        var unknownModel = new HandheldDeviceModelId("msi.claw.future");

        Assert.False(MsiClawDeviceModels.TryGetFeatures(new HandheldDeviceId("other.family"), knownModel, out var wrongFamily));
        Assert.Equal(HandheldModelFeatures.None, wrongFamily);
        Assert.False(MsiClawDeviceModels.TryGetFeatures(MsiClawDeviceModels.FamilyId, unknownModel, out var missingModel));
        Assert.Equal(HandheldModelFeatures.None, missingModel);
    }

    [Fact]
    public void A_model_descriptor_without_an_explicit_policy_defaults_to_all_false()
    {
        var descriptor = new HandheldDeviceModelDescriptor(
            new HandheldDeviceModelId("msi.claw.future"),
            MsiClawDeviceModels.FamilyId,
            "Future Claw",
            "MS-1T8K");

        Assert.Equal(HandheldModelFeatures.None, descriptor.Features);
        Assert.False(descriptor.Features.SupportsTdp);
        Assert.False(descriptor.Features.SupportsBatteryChargeLimit);
        Assert.False(descriptor.Features.SupportsControllerVibrationStrength);
        Assert.False(descriptor.Features.SupportsControllerLed);
        Assert.False(descriptor.Features.SupportsMotion);
        Assert.False(descriptor.Features.SupportsIntelFpsLimit);
        Assert.False(descriptor.Features.SupportsIntelGpuMinimumClock);
    }

    [Fact]
    public void A8_board_remains_unsupported_by_the_registered_model_resolver()
    {
        var resolution = new MsiClawDeviceModelResolver().Resolve(
            new DeviceProbeContext([], baseBoardProduct: "MS-1T8K"));

        Assert.Equal(HandheldDeviceModelResolutionStatus.Unsupported, resolution.Status);
        Assert.Null(resolution.Model);
    }
}
