namespace SteamInputAddonforClaw.Routing;

internal readonly record struct RoutingStageExperimentOptions(
    RoutingStageMode? NativeMode = null,
    RoutingStageMode? PhysicalInput = null,
    RoutingStageMode? PhysicalIsolation = null,
    RoutingStageMode? ThirdPartyIsolation = null,
    RoutingStageMode? SteamOutput = null,
    RoutingStageMode? XboxOutput = null,
    RoutingStageMode? GameBarRouting = null)
{
    internal static RoutingStageExperimentOptions None => default;
}

internal readonly record struct RoutingExperimentOptions(
    RoutingStageExperimentOptions StockCenterM,
    RoutingStageExperimentOptions ClawTweaks)
{
    internal static RoutingExperimentOptions None => new(
        RoutingStageExperimentOptions.None,
        RoutingStageExperimentOptions.None);
}

internal static class RoutingExperimentPlanBuilder
{
    internal static RoutingPipelinePlan Build(IRoutingEnvironmentStrategy strategy, RoutingExperimentOptions options)
    {
        if (strategy.Kind is not (RoutingEnvironmentStrategyKind.StockCenterM or RoutingEnvironmentStrategyKind.ClawTweaks))
            return RoutingPipelinePlan.AllDisabled;

        var overrides = strategy.Kind == RoutingEnvironmentStrategyKind.StockCenterM ? options.StockCenterM : options.ClawTweaks;
        if (!AreKnownModes(overrides)) return RoutingPipelinePlan.AllDisabled;

        var baseline = strategy.BuildBaselinePlan();
        if (!AreKnownModes(baseline)) return RoutingPipelinePlan.AllDisabled;

        var plan = new RoutingPipelinePlan(
            overrides.NativeMode ?? baseline.NativeMode,
            overrides.PhysicalInput ?? baseline.PhysicalInput,
            overrides.PhysicalIsolation ?? baseline.PhysicalIsolation,
            overrides.ThirdPartyIsolation ?? baseline.ThirdPartyIsolation,
            overrides.SteamOutput ?? baseline.SteamOutput,
            overrides.XboxOutput ?? baseline.XboxOutput,
            overrides.GameBarRouting ?? baseline.GameBarRouting);
        if (strategy.Kind == RoutingEnvironmentStrategyKind.StockCenterM
            && plan.PhysicalIsolation == RoutingStageMode.Enabled
            && plan.PhysicalInput != RoutingStageMode.Enabled)
            return RoutingPipelinePlan.AllDisabled;
        return plan;
    }

    private static bool AreKnownModes(RoutingStageExperimentOptions options) =>
        IsKnownMode(options.NativeMode)
        && IsKnownMode(options.PhysicalInput)
        && IsKnownMode(options.PhysicalIsolation)
        && IsKnownMode(options.ThirdPartyIsolation)
        && IsKnownMode(options.SteamOutput)
        && IsKnownMode(options.XboxOutput)
        && IsKnownMode(options.GameBarRouting);

    private static bool AreKnownModes(RoutingPipelinePlan plan) =>
        Enum.GetValues<RoutingStageKind>().All(stage => IsKnownMode(plan.GetMode(stage)));

    private static bool IsKnownMode(RoutingStageMode? mode) => mode is null || IsKnownMode(mode.Value);

    private static bool IsKnownMode(RoutingStageMode mode) => mode is RoutingStageMode.Disabled or RoutingStageMode.ObserveOnly or RoutingStageMode.Enabled;
}
