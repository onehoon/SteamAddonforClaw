using SteamInputAddonforClaw.Contracts.FrontButtons;
using SteamInputAddonforClaw.Devices.Abstractions;
using SteamInputAddonforClaw.Runtime;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class FrontButtonMappingDefaultSelectionTests
{
    [Theory]
    [InlineData("msi.claw.a2vm.7")]
    [InlineData("msi.claw.a2vm.8")]
    public void Exact_A2vm_model_ids_select_the_A2vm_defaults(string modelId)
        => Assert.Equal(
            FrontButtonMappingSettings.A2vmDefault,
            AddonRuntimeCompositionFactory.SelectFrontButtonMappingDefault(new HandheldDeviceModelId(modelId)));

    [Theory]
    [InlineData("msi.claw.cg3em")]
    [InlineData("msi.claw.unknown")]
    [InlineData("")]
    public void EX_unknown_and_invalid_model_ids_keep_the_existing_defaults(string modelId)
        => Assert.Equal(
            FrontButtonMappingSettings.Default,
            AddonRuntimeCompositionFactory.SelectFrontButtonMappingDefault(new HandheldDeviceModelId(modelId)));

    [Fact]
    public void Null_model_keeps_the_existing_defaults()
        => Assert.Equal(
            FrontButtonMappingSettings.Default,
            AddonRuntimeCompositionFactory.SelectFrontButtonMappingDefault(null));
}
