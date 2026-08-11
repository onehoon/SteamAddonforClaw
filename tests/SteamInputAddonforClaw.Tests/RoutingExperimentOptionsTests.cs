using SteamInputAddonforClaw.Routing;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class RoutingExperimentOptionsTests
{
    [Fact]
    public void None_PreservesBothStrategyBaselines()
    {
        Assert.Equal(new StockCenterMRoutingStrategy().BuildBaselinePlan(), Build(new StockCenterMRoutingStrategy(), RoutingExperimentOptions.None));
        Assert.Equal(RoutingPipelinePlan.AllDisabled, Build(new ClawTweaksRoutingStrategy(), RoutingExperimentOptions.None));
    }

    [Fact]
    public void NullAndExplicitDisabledRemainDistinct()
    {
        var inherited = Build(new StockCenterMRoutingStrategy(), RoutingExperimentOptions.None);
        var disabled = Build(new StockCenterMRoutingStrategy(), new(RoutingStageExperimentOptions.None with { NativeMode = RoutingStageMode.Disabled }, RoutingStageExperimentOptions.None));
        Assert.Equal(RoutingStageMode.Enabled, inherited.NativeMode);
        Assert.Equal(RoutingStageMode.Disabled, disabled.NativeMode);
    }

    [Fact]
    public void ObserveOnlyAndEnabledOverridesArePreservedWithoutInference()
    {
        var options = new RoutingExperimentOptions(
            RoutingStageExperimentOptions.None,
            new(NativeMode: RoutingStageMode.Disabled, PhysicalInput: RoutingStageMode.ObserveOnly, PhysicalIsolation: RoutingStageMode.Enabled, SteamOutput: RoutingStageMode.Enabled, GameBarRouting: RoutingStageMode.Enabled));
        var plan = Build(new ClawTweaksRoutingStrategy(), options);
        Assert.Equal(RoutingStageMode.Disabled, plan.NativeMode);
        Assert.Equal(RoutingStageMode.ObserveOnly, plan.PhysicalInput);
        Assert.Equal(RoutingStageMode.Enabled, plan.PhysicalIsolation);
        Assert.Equal(RoutingStageMode.Disabled, plan.ThirdPartyIsolation);
        Assert.Equal(RoutingStageMode.Enabled, plan.SteamOutput);
        Assert.Equal(RoutingStageMode.Disabled, plan.XboxOutput);
        Assert.Equal(RoutingStageMode.Enabled, plan.GameBarRouting);
    }

    [Theory]
    [MemberData(nameof(AllStages))]
    public void EveryStockStageCanBeOverriddenIndependently(int stageValue)
    {
        var stage = (RoutingStageKind)stageValue;
        var plan = Build(new StockCenterMRoutingStrategy(), new(WithMode(RoutingStageExperimentOptions.None, stage, stage == RoutingStageKind.NativeMode ? RoutingStageMode.Disabled : RoutingStageMode.ObserveOnly), RoutingStageExperimentOptions.None));
        Assert.Equal(stage == RoutingStageKind.NativeMode ? RoutingStageMode.Disabled : RoutingStageMode.ObserveOnly, plan.GetMode(stage));
        foreach (var other in Enum.GetValues<RoutingStageKind>().Where(other => other != stage))
            Assert.Equal(new StockCenterMRoutingStrategy().BuildBaselinePlan().GetMode(other), plan.GetMode(other));
    }

    [Theory]
    [MemberData(nameof(AllStages))]
    public void EveryClawTweaksStageCanBeOverriddenIndependently(int stageValue)
    {
        var stage = (RoutingStageKind)stageValue;
        var plan = Build(new ClawTweaksRoutingStrategy(), new(RoutingStageExperimentOptions.None, WithMode(RoutingStageExperimentOptions.None, stage, RoutingStageMode.Enabled)));
        Assert.Equal(RoutingStageMode.Enabled, plan.GetMode(stage));
        foreach (var other in Enum.GetValues<RoutingStageKind>().Where(other => other != stage))
            Assert.Equal(RoutingStageMode.Disabled, plan.GetMode(other));
    }

    [Fact]
    public void StockPhysicalIsolationRequiresEnabledPhysicalInput()
    {
        var options = new RoutingExperimentOptions(new(PhysicalInput: RoutingStageMode.ObserveOnly, PhysicalIsolation: RoutingStageMode.Enabled), RoutingStageExperimentOptions.None);
        Assert.Equal(RoutingPipelinePlan.AllDisabled, Build(new StockCenterMRoutingStrategy(), options));
    }

    [Fact]
    public void OptionsAreIndependentBetweenStockAndClawTweaks()
    {
        var options = new RoutingExperimentOptions(new(PhysicalInput: RoutingStageMode.Enabled, SteamOutput: RoutingStageMode.Enabled), RoutingStageExperimentOptions.None);
        var plan = Build(new ClawTweaksRoutingStrategy(), options);
        Assert.Equal(RoutingPipelinePlan.AllDisabled, plan);

        options = new(RoutingStageExperimentOptions.None, new(PhysicalInput: RoutingStageMode.ObserveOnly));
        plan = Build(new StockCenterMRoutingStrategy(), options);
        Assert.Equal(RoutingStageMode.Disabled, plan.PhysicalInput);
    }

    [Fact]
    public void InvalidSelectedOverride_DisablesWholePlan()
    {
        var options = new RoutingExperimentOptions(new(PhysicalInput: RoutingStageMode.Enabled, SteamOutput: (RoutingStageMode)999), RoutingStageExperimentOptions.None);
        Assert.Equal(RoutingPipelinePlan.AllDisabled, Build(new StockCenterMRoutingStrategy(), options));
    }

    [Fact]
    public void InvalidUnselectedEnvironmentDoesNotLeak()
    {
        var options = new RoutingExperimentOptions(new(SteamOutput: (RoutingStageMode)999), new(PhysicalInput: RoutingStageMode.ObserveOnly));
        var plan = Build(new ClawTweaksRoutingStrategy(), options);
        Assert.Equal(RoutingStageMode.ObserveOnly, plan.PhysicalInput);
    }

    [Fact]
    public void UnsupportedIgnoresAllOptions()
    {
        var enabled = new RoutingStageExperimentOptions(RoutingStageMode.Enabled, RoutingStageMode.Enabled, RoutingStageMode.Enabled, RoutingStageMode.Enabled, RoutingStageMode.Enabled, RoutingStageMode.Enabled, RoutingStageMode.Enabled);
        Assert.Equal(RoutingPipelinePlan.AllDisabled, Build(new UnsupportedRoutingStrategy(), new(enabled, enabled)));
    }

    [Fact]
    public void UnknownStrategyKind_DisablesWithoutBuildingBaseline()
    {
        var strategy = new FakeStrategy((RoutingEnvironmentStrategyKind)999, throwOnBuild: true);
        Assert.Equal(RoutingPipelinePlan.AllDisabled, Build(strategy, RoutingExperimentOptions.None));
    }

    [Fact]
    public void MalformedSelectedBaseline_DisablesWholePlan()
    {
        var strategy = new FakeStrategy(RoutingEnvironmentStrategyKind.StockCenterM, new(RoutingStageMode.Disabled, RoutingStageMode.Disabled, RoutingStageMode.Disabled, RoutingStageMode.Disabled, (RoutingStageMode)999, RoutingStageMode.Disabled, RoutingStageMode.Disabled));
        Assert.Equal(RoutingPipelinePlan.AllDisabled, Build(strategy, RoutingExperimentOptions.None));
    }

    [Fact]
    public void OptionsAreImmutableSnapshots()
    {
        var original = RoutingExperimentOptions.None;
        var changed = original with { StockCenterM = original.StockCenterM with { SteamOutput = RoutingStageMode.Enabled } };
        Assert.Null(original.StockCenterM.SteamOutput);
        Assert.Equal(RoutingStageMode.Enabled, changed.StockCenterM.SteamOutput);
        Assert.Equal(RoutingExperimentOptions.None.ClawTweaks, original.ClawTweaks);
    }

    public static IEnumerable<object[]> AllStages() => Enum.GetValues<RoutingStageKind>().Select(stage => new object[] { (int)stage });

    private static RoutingPipelinePlan Build(IRoutingEnvironmentStrategy strategy, RoutingExperimentOptions options) => RoutingExperimentPlanBuilder.Build(strategy, options);

    private static RoutingStageExperimentOptions WithMode(RoutingStageExperimentOptions options, RoutingStageKind stage, RoutingStageMode mode) => stage switch
    {
        RoutingStageKind.NativeMode => options with { NativeMode = mode },
        RoutingStageKind.PhysicalInput => options with { PhysicalInput = mode },
        RoutingStageKind.PhysicalIsolation => options with { PhysicalIsolation = mode },
        RoutingStageKind.ThirdPartyIsolation => options with { ThirdPartyIsolation = mode },
        RoutingStageKind.SteamOutput => options with { SteamOutput = mode },
        RoutingStageKind.XboxOutput => options with { XboxOutput = mode },
        RoutingStageKind.GameBarRouting => options with { GameBarRouting = mode },
        _ => options
    };

    private sealed class FakeStrategy(RoutingEnvironmentStrategyKind kind, RoutingPipelinePlan? baseline = null, bool throwOnBuild = false) : IRoutingEnvironmentStrategy
    {
        public RoutingEnvironmentStrategyKind Kind => kind;
        public RoutingPipelinePlan BuildBaselinePlan() => throwOnBuild ? throw new InvalidOperationException() : baseline ?? RoutingPipelinePlan.AllDisabled;
    }
}
