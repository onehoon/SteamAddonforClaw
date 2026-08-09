using SteamInputAddonforClaw.Routing;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class RoutingActionPlannerTests
{
    [Fact]
    public void PassiveEligible_PlansEnterOverride()
    {
        var plan = RoutingActionPlanner.Plan(RoutingOperationalState.Passive, Decision(RoutingDecisionKind.Eligible, RoutingDecisionReason.Eligible));

        Assert.Equal(RoutingActionKind.EnterOverride, plan.Action);
        Assert.Equal(RoutingOperationalState.OverrideActive, plan.TargetState);
        Assert.Equal(RoutingActionReason.RoutingBecameEligible, plan.Reason);
    }

    [Fact]
    public void ActiveEligible_DoesNotReenterOverride()
    {
        var plan = RoutingActionPlanner.Plan(RoutingOperationalState.OverrideActive, Decision(RoutingDecisionKind.Eligible, RoutingDecisionReason.Eligible));

        Assert.Equal(RoutingActionKind.None, plan.Action);
        Assert.Equal(RoutingOperationalState.OverrideActive, plan.TargetState);
    }

    [Theory]
    [InlineData((int)RoutingDecisionKind.WaitingForSteam, (int)RoutingDecisionReason.SteamInactive, (int)RoutingActionReason.SteamSessionEnded)]
    [InlineData((int)RoutingDecisionKind.VetoedForSession, (int)RoutingDecisionReason.ExternalControllerPresent, (int)RoutingActionReason.ExternalControllerVeto)]
    [InlineData((int)RoutingDecisionKind.SetupRequired, (int)RoutingDecisionReason.PrerequisitesNotReady, (int)RoutingActionReason.SetupRequired)]
    [InlineData((int)RoutingDecisionKind.Indeterminate, (int)RoutingDecisionReason.RecoveryUnsafe, (int)RoutingActionReason.RecoveryUnsafe)]
    [InlineData((int)RoutingDecisionKind.Passive, (int)RoutingDecisionReason.HandheldCompanionRunning, (int)RoutingActionReason.ControllerSoftwareVeto)]
    [InlineData((int)RoutingDecisionKind.Passive, (int)RoutingDecisionReason.ClawTweaksRunning, (int)RoutingActionReason.ControllerSoftwareVeto)]
    public void ActiveNonEligible_PlansFailSafeExit(int kindValue, int decisionReasonValue, int actionReasonValue)
    {
        var plan = RoutingActionPlanner.Plan(
            RoutingOperationalState.OverrideActive,
            Decision((RoutingDecisionKind)kindValue, (RoutingDecisionReason)decisionReasonValue));

        Assert.Equal(RoutingActionKind.ExitOverride, plan.Action);
        Assert.Equal(RoutingOperationalState.Passive, plan.TargetState);
        Assert.Equal((RoutingActionReason)actionReasonValue, plan.Reason);
    }

    [Theory]
    [InlineData((int)RoutingDecisionKind.WaitingForSteam, (int)RoutingDecisionReason.SteamInactive)]
    [InlineData((int)RoutingDecisionKind.VetoedForSession, (int)RoutingDecisionReason.ExternalControllerSessionLatched)]
    [InlineData((int)RoutingDecisionKind.Indeterminate, (int)RoutingDecisionReason.ExternalControllerIndeterminate)]
    public void PassiveNonEligible_DoesNotPlanRepeatedExit(int kindValue, int reasonValue)
    {
        var plan = RoutingActionPlanner.Plan(RoutingOperationalState.Passive, Decision((RoutingDecisionKind)kindValue, (RoutingDecisionReason)reasonValue));

        Assert.Equal(RoutingActionKind.None, plan.Action);
        Assert.Equal(RoutingOperationalState.Passive, plan.TargetState);
    }

    [Fact]
    public void IndeterminateOperationalState_PlansExitBeforeAnyNewEnter()
    {
        var plan = RoutingActionPlanner.Plan(RoutingOperationalState.Indeterminate, Decision(RoutingDecisionKind.Eligible, RoutingDecisionReason.Eligible));

        Assert.Equal(RoutingActionKind.ExitOverride, plan.Action);
        Assert.Equal(RoutingOperationalState.Passive, plan.TargetState);
    }

    private static RoutingDecision Decision(RoutingDecisionKind kind, RoutingDecisionReason reason) => new(kind, reason);
}

public sealed class RoutingCoordinatorTests
{
    [Fact]
    public void PlanDoesNotMutateState_AndCommitEntersOverride()
    {
        var coordinator = new RoutingCoordinator();

        var plan = coordinator.Plan(Decision(RoutingDecisionKind.Eligible, RoutingDecisionReason.Eligible));

        Assert.Equal(RoutingOperationalState.Passive, coordinator.CurrentState);
        Assert.True(coordinator.TryCommit(plan));
        Assert.Equal(RoutingOperationalState.OverrideActive, coordinator.CurrentState);
        Assert.Equal(RoutingActionKind.None, coordinator.Plan(Decision(RoutingDecisionKind.Eligible, RoutingDecisionReason.Eligible)).Action);
    }

    [Fact]
    public void ExitCommit_ReturnsToPassiveAndPreventsRepeatedExit()
    {
        var coordinator = new RoutingCoordinator();
        coordinator.TryCommit(coordinator.Plan(Decision(RoutingDecisionKind.Eligible, RoutingDecisionReason.Eligible)));

        var exitPlan = coordinator.Plan(Decision(RoutingDecisionKind.VetoedForSession, RoutingDecisionReason.ExternalControllerPresent));

        Assert.True(coordinator.TryCommit(exitPlan));
        Assert.Equal(RoutingOperationalState.Passive, coordinator.CurrentState);
        Assert.Equal(RoutingActionKind.None, coordinator.Plan(Decision(RoutingDecisionKind.VetoedForSession, RoutingDecisionReason.ExternalControllerSessionLatched)).Action);
    }

    [Fact]
    public void TryCommit_RejectsPlanCreatedForAnEarlierState()
    {
        var coordinator = new RoutingCoordinator();
        var enterPlan = coordinator.Plan(Decision(RoutingDecisionKind.Eligible, RoutingDecisionReason.Eligible));
        coordinator.TryCommit(enterPlan);
        var exitPlan = coordinator.Plan(Decision(RoutingDecisionKind.WaitingForSteam, RoutingDecisionReason.SteamInactive));
        coordinator.TryCommit(exitPlan);

        Assert.False(coordinator.TryCommit(enterPlan));
        Assert.Equal(RoutingOperationalState.Passive, coordinator.CurrentState);
    }

    [Fact]
    public void CommitNonePlan_PreservesState()
    {
        var coordinator = new RoutingCoordinator();
        var plan = coordinator.Plan(Decision(RoutingDecisionKind.WaitingForSteam, RoutingDecisionReason.SteamInactive));

        Assert.True(coordinator.TryCommit(plan));
        Assert.Equal(RoutingOperationalState.Passive, coordinator.CurrentState);
    }

    private static RoutingDecision Decision(RoutingDecisionKind kind, RoutingDecisionReason reason) => new(kind, reason);
}
