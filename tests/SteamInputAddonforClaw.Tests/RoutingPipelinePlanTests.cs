using SteamInputAddonforClaw.Routing;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class RoutingPipelinePlanTests
{
    [Fact]
    public void InitialStageSet_IsExactlyTheDefinedStages() => Assert.Equal(
        [
            RoutingStageKind.NativeMode,
            RoutingStageKind.PhysicalInput,
            RoutingStageKind.PhysicalIsolation,
            RoutingStageKind.ThirdPartyIsolation,
            RoutingStageKind.SteamOutput,
            RoutingStageKind.XboxOutput,
            RoutingStageKind.GameBarRouting,
            RoutingStageKind.CenterMGuard
        ],
        Enum.GetValues<RoutingStageKind>());

    [Fact]
    public void AllDisabled_HasDisabledModeForEveryStage()
    {
        foreach (var stage in Enum.GetValues<RoutingStageKind>())
            Assert.Equal(RoutingStageMode.Disabled, RoutingPipelinePlan.AllDisabled.GetMode(stage));
    }

    [Fact]
    public void StockCenterM_HasTheFixedRoutingBaseline()
    {
        var plan = RoutingPipelinePlan.StockCenterM;

        Assert.Equal(RoutingStageMode.Enabled, plan.NativeMode);
        Assert.Equal(RoutingStageMode.Enabled, plan.PhysicalInput);
        Assert.Equal(RoutingStageMode.Enabled, plan.PhysicalIsolation);
        Assert.Equal(RoutingStageMode.Disabled, plan.ThirdPartyIsolation);
        Assert.Equal(RoutingStageMode.Enabled, plan.SteamOutput);
        Assert.Equal(RoutingStageMode.Disabled, plan.XboxOutput);
        Assert.Equal(RoutingStageMode.Disabled, plan.GameBarRouting);
        Assert.Equal(RoutingStageMode.Enabled, plan.CenterMGuard);
    }

    [Fact]
    public void StageModes_HaveFailSafeNumericValues()
    {
        Assert.Equal(0, (int)RoutingStageMode.Disabled);
        Assert.NotEqual(RoutingStageMode.Disabled, RoutingStageMode.ObserveOnly);
        Assert.NotEqual(RoutingStageMode.Disabled, RoutingStageMode.Enabled);
        Assert.NotEqual(RoutingStageMode.ObserveOnly, RoutingStageMode.Enabled);
    }

    [Fact]
    public void EachStageCanBeConfiguredIndependently()
    {
        var plan = new RoutingPipelinePlan(
            RoutingStageMode.ObserveOnly,
            RoutingStageMode.Disabled,
            RoutingStageMode.Disabled,
            RoutingStageMode.Disabled,
            RoutingStageMode.Disabled,
            RoutingStageMode.Disabled,
            RoutingStageMode.Disabled);

        Assert.Equal(RoutingStageMode.ObserveOnly, plan.NativeMode);
        Assert.Equal(RoutingStageMode.Disabled, plan.PhysicalInput);

        plan = plan with { PhysicalInput = RoutingStageMode.Enabled };
        Assert.Equal(RoutingStageMode.ObserveOnly, plan.NativeMode);
        Assert.Equal(RoutingStageMode.Enabled, plan.PhysicalInput);
        Assert.Equal(RoutingStageMode.Disabled, plan.PhysicalIsolation);
    }

    [Fact]
    public void ObserveOnly_IsPreservedWithoutNormalization() => Assert.Equal(
        RoutingStageMode.ObserveOnly,
        new RoutingPipelinePlan(
            RoutingStageMode.Disabled,
            RoutingStageMode.Disabled,
            RoutingStageMode.ObserveOnly,
            RoutingStageMode.Disabled,
            RoutingStageMode.Disabled,
            RoutingStageMode.Disabled,
            RoutingStageMode.Disabled).GetMode(RoutingStageKind.PhysicalIsolation));

    [Fact]
    public void RecordWithExpression_DoesNotMutateOriginalPlan()
    {
        var original = RoutingPipelinePlan.AllDisabled;
        var changed = original with { SteamOutput = RoutingStageMode.Enabled };

        Assert.Equal(RoutingStageMode.Disabled, original.SteamOutput);
        Assert.Equal(RoutingStageMode.Enabled, changed.SteamOutput);
    }

    [Fact]
    public void UnknownStage_IsDisabled()
    {
        var plan = new RoutingPipelinePlan(
            RoutingStageMode.Enabled,
            RoutingStageMode.Enabled,
            RoutingStageMode.Enabled,
            RoutingStageMode.Enabled,
            RoutingStageMode.Enabled,
            RoutingStageMode.Enabled,
            RoutingStageMode.Enabled);

        Assert.Equal(RoutingStageMode.Disabled, plan.GetMode((RoutingStageKind)999));
    }

    [Fact]
    public void ExperimentalCombination_IsPreservedExactly()
    {
        var plan = new RoutingPipelinePlan(
            RoutingStageMode.Enabled,
            RoutingStageMode.ObserveOnly,
            RoutingStageMode.Disabled,
            RoutingStageMode.ObserveOnly,
            RoutingStageMode.Enabled,
            RoutingStageMode.Disabled,
            RoutingStageMode.Enabled);

        Assert.Equal(RoutingStageMode.Enabled, plan.GetMode(RoutingStageKind.NativeMode));
        Assert.Equal(RoutingStageMode.ObserveOnly, plan.GetMode(RoutingStageKind.PhysicalInput));
        Assert.Equal(RoutingStageMode.Disabled, plan.GetMode(RoutingStageKind.PhysicalIsolation));
        Assert.Equal(RoutingStageMode.ObserveOnly, plan.GetMode(RoutingStageKind.ThirdPartyIsolation));
        Assert.Equal(RoutingStageMode.Enabled, plan.GetMode(RoutingStageKind.SteamOutput));
        Assert.Equal(RoutingStageMode.Disabled, plan.GetMode(RoutingStageKind.XboxOutput));
        Assert.Equal(RoutingStageMode.Enabled, plan.GetMode(RoutingStageKind.GameBarRouting));
    }
}
