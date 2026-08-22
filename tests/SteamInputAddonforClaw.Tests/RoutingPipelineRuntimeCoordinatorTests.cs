using SteamInputAddonforClaw.Routing;
using SteamInputAddonforClaw.Status;
using SteamInputAddonforClaw.Input;
using SteamInputAddonforClaw.VirtualOutput.Viiper;
using SteamInputAddonforClaw.Power;
using SteamInputAddonforClaw.Devices.MSI.Claw;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class RoutingPipelineRuntimeCoordinatorTests
{
    [Fact]
    public async Task External_takeover_yields_before_forward_pipeline_and_clears_at_session_end()
    {
        var executor = new FakeExecutor();
        var bridge = Create(
            new FakeStatusProvider(
                Snapshot(Eligible(), Software()),
                Snapshot(Eligible(), Software()),
                Snapshot(WaitingForSteam(), Software()),
                Snapshot(Eligible(), Software())),
            executor);

        Assert.True((await bridge.Bridge.ReconcileAsync(CancellationToken.None)).Succeeded);
        var yieldRequest = bridge.Bridge.RequestCurrentSessionYield();
        Assert.NotNull(yieldRequest);
        var yielded = await bridge.Bridge.FailClosedForSessionYieldAsync(yieldRequest!.Value);
        Assert.True(yielded.Succeeded);
        var ignored = await bridge.Bridge.ReconcileAsync(CancellationToken.None);

        Assert.True(ignored.Succeeded);
        Assert.Equal("ExternalNativeTakeoverLatched", ignored.Reason);
        Assert.Single(executor.ExecutedPlans);
        Assert.Single(executor.RollbackPlans);

        var ended = await bridge.Bridge.ReconcileAsync(CancellationToken.None);
        Assert.True(ended.Succeeded);
        var nextSession = await bridge.Bridge.ReconcileAsync(CancellationToken.None);

        Assert.True(nextSession.Succeeded);
        Assert.Equal(RoutingActionKind.EnterOverride, nextSession.Action);
        Assert.Equal(2, executor.ExecutedPlans.Count);
    }

    [Fact]
    public async Task Yield_request_retired_by_old_session_cannot_poison_next_session()
    {
        var executor = new FakeExecutor();
        var bridge = Create(
            new FakeStatusProvider(
                Snapshot(Eligible(), Software()),
                Snapshot(WaitingForSteam(), Software()),
                Snapshot(Eligible(), Software())),
            executor);

        Assert.True((await bridge.Bridge.ReconcileAsync(CancellationToken.None)).Succeeded);
        var request = bridge.Bridge.RequestCurrentSessionYield();
        Assert.NotNull(request);
        Assert.True((await bridge.Bridge.ReconcileAsync(CancellationToken.None)).Succeeded);
        Assert.False(bridge.Bridge.IsCurrentSessionYieldRequest(request!.Value));

        var nextSession = await bridge.Bridge.ReconcileAsync(CancellationToken.None);

        Assert.True(nextSession.Succeeded);
        Assert.Equal(RoutingActionKind.EnterOverride, nextSession.Action);
        Assert.Equal(2, executor.ExecutedPlans.Count);
    }

    [Fact]
    public async Task Yield_request_binds_to_entering_session_and_cancels_forward_entry()
    {
        var executor = new FakeExecutor { BlockNextExecute = true };
        var bridge = Create(
            new FakeStatusProvider(
                Snapshot(Eligible(), Software()),
                Snapshot(Eligible(), Software())),
            executor);
        var entering = bridge.Bridge.ReconcileAsync(CancellationToken.None).AsTask();
        await executor.ExecuteStarted.Task;

        Assert.Null(bridge.Session.ActiveSession);
        var request = bridge.Bridge.RequestCurrentSessionYield();

        Assert.NotNull(request);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => entering);
        Assert.True(executor.ExecuteCancellationObserved.Task.IsCompletedSuccessfully);
        var ignored = await bridge.Bridge.ReconcileAsync(CancellationToken.None);

        Assert.Equal("ExternalNativeTakeoverLatched", ignored.Reason);
        Assert.Single(executor.ExecutedPlans);
    }

    [Fact]
    public async Task Yield_request_captures_entering_session_before_inline_cancellation_retires_it()
    {
        var executor = new InlineCancelExecutor();
        var bridge = Create(
            new FakeStatusProvider(Snapshot(Eligible(), Software()), Snapshot(Eligible(), Software())),
            executor);
        var entering = bridge.Bridge.ReconcileAsync(CancellationToken.None).AsTask();
        await executor.Started.Task;

        Assert.NotNull(bridge.Session.EnteringSession);
        var request = bridge.Bridge.RequestCurrentSessionYield();

        Assert.NotNull(request);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => entering);
        var ignored = await bridge.Bridge.ReconcileAsync(CancellationToken.None);

        Assert.Equal("ExternalNativeTakeoverLatched", ignored.Reason);
        Assert.Equal(1, executor.ExecuteCount);
    }

    [Fact]
    public async Task Retired_yield_request_is_noop_and_next_session_enters()
    {
        var executor = new FakeExecutor();
        var bridge = Create(
            new FakeStatusProvider(
                Snapshot(Eligible(), Software()),
                Snapshot(WaitingForSteam(), Software()),
                Snapshot(Eligible(), Software())),
            executor);

        Assert.True((await bridge.Bridge.ReconcileAsync(CancellationToken.None)).Succeeded);
        var request = bridge.Bridge.RequestCurrentSessionYield();
        Assert.NotNull(request);
        Assert.True((await bridge.Bridge.ReconcileAsync(CancellationToken.None)).Succeeded);

        var delayed = await bridge.Bridge.FailClosedForSessionYieldAsync(request!.Value);
        Assert.True(delayed.Succeeded);
        Assert.Equal("SessionYieldRequestRetired", delayed.Reason);

        var nextSession = await bridge.Bridge.ReconcileAsync(CancellationToken.None);
        Assert.Equal(RoutingActionKind.EnterOverride, nextSession.Action);
    }

    [Fact]
    public async Task Yield_request_without_owned_session_does_not_close_next_session_admission()
    {
        var executor = new FakeExecutor();
        var bridge = Create(
            new FakeStatusProvider(
                Snapshot(WaitingForSteam(), Software()),
                Snapshot(Eligible(), Software())),
            executor);

        Assert.Null(bridge.Bridge.RequestCurrentSessionYield());
        Assert.True((await bridge.Bridge.ReconcileAsync(CancellationToken.None)).Succeeded);
        var nextSession = await bridge.Bridge.ReconcileAsync(CancellationToken.None);

        Assert.True(nextSession.Succeeded);
        Assert.Equal(RoutingActionKind.EnterOverride, nextSession.Action);
    }

    [Fact]
    public async Task Yielded_session_retries_pending_cleanup_without_forward_entry()
    {
        var executor = new FakeExecutor();
        executor.RollbackResults.Enqueue(new RoutingPipelineRollbackResult(false, RoutingStageKind.SteamOutput, "blocked"));
        executor.RollbackResults.Enqueue(new RoutingPipelineRollbackResult(true, null, "recovered"));
        var bridge = Create(new FakeStatusProvider(Snapshot(Eligible(), Software()), Snapshot(Eligible(), Software())), executor);

        Assert.True((await bridge.Bridge.ReconcileAsync(CancellationToken.None)).Succeeded);
        var yieldRequest = bridge.Bridge.RequestCurrentSessionYield();
        Assert.NotNull(yieldRequest);
        Assert.False((await bridge.Bridge.FailClosedForSessionYieldAsync(yieldRequest!.Value)).Succeeded);
        var retry = await bridge.Bridge.ReconcileAsync(CancellationToken.None);

        Assert.True(retry.Succeeded);
        Assert.Single(executor.ExecutedPlans);
        Assert.Equal(2, executor.RollbackPlans.Count);
        Assert.Null(bridge.Session.PendingCleanup);
    }

    [Fact]
    public async Task Yield_request_closes_admission_before_fail_close_gate_is_released()
    {
        var executor = new FakeExecutor { BlockNextExecute = true };
        var bridge = Create(new FakeStatusProvider(Snapshot(Eligible(), Software()), Snapshot(Eligible(), Software())), executor);
        var first = bridge.Bridge.ReconcileAsync(CancellationToken.None).AsTask();
        await executor.ExecuteStarted.Task;

        var request = bridge.Bridge.RequestCurrentSessionYield();
        Assert.NotNull(request);
        var failClose = bridge.Bridge.FailClosedForSessionYieldAsync(request!.Value).AsTask();
        var queued = bridge.Bridge.ReconcileAsync(CancellationToken.None).AsTask();
        await executor.ExecuteCancellationObserved.Task;
        await failClose;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
        var result = await queued;

        Assert.True(result.Succeeded);
        Assert.Equal("ExternalNativeTakeoverLatched", result.Reason);
        Assert.Single(executor.ExecutedPlans);
    }

    [Fact]
    public async Task SyntheticTestModeSessionBoundaryEntersAndRollsBackTheProductionPipeline()
    {
        var executor = new FakeExecutor();
        var provider = new FakeStatusProvider(
            Snapshot(Eligible(), Software()),
            Snapshot(WaitingForSteam(), Software()));
        var bridge = Create(provider, executor);

        var entered = await bridge.Bridge.ReconcileAsync(CancellationToken.None);
        var exited = await bridge.Bridge.ReconcileAsync(CancellationToken.None);

        Assert.True(entered.Succeeded);
        Assert.Equal(RoutingActionKind.EnterOverride, entered.Action);
        Assert.True(exited.Succeeded);
        Assert.Equal(RoutingActionKind.ExitOverride, exited.Action);
        Assert.Single(executor.ExecutedPlans);
        Assert.Single(executor.RollbackPlans);
        Assert.Equal(RoutingPipelinePlan.StockCenterM, executor.ExecutedPlans.Single());
        Assert.Equal(executor.ExecutedPlans.Single(), executor.RollbackPlans.Single());
    }

    [Fact]
    public async Task ActiveEligibleDoesNotRetirePresentation()
    {
        var callbackCalls = 0;
        var bridge = CreateWithCallback(
            new FakeStatusProvider(Snapshot(Eligible(), Software()), Snapshot(Eligible(), Software())),
            new FakeExecutor(),
            beforeActiveSessionExit: _ => { callbackCalls++; return Task.FromResult(true); });

        Assert.True((await bridge.Bridge.ReconcileAsync(CancellationToken.None)).Succeeded);
        var result = await bridge.Bridge.ReconcileAsync(CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal("AlreadyActive", result.Reason);
        Assert.Equal(0, callbackCalls);
    }

    [Fact]
    public async Task ActiveNonEligibleRetiresPresentationBeforeOuterRollback()
    {
        var events = new List<string>();
        var executor = new FakeExecutor(events);
        var bridge = CreateWithCallback(
            new FakeStatusProvider(Snapshot(Eligible(), Software()), Snapshot(WaitingForSteam(), Software())),
            executor,
            beforeActiveSessionExit: _ => { events.Add("RetireX360"); return Task.FromResult(true); });

        Assert.True((await bridge.Bridge.ReconcileAsync(CancellationToken.None)).Succeeded);
        var result = await bridge.Bridge.ReconcileAsync(CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(RoutingActionKind.ExitOverride, result.Action);
        Assert.Equal(["RetireX360", "PipelineRollback"], events);
    }

    [Fact]
    public async Task ActiveSetupRequiredAlsoRetiresPresentationBeforeOuterRollback()
    {
        var callbackCalls = 0;
        var executor = new FakeExecutor();
        var bridge = CreateWithCallback(
            new FakeStatusProvider(
                Snapshot(Eligible(), Software()),
                Snapshot(new(RoutingDecisionKind.SetupRequired, RoutingDecisionReason.PrerequisitesNotReady), Software())),
            executor,
            beforeActiveSessionExit: _ => { callbackCalls++; return Task.FromResult(true); });

        Assert.True((await bridge.Bridge.ReconcileAsync(CancellationToken.None)).Succeeded);
        var result = await bridge.Bridge.ReconcileAsync(CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(1, callbackCalls);
        Assert.Single(executor.RollbackPlans);
    }

    [Fact]
    public async Task FailedPresentationRetirementBlocksOuterRollbackAndPreservesActiveSession()
    {
        var executor = new FakeExecutor();
        var bridge = CreateWithCallback(
            new FakeStatusProvider(Snapshot(Eligible(), Software()), Snapshot(WaitingForSteam(), Software())),
            executor,
            beforeActiveSessionExit: _ => Task.FromResult(false));

        Assert.True((await bridge.Bridge.ReconcileAsync(CancellationToken.None)).Succeeded);
        var result = await bridge.Bridge.ReconcileAsync(CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(RoutingOperationalState.OverrideActive, result.State);
        Assert.Equal(RoutingActionKind.ExitOverride, result.Action);
        Assert.Equal("Xbox360PresentationRetirementFailed", result.Reason);
        Assert.Empty(executor.RollbackPlans);
        Assert.NotNull(bridge.Session.ActiveSession);
    }

    [Fact]
    public async Task PassiveNonEligibleDoesNotRetirePresentation()
    {
        var callbackCalls = 0;
        var bridge = CreateWithCallback(
            new FakeStatusProvider(Snapshot(WaitingForSteam(), Software())),
            new FakeExecutor(),
            beforeActiveSessionExit: _ => { callbackCalls++; return Task.FromResult(true); });

        var result = await bridge.Bridge.ReconcileAsync(CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal("AlreadyPassive", result.Reason);
        Assert.Equal(0, callbackCalls);
    }

    [Fact]
    public async Task SuspendRetiresPresentationBeforeOuterRollback()
    {
        var events = new List<string>();
        var executor = new FakeExecutor(events);
        var bridge = CreateWithCallback(
            new FakeStatusProvider(Snapshot(Eligible(), Software())),
            executor,
            beforeActiveSessionExit: _ => { events.Add("RetireX360"); return Task.FromResult(true); });

        Assert.True((await bridge.Bridge.ReconcileAsync(CancellationToken.None)).Succeeded);
        var quiesced = await bridge.Bridge.QuiesceForSuspendAsync(
            DateTimeOffset.UtcNow.AddSeconds(1), 1, 1, CancellationToken.None);

        Assert.True(quiesced);
        Assert.Equal(["RetireX360", "PipelineRollback"], events);
        Assert.Null(bridge.Session.ActiveSession);
    }

    [Fact]
    public async Task SuspendRetirementFailureBlocksOuterRollbackAndPreservesActiveSession()
    {
        var executor = new FakeExecutor();
        var bridge = CreateWithCallback(
            new FakeStatusProvider(Snapshot(Eligible(), Software())),
            executor,
            beforeActiveSessionExit: _ => Task.FromResult(false));

        Assert.True((await bridge.Bridge.ReconcileAsync(CancellationToken.None)).Succeeded);
        var quiesced = await bridge.Bridge.QuiesceForSuspendAsync(
            DateTimeOffset.UtcNow.AddSeconds(1), 1, 1, CancellationToken.None);

        Assert.False(quiesced);
        Assert.Empty(executor.RollbackPlans);
        Assert.NotNull(bridge.Session.ActiveSession);
    }

    [Fact]
    public async Task SuspendRetirementExceptionBlocksOuterRollback()
    {
        var executor = new FakeExecutor();
        var bridge = CreateWithCallback(
            new FakeStatusProvider(Snapshot(Eligible(), Software())),
            executor,
            beforeActiveSessionExit: _ => throw new InvalidOperationException("stop failed"));

        Assert.True((await bridge.Bridge.ReconcileAsync(CancellationToken.None)).Succeeded);
        var quiesced = await bridge.Bridge.QuiesceForSuspendAsync(
            DateTimeOffset.UtcNow.AddSeconds(1), 1, 1, CancellationToken.None);

        Assert.False(quiesced);
        Assert.Empty(executor.RollbackPlans);
        Assert.NotNull(bridge.Session.ActiveSession);
    }

    [Fact]
    public async Task FailClosedRetiresPresentationBeforeOuterRollback()
    {
        var events = new List<string>();
        var executor = new FakeExecutor(events);
        var bridge = CreateWithCallback(
            new FakeStatusProvider(Snapshot(Eligible(), Software())),
            executor,
            beforeActiveSessionExit: _ => { events.Add("RetireX360"); return Task.FromResult(true); });

        Assert.True((await bridge.Bridge.ReconcileAsync(CancellationToken.None)).Succeeded);
        var result = await bridge.Bridge.FailClosedAsync();

        Assert.True(result.Succeeded);
        Assert.Equal(["RetireX360", "PipelineRollback"], events);
        Assert.Null(bridge.Session.ActiveSession);
    }

    [Fact]
    public async Task FailClosedRetirementFailureBlocksOuterRollbackAndPreservesActiveSession()
    {
        var executor = new FakeExecutor();
        var bridge = CreateWithCallback(
            new FakeStatusProvider(Snapshot(Eligible(), Software())),
            executor,
            beforeActiveSessionExit: _ => Task.FromResult(false));

        Assert.True((await bridge.Bridge.ReconcileAsync(CancellationToken.None)).Succeeded);
        var result = await bridge.Bridge.FailClosedAsync();

        Assert.False(result.Succeeded);
        Assert.Equal("Xbox360PresentationRetirementFailed", result.Reason);
        Assert.Empty(executor.RollbackPlans);
        Assert.NotNull(bridge.Session.ActiveSession);
    }

    [Fact]
    public async Task FailClosedRetirementExceptionBlocksOuterRollbackAndPreservesActiveSession()
    {
        var executor = new FakeExecutor();
        var bridge = CreateWithCallback(
            new FakeStatusProvider(Snapshot(Eligible(), Software())),
            executor,
            beforeActiveSessionExit: _ => throw new InvalidOperationException("stop failed"));

        Assert.True((await bridge.Bridge.ReconcileAsync(CancellationToken.None)).Succeeded);
        var result = await bridge.Bridge.FailClosedAsync();

        Assert.False(result.Succeeded);
        Assert.Equal("Xbox360PresentationRetirementFailed", result.Reason);
        Assert.Empty(executor.RollbackPlans);
        Assert.NotNull(bridge.Session.ActiveSession);
    }

    [Fact]
    public async Task PassiveFailClosedDoesNotInvokeRetirementCallback()
    {
        var callbackCalls = 0;
        var bridge = CreateWithCallback(
            new FakeStatusProvider(Snapshot(Eligible(), Software())),
            new FakeExecutor(),
            beforeActiveSessionExit: _ => { callbackCalls++; return Task.FromResult(true); });

        var result = await bridge.Bridge.FailClosedAsync();

        Assert.True(result.Succeeded);
        Assert.Equal(0, callbackCalls);
    }

    [Fact]
    public async Task PassiveSuspendDoesNotRetirePresentation()
    {
        var callbackCalls = 0;
        var bridge = CreateWithCallback(
            new FakeStatusProvider(Snapshot(WaitingForSteam(), Software())),
            new FakeExecutor(),
            beforeActiveSessionExit: _ => { callbackCalls++; return Task.FromResult(true); });

        var quiesced = await bridge.Bridge.QuiesceForSuspendAsync(
            DateTimeOffset.UtcNow.AddSeconds(1), 1, 1, CancellationToken.None);

        Assert.True(quiesced);
        Assert.Equal(0, callbackCalls);
    }

    [Fact]
    public async Task StockEligibleUsesCanonicalSnapshotAndNormalStockRoutingBaseline()
    {
        var executor = new FakeExecutor();
        var provider = new FakeStatusProvider(Snapshot(Eligible(), Software()));
        var bridge = Create(provider, executor);

        var result = await bridge.Bridge.ReconcileAsync(CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Single(executor.ExecutedPlans);
        var plan = executor.ExecutedPlans.Single();
        Assert.Equal(RoutingStageMode.Enabled, plan.NativeMode);
        Assert.Equal(RoutingStageMode.Enabled, plan.PhysicalInput);
        Assert.Equal(RoutingStageMode.Enabled, plan.PhysicalIsolation);
        Assert.Equal(RoutingStageMode.Enabled, plan.SteamOutput);
        Assert.NotNull(bridge.Session.ActiveSession);
    }

    [Fact]
    public void CurrentOperationalState_IsPassiveWithNoActiveSession()
    {
        var bridge = Create(new FakeStatusProvider(Snapshot(Eligible(), Software())), new FakeExecutor());

        Assert.Equal(RoutingOperationalState.Passive, bridge.Bridge.CurrentOperationalState);
        Assert.False(bridge.Bridge.ActiveSessionHasSteamOutputEnabled);
    }

    [Fact]
    public async Task CurrentOperationalState_ReflectsActiveSessionSteamOutputAfterEntry()
    {
        var executor = new FakeExecutor();
        var provider = new FakeStatusProvider(Snapshot(Eligible(), Software()));
        var bridge = Create(provider, executor);

        var result = await bridge.Bridge.ReconcileAsync(CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(RoutingOperationalState.OverrideActive, bridge.Bridge.CurrentOperationalState);
        Assert.True(bridge.Bridge.ActiveSessionHasSteamOutputEnabled);
        // Reading the accessors again must not mutate the session.
        Assert.Equal(RoutingOperationalState.OverrideActive, bridge.Bridge.CurrentOperationalState);
        Assert.NotNull(bridge.Session.ActiveSession);
    }

    [Fact]
    public async Task FailClosedRollsBackTheActiveExperimentalPipeline()
    {
        var executor = new FakeExecutor();
        var provider = new FakeStatusProvider(Snapshot(Eligible(), Software()));
        var bridge = Create(provider, executor);

        Assert.True((await bridge.Bridge.ReconcileAsync(CancellationToken.None)).Succeeded);
        var result = await bridge.Bridge.FailClosedAsync();

        Assert.True(result.Succeeded);
        Assert.Single(executor.RollbackPlans);
        Assert.Null(bridge.Session.ActiveSession);
    }

    [Fact]
    public async Task SuspendQuiesceRollsBackFrozenSessionWithoutCapturingStatusOrEndingSteamSession()
    {
        var executor = new FakeExecutor();
        var provider = new FakeStatusProvider(Snapshot(Eligible(), Software()));
        var boundary = new SuspendBoundaryParticipant();
        var bridge = Create(provider, executor, boundary);
        Assert.True((await bridge.Bridge.ReconcileAsync(CancellationToken.None)).Succeeded);
        var frozen = bridge.Session.ActiveSession!;
        var captures = provider.CaptureCount;

        Assert.True(await bridge.Bridge.QuiesceForSuspendAsync(DateTimeOffset.UtcNow.AddSeconds(1), 1, 1, CancellationToken.None));

        Assert.Equal(captures, provider.CaptureCount);
        Assert.Equal(frozen.Plan, executor.RollbackPlans.Single());
        Assert.Null(bridge.Session.ActiveSession);
        Assert.Null(bridge.Session.PendingCleanup);
        Assert.Equal(0, boundary.CallCount);
    }

    [Fact]
    public async Task SuspendQuiesceFailurePreservesRoutingCleanupState()
    {
        var executor = new FakeExecutor();
        executor.RollbackResults.Enqueue(new(false, RoutingStageKind.NativeMode, "Failed"));
        var provider = new FakeStatusProvider(Snapshot(Eligible(), Software()));
        var bridge = Create(provider, executor);
        await bridge.Bridge.ReconcileAsync(CancellationToken.None);

        Assert.False(await bridge.Bridge.QuiesceForSuspendAsync(DateTimeOffset.UtcNow.AddSeconds(1), 1, 1, CancellationToken.None));

        Assert.NotNull(bridge.Session.ActiveSession);
        Assert.NotNull(bridge.Session.PendingCleanup);
    }

    [Fact]
    public async Task RetryResidualCleanupForResumeRollsBackTheFrozenPlanWithoutCapturingStatus()
    {
        var executor = new FakeExecutor();
        var provider = new FakeStatusProvider(Snapshot(Eligible(), Software()));
        var bridge = Create(provider, executor);
        Assert.True((await bridge.Bridge.ReconcileAsync(CancellationToken.None)).Succeeded);
        var frozen = bridge.Session.ActiveSession!;
        var captures = provider.CaptureCount;

        Assert.True(bridge.Bridge.HasResidualSessionState);
        var retried = await bridge.Bridge.RetryResidualCleanupForResumeAsync(CancellationToken.None);

        Assert.True(retried);
        Assert.Equal(captures, provider.CaptureCount);
        Assert.Equal(frozen.Plan, executor.RollbackPlans.Single());
        Assert.Null(bridge.Session.ActiveSession);
        Assert.Null(bridge.Session.PendingCleanup);
        Assert.False(bridge.Bridge.HasResidualSessionState);
    }

    [Fact]
    public async Task RetryResidualCleanupForResumeFailurePreservesRoutingCleanupState()
    {
        var executor = new FakeExecutor();
        executor.RollbackResults.Enqueue(new(false, RoutingStageKind.NativeMode, "Failed"));
        var provider = new FakeStatusProvider(Snapshot(Eligible(), Software()));
        var bridge = Create(provider, executor);
        await bridge.Bridge.ReconcileAsync(CancellationToken.None);

        Assert.False(await bridge.Bridge.RetryResidualCleanupForResumeAsync(CancellationToken.None));

        Assert.NotNull(bridge.Session.ActiveSession);
        Assert.NotNull(bridge.Session.PendingCleanup);
        Assert.True(bridge.Bridge.HasResidualSessionState);
    }

    [Fact]
    public async Task HasResidualSessionState_TrueForInFlightTransitionEvenBeforeActiveSessionIsRecorded()
    {
        // A routing Enter can still be mid-flight (holding _transitionGate) when suspend cancels
        // it and quiesce's own deadline expires before that transition has released the gate --
        // at that point ActiveSession/PendingCleanup are both still null, but the process still
        // owns in-progress cleanup work that must be retried before falling back to journal
        // recovery, not skipped just because nothing has been recorded yet.
        var executor = new FakeExecutor { BlockNextExecute = true };
        var provider = new FakeStatusProvider(Snapshot(Eligible(), Software()));
        var bridge = Create(provider, executor);

        var enter = bridge.Bridge.ReconcileAsync(CancellationToken.None).AsTask();
        await executor.ExecuteStarted.Task;

        Assert.Null(bridge.Session.ActiveSession);
        Assert.Null(bridge.Session.PendingCleanup);
        Assert.True(bridge.Bridge.HasResidualSessionState);

        // _transitionGate is a SemaphoreSlim(1,1) still held by `enter`, so this genuinely
        // serializes behind it rather than racing a session/cleanup snapshot that doesn't exist
        // yet.
        var retry = bridge.Bridge.RetryResidualCleanupForResumeAsync(CancellationToken.None).AsTask();

        bridge.Bridge.CancelInFlightTransition();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => enter);

        Assert.True(await retry);
        Assert.False(bridge.Bridge.HasResidualSessionState);
        Assert.Null(bridge.Session.ActiveSession);
        Assert.Null(bridge.Session.PendingCleanup);
    }

    [Fact]
    public async Task HasResidualSessionStateIsFalseWhenPassive()
    {
        var executor = new FakeExecutor();
        var provider = new FakeStatusProvider(Snapshot(Eligible(), Software()));
        var bridge = Create(provider, executor);

        Assert.False(bridge.Bridge.HasResidualSessionState);
        Assert.True(await bridge.Bridge.RetryResidualCleanupForResumeAsync(CancellationToken.None));
        Assert.Empty(executor.RollbackPlans);
        Assert.Equal(0, provider.CaptureCount);
    }

    [Fact]
    public async Task SuspendQuiesceWhenPassiveDoesNotCaptureStatusOrEnterRouting()
    {
        var executor = new FakeExecutor();
        var provider = new FakeStatusProvider(Snapshot(Eligible(), Software()));
        var bridge = Create(provider, executor);

        Assert.True(await bridge.Bridge.QuiesceForSuspendAsync(DateTimeOffset.UtcNow.AddSeconds(1), 1, 1, CancellationToken.None));

        Assert.Equal(0, provider.CaptureCount);
        Assert.Empty(executor.ExecutedPlans);
        Assert.Empty(executor.RollbackPlans);
        Assert.Null(bridge.Session.ActiveSession);
        Assert.Null(bridge.Session.PendingCleanup);
    }

    [Fact]
    public async Task FailClosedCancelsAnInFlightEnterBeforeForwardCompletion()
    {
        // FailClosedAsync() must preempt an in-flight routing Enter rather than sitting behind
        // _transitionGate while that Enter is still free to keep mutating forward -- otherwise a
        // caller reporting a fault that already invalidates the active session (e.g. the owned
        // MSI physical-input session dying mid-Enter) could still race a later stage (e.g. Steam
        // output attach) into completing before rollback ever starts.
        var executor = new FakeExecutor { BlockNextExecute = true };
        var provider = new FakeStatusProvider(Snapshot(Eligible(), Software()));
        var bridge = Create(provider, executor);

        var entering = bridge.Bridge.ReconcileAsync(CancellationToken.None).AsTask();
        await executor.ExecuteStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var failClosing = bridge.Bridge.FailClosedAsync().AsTask();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => entering);
        await executor.ExecuteCancellationObserved.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var result = await failClosing;
        Assert.True(result.Succeeded);
        Assert.Null(bridge.Session.ActiveSession);
        Assert.Equal(RoutingOperationalState.Passive, bridge.Bridge.CurrentOperationalState);
    }

    [Fact]
    public async Task PublisherFaultInvokesTheActualRuntimeFailClosedPath()
    {
        var executor = new FakeExecutor();
        var provider = new FakeStatusProvider(Snapshot(Eligible(), Software()));
        var bridge = Create(provider, executor);
        Assert.True((await bridge.Bridge.ReconcileAsync(CancellationToken.None)).Succeeded);

        var ticks = new PublisherManualTicks();
        var sink = new PublisherFailingSink();
        var failClosed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var publisher = new CanonicalSteamDeckInputPublisher(
            new PublisherSnapshot(), sink, ticks,
            exception => _ = Task.Run(async () => { var result = await bridge.Bridge.FailClosedAsync(); if (result.Succeeded) failClosed.TrySetResult(); }));
        publisher.Start(); await Task.Yield(); ticks.Tick();

        await failClosed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await publisher.StopAsync();
        Assert.Single(executor.RollbackPlans);
        Assert.Null(bridge.Session.ActiveSession);
    }

    [Fact]
    public async Task BackendRuntimeFaultLatchesThenInvokesTheActualRuntimeFailClosedPath()
    {
        // Mirrors AddonRoutingRuntime.Create's registered runtime-fault handler: latch the routing
        // safety fault, then drive the same canonical FailClosedAsync() the Steam-output publisher
        // fault path already uses (PublisherFaultInvokesTheActualRuntimeFailClosedPath above) --
        // proving an unexpected physical-input loss retires the active session/Steam output via
        // the existing fail-close path, with no second fault mechanism.
        var executor = new FakeExecutor();
        var provider = new FakeStatusProvider(Snapshot(Eligible(), Software()));
        var bridge = Create(provider, executor);
        Assert.True((await bridge.Bridge.ReconcileAsync(CancellationToken.None)).Succeeded);
        Assert.True(bridge.Bridge.ActiveSessionHasSteamOutputEnabled);

        var events = new List<string>();
        var safetySession = new FakeSafetySession(events);
        Func<string, ValueTask> runtimeFaultHandler = async reason =>
        {
            await safetySession.LatchRoutingFaultAsync(reason, CancellationToken.None).ConfigureAwait(false);
            events.Add("FailCloseStarted");
            var rollback = await bridge.Bridge.FailClosedAsync().ConfigureAwait(false);
            Assert.True(rollback.Succeeded);
        };

        await runtimeFaultHandler(MsiClawPhysicalInputFaultPolicy.PhysicalInputSessionLostReason);

        Assert.Equal([MsiClawPhysicalInputFaultPolicy.PhysicalInputSessionLostReason], safetySession.LatchedReasons);
        Assert.Single(executor.RollbackPlans);
        Assert.Null(bridge.Session.ActiveSession);
        Assert.False(bridge.Bridge.ActiveSessionHasSteamOutputEnabled);
        Assert.Equal(RoutingOperationalState.Passive, bridge.Bridge.CurrentOperationalState);

        // The fault must be latched strictly before fail-close runs, not merely both eventually
        // called -- otherwise a still-eligible Steam session could race back in immediately after
        // rollback completes.
        Assert.Equal(["Latch:PhysicalInputSessionLost", "FailCloseStarted"], events);
    }

    [Fact]
    public async Task InvalidSoftwareSnapshotFailsClosedWithoutMutation()
    {
        var executor = new FakeExecutor();
        var provider = new FakeStatusProvider(Snapshot(Eligible(), Software().Where(status => status.Kind != ControllerSoftwareKind.HandheldCompanion).ToArray()));
        var bridge = Create(provider, executor);

        var result = await bridge.Bridge.ReconcileAsync(CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Empty(executor.ExecutedPlans);
        Assert.Null(bridge.Session.ActiveSession);
    }

    [Fact]
    public async Task RepeatedEligibleDoesNotRebuildActiveSession()
    {
        var executor = new FakeExecutor();
        var provider = new FakeStatusProvider(Snapshot(Eligible(), Software()), Snapshot(Eligible(), Software()));
        var bridge = Create(provider, executor);

        await bridge.Bridge.ReconcileAsync(CancellationToken.None);
        var frozen = bridge.Session.ActiveSession;
        await bridge.Bridge.ReconcileAsync(CancellationToken.None);

        Assert.Single(executor.ExecutedPlans);
        Assert.Equal(frozen, bridge.Session.ActiveSession);
    }

    [Fact]
    public async Task FailClosedRetiresActiveSessionWithoutTerminatingRuntime()
    {
        var executor = new FakeExecutor();
        var provider = new FakeStatusProvider(Snapshot(Eligible(), Software()), Snapshot(Eligible(), Software()));
        var bridge = Create(provider, executor);

        await bridge.Bridge.ReconcileAsync(CancellationToken.None);
        var result = await bridge.Bridge.FailClosedAsync();

        Assert.True(result.Succeeded);
        Assert.Null(bridge.Session.ActiveSession);
        Assert.Null(bridge.Session.PendingCleanup);
        Assert.Equal(RoutingOperationalState.Passive, bridge.Session.CurrentState);

        await bridge.Bridge.ReconcileAsync(CancellationToken.None);
        Assert.Equal(2, executor.ExecutedPlans.Count);
    }

    [Fact]
    public async Task FailClosedCleanupFailurePreservesPendingCleanup()
    {
        var executor = new FakeExecutor();
        executor.RollbackResults.Enqueue(new(false, RoutingStageKind.NativeMode, "CleanupFailed"));
        var provider = new FakeStatusProvider(Snapshot(Eligible(), Software()));
        var bridge = Create(provider, executor);

        await bridge.Bridge.ReconcileAsync(CancellationToken.None);
        var result = await bridge.Bridge.FailClosedAsync();

        Assert.False(result.Succeeded);
        Assert.NotNull(bridge.Session.PendingCleanup);
        Assert.Equal(RoutingOperationalState.OverrideActive, bridge.Session.CurrentState);
    }

    [Fact]
    public async Task PostRecoverySteamInactiveAppliesSessionBoundary()
    {
        var executor = new FakeExecutor();
        var participant = new FakeBoundaryParticipant();
        var provider = new FakeStatusProvider(
            Snapshot(Eligible(), Software()),
            Snapshot(new(RoutingDecisionKind.WaitingForSteam, RoutingDecisionReason.SteamInactive), Software()));
        var bridge = Create(provider, executor, participant);

        await bridge.Bridge.ReconcileAsync(CancellationToken.None);
        Assert.True(await bridge.Bridge.ReconcileFreshAfterResumeAsync(CancellationToken.None));

        Assert.Equal(1, participant.CallCount);
        Assert.Null(bridge.Session.ActiveSession);
        Assert.Equal(RoutingOperationalState.Passive, bridge.Session.CurrentState);
        Assert.Single(executor.RollbackPlans);
        Assert.Single(executor.ExecutedPlans);
    }

    [Fact]
    public async Task RecoveryRetiresOldSessionThenFreshEnters()
    {
        var executor = new FakeExecutor();
        var provider = new FakeStatusProvider(Snapshot(Eligible(), Software()), Snapshot(Eligible(), Software()));
        var bridge = Create(provider, executor);

        await bridge.Bridge.ReconcileAsync(CancellationToken.None);
        var oldSession = bridge.Session.ActiveSession!;
        Assert.True(await bridge.Bridge.ReconcileFreshAfterResumeAsync(CancellationToken.None));
        var newSession = bridge.Session.ActiveSession!;

        Assert.Equal(2, executor.ExecutedPlans.Count);
        Assert.Single(executor.RollbackPlans);
        Assert.Equal(oldSession.Plan, executor.RollbackPlans[0]);
        Assert.NotSame(oldSession, newSession);
        Assert.Equal(RoutingPipelinePlan.StockCenterM, newSession.Plan);
    }

    [Fact]
    public async Task RecoveryRetirementFailureBlocksFreshCaptureAndEntry()
    {
        var executor = new FakeExecutor();
        executor.RollbackResults.Enqueue(new(false, RoutingStageKind.NativeMode, "CleanupFailed"));
        var provider = new FakeStatusProvider(Snapshot(Eligible(), Software()), Snapshot(Eligible(), Software()));
        var bridge = Create(provider, executor);

        await bridge.Bridge.ReconcileAsync(CancellationToken.None);
        Assert.False(await bridge.Bridge.ReconcileFreshAfterResumeAsync(CancellationToken.None));

        Assert.Equal(1, provider.CaptureCount);
        Assert.Single(executor.ExecutedPlans);
        Assert.NotNull(bridge.Session.PendingCleanup);
    }

    [Fact]
    public async Task RecoveryPendingCleanupRetriesBeforeFreshEntry()
    {
        var executor = new FakeExecutor();
        executor.RollbackResults.Enqueue(new(false, RoutingStageKind.NativeMode, "CleanupFailed"));
        var provider = new FakeStatusProvider(Snapshot(Eligible(), Software()), Snapshot(Eligible(), Software()));
        var bridge = Create(provider, executor);

        await bridge.Bridge.ReconcileAsync(CancellationToken.None);
        var frozenPlan = bridge.Session.ActiveSession!.Plan;

        Assert.False(await bridge.Bridge.ReconcileFreshAfterResumeAsync(CancellationToken.None));
        Assert.NotNull(bridge.Session.PendingCleanup);
        Assert.Equal(1, provider.CaptureCount);
        Assert.Single(executor.ExecutedPlans);

        executor.RollbackResults.Enqueue(new(true, null, "Success"));
        Assert.True(await bridge.Bridge.ReconcileFreshAfterResumeAsync(CancellationToken.None));

        Assert.Null(bridge.Session.PendingCleanup);
        Assert.NotNull(bridge.Session.ActiveSession);
        Assert.Equal(2, executor.RollbackPlans.Count);
        Assert.Equal(frozenPlan, executor.RollbackPlans[0]);
        Assert.Equal(frozenPlan, executor.RollbackPlans[1]);
        Assert.Equal(2, provider.CaptureCount);
        Assert.Equal(2, executor.ExecutedPlans.Count);
    }

    [Fact]
    public async Task ShutdownRollsBackWithoutCapturingStatus()
    {
        var executor = new FakeExecutor();
        var provider = new FakeStatusProvider(Snapshot(Eligible(), Software()));
        var bridge = Create(provider, executor);
        await bridge.Bridge.ReconcileAsync(CancellationToken.None);

        var result = await bridge.Bridge.ShutdownAsync();

        Assert.True(result.Succeeded);
        Assert.Single(executor.RollbackPlans);
        Assert.False(executor.LastRollbackCancellationTokenCancelled);
        Assert.Null(bridge.Session.ActiveSession);
        Assert.Equal(1, provider.CaptureCount);
    }

    [Theory]
    [InlineData("PhysicalRumbleFinalStopFailed")]
    [InlineData("SteamDeckFeedbackCallbackClearFailed")]
    public async Task ShutdownPreservesPendingCanonicalCleanupForFeedbackBarrier(string reason)
    {
        var executor = new FakeExecutor();
        var provider = new FakeStatusProvider(Snapshot(Eligible(), Software()));
        var bridge = Create(provider, executor);
        await bridge.Bridge.ReconcileAsync(CancellationToken.None);
        executor.RollbackResults.Enqueue(new(false, RoutingStageKind.SteamOutput, reason));

        var result = await bridge.Bridge.ShutdownAsync();

        Assert.False(result.Succeeded);
        Assert.Contains(reason, result.Reason);
        Assert.NotNull(bridge.Session.PendingCleanup);
        Assert.Equal(RoutingStageMode.Enabled, bridge.Session.PendingCleanup!.Session.Plan.SteamOutput);
        Assert.Equal(RoutingActionKind.ExitOverride, bridge.Session.PendingCleanup.OriginAction);
        Assert.NotNull(bridge.Session.PendingCleanup.Session);
    }

    [Fact]
    public async Task SteamSessionBoundaryRunsAfterSuccessfulCleanup()
    {
        var executor = new FakeExecutor();
        var participant = new FakeBoundaryParticipant();
        var provider = new FakeStatusProvider(Snapshot(Eligible(), Software()), Snapshot(new(RoutingDecisionKind.WaitingForSteam, RoutingDecisionReason.SteamInactive), Software()));
        var bridge = Create(provider, executor, participant);
        await bridge.Bridge.ReconcileAsync(CancellationToken.None);

        var result = await bridge.Bridge.ReconcileAsync(CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(1, participant.CallCount);
        Assert.Single(executor.RollbackPlans);
    }

    [Fact]
    public async Task SteamSessionBoundaryFailurePropagates()
    {
        var executor = new FakeExecutor();
        var participant = new FakeBoundaryParticipant { Result = false };
        var provider = new FakeStatusProvider(Snapshot(Eligible(), Software()), Snapshot(new(RoutingDecisionKind.WaitingForSteam, RoutingDecisionReason.SteamInactive), Software()));
        var bridge = Create(provider, executor, participant);
        await bridge.Bridge.ReconcileAsync(CancellationToken.None);

        var result = await bridge.Bridge.ReconcileAsync(CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("SteamSessionBoundaryFailed", result.Reason);
        Assert.Equal(1, participant.CallCount);
    }

    [Fact]
    public async Task CleanupFailureDoesNotInvokeSteamSessionBoundary()
    {
        var executor = new FakeExecutor();
        executor.RollbackResults.Enqueue(new(false, RoutingStageKind.NativeMode, "CleanupFailed"));
        var participant = new FakeBoundaryParticipant();
        var provider = new FakeStatusProvider(Snapshot(Eligible(), Software()), Snapshot(new(RoutingDecisionKind.WaitingForSteam, RoutingDecisionReason.SteamInactive), Software()));
        var bridge = Create(provider, executor, participant);
        await bridge.Bridge.ReconcileAsync(CancellationToken.None);

        var result = await bridge.Bridge.ReconcileAsync(CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(0, participant.CallCount);
        Assert.NotNull(bridge.Session.PendingCleanup);
    }

    [Fact]
    public async Task FullNormalReconciliationIsSerializedBeforeStatusCapture()
    {
        var executor = new FakeExecutor();
        var provider = new FakeStatusProvider(Snapshot(Eligible(), Software()), Snapshot(Eligible(), Software()))
        {
            BlockNextCapture = true
        };
        var bridge = Create(provider, executor);
        var first = bridge.Bridge.ReconcileAsync(CancellationToken.None).AsTask();
        await provider.CaptureStarted.Task;
        var second = bridge.Bridge.ReconcileAsync(CancellationToken.None).AsTask();
        await Task.Delay(10);
        Assert.Equal(1, provider.CaptureCount);

        provider.ReleaseCapture.TrySetResult();
        await Task.WhenAll(first, second);
        Assert.Equal(2, provider.CaptureCount);
        Assert.Single(executor.ExecutedPlans);
    }

    [Fact]
    public async Task TerminationSnapshotShowsInFlightReconcile()
    {
        var executor = new FakeExecutor();
        var provider = new FakeStatusProvider(Snapshot(Eligible(), Software())) { BlockNextCapture = true };
        var bridge = Create(provider, executor);
        var reconcile = bridge.Bridge.ReconcileAsync(CancellationToken.None).AsTask();
        await provider.CaptureStarted.Task;

        Assert.True(bridge.Bridge.CaptureTerminationSnapshot().TransitionInProgress);
        provider.ReleaseCapture.TrySetResult();
        await reconcile;
        Assert.False(bridge.Bridge.CaptureTerminationSnapshot().TransitionInProgress);
    }

    [Fact]
    public async Task InteractivePresentationPermissionTracksRoutingTransition()
    {
        var executor = new FakeExecutor();
        var provider = new FakeStatusProvider(Snapshot(Eligible(), Software())) { BlockNextCapture = true };
        var bridge = Create(provider, executor);
        var reconcile = bridge.Bridge.ReconcileAsync(CancellationToken.None).AsTask();
        await provider.CaptureStarted.Task;

        Assert.False(bridge.Bridge.CanApplyInteractivePresentation);
        provider.ReleaseCapture.TrySetResult();
        await reconcile;
        Assert.True(bridge.Bridge.CanApplyInteractivePresentation);
    }

    [Fact]
    public async Task SuspendQuiesceCountsAsInteractivePresentationTransition()
    {
        var executor = new FakeExecutor { BlockNextRollback = true };
        var provider = new FakeStatusProvider(Snapshot(Eligible(), Software()));
        var bridge = Create(provider, executor);
        await bridge.Bridge.ReconcileAsync(CancellationToken.None);

        var quiesce = bridge.Bridge.QuiesceForSuspendAsync(
            DateTimeOffset.UtcNow.AddSeconds(5), 1, 1, CancellationToken.None);
        await executor.RollbackStarted.Task;

        Assert.False(bridge.Bridge.CanApplyInteractivePresentation);
        Assert.True(bridge.Bridge.CaptureTerminationSnapshot().TransitionInProgress);
        executor.ReleaseRollback.TrySetResult();
        Assert.True(await quiesce);
        Assert.True(bridge.Bridge.CanApplyInteractivePresentation);
    }

    [Fact]
    public async Task CancelledQueuedSuspendPropagatesAndDoesNotLeakTransitionPermission()
    {
        var executor = new FakeExecutor();
        var provider = new FakeStatusProvider(Snapshot(Eligible(), Software()));
        var bridge = Create(provider, executor);
        await bridge.Bridge.ReconcileAsync(CancellationToken.None);

        executor.BlockNextRollback = true;
        var holder = bridge.Bridge.RetryResidualCleanupForResumeAsync(CancellationToken.None).AsTask();
        await executor.RollbackStarted.Task;

        using var cancellation = new CancellationTokenSource();
        var quiesce = bridge.Bridge.QuiesceForSuspendAsync(
            DateTimeOffset.UtcNow.AddSeconds(5), 1, 1, cancellation.Token);
        Assert.False(bridge.Bridge.CanApplyInteractivePresentation);
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => quiesce);
        Assert.False(bridge.Bridge.CanApplyInteractivePresentation);
        executor.ReleaseRollback.TrySetResult();
        Assert.True(await holder);
        Assert.True(bridge.Bridge.CanApplyInteractivePresentation);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task GameBarMutationIsBenignNoOpAfterRetirementWhileOuterRollbackIsStillRunning(bool foreground)
    {
        var executor = new FakeExecutor { BlockNextRollback = true };
        var provider = new FakeStatusProvider(
            Snapshot(Eligible(), Software()),
            Snapshot(WaitingForSteam(), Software()));
        var bridge = CreateWithCallback(provider, executor, _ => Task.FromResult(true));

        await bridge.Bridge.ReconcileAsync(CancellationToken.None);
        var outerExit = bridge.Bridge.ReconcileAsync(CancellationToken.None).AsTask();
        await executor.RollbackStarted.Task;
        Assert.False(bridge.Bridge.CanApplyInteractivePresentation);

        using var presentationGate = new SemaphoreSlim(1, 1);
        var mutationCalls = 0;
        var failCloseCalls = 0;

        Func<CancellationToken, Task<bool>> enter = token =>
            AddonRoutingRuntime.RunGatedPresentationMutationAsync(
                presentationGate,
                () =>
                {
                    if (!bridge.Bridge.CanApplyInteractivePresentation)
                        return Task.FromResult((false, (string?)null));

                    mutationCalls++;
                    return Task.FromResult((true, (string?)null));
                },
                _ => { failCloseCalls++; return Task.CompletedTask; },
                token);
        Func<CancellationToken, Task<bool>> exit = token =>
            AddonRoutingRuntime.RunGatedPresentationMutationAsync(
                presentationGate,
                () =>
                {
                    if (!bridge.Bridge.CanApplyInteractivePresentation)
                        return Task.FromResult((false, (string?)null));

                    mutationCalls++;
                    return Task.FromResult((true, (string?)null));
                },
                _ => { failCloseCalls++; return Task.CompletedTask; },
                token);

        Assert.False(await AddonRoutingRuntime.HandleGameBarForegroundChangedCoreAsync(
            foreground, enter, exit, CancellationToken.None));
        Assert.Equal(0, mutationCalls);
        Assert.Equal(0, failCloseCalls);

        executor.ReleaseRollback.TrySetResult();
        Assert.True((await outerExit).Succeeded);
    }

    [Fact]
    public async Task CancelledQueuedReconcileDoesNotLeakTransitionSnapshot()
    {
        var executor = new FakeExecutor();
        var provider = new FakeStatusProvider(Snapshot(Eligible(), Software()), Snapshot(Eligible(), Software())) { BlockNextCapture = true };
        var bridge = Create(provider, executor);
        var first = bridge.Bridge.ReconcileAsync(CancellationToken.None).AsTask();
        await provider.CaptureStarted.Task;
        using var cancellation = new CancellationTokenSource();
        var queued = bridge.Bridge.ReconcileAsync(cancellation.Token).AsTask();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => queued);
        Assert.True(bridge.Bridge.CaptureTerminationSnapshot().TransitionInProgress);
        provider.ReleaseCapture.TrySetResult();
        await first;
        Assert.False(bridge.Bridge.CaptureTerminationSnapshot().TransitionInProgress);
    }

    [Fact]
    public async Task PowerBarrierCancellationCancelsInFlightPipelineTransition()
    {
        var executor = new FakeExecutor { BlockNextExecute = true };
        var provider = new FakeStatusProvider(Snapshot(Eligible(), Software()));
        var bridge = Create(provider, executor);
        var reconcile = bridge.Bridge.ReconcileAsync(CancellationToken.None).AsTask();
        await executor.ExecuteStarted.Task;

        bridge.Bridge.CancelInFlightTransition();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => reconcile);
        Assert.Null(bridge.Session.ActiveSession);
        Assert.False(bridge.Bridge.CaptureTerminationSnapshot().TransitionInProgress);
    }

    [Fact]
    public async Task PendingCleanupAppearsInTerminationSnapshotUntilRetrySucceeds()
    {
        var executor = new FakeExecutor();
        executor.RollbackResults.Enqueue(new(false, RoutingStageKind.NativeMode, "CleanupFailed"));
        var provider = new FakeStatusProvider(Snapshot(Eligible(), Software()));
        var bridge = Create(provider, executor);
        await bridge.Bridge.ReconcileAsync(CancellationToken.None);

        Assert.False((await bridge.Bridge.FailClosedAsync()).Succeeded);
        Assert.True(bridge.Bridge.CaptureTerminationSnapshot().HasPendingCleanup);
    }

    [Fact]
    public async Task PostRecoveryTransitionCannotInterleaveWithNormalReconcile()
    {
        var executor = new FakeExecutor { BlockNextRollback = true };
        var provider = new FakeStatusProvider(
            Snapshot(Eligible(), Software()),
            Snapshot(Eligible(), Software()),
            Snapshot(new(RoutingDecisionKind.WaitingForSteam, RoutingDecisionReason.SteamInactive), Software()));
        var bridge = Create(provider, executor);
        await bridge.Bridge.ReconcileAsync(CancellationToken.None);

        var recovery = bridge.Bridge.ReconcileFreshAfterResumeAsync(CancellationToken.None).AsTask();
        await executor.RollbackStarted.Task;
        var normal = bridge.Bridge.ReconcileAsync(CancellationToken.None).AsTask();
        await Task.Delay(10);
        Assert.Equal(1, provider.CaptureCount);

        executor.ReleaseRollback.TrySetResult();
        await Task.WhenAll(recovery, normal);
        Assert.Equal(3, provider.CaptureCount);
        Assert.Equal(2, executor.ExecutedPlans.Count);
    }

    [Fact]
    public async Task ShutdownRequestedPreventsQueuedReentry()
    {
        var executor = new FakeExecutor();
        var provider = new FakeStatusProvider(Snapshot(Eligible(), Software())) { BlockNextCapture = true };
        var bridge = Create(provider, executor);
        var first = bridge.Bridge.ReconcileAsync(CancellationToken.None).AsTask();
        await provider.CaptureStarted.Task;
        var shutdown = bridge.Bridge.ShutdownAsync().AsTask();
        var queued = bridge.Bridge.ReconcileAsync(CancellationToken.None).AsTask();
        provider.ReleaseCapture.TrySetResult();

        await Task.WhenAll(shutdown, queued);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
        Assert.Equal(1, provider.CaptureCount);
        Assert.Empty(executor.ExecutedPlans);
        Assert.Empty(executor.RollbackPlans);
        Assert.Null(bridge.Session.ActiveSession);
        Assert.Equal(RoutingOperationalState.Passive, bridge.Session.CurrentState);
    }

    [Fact]
    public async Task ShutdownCancelsInFlightForwardTransitionBeforeWaitingForGate()
    {
        var executor = new FakeExecutor { BlockNextExecute = true };
        var provider = new FakeStatusProvider(Snapshot(Eligible(), Software()));
        var bridge = Create(provider, executor);

        var reconcile = bridge.Bridge.ReconcileAsync(CancellationToken.None).AsTask();
        await executor.ExecuteStarted.Task;

        var shutdown = bridge.Bridge.ShutdownAsync().AsTask();
        await executor.ExecuteCancellationObserved.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await shutdown.WaitAsync(TimeSpan.FromSeconds(2));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => reconcile);
        Assert.Null(bridge.Session.ActiveSession);
        Assert.Null(bridge.Session.PendingCleanup);
        Assert.Equal(RoutingOperationalState.Passive, bridge.Session.CurrentState);
    }

    [Fact]
    public async Task RepeatedShutdownIsIdempotent()
    {
        var executor = new FakeExecutor();
        var bridge = Create(new FakeStatusProvider(Snapshot(Eligible(), Software())), executor);

        var first = bridge.Bridge.ShutdownAsync().AsTask();
        var second = bridge.Bridge.ShutdownAsync().AsTask();

        var results = await Task.WhenAll(first, second);
        bridge.Bridge.CancelInFlightTransition();
        var afterShutdown = await bridge.Bridge.ReconcileAsync(CancellationToken.None);

        Assert.All(results, result => Assert.True(result.Succeeded));
        Assert.True(afterShutdown.Succeeded);
        Assert.Equal("RuntimeShuttingDown", afterShutdown.Reason);
        Assert.Empty(executor.ExecutedPlans);
        Assert.Null(bridge.Session.ActiveSession);
        Assert.Null(bridge.Session.PendingCleanup);
    }

    [Fact]
    public async Task ShutdownIsTerminalAndLaterReconcileIsNoOp()
    {
        var executor = new FakeExecutor();
        var provider = new FakeStatusProvider(Snapshot(Eligible(), Software()));
        var bridge = Create(provider, executor);
        await bridge.Bridge.ReconcileAsync(CancellationToken.None);

        Assert.True((await bridge.Bridge.ShutdownAsync()).Succeeded);
        var captures = provider.CaptureCount;
        var result = await bridge.Bridge.ReconcileAsync(CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal("RuntimeShuttingDown", result.Reason);
        Assert.Equal(captures, provider.CaptureCount);
        Assert.Single(executor.ExecutedPlans);
        Assert.Null(bridge.Session.ActiveSession);
    }

    [Fact]
    public async Task RecoveryCancellationPropagates()
    {
        var executor = new FakeExecutor();
        var provider = new FakeStatusProvider(Snapshot(Eligible(), Software()), Snapshot(Eligible(), Software()));
        var bridge = Create(provider, executor);
        await bridge.Bridge.ReconcileAsync(CancellationToken.None);
        provider.ThrowOnNextCapture = true;

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => bridge.Bridge.ReconcileFreshAfterResumeAsync(new CancellationToken(true)).AsTask());
    }

    private static (RoutingPipelineRuntimeCoordinator Bridge, RoutingPipelineSessionCoordinator Session) Create(
        FakeStatusProvider provider,
        IRoutingPipelineExecutor executor,
        params IRoutingRuntimeSessionBoundaryParticipant[] participants)
    {
        var session = new RoutingPipelineSessionCoordinator(executor);
        return (new RoutingPipelineRuntimeCoordinator(provider, session, participants), session);
    }

    private static (RoutingPipelineRuntimeCoordinator Bridge, RoutingPipelineSessionCoordinator Session) CreateWithCallback(
        FakeStatusProvider provider,
        FakeExecutor executor,
        Func<CancellationToken, Task<bool>> beforeActiveSessionExit)
    {
        var session = new RoutingPipelineSessionCoordinator(executor);
        return (new RoutingPipelineRuntimeCoordinator(provider, session, beforeActiveSessionExit: beforeActiveSessionExit), session);
    }

    private static IReadOnlyList<ControllerSoftwareStatus> Software() =>
    [
        new(ControllerSoftwareKind.MsiCenterM, "MSI Center M", SoftwareInstallationStatus.Installed, SoftwareRuntimeStatus.Running, "Test"),
        new(ControllerSoftwareKind.ClawTweaks, "ClawTweaks", SoftwareInstallationStatus.NotInstalled, SoftwareRuntimeStatus.NotRunning, "Test"),
        new(ControllerSoftwareKind.HandheldCompanion, "Handheld Companion", SoftwareInstallationStatus.NotInstalled, SoftwareRuntimeStatus.NotRunning, "Test")
    ];

    private static SystemStatusSnapshot Snapshot(RoutingDecision decision, IReadOnlyList<ControllerSoftwareStatus> software) =>
        new(new("Test", "Test", "Test", []), null!, software, null!, null!, null!, decision, null!, true, false);

    private static RoutingDecision Eligible() => new(RoutingDecisionKind.Eligible, RoutingDecisionReason.Eligible);
    private static RoutingDecision WaitingForSteam() => new(RoutingDecisionKind.WaitingForSteam, RoutingDecisionReason.SteamInactive);

    private sealed class FakeStatusProvider(params SystemStatusSnapshot[] snapshots) : ISystemStatusProvider
    {
        private readonly Queue<SystemStatusSnapshot> _snapshots = new(snapshots);
        internal int CaptureCount { get; private set; }
        internal bool ThrowOnNextCapture { get; set; }
        internal bool BlockNextCapture { get; set; }
        internal TaskCompletionSource CaptureStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource ReleaseCapture { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<SystemStatusSnapshot> CaptureAsync(CancellationToken cancellationToken = default)
        {
            CaptureCount++;
            if (BlockNextCapture)
            {
                BlockNextCapture = false;
                CaptureStarted.TrySetResult();
                await ReleaseCapture.Task.WaitAsync(cancellationToken);
            }
            if (ThrowOnNextCapture)
            {
                ThrowOnNextCapture = false;
                throw new OperationCanceledException(cancellationToken);
            }
            return _snapshots.Count > 0 ? _snapshots.Dequeue() : snapshots[^1];
        }
    }

    private sealed class FakeSafetySession(List<string> events) : IRoutingSafetySession
    {
        internal List<string> LatchedReasons { get; } = [];
        public bool IsActive => false;
        public bool HasOwnedRecoveryBoundary => false;
        public Guid? CurrentRecoverySessionId => null;

        public Task LatchRoutingFaultAsync(string reason, CancellationToken cancellationToken = default)
        {
            LatchedReasons.Add(reason);
            events.Add($"Latch:{reason}");
            return Task.CompletedTask;
        }

        public Task FailClosedAsync(string reason, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<bool> ConvergeAfterRoutingCleanupAsync(CancellationToken cancellationToken = default)
        {
            events.Add("ConvergeAfterRoutingCleanup");
            return Task.FromResult(true);
        }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class SuspendBoundaryParticipant : IRoutingRuntimeSessionBoundaryParticipant
    {
        internal int CallCount { get; private set; }
        public ValueTask<bool> OnSteamSessionEndedAsync(CancellationToken cancellationToken)
        {
            CallCount++;
            return ValueTask.FromResult(true);
        }
    }

    private sealed class FakeExecutor(List<string>? events = null) : IRoutingPipelineExecutor
    {
        internal List<RoutingPipelinePlan> ExecutedPlans { get; } = [];
        internal List<RoutingPipelinePlan> RollbackPlans { get; } = [];
        internal bool LastRollbackCancellationTokenCancelled { get; private set; }
        internal Queue<RoutingPipelineRollbackResult> RollbackResults { get; } = [];
        internal bool BlockNextExecute { get; set; }
        internal bool BlockNextRollback { get; set; }
        internal TaskCompletionSource ExecuteStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource ExecuteCancellationObserved { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource RollbackStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource ReleaseRollback { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public ValueTask<RoutingPipelineExecutionResult> ExecuteAsync(RoutingPipelinePlan plan, CancellationToken cancellationToken)
        {
            ExecutedPlans.Add(plan);
            return ExecuteCoreAsync(cancellationToken);
        }

        private async ValueTask<RoutingPipelineExecutionResult> ExecuteCoreAsync(CancellationToken cancellationToken)
        {
            if (BlockNextExecute)
            {
                BlockNextExecute = false;
                ExecuteStarted.TrySetResult();
                try { await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken); }
                catch (OperationCanceledException)
                {
                    ExecuteCancellationObserved.TrySetResult();
                    throw;
                }
            }
            return RoutingPipelineExecutionResult.Success();
        }

        public ValueTask<RoutingPipelineRollbackResult> RollbackAsync(RoutingPipelinePlan plan, CancellationToken cancellationToken)
        {
            RollbackPlans.Add(plan);
            events?.Add("PipelineRollback");
            LastRollbackCancellationTokenCancelled = cancellationToken.IsCancellationRequested;
            return RollbackCoreAsync(plan, cancellationToken);
        }

        private async ValueTask<RoutingPipelineRollbackResult> RollbackCoreAsync(RoutingPipelinePlan plan, CancellationToken cancellationToken)
        {
            if (BlockNextRollback)
            {
                BlockNextRollback = false;
                RollbackStarted.TrySetResult();
                await ReleaseRollback.Task.WaitAsync(cancellationToken);
            }
            return RollbackResults.Count == 0 ? new RoutingPipelineRollbackResult(true, null, "Success") : RollbackResults.Dequeue();
        }
    }

    private sealed class InlineCancelExecutor : IRoutingPipelineExecutor
    {
        internal TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal int ExecuteCount { get; private set; }

        public async ValueTask<RoutingPipelineExecutionResult> ExecuteAsync(
            RoutingPipelinePlan plan,
            CancellationToken cancellationToken)
        {
            ExecuteCount++;
            var blocked = new TaskCompletionSource<RoutingPipelineExecutionResult>();
            using var registration = cancellationToken.Register(() => blocked.TrySetCanceled(cancellationToken));
            Started.TrySetResult();
            return await blocked.Task.ConfigureAwait(false);
        }

        public ValueTask<RoutingPipelineRollbackResult> RollbackAsync(
            RoutingPipelinePlan plan,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(new RoutingPipelineRollbackResult(true, null, "Success"));
    }

    private sealed class FakeBoundaryParticipant : IRoutingRuntimeSessionBoundaryParticipant
    {
        internal bool Result { get; set; } = true;
        internal int CallCount { get; private set; }
        public ValueTask<bool> OnSteamSessionEndedAsync(CancellationToken cancellationToken)
        {
            CallCount++;
            return ValueTask.FromResult(Result);
        }
    }

    private sealed class PublisherSnapshot : IControllerStateSnapshotSource
    { public ControllerState LatestState => new(new AuxiliaryButtonState([false, false])); }
    private sealed class PublisherFailingSink : ICanonicalSteamDeckStateSink
    {
        public bool SetState(SteamDeckDeviceState state) => false;
    }
    private sealed class PublisherManualTicks : IInputReportTickSource
    {
        private readonly Queue<TaskCompletionSource<bool>> _waiters = new();
        public ValueTask<bool> WaitForTickAsync(CancellationToken token)
        { var waiter = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously); _waiters.Enqueue(waiter); token.Register(() => waiter.TrySetCanceled(token)); return new(waiter.Task); }
        public void Tick() { Assert.NotEmpty(_waiters); _waiters.Dequeue().TrySetResult(true); }
    }
}
