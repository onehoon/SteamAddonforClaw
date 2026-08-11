using SteamInputAddonforClaw.Routing;
using SteamInputAddonforClaw.Status;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class RoutingPipelineSessionCoordinatorTests
{
    [Fact]
    public async Task PassiveDecisionDoesNotExecutePipeline()
    {
        var executor = new FakeExecutor();
        var resolver = new FakeResolver();
        var coordinator = Create(resolver, executor);

        var result = await coordinator.ReconcileAsync(Decision(RoutingDecisionKind.WaitingForSteam), Classification(ControllerManagerKind.None), RoutingExperimentOptions.None, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(RoutingOperationalState.Passive, result.State);
        Assert.Empty(executor.ExecutedPlans);
        Assert.Empty(executor.RollbackPlans);
        Assert.Equal(0, resolver.ResolveCount);
    }

    [Fact]
    public async Task EligibleStockCreatesFrozenSession()
    {
        var executor = new FakeExecutor();
        var coordinator = Create(new RoutingEnvironmentStrategyResolver(), executor);
        var classification = Classification(ControllerManagerKind.None);

        var result = await coordinator.ReconcileAsync(Decision(RoutingDecisionKind.Eligible), classification, RoutingExperimentOptions.None, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(RoutingOperationalState.OverrideActive, result.State);
        var session = coordinator.ActiveSession;
        Assert.NotNull(session);
        Assert.Equal(RoutingEnvironmentStrategyKind.StockCenterM, session.StrategyKind);
        Assert.Equal(classification, session.Classification);
        Assert.Equal(executor.ExecutedPlans.Single(), session.Plan);
    }

    [Fact]
    public async Task ActiveEligibleDoesNotRebuildOrReplaceFrozenPlan()
    {
        var executor = new FakeExecutor();
        var coordinator = Create(new RoutingEnvironmentStrategyResolver(), executor);
        var original = Classification(ControllerManagerKind.None);
        var options = new RoutingExperimentOptions(
            new RoutingStageExperimentOptions(PhysicalInput: RoutingStageMode.ObserveOnly),
            RoutingStageExperimentOptions.None);

        await coordinator.ReconcileAsync(Decision(RoutingDecisionKind.Eligible), original, options, CancellationToken.None);
        var frozen = coordinator.ActiveSession;
        Assert.NotNull(frozen);

        var changed = await coordinator.ReconcileAsync(Decision(RoutingDecisionKind.Eligible), Classification(ControllerManagerKind.ClawTweaks), RoutingExperimentOptions.None, CancellationToken.None);

        Assert.True(changed.Succeeded);
        Assert.Single(executor.ExecutedPlans);
        Assert.Equal(frozen, coordinator.ActiveSession);
    }

    [Fact]
    public async Task ExitUsesFrozenPlanWhenEnvironmentChanges()
    {
        var executor = new FakeExecutor();
        var coordinator = Create(new RoutingEnvironmentStrategyResolver(), executor);
        await coordinator.ReconcileAsync(Decision(RoutingDecisionKind.Eligible), Classification(ControllerManagerKind.None), RoutingExperimentOptions.None, CancellationToken.None);
        var frozen = coordinator.ActiveSession;
        Assert.NotNull(frozen);

        var result = await coordinator.ReconcileAsync(Decision(RoutingDecisionKind.WaitingForSteam), Classification(ControllerManagerKind.ClawTweaks), new RoutingExperimentOptions(
            RoutingStageExperimentOptions.None,
            new RoutingStageExperimentOptions(SteamOutput: RoutingStageMode.Enabled)), CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(frozen.Plan, executor.RollbackPlans.Single());
        Assert.Equal(RoutingOperationalState.Passive, coordinator.CurrentState);
        Assert.Null(coordinator.ActiveSession);
    }

    [Fact]
    public async Task FailedEnterDoesNotPublishSession()
    {
        var executor = new FakeExecutor { ExecuteResult = new(false, RoutingStageKind.NativeMode, "Failed", true) };
        var coordinator = Create(new RoutingEnvironmentStrategyResolver(), executor);

        var result = await coordinator.ReconcileAsync(Decision(RoutingDecisionKind.Eligible), Classification(ControllerManagerKind.None), RoutingExperimentOptions.None, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(RoutingOperationalState.Passive, coordinator.CurrentState);
        Assert.Null(coordinator.ActiveSession);
    }

    [Fact]
    public async Task FailedExitPreservesFrozenSessionAndRetryUsesSamePlan()
    {
        var executor = new FakeExecutor();
        var coordinator = Create(new RoutingEnvironmentStrategyResolver(), executor);
        await coordinator.ReconcileAsync(Decision(RoutingDecisionKind.Eligible), Classification(ControllerManagerKind.None), RoutingExperimentOptions.None, CancellationToken.None);
        var frozen = coordinator.ActiveSession;
        Assert.NotNull(frozen);
        executor.RollbackResults.Enqueue(new(false, RoutingStageKind.NativeMode, "CleanupFailed"));
        executor.RollbackResults.Enqueue(new(true, null, "Success"));

        var first = await coordinator.ReconcileAsync(Decision(RoutingDecisionKind.WaitingForSteam), Classification(ControllerManagerKind.ClawTweaks), RoutingExperimentOptions.None, CancellationToken.None);

        Assert.False(first.Succeeded);
        Assert.Equal(RoutingOperationalState.OverrideActive, coordinator.CurrentState);
        Assert.Equal(frozen, coordinator.ActiveSession);

        var second = await coordinator.ReconcileAsync(Decision(RoutingDecisionKind.WaitingForSteam), Classification(ControllerManagerKind.Winhanced), RoutingExperimentOptions.None, CancellationToken.None);

        Assert.True(second.Succeeded);
        Assert.Equal(frozen.Plan, executor.RollbackPlans[0]);
        Assert.Equal(frozen.Plan, executor.RollbackPlans[1]);
        Assert.Null(coordinator.ActiveSession);
    }

    [Fact]
    public async Task UnsupportedStrategiesFailClosed()
    {
        foreach (var kind in new[] { ControllerManagerKind.HandheldCompanion, ControllerManagerKind.Winhanced, ControllerManagerKind.Multiple, ControllerManagerKind.Indeterminate })
        {
            var executor = new FakeExecutor();
            var coordinator = Create(new RoutingEnvironmentStrategyResolver(), executor);
            var result = await coordinator.ReconcileAsync(Decision(RoutingDecisionKind.Eligible), Classification(kind), RoutingExperimentOptions.None, CancellationToken.None);

            Assert.False(result.Succeeded);
            Assert.Equal("UnsupportedEnvironmentStrategy", result.Reason);
            Assert.Empty(executor.ExecutedPlans);
            Assert.Null(coordinator.ActiveSession);
        }
    }

    [Fact]
    public async Task ClawTweaksIsAValidFrameworkStrategy()
    {
        var executor = new FakeExecutor();
        var coordinator = Create(new RoutingEnvironmentStrategyResolver(), executor);

        var result = await coordinator.ReconcileAsync(Decision(RoutingDecisionKind.Eligible), Classification(ControllerManagerKind.ClawTweaks), RoutingExperimentOptions.None, CancellationToken.None);

        Assert.True(result.Succeeded);
        var session = coordinator.ActiveSession;
        Assert.NotNull(session);
        Assert.Equal(RoutingEnvironmentStrategyKind.ClawTweaks, session.StrategyKind);
        Assert.Equal(RoutingPipelinePlan.AllDisabled, executor.ExecutedPlans.Single());
    }

    [Fact]
    public async Task CancellationDuringEnterDoesNotPublishSession()
    {
        var executor = new FakeExecutor { ThrowCancellation = true };
        var coordinator = Create(new RoutingEnvironmentStrategyResolver(), executor);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await coordinator.ReconcileAsync(Decision(RoutingDecisionKind.Eligible), Classification(ControllerManagerKind.None), RoutingExperimentOptions.None, CancellationToken.None));
        Assert.Equal(RoutingOperationalState.Passive, coordinator.CurrentState);
        Assert.Null(coordinator.ActiveSession);
    }

    [Fact]
    public async Task ReconcileTransitionsAreSerialized()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var executor = new FakeExecutor
        {
            ExecuteStarted = () => entered.TrySetResult(),
            WaitForExecuteRelease = () => release.Task
        };
        var coordinator = Create(new RoutingEnvironmentStrategyResolver(), executor);

        var first = coordinator.ReconcileAsync(Decision(RoutingDecisionKind.Eligible), Classification(ControllerManagerKind.None), RoutingExperimentOptions.None, CancellationToken.None).AsTask();
        await entered.Task;
        var second = coordinator.ReconcileAsync(Decision(RoutingDecisionKind.Eligible), Classification(ControllerManagerKind.ClawTweaks), RoutingExperimentOptions.None, CancellationToken.None).AsTask();
        await Task.Delay(10);
        Assert.Equal(1, executor.MaxConcurrentExecutions);

        release.TrySetResult();
        await Task.WhenAll(first, second);
        Assert.Equal(1, executor.MaxConcurrentExecutions);
    }

    private static RoutingPipelineSessionCoordinator Create(IRoutingEnvironmentStrategyResolver resolver, FakeExecutor executor) =>
        new(resolver, executor);

    private static RoutingDecision Decision(RoutingDecisionKind kind) => new(kind, RoutingDecisionReason.Eligible);

    private static ControllerManagerClassification Classification(ControllerManagerKind kind) =>
        new(kind, ControllerManagerClassificationReason.ControllerManagerStateIndeterminate);

    private sealed class FakeResolver : IRoutingEnvironmentStrategyResolver
    {
        internal int ResolveCount { get; private set; }
        public IRoutingEnvironmentStrategy Resolve(ControllerManagerClassification classification)
        {
            ResolveCount++;
            return new RoutingEnvironmentStrategyResolver().Resolve(classification);
        }
    }

    private sealed class FakeExecutor : IRoutingPipelineExecutor
    {
        internal List<RoutingPipelinePlan> ExecutedPlans { get; } = [];
        internal List<RoutingPipelinePlan> RollbackPlans { get; } = [];
        internal Queue<RoutingPipelineRollbackResult> RollbackResults { get; } = [];
        internal RoutingPipelineExecutionResult ExecuteResult { get; set; } = RoutingPipelineExecutionResult.Success();
        internal bool ThrowCancellation { get; set; }
        internal Action? ExecuteStarted { get; set; }
        internal Func<Task>? WaitForExecuteRelease { get; set; }
        internal int MaxConcurrentExecutions { get; private set; }
        private int _concurrentExecutions;

        public async ValueTask<RoutingPipelineExecutionResult> ExecuteAsync(RoutingPipelinePlan plan, CancellationToken cancellationToken)
        {
            ExecutedPlans.Add(plan);
            var concurrent = Interlocked.Increment(ref _concurrentExecutions);
            MaxConcurrentExecutions = Math.Max(MaxConcurrentExecutions, concurrent);
            try
            {
                ExecuteStarted?.Invoke();
                if (WaitForExecuteRelease is not null) await WaitForExecuteRelease().ConfigureAwait(false);
                if (ThrowCancellation) throw new OperationCanceledException(cancellationToken);
                return ExecuteResult;
            }
            finally
            {
                Interlocked.Decrement(ref _concurrentExecutions);
            }
        }

        public ValueTask<RoutingPipelineRollbackResult> RollbackAsync(RoutingPipelinePlan plan, CancellationToken cancellationToken)
        {
            RollbackPlans.Add(plan);
            return ValueTask.FromResult(RollbackResults.Count == 0 ? new RoutingPipelineRollbackResult(true, null, "Success") : RollbackResults.Dequeue());
        }
    }
}
