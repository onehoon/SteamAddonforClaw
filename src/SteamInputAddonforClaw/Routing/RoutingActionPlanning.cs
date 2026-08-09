using SteamInputAddonforClaw.Diagnostics;

namespace SteamInputAddonforClaw.Routing;

internal enum RoutingOperationalState { Passive, OverrideActive, Indeterminate }
internal enum RoutingActionKind { None, EnterOverride, ExitOverride }
internal enum RoutingActionReason
{
    AlreadyPassive,
    AlreadyActive,
    RoutingBecameEligible,
    RoutingNoLongerEligible,
    ExternalControllerVeto,
    SteamSessionEnded,
    SetupRequired,
    RecoveryUnsafe,
    ControllerSoftwareVeto,
    IndeterminateState
}

internal sealed record RoutingActionPlan(
    RoutingOperationalState CurrentState,
    RoutingOperationalState TargetState,
    RoutingActionKind Action,
    RoutingActionReason Reason,
    RoutingDecision Decision,
    long Generation = 0);

internal static class RoutingActionPlanner
{
    public static RoutingActionPlan Plan(RoutingOperationalState currentState, RoutingDecision decision)
    {
        if (currentState == RoutingOperationalState.Passive)
        {
            return decision.Kind == RoutingDecisionKind.Eligible
                ? new(currentState, RoutingOperationalState.OverrideActive, RoutingActionKind.EnterOverride, RoutingActionReason.RoutingBecameEligible, decision)
                : new(currentState, RoutingOperationalState.Passive, RoutingActionKind.None, RoutingActionReason.AlreadyPassive, decision);
        }

        if (currentState == RoutingOperationalState.OverrideActive)
        {
            return decision.Kind == RoutingDecisionKind.Eligible
                ? new(currentState, RoutingOperationalState.OverrideActive, RoutingActionKind.None, RoutingActionReason.AlreadyActive, decision)
                : new(currentState, RoutingOperationalState.Passive, RoutingActionKind.ExitOverride, ExitReason(decision), decision);
        }

        return new(currentState, RoutingOperationalState.Passive, RoutingActionKind.ExitOverride, RoutingActionReason.IndeterminateState, decision);
    }

    private static RoutingActionReason ExitReason(RoutingDecision decision) => decision.Kind switch
    {
        RoutingDecisionKind.VetoedForSession => RoutingActionReason.ExternalControllerVeto,
        RoutingDecisionKind.WaitingForSteam => RoutingActionReason.SteamSessionEnded,
        RoutingDecisionKind.SetupRequired => RoutingActionReason.SetupRequired,
        RoutingDecisionKind.Indeterminate when decision.Reason == RoutingDecisionReason.RecoveryUnsafe => RoutingActionReason.RecoveryUnsafe,
        RoutingDecisionKind.Indeterminate => RoutingActionReason.IndeterminateState,
        RoutingDecisionKind.Passive when decision.Reason is RoutingDecisionReason.HandheldCompanionRunning or RoutingDecisionReason.ClawTweaksRunning => RoutingActionReason.ControllerSoftwareVeto,
        RoutingDecisionKind.Passive when decision.Reason == RoutingDecisionReason.ExternalControllerPresent => RoutingActionReason.ExternalControllerVeto,
        _ => RoutingActionReason.RoutingNoLongerEligible
    };
}

internal interface IRoutingCoordinator
{
    RoutingOperationalState CurrentState { get; }
    RoutingActionPlan Plan(RoutingDecision decision);
    bool TryCommit(RoutingActionPlan plan);
}

internal sealed class RoutingCoordinator : IRoutingCoordinator
{
    private readonly Lock _sync = new();
    private RoutingOperationalState _state = RoutingOperationalState.Passive;
    private long _latestPlanGeneration;

    public RoutingOperationalState CurrentState
    {
        get
        {
            lock (_sync) return _state;
        }
    }

    public RoutingActionPlan Plan(RoutingDecision decision)
    {
        lock (_sync)
        {
            var plan = RoutingActionPlanner.Plan(_state, decision) with { Generation = ++_latestPlanGeneration };
            if (plan.Action != RoutingActionKind.None)
            {
                AppLog.Info("Routing", "Routing action planned.",
                    ("Current", plan.CurrentState),
                    ("Action", plan.Action),
                    ("Target", plan.TargetState),
                    ("Decision", plan.Decision.Kind),
                    ("Reason", plan.Reason));
            }

            return plan;
        }
    }

    public bool TryCommit(RoutingActionPlan plan)
    {
        lock (_sync)
        {
            if (plan.Generation != _latestPlanGeneration || plan.CurrentState != _state) return false;
            if (plan.TargetState == _state) return true;

            var previousState = _state;
            _state = plan.TargetState;
            AppLog.Info("Routing", "Routing operational state changed.", ("Previous", previousState), ("Current", _state));
            return true;
        }
    }
}
