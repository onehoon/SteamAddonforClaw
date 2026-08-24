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
        var coordinator = Create(executor);

        var result = await coordinator.ReconcileAsync(Decision(RoutingDecisionKind.WaitingForSteam), Classification(ControllerManagerKind.None), CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(RoutingOperationalState.Passive, result.State);
        Assert.Empty(executor.ExecutedPlans);
        Assert.Empty(executor.RollbackPlans);
        Assert.Null(coordinator.ActiveSession);
        Assert.Null(coordinator.PendingCleanup);
    }

    [Theory]
    [InlineData((int)RoutingDecisionKind.WaitingForSteam, (int)RoutingDecisionReason.SteamInactive)]
    [InlineData((int)RoutingDecisionKind.SetupRequired, (int)RoutingDecisionReason.PrerequisitesNotReady)]
    [InlineData((int)RoutingDecisionKind.Indeterminate, (int)RoutingDecisionReason.RecoveryUnsafe)]
    [InlineData((int)RoutingDecisionKind.Passive, (int)RoutingDecisionReason.ControllerEnvironmentUnsupported)]
    public async Task PassiveNonEligibleDecisionRemainsPassive(int kindValue, int reasonValue)
    {
        var executor = new FakeExecutor();
        var coordinator = Create(executor);

        var result = await coordinator.ReconcileAsync(
            Decision((RoutingDecisionKind)kindValue, (RoutingDecisionReason)reasonValue),
            Classification(ControllerManagerKind.None),
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(RoutingActionKind.None, result.Action);
        Assert.Equal("AlreadyPassive", result.Reason);
        Assert.Equal(RoutingOperationalState.Passive, result.State);
        Assert.Empty(executor.ExecutedPlans);
        Assert.Empty(executor.RollbackPlans);
    }

    [Fact]
    public async Task EligibleStockCreatesFrozenSession()
    {
        var executor = new FakeExecutor();
        var coordinator = Create(executor);
        var classification = Classification(ControllerManagerKind.None);

        var result = await coordinator.ReconcileAsync(Decision(RoutingDecisionKind.Eligible), classification, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(RoutingOperationalState.OverrideActive, result.State);
        var session = coordinator.ActiveSession;
        Assert.NotNull(session);
        Assert.Equal(RoutingPipelinePlan.StockCenterM, executor.ExecutedPlans.Single());
        Assert.Equal(executor.ExecutedPlans.Single(), session.Plan);
    }

    [Fact]
    public async Task ActiveEligibleDoesNotRebuildOrReplaceFrozenPlan()
    {
        var executor = new FakeExecutor();
        var coordinator = Create(executor);
        var original = Classification(ControllerManagerKind.None);

        await coordinator.ReconcileAsync(Decision(RoutingDecisionKind.Eligible), original, CancellationToken.None);
        var frozen = coordinator.ActiveSession;
        Assert.NotNull(frozen);

        var changed = await coordinator.ReconcileAsync(Decision(RoutingDecisionKind.Eligible), Classification(ControllerManagerKind.ClawTweaks), CancellationToken.None);

        Assert.True(changed.Succeeded);
        Assert.Equal(RoutingActionKind.None, changed.Action);
        Assert.Equal("AlreadyActive", changed.Reason);
        Assert.Single(executor.ExecutedPlans);
        Assert.Equal(frozen, coordinator.ActiveSession);
    }

    [Fact]
    public async Task ExitUsesFrozenPlanWhenEnvironmentChanges()
    {
        var executor = new FakeExecutor();
        var coordinator = Create(executor);
        await coordinator.ReconcileAsync(Decision(RoutingDecisionKind.Eligible), Classification(ControllerManagerKind.None), CancellationToken.None);
        var frozen = coordinator.ActiveSession;
        Assert.NotNull(frozen);

        var result = await coordinator.ReconcileAsync(Decision(RoutingDecisionKind.WaitingForSteam), Classification(ControllerManagerKind.ClawTweaks), CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(frozen.Plan, executor.RollbackPlans.Single());
        Assert.Equal(RoutingOperationalState.Passive, coordinator.CurrentState);
        Assert.Null(coordinator.ActiveSession);
    }

    [Fact]
    public async Task FailedEnterDoesNotPublishSession()
    {
        var executor = new FakeExecutor { ExecuteResult = new(false, RoutingStageKind.NativeMode, "Failed", true) };
        var coordinator = Create(executor);

        var result = await coordinator.ReconcileAsync(Decision(RoutingDecisionKind.Eligible), Classification(ControllerManagerKind.None), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(RoutingOperationalState.Passive, coordinator.CurrentState);
        Assert.Null(coordinator.ActiveSession);
    }

    [Fact]
    public async Task FailedEnterWithFailedRollbackPreservesPendingCleanupForRetry()
    {
        var executor = new FakeExecutor
        {
            ExecuteResult = new(false, RoutingStageKind.PhysicalIsolation, "StageFailed", false)
        };
        var coordinator = Create(executor);

        var failed = await coordinator.ReconcileAsync(Decision(RoutingDecisionKind.Eligible), Classification(ControllerManagerKind.None), CancellationToken.None);

        Assert.False(failed.Succeeded);
        Assert.Null(coordinator.ActiveSession);
        Assert.NotNull(coordinator.PendingCleanup);
        var frozen = coordinator.PendingCleanup!.Session;
        executor.RollbackResults.Enqueue(new(true, null, "Success"));

        var cleanup = await coordinator.ReconcileAsync(Decision(RoutingDecisionKind.Eligible), Classification(ControllerManagerKind.None), CancellationToken.None);

        Assert.True(cleanup.Succeeded);
        Assert.Equal(RoutingOperationalState.Passive, cleanup.State);
        Assert.Equal(RoutingOperationalState.Passive, coordinator.CurrentState);
        Assert.Equal(frozen.Plan, executor.RollbackPlans.Single());
        Assert.Null(coordinator.PendingCleanup);
        Assert.Null(coordinator.ActiveSession);
    }

    [Fact]
    public async Task FailedExitPreservesFrozenSessionAndRetryUsesSamePlan()
    {
        var executor = new FakeExecutor();
        var coordinator = Create(executor);
        await coordinator.ReconcileAsync(Decision(RoutingDecisionKind.Eligible), Classification(ControllerManagerKind.None), CancellationToken.None);
        var frozen = coordinator.ActiveSession;
        Assert.NotNull(frozen);
        executor.RollbackResults.Enqueue(new(false, RoutingStageKind.NativeMode, "CleanupFailed"));
        executor.RollbackResults.Enqueue(new(true, null, "Success"));

        var first = await coordinator.ReconcileAsync(Decision(RoutingDecisionKind.WaitingForSteam), Classification(ControllerManagerKind.ClawTweaks), CancellationToken.None);

        Assert.False(first.Succeeded);
        Assert.Equal(RoutingOperationalState.OverrideActive, coordinator.CurrentState);
        Assert.Equal(frozen, coordinator.ActiveSession);

        var second = await coordinator.ReconcileAsync(Decision(RoutingDecisionKind.WaitingForSteam), Classification(ControllerManagerKind.Winhanced), CancellationToken.None);

        Assert.True(second.Succeeded);
        Assert.Equal(frozen.Plan, executor.RollbackPlans[0]);
        Assert.Equal(frozen.Plan, executor.RollbackPlans[1]);
        Assert.Null(coordinator.ActiveSession);
    }

    [Theory]
    [InlineData((int)RoutingDecisionKind.WaitingForSteam, (int)RoutingDecisionReason.SteamInactive)]
    [InlineData((int)RoutingDecisionKind.SetupRequired, (int)RoutingDecisionReason.PrerequisitesNotReady)]
    [InlineData((int)RoutingDecisionKind.Indeterminate, (int)RoutingDecisionReason.RecoveryUnsafe)]
    [InlineData((int)RoutingDecisionKind.Passive, (int)RoutingDecisionReason.ControllerEnvironmentUnsupported)]
    [InlineData((int)RoutingDecisionKind.Passive, (int)RoutingDecisionReason.UnsupportedDevice)]
    public async Task ActiveNonEligibleDecisionExitsFrozenSession(int kindValue, int reasonValue)
    {
        var executor = new FakeExecutor();
        var coordinator = Create(executor);
        await coordinator.ReconcileAsync(Decision(RoutingDecisionKind.Eligible), Classification(ControllerManagerKind.None), CancellationToken.None);
        var frozen = coordinator.ActiveSession!;

        var result = await coordinator.ReconcileAsync(
            Decision((RoutingDecisionKind)kindValue, (RoutingDecisionReason)reasonValue),
            Classification(ControllerManagerKind.ClawTweaks),
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(RoutingActionKind.ExitOverride, result.Action);
        Assert.Equal("ExitedOverride", result.Reason);
        Assert.Equal(frozen.Plan, executor.RollbackPlans.Single());
        Assert.Null(coordinator.ActiveSession);
        Assert.Equal(RoutingOperationalState.Passive, coordinator.CurrentState);
    }

    [Fact]
    public async Task CurrentStateIsDerivedFromActiveSessionDuringCleanup()
    {
        var executor = new FakeExecutor();
        var coordinator = Create(executor);

        Assert.Equal(RoutingOperationalState.Passive, coordinator.CurrentState);
        await coordinator.ReconcileAsync(Decision(RoutingDecisionKind.Eligible), Classification(ControllerManagerKind.None), CancellationToken.None);
        Assert.NotNull(coordinator.ActiveSession);
        Assert.Equal(RoutingOperationalState.OverrideActive, coordinator.CurrentState);

        executor.RollbackResults.Enqueue(new(false, RoutingStageKind.NativeMode, "CleanupFailed"));
        await coordinator.ReconcileAsync(Decision(RoutingDecisionKind.WaitingForSteam), Classification(ControllerManagerKind.None), CancellationToken.None);
        Assert.NotNull(coordinator.ActiveSession);
        Assert.NotNull(coordinator.PendingCleanup);
        Assert.Equal(RoutingOperationalState.OverrideActive, coordinator.CurrentState);

        executor.RollbackResults.Enqueue(new(true, null, "Success"));
        await coordinator.ReconcileAsync(Decision(RoutingDecisionKind.WaitingForSteam), Classification(ControllerManagerKind.None), CancellationToken.None);
        Assert.Null(coordinator.ActiveSession);
        Assert.Null(coordinator.PendingCleanup);
        Assert.Equal(RoutingOperationalState.Passive, coordinator.CurrentState);
    }

    [Fact]
    public async Task PendingExitCleanupPrecedesEligibleDecision()
    {
        var executor = new FakeExecutor();
        var coordinator = Create(executor);
        await coordinator.ReconcileAsync(Decision(RoutingDecisionKind.Eligible), Classification(ControllerManagerKind.None), CancellationToken.None);
        var frozen = coordinator.ActiveSession;
        Assert.NotNull(frozen);
        executor.RollbackResults.Enqueue(new(false, RoutingStageKind.PhysicalIsolation, "CleanupFailed"));
        executor.RollbackResults.Enqueue(new(true, null, "Success"));

        var failedExit = await coordinator.ReconcileAsync(Decision(RoutingDecisionKind.WaitingForSteam), Classification(ControllerManagerKind.None), CancellationToken.None);
        var retry = await coordinator.ReconcileAsync(Decision(RoutingDecisionKind.Eligible), Classification(ControllerManagerKind.ClawTweaks), CancellationToken.None);

        Assert.False(failedExit.Succeeded);
        Assert.True(retry.Succeeded);
        Assert.Equal("PendingCleanupCompleted", retry.Reason);
        Assert.Equal(frozen.Plan, executor.RollbackPlans[0]);
        Assert.Equal(frozen.Plan, executor.RollbackPlans[1]);
        Assert.Empty(executor.ExecutedPlans.Skip(1));
        Assert.Equal(RoutingOperationalState.Passive, coordinator.CurrentState);
        Assert.Null(coordinator.ActiveSession);
    }

    [Fact]
    public async Task CancellationDuringEnterDoesNotPublishSession()
    {
        var executor = new FakeExecutor { ThrowCancellation = true };
        var coordinator = Create(executor);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await coordinator.ReconcileAsync(Decision(RoutingDecisionKind.Eligible), Classification(ControllerManagerKind.None), CancellationToken.None));
        Assert.Equal(RoutingOperationalState.Passive, coordinator.CurrentState);
        Assert.Null(coordinator.ActiveSession);
    }

    [Fact]
    public async Task CancellationRollbackFailurePreservesPendingCleanup()
    {
        var executor = new FakeExecutor { CancellationRollback = new(false, RoutingStageKind.NativeMode, "CleanupFailed") };
        var coordinator = Create(executor);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await coordinator.ReconcileAsync(
            Decision(RoutingDecisionKind.Eligible), Classification(ControllerManagerKind.None), CancellationToken.None));

        Assert.NotNull(coordinator.PendingCleanup);
        executor.RollbackResults.Enqueue(new(true, null, "Success"));
        var retry = await coordinator.ReconcileAsync(Decision(RoutingDecisionKind.WaitingForSteam), Classification(ControllerManagerKind.None), CancellationToken.None);

        Assert.True(retry.Succeeded);
        Assert.Equal(RoutingOperationalState.Passive, retry.State);
        Assert.Null(coordinator.PendingCleanup);
        Assert.Equal(2, executor.RollbackPlans.Count);
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
        var coordinator = Create(executor);

        var first = coordinator.ReconcileAsync(Decision(RoutingDecisionKind.Eligible), Classification(ControllerManagerKind.None), CancellationToken.None).AsTask();
        await entered.Task;
        var second = coordinator.ReconcileAsync(Decision(RoutingDecisionKind.Eligible), Classification(ControllerManagerKind.ClawTweaks), CancellationToken.None).AsTask();
        await Task.Delay(10);
        Assert.Equal(1, executor.MaxConcurrentExecutions);

        release.TrySetResult();
        await Task.WhenAll(first, second);
        Assert.Equal(1, executor.MaxConcurrentExecutions);
    }

    private static RoutingPipelineSessionCoordinator Create(FakeExecutor executor) => new(executor);

    private static RoutingDecision Decision(RoutingDecisionKind kind) => new(kind, RoutingDecisionReason.Eligible);

    private static RoutingDecision Decision(RoutingDecisionKind kind, RoutingDecisionReason reason) => new(kind, reason);

    private static ControllerManagerClassification Classification(ControllerManagerKind kind) =>
        new(kind, ControllerManagerClassificationReason.ControllerManagerStateIndeterminate);

    private sealed class FakeExecutor : IRoutingPipelineExecutor
    {
        internal List<RoutingPipelinePlan> ExecutedPlans { get; } = [];
        internal List<RoutingPipelinePlan> RollbackPlans { get; } = [];
        internal Queue<RoutingPipelineRollbackResult> RollbackResults { get; } = [];
        internal RoutingPipelineExecutionResult ExecuteResult { get; set; } = RoutingPipelineExecutionResult.Success();
        internal bool ThrowCancellation { get; set; }
        internal RoutingPipelineRollbackResult? CancellationRollback { get; set; }
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
                if (CancellationRollback is not null)
                {
                    RollbackPlans.Add(plan);
                    var cancellation = new OperationCanceledException(cancellationToken);
                    RoutingPipelineCancellationMetadata.Attach(cancellation, CancellationRollback);
                    throw cancellation;
                }
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
