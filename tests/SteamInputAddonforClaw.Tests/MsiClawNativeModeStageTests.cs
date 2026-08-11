using SteamInputAddonforClaw.Devices.MSI.Claw;
using SteamInputAddonforClaw.Routing;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class MsiClawNativeModeStageTests
{
    [Fact]
    public async Task ObserveOnlyDoesNotEnterNativeOverride()
    {
        var session = new FakeSession();
        var stage = new MsiClawNativeModeStage(session);

        var result = await stage.ObserveAsync(CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(1, session.InspectCalls);
        Assert.Equal(0, session.EnterCalls);
        Assert.Equal(0, session.ExitCalls);
    }

    [Fact]
    public async Task PreflightFailureIsReturnedWithoutMutation()
    {
        var session = new FakeSession { Preflight = MsiClawNativeModePreflightResult.Failure("PowerGateClosed") };
        var stage = new MsiClawNativeModeStage(session);

        var result = await stage.PrepareMutationAsync(CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("PowerGateClosed", result.Reason);
        Assert.Equal(0, session.EnterCalls);
    }

    [Fact]
    public async Task PrepareDoesNotMutateAndRepeatedPrepareFails()
    {
        var session = new FakeSession();
        var stage = new MsiClawNativeModeStage(session);

        var first = await stage.PrepareMutationAsync(CancellationToken.None);
        var second = await stage.PrepareMutationAsync(CancellationToken.None);

        Assert.True(first.Succeeded);
        Assert.False(second.Succeeded);
        Assert.Equal(0, session.EnterCalls);
    }

    [Fact]
    public async Task ExecuteWithoutPrepareFails()
    {
        var session = new FakeSession();
        var stage = new MsiClawNativeModeStage(session);

        var result = await stage.ExecuteMutationAsync(CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("NativeModeNotPrepared", result.Reason);
        Assert.Equal(0, session.EnterCalls);
    }

    [Fact]
    public async Task SuccessfulExecuteOwnsMutationAndRollbackRestores()
    {
        var session = new FakeSession();
        var stage = new MsiClawNativeModeStage(session);
        await stage.PrepareMutationAsync(CancellationToken.None);

        var execute = await stage.ExecuteMutationAsync(CancellationToken.None);
        var rollback = await stage.RollbackMutationAsync(CancellationToken.None);

        Assert.True(execute.Succeeded);
        Assert.True(rollback.Succeeded);
        Assert.Equal(1, session.EnterCalls);
        Assert.Equal(1, session.ExitCalls);
        Assert.False(session.IsActive);
    }

    [Fact]
    public async Task EnterFailureDoesNotOwnMutation()
    {
        var session = new FakeSession { EnterResult = false };
        var stage = new MsiClawNativeModeStage(session);
        await stage.PrepareMutationAsync(CancellationToken.None);

        var execute = await stage.ExecuteMutationAsync(CancellationToken.None);
        var rollback = await stage.RollbackMutationAsync(CancellationToken.None);

        Assert.False(execute.Succeeded);
        Assert.True(rollback.Succeeded);
        Assert.Equal(0, session.ExitCalls);
    }

    [Fact]
    public async Task UnderlyingEnterMustReportActive()
    {
        var session = new FakeSession { EnterResult = true, ActiveAfterEnter = false };
        var stage = new MsiClawNativeModeStage(session);
        await stage.PrepareMutationAsync(CancellationToken.None);

        var result = await stage.ExecuteMutationAsync(CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(0, session.ExitCalls);
    }

    [Fact]
    public async Task RollbackBeforeOwnershipDoesNotExitUnderlyingSession()
    {
        var session = new FakeSession();
        var stage = new MsiClawNativeModeStage(session);

        var result = await stage.RollbackMutationAsync(CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(0, session.ExitCalls);
    }

    [Fact]
    public async Task RollbackFailureRetainsOwnershipForRetry()
    {
        var session = new FakeSession { ExitResults = new Queue<bool>([false, true]) };
        var stage = new MsiClawNativeModeStage(session);
        await stage.PrepareMutationAsync(CancellationToken.None);
        await stage.ExecuteMutationAsync(CancellationToken.None);

        var first = await stage.RollbackMutationAsync(CancellationToken.None);
        var second = await stage.RollbackMutationAsync(CancellationToken.None);

        Assert.False(first.Succeeded);
        Assert.True(second.Succeeded);
        Assert.Equal(2, session.ExitCalls);
    }

    [Fact]
    public async Task SuccessfulRollbackIsIdempotent()
    {
        var session = new FakeSession();
        var stage = new MsiClawNativeModeStage(session);
        await stage.PrepareMutationAsync(CancellationToken.None);
        await stage.ExecuteMutationAsync(CancellationToken.None);

        Assert.True((await stage.RollbackMutationAsync(CancellationToken.None)).Succeeded);
        Assert.True((await stage.RollbackMutationAsync(CancellationToken.None)).Succeeded);
        Assert.Equal(1, session.ExitCalls);
    }

    private sealed class FakeSession : IMsiClawNativeModeStageSession
    {
        internal MsiClawNativeModePreflightResult Preflight { get; set; } = MsiClawNativeModePreflightResult.Success();
        internal bool EnterResult { get; set; } = true;
        internal bool ActiveAfterEnter { get; set; } = true;
        internal Queue<bool> ExitResults { get; set; } = new([true]);
        public bool IsActive { get; private set; }
        internal int InspectCalls { get; private set; }
        internal int EnterCalls { get; private set; }
        internal int ExitCalls { get; private set; }

        public ValueTask<MsiClawNativeModePreflightResult> InspectForPipelineAsync(CancellationToken cancellationToken)
        {
            InspectCalls++;
            return ValueTask.FromResult(Preflight);
        }

        public Task<bool> EnterForPipelineAsync(CancellationToken cancellationToken)
        {
            EnterCalls++;
            IsActive = EnterResult && ActiveAfterEnter;
            return Task.FromResult(EnterResult);
        }

        public Task<bool> ExitForPipelineAsync(CancellationToken cancellationToken)
        {
            ExitCalls++;
            var result = ExitResults.Dequeue();
            if (result) IsActive = false;
            return Task.FromResult(result);
        }
    }
}
