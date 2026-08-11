using SteamInputAddonforClaw.Status;

namespace SteamInputAddonforClaw.Routing;

internal enum RoutingEnvironmentStrategyKind
{
    StockCenterM = 0,
    ClawTweaks = 1,
    Unsupported = 2
}

internal interface IRoutingEnvironmentStrategy
{
    RoutingEnvironmentStrategyKind Kind { get; }
    RoutingPipelinePlan BuildBaselinePlan();
}

internal sealed class StockCenterMRoutingStrategy : IRoutingEnvironmentStrategy
{
    public RoutingEnvironmentStrategyKind Kind => RoutingEnvironmentStrategyKind.StockCenterM;

    public RoutingPipelinePlan BuildBaselinePlan() => new(
        RoutingStageMode.Enabled,
        RoutingStageMode.Disabled,
        RoutingStageMode.Disabled,
        RoutingStageMode.Disabled,
        RoutingStageMode.Disabled,
        RoutingStageMode.Disabled,
        RoutingStageMode.Disabled);
}

internal sealed class ClawTweaksRoutingStrategy : IRoutingEnvironmentStrategy
{
    public RoutingEnvironmentStrategyKind Kind => RoutingEnvironmentStrategyKind.ClawTweaks;

    public RoutingPipelinePlan BuildBaselinePlan() => RoutingPipelinePlan.AllDisabled;
}

internal sealed class UnsupportedRoutingStrategy : IRoutingEnvironmentStrategy
{
    public RoutingEnvironmentStrategyKind Kind => RoutingEnvironmentStrategyKind.Unsupported;

    public RoutingPipelinePlan BuildBaselinePlan() => RoutingPipelinePlan.AllDisabled;
}

internal sealed class RoutingEnvironmentStrategyResolver
{
    private readonly IRoutingEnvironmentStrategy _stockCenterM;
    private readonly IRoutingEnvironmentStrategy _clawTweaks;
    private readonly IRoutingEnvironmentStrategy _unsupported;

    internal RoutingEnvironmentStrategyResolver(
        IRoutingEnvironmentStrategy? stockCenterM = null,
        IRoutingEnvironmentStrategy? clawTweaks = null,
        IRoutingEnvironmentStrategy? unsupported = null)
    {
        _stockCenterM = stockCenterM ?? new StockCenterMRoutingStrategy();
        _clawTweaks = clawTweaks ?? new ClawTweaksRoutingStrategy();
        _unsupported = unsupported ?? new UnsupportedRoutingStrategy();
    }

    internal IRoutingEnvironmentStrategy Resolve(ControllerManagerClassification classification) => classification.Kind switch
    {
        ControllerManagerKind.None => _stockCenterM,
        ControllerManagerKind.ClawTweaks => _clawTweaks,
        _ => _unsupported
    };
}
