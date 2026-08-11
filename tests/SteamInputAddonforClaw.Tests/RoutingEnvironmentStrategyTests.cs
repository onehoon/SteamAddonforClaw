using SteamInputAddonforClaw.Routing;
using SteamInputAddonforClaw.Status;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class RoutingEnvironmentStrategyTests
{
    private readonly RoutingEnvironmentStrategyResolver _resolver = new();

    [Fact]
    public void None_ResolvesToStockCenterM()
    {
        var strategy = _resolver.Resolve(Classification(ControllerManagerKind.None));
        Assert.Equal(RoutingEnvironmentStrategyKind.StockCenterM, strategy.Kind);
        Assert.IsType<StockCenterMRoutingStrategy>(strategy);
    }

    [Fact]
    public void ClawTweaks_ResolvesToClawTweaks()
    {
        var strategy = _resolver.Resolve(Classification(ControllerManagerKind.ClawTweaks));
        Assert.Equal(RoutingEnvironmentStrategyKind.ClawTweaks, strategy.Kind);
        Assert.IsType<ClawTweaksRoutingStrategy>(strategy);
    }

    [Theory]
    [InlineData((int)ControllerManagerKind.HandheldCompanion)]
    [InlineData((int)ControllerManagerKind.Winhanced)]
    [InlineData((int)ControllerManagerKind.Multiple)]
    [InlineData((int)ControllerManagerKind.Indeterminate)]
    [InlineData(999)]
    public void UnsupportedOrUnknown_ResolvesToUnsupported(int kindValue)
    {
        var strategy = _resolver.Resolve(Classification((ControllerManagerKind)kindValue));
        Assert.Equal(RoutingEnvironmentStrategyKind.Unsupported, strategy.Kind);
        Assert.IsType<UnsupportedRoutingStrategy>(strategy);
    }

    [Fact]
    public void ResolverUsesClassificationWithoutCompatibilityInput()
    {
        var strategy = _resolver.Resolve(Classification(ControllerManagerKind.None));
        Assert.Equal(RoutingEnvironmentStrategyKind.StockCenterM, strategy.Kind);
    }

    [Fact]
    public void StockBaselineEnablesNormalSteamRoutingStages()
    {
        var plan = new StockCenterMRoutingStrategy().BuildBaselinePlan();
        Assert.Equal(RoutingStageMode.Enabled, plan.NativeMode);
        Assert.Equal(RoutingStageMode.Enabled, plan.PhysicalInput);
        Assert.Equal(RoutingStageMode.Enabled, plan.PhysicalIsolation);
        Assert.Equal(RoutingStageMode.Disabled, plan.ThirdPartyIsolation);
        Assert.Equal(RoutingStageMode.Enabled, plan.SteamOutput);
        Assert.Equal(RoutingStageMode.Disabled, plan.XboxOutput);
        Assert.Equal(RoutingStageMode.Disabled, plan.GameBarRouting);
    }

    [Fact]
    public void ClawTweaksBaseline_IsAllDisabled()
        => AssertAllDisabled(new ClawTweaksRoutingStrategy().BuildBaselinePlan());

    [Fact]
    public void UnsupportedBaseline_IsAllDisabled()
        => AssertAllDisabled(new UnsupportedRoutingStrategy().BuildBaselinePlan());

    [Fact]
    public void ClawTweaksAndUnsupportedHaveDistinctStrategyIdentity()
    {
        Assert.Equal(RoutingEnvironmentStrategyKind.ClawTweaks, new ClawTweaksRoutingStrategy().Kind);
        Assert.Equal(RoutingEnvironmentStrategyKind.Unsupported, new UnsupportedRoutingStrategy().Kind);
    }

    private static ControllerManagerClassification Classification(ControllerManagerKind kind) =>
        new(kind, ControllerManagerClassificationReason.ControllerManagerStateIndeterminate);

    private static void AssertAllDisabled(RoutingPipelinePlan plan)
    {
        foreach (var stage in Enum.GetValues<RoutingStageKind>())
            Assert.Equal(RoutingStageMode.Disabled, plan.GetMode(stage));
    }
}
