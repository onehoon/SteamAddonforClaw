namespace SteamInputAddonforClaw.Routing;

/// <summary>Describes whether a future routing stage is inactive, observational, or executable.</summary>
internal enum RoutingStageMode
{
    Disabled = 0,
    ObserveOnly = 1,
    Enabled = 2
}

internal enum RoutingStageKind
{
    WinGProtection,
    NativeMode,
    PhysicalInput,
    PhysicalIsolation,
    HidHideBaseline,
    SteamOutput,
    XboxOutput,
    GameBarRouting,
    /// <summary>Transient prevention of a new real MSI Center M MainUI becoming operational while
    /// routing is active. It runs first among MSI/native routing stages after WinGProtection and
    /// rolls back last among those stages before WinGProtection is released.</summary>
    CenterMGuard
}

internal sealed record RoutingPipelinePlan(
    RoutingStageMode NativeMode,
    RoutingStageMode PhysicalInput,
    RoutingStageMode PhysicalIsolation,
    RoutingStageMode HidHideBaseline,
    RoutingStageMode SteamOutput,
    RoutingStageMode XboxOutput,
    RoutingStageMode GameBarRouting,
    RoutingStageMode CenterMGuard = RoutingStageMode.Disabled,
    RoutingStageMode WinGProtection = RoutingStageMode.Disabled)
{
    internal static RoutingPipelinePlan StockCenterM { get; } = new(
        RoutingStageMode.Enabled,
        RoutingStageMode.Enabled,
        RoutingStageMode.Enabled,
        RoutingStageMode.Enabled,
        RoutingStageMode.Enabled,
        RoutingStageMode.Disabled,
        RoutingStageMode.Disabled,
        RoutingStageMode.Enabled,
        RoutingStageMode.Enabled);

    internal static RoutingPipelinePlan AllDisabled { get; } = new(
        RoutingStageMode.Disabled,
        RoutingStageMode.Disabled,
        RoutingStageMode.Disabled,
        RoutingStageMode.Disabled,
        RoutingStageMode.Disabled,
        RoutingStageMode.Disabled,
        RoutingStageMode.Disabled,
        RoutingStageMode.Disabled,
        RoutingStageMode.Disabled);

    internal RoutingStageMode GetMode(RoutingStageKind stage) => stage switch
    {
        RoutingStageKind.NativeMode => NativeMode,
        RoutingStageKind.PhysicalInput => PhysicalInput,
        RoutingStageKind.PhysicalIsolation => PhysicalIsolation,
        RoutingStageKind.HidHideBaseline => HidHideBaseline,
        RoutingStageKind.SteamOutput => SteamOutput,
        RoutingStageKind.XboxOutput => XboxOutput,
        RoutingStageKind.GameBarRouting => GameBarRouting,
        RoutingStageKind.CenterMGuard => CenterMGuard,
        RoutingStageKind.WinGProtection => WinGProtection,
        _ => RoutingStageMode.Disabled
    };
}
