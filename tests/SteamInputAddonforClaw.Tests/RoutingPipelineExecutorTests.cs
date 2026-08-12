using SteamInputAddonforClaw.Routing;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class RoutingPipelineExecutorTests
{
    [Fact]
    public void StageOrder_IsExplicitAndDependencyAwareForRollback()
    {
        Assert.Equal([RoutingStageKind.NativeMode, RoutingStageKind.PhysicalInput, RoutingStageKind.PhysicalIsolation, RoutingStageKind.ThirdPartyIsolation, RoutingStageKind.SteamOutput, RoutingStageKind.XboxOutput, RoutingStageKind.GameBarRouting], RoutingPipelineStageOrder.Forward);
        Assert.Equal([RoutingStageKind.GameBarRouting, RoutingStageKind.XboxOutput, RoutingStageKind.SteamOutput, RoutingStageKind.ThirdPartyIsolation, RoutingStageKind.PhysicalInput, RoutingStageKind.NativeMode, RoutingStageKind.PhysicalIsolation], RoutingPipelineStageOrder.Rollback);
    }

    [Fact]
    public async Task AllDisabled_WithNoStages_SucceedsWithoutCalls()
    {
        var result = await new RoutingPipelineExecutor([]).ExecuteAsync(RoutingPipelinePlan.AllDisabled, CancellationToken.None);
        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task ObserveOnly_CallsOnlyObserve()
    {
        var stage = new FakeStage(RoutingStageKind.PhysicalInput);
        var result = await Execute(Plan(RoutingStageKind.PhysicalInput, RoutingStageMode.ObserveOnly), stage);
        Assert.True(result.Succeeded);
        Assert.Equal(["Observe"], stage.Calls);
    }

    [Fact]
    public async Task Enabled_PreparesBeforeExecute()
    {
        var stage = new FakeStage(RoutingStageKind.NativeMode);
        var result = await Execute(Plan(RoutingStageKind.NativeMode, RoutingStageMode.Enabled), stage);
        Assert.True(result.Succeeded);
        Assert.Equal(["Prepare", "Execute"], stage.Calls);
    }

    [Fact]
    public async Task PrepareFailure_RollsBackCurrentStageWithoutExecute()
    {
        var stage = new FakeStage(RoutingStageKind.NativeMode) { PrepareResult = RoutingStageOperationResult.Failure("prepare") };
        var result = await Execute(Plan(RoutingStageKind.NativeMode, RoutingStageMode.Enabled), stage);
        Assert.False(result.Succeeded);
        Assert.Equal(RoutingStageKind.NativeMode, result.FailedStage);
        Assert.True(result.RollbackSucceeded);
        Assert.Equal(["Prepare", "Rollback"], stage.Calls);
    }

    [Fact]
    public async Task ExecuteFailure_RollsBackCurrentStage()
    {
        var stage = new FakeStage(RoutingStageKind.NativeMode) { ExecuteResult = RoutingStageOperationResult.Failure("execute") };
        var result = await Execute(Plan(RoutingStageKind.NativeMode, RoutingStageMode.Enabled), stage);
        Assert.False(result.Succeeded);
        Assert.Equal(["Prepare", "Execute", "Rollback"], stage.Calls);
    }

    [Fact]
    public async Task MultiStageFailure_UsesCanonicalDependencyRollbackOrder()
    {
        var trace = new List<string>();
        var native = new FakeStage(RoutingStageKind.NativeMode, trace);
        var physical = new FakeStage(RoutingStageKind.PhysicalInput, trace);
        var isolation = new FakeStage(RoutingStageKind.PhysicalIsolation, trace) { ExecuteResult = RoutingStageOperationResult.Failure("execute") };
        var result = await Execute(new(RoutingStageMode.Enabled, RoutingStageMode.Enabled, RoutingStageMode.Enabled, RoutingStageMode.Disabled, RoutingStageMode.Disabled, RoutingStageMode.Disabled, RoutingStageMode.Disabled), native, physical, isolation);
        Assert.False(result.Succeeded);
        Assert.Equal(["NativeMode.Prepare", "NativeMode.Execute", "PhysicalInput.Prepare", "PhysicalInput.Execute", "PhysicalIsolation.Prepare", "PhysicalIsolation.Execute", "PhysicalInput.Rollback", "NativeMode.Rollback", "PhysicalIsolation.Rollback"], trace);
        Assert.Equal(["Prepare", "Execute", "Rollback"], isolation.Calls);
        Assert.Equal(["Prepare", "Execute", "Rollback"], physical.Calls);
        Assert.Equal(["Prepare", "Execute", "Rollback"], native.Calls);
    }

    [Fact]
    public async Task LaterObserveFailure_RollsBackPreviousEnabledStage()
    {
        var native = new FakeStage(RoutingStageKind.NativeMode);
        var observation = new FakeStage(RoutingStageKind.PhysicalInput) { ObserveResult = RoutingStageOperationResult.Failure("observe") };
        var result = await Execute(new(RoutingStageMode.Enabled, RoutingStageMode.ObserveOnly, RoutingStageMode.Disabled, RoutingStageMode.Disabled, RoutingStageMode.Disabled, RoutingStageMode.Disabled, RoutingStageMode.Disabled), native, observation);
        Assert.False(result.Succeeded);
        Assert.Equal(["Prepare", "Execute", "Rollback"], native.Calls);
        Assert.Equal(["Observe"], observation.Calls);
    }

    [Fact]
    public async Task MissingObserveOnlyOrEnabledStage_FailsClosed()
    {
        var observe = await new RoutingPipelineExecutor([]).ExecuteAsync(Plan(RoutingStageKind.PhysicalInput, RoutingStageMode.ObserveOnly), CancellationToken.None);
        var enabled = await new RoutingPipelineExecutor([]).ExecuteAsync(Plan(RoutingStageKind.PhysicalInput, RoutingStageMode.Enabled), CancellationToken.None);
        Assert.False(observe.Succeeded);
        Assert.Equal(RoutingStageKind.PhysicalInput, observe.FailedStage);
        Assert.False(enabled.Succeeded);
        Assert.Equal(RoutingStageKind.PhysicalInput, enabled.FailedStage);
    }

    [Fact]
    public async Task UnknownMode_FailsWithoutCallingStage()
    {
        var stage = new FakeStage(RoutingStageKind.SteamOutput);
        var result = await Execute(RoutingPipelinePlan.AllDisabled with { SteamOutput = (RoutingStageMode)999 }, stage);
        Assert.False(result.Succeeded);
        Assert.Equal(RoutingStageKind.SteamOutput, result.FailedStage);
        Assert.Empty(stage.Calls);
    }

    [Fact]
    public async Task PublicRollback_UsesDependencyOrderAndIgnoresNonEnabled()
    {
        var native = new FakeStage(RoutingStageKind.NativeMode);
        var observe = new FakeStage(RoutingStageKind.PhysicalInput);
        var output = new FakeStage(RoutingStageKind.SteamOutput);
        var executor = new RoutingPipelineExecutor([native, observe, output]);
        var plan = new RoutingPipelinePlan(RoutingStageMode.Enabled, RoutingStageMode.ObserveOnly, RoutingStageMode.Disabled, RoutingStageMode.Disabled, RoutingStageMode.Enabled, RoutingStageMode.Disabled, RoutingStageMode.Disabled);
        var result = await executor.RollbackAsync(plan, CancellationToken.None);
        Assert.True(result.Succeeded);
        Assert.Equal(["Rollback"], output.Calls);
        Assert.Equal(["Rollback"], native.Calls);
        Assert.Empty(observe.Calls);
    }

    [Fact]
    public async Task SteamOutputRollbackFailure_BlocksDependentNativeTeardown()
    {
        var native = new FakeStage(RoutingStageKind.NativeMode);
        var output = new FakeStage(RoutingStageKind.SteamOutput) { RollbackResult = RoutingStageOperationResult.Failure("rollback") };
        var result = await new RoutingPipelineExecutor([native, output]).RollbackAsync(new(RoutingStageMode.Enabled, RoutingStageMode.Disabled, RoutingStageMode.Disabled, RoutingStageMode.Disabled, RoutingStageMode.Enabled, RoutingStageMode.Disabled, RoutingStageMode.Disabled), CancellationToken.None);
        Assert.False(result.Succeeded);
        Assert.Equal(["Rollback"], output.Calls);
        Assert.Empty(native.Calls);
    }

    [Fact]
    public async Task ThirdPartyIsolationRollbackFailure_ContinuesWithIndependentTeardown()
    {
        var native = new FakeStage(RoutingStageKind.NativeMode);
        var thirdParty = new FakeStage(RoutingStageKind.ThirdPartyIsolation) { RollbackResult = RoutingStageOperationResult.Failure("rollback") };
        var result = await new RoutingPipelineExecutor([native, thirdParty]).RollbackAsync(
            new(RoutingStageMode.Enabled, RoutingStageMode.Disabled, RoutingStageMode.Disabled, RoutingStageMode.Enabled, RoutingStageMode.Disabled, RoutingStageMode.Disabled, RoutingStageMode.Disabled),
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(["Rollback"], thirdParty.Calls);
        Assert.Equal(["Rollback"], native.Calls);
    }

    [Fact]
    public async Task PhysicalInputRollbackFailure_BlocksNativeAndPhysicalIsolationTeardown()
    {
        var input = new FakeStage(RoutingStageKind.PhysicalInput) { RollbackResult = RoutingStageOperationResult.Failure("rollback") };
        var native = new FakeStage(RoutingStageKind.NativeMode);
        var isolation = new FakeStage(RoutingStageKind.PhysicalIsolation);
        var result = await new RoutingPipelineExecutor([native, input, isolation]).RollbackAsync(
            new(RoutingStageMode.Enabled, RoutingStageMode.Enabled, RoutingStageMode.Enabled, RoutingStageMode.Disabled, RoutingStageMode.Disabled, RoutingStageMode.Disabled, RoutingStageMode.Disabled),
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(["Rollback"], input.Calls);
        Assert.Empty(native.Calls);
        Assert.Empty(isolation.Calls);
    }

    [Fact]
    public async Task NativeModeRollbackFailure_BlocksPhysicalIsolationTeardown()
    {
        var native = new FakeStage(RoutingStageKind.NativeMode) { RollbackResult = RoutingStageOperationResult.Failure("rollback") };
        var isolation = new FakeStage(RoutingStageKind.PhysicalIsolation);
        var result = await new RoutingPipelineExecutor([native, isolation]).RollbackAsync(
            new(RoutingStageMode.Enabled, RoutingStageMode.Disabled, RoutingStageMode.Enabled, RoutingStageMode.Disabled, RoutingStageMode.Disabled, RoutingStageMode.Disabled, RoutingStageMode.Disabled),
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(["Rollback"], native.Calls);
        Assert.Empty(isolation.Calls);
    }

    [Fact]
    public async Task PublicRollback_MissingSteamOutputImplementationBlocksDependentTeardown()
    {
        var native = new FakeStage(RoutingStageKind.NativeMode);
        var plan = new RoutingPipelinePlan(RoutingStageMode.Enabled, RoutingStageMode.Disabled, RoutingStageMode.Disabled, RoutingStageMode.Disabled, RoutingStageMode.Enabled, RoutingStageMode.Disabled, RoutingStageMode.Disabled);
        var result = await new RoutingPipelineExecutor([native]).RollbackAsync(plan, CancellationToken.None);
        Assert.False(result.Succeeded);
        Assert.Equal(RoutingStageKind.SteamOutput, result.FailedStage);
        Assert.Empty(native.Calls);
    }

    [Fact]
    public async Task PrepareException_RollsBackAndReturnsFailure()
    {
        var stage = new FakeStage(RoutingStageKind.NativeMode) { ThrowOnPrepare = true };
        var result = await Execute(Plan(RoutingStageKind.NativeMode, RoutingStageMode.Enabled), stage);
        Assert.False(result.Succeeded);
        Assert.Equal(RoutingStageKind.NativeMode, result.FailedStage);
        Assert.Equal(["Prepare", "Rollback"], stage.Calls);
    }

    [Fact]
    public async Task Cancellation_RollsBackThenRethrows()
    {
        using var cancellation = new CancellationTokenSource();
        var stage = new FakeStage(RoutingStageKind.NativeMode) { ThrowOnExecuteCancellation = cancellation };
        await Assert.ThrowsAsync<OperationCanceledException>(() => Execute(Plan(RoutingStageKind.NativeMode, RoutingStageMode.Enabled), cancellation.Token, stage).AsTask());
        Assert.Equal(["Prepare", "Execute", "Rollback"], stage.Calls);
    }

    [Fact]
    public async Task MultiStageCancellation_UsesCanonicalDependencyRollbackOrder()
    {
        using var cancellation = new CancellationTokenSource();
        var trace = new List<string>();
        var native = new FakeStage(RoutingStageKind.NativeMode, trace);
        var input = new FakeStage(RoutingStageKind.PhysicalInput, trace);
        var isolation = new FakeStage(RoutingStageKind.PhysicalIsolation, trace);
        var output = new FakeStage(RoutingStageKind.SteamOutput, trace) { ThrowOnExecuteCancellation = cancellation };
        var plan = new RoutingPipelinePlan(RoutingStageMode.Enabled, RoutingStageMode.Enabled, RoutingStageMode.Enabled, RoutingStageMode.Disabled, RoutingStageMode.Enabled, RoutingStageMode.Disabled, RoutingStageMode.Disabled);

        await Assert.ThrowsAsync<OperationCanceledException>(() => new RoutingPipelineExecutor([native, input, isolation, output]).ExecuteAsync(plan, cancellation.Token).AsTask());

        Assert.Equal(["NativeMode.Prepare", "NativeMode.Execute", "PhysicalInput.Prepare", "PhysicalInput.Execute", "PhysicalIsolation.Prepare", "PhysicalIsolation.Execute", "SteamOutput.Prepare", "SteamOutput.Execute", "SteamOutput.Rollback", "PhysicalInput.Rollback", "NativeMode.Rollback", "PhysicalIsolation.Rollback"], trace);
    }

    [Fact]
    public async Task MultiStageException_UsesCanonicalDependencyRollbackOrder()
    {
        var trace = new List<string>();
        var native = new FakeStage(RoutingStageKind.NativeMode, trace);
        var input = new FakeStage(RoutingStageKind.PhysicalInput, trace);
        var isolation = new FakeStage(RoutingStageKind.PhysicalIsolation, trace);
        var output = new FakeStage(RoutingStageKind.SteamOutput, trace) { ThrowOnExecute = true };
        var plan = new RoutingPipelinePlan(RoutingStageMode.Enabled, RoutingStageMode.Enabled, RoutingStageMode.Enabled, RoutingStageMode.Disabled, RoutingStageMode.Enabled, RoutingStageMode.Disabled, RoutingStageMode.Disabled);

        var result = await new RoutingPipelineExecutor([native, input, isolation, output]).ExecuteAsync(plan, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(RoutingStageKind.SteamOutput, result.FailedStage);
        Assert.Equal(["NativeMode.Prepare", "NativeMode.Execute", "PhysicalInput.Prepare", "PhysicalInput.Execute", "PhysicalIsolation.Prepare", "PhysicalIsolation.Execute", "SteamOutput.Prepare", "SteamOutput.Execute", "SteamOutput.Rollback", "PhysicalInput.Rollback", "NativeMode.Rollback", "PhysicalIsolation.Rollback"], trace);
    }

    [Fact]
    public void DuplicateOrUnknownRegistrations_AreRejected()
    {
        Assert.Throws<ArgumentException>(() => new RoutingPipelineExecutor([new FakeStage(RoutingStageKind.NativeMode), new FakeStage(RoutingStageKind.NativeMode)]));
        Assert.Throws<ArgumentException>(() => new RoutingPipelineExecutor([new FakeStage((RoutingStageKind)999)]));
    }

    private static ValueTask<RoutingPipelineExecutionResult> Execute(RoutingPipelinePlan plan, params FakeStage[] stages) =>
        new RoutingPipelineExecutor(stages).ExecuteAsync(plan, CancellationToken.None);

    private static ValueTask<RoutingPipelineExecutionResult> Execute(RoutingPipelinePlan plan, CancellationToken cancellationToken, params FakeStage[] stages) =>
        new RoutingPipelineExecutor(stages).ExecuteAsync(plan, cancellationToken);

    private static RoutingPipelinePlan Plan(RoutingStageKind stage, RoutingStageMode mode) => RoutingPipelinePlan.AllDisabled with
    {
        NativeMode = stage == RoutingStageKind.NativeMode ? mode : RoutingStageMode.Disabled,
        PhysicalInput = stage == RoutingStageKind.PhysicalInput ? mode : RoutingStageMode.Disabled,
        PhysicalIsolation = stage == RoutingStageKind.PhysicalIsolation ? mode : RoutingStageMode.Disabled,
        SteamOutput = stage == RoutingStageKind.SteamOutput ? mode : RoutingStageMode.Disabled
    };

    private sealed class FakeStage(RoutingStageKind kind, List<string>? trace = null) : IRoutingPipelineStage
    {
        public RoutingStageKind Kind { get; } = kind;
        public List<string> Calls { get; } = [];
        public RoutingStageOperationResult ObserveResult { get; init; } = RoutingStageOperationResult.Success();
        public RoutingStageOperationResult PrepareResult { get; init; } = RoutingStageOperationResult.Success();
        public RoutingStageOperationResult ExecuteResult { get; init; } = RoutingStageOperationResult.Success();
        public RoutingStageOperationResult RollbackResult { get; init; } = RoutingStageOperationResult.Success();
        public bool ThrowOnPrepare { get; init; }
        public bool ThrowOnExecute { get; init; }
        public CancellationTokenSource? ThrowOnExecuteCancellation { get; init; }

        public ValueTask<RoutingStageOperationResult> ObserveAsync(CancellationToken cancellationToken)
        {
            Calls.Add("Observe");
            trace?.Add($"{Kind}.Observe");
            return ValueTask.FromResult(ObserveResult);
        }

        public ValueTask<RoutingStageOperationResult> PrepareMutationAsync(CancellationToken cancellationToken)
        {
            Calls.Add("Prepare");
            trace?.Add($"{Kind}.Prepare");
            if (ThrowOnPrepare) throw new InvalidOperationException("prepare");
            return ValueTask.FromResult(PrepareResult);
        }

        public ValueTask<RoutingStageOperationResult> ExecuteMutationAsync(CancellationToken cancellationToken)
        {
            Calls.Add("Execute");
            trace?.Add($"{Kind}.Execute");
            if (ThrowOnExecuteCancellation is not null)
            {
                ThrowOnExecuteCancellation.Cancel();
                throw new OperationCanceledException(ThrowOnExecuteCancellation.Token);
            }
            if (ThrowOnExecute) throw new InvalidOperationException("execute");
            return ValueTask.FromResult(ExecuteResult);
        }

        public ValueTask<RoutingStageOperationResult> RollbackMutationAsync(CancellationToken cancellationToken)
        {
            Calls.Add("Rollback");
            trace?.Add($"{Kind}.Rollback");
            return ValueTask.FromResult(RollbackResult);
        }
    }
}
