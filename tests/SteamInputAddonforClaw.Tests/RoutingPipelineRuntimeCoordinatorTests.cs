using SteamInputAddonforClaw.Routing;
using SteamInputAddonforClaw.Status;
using SteamInputAddonforClaw.Input;
using SteamInputAddonforClaw.Devices.MSI.Claw;
using SteamInputAddonforClaw.VirtualOutput.Viiper;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class RoutingPipelineRuntimeCoordinatorTests
{
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
    public async Task FailClosedRollsBackTheActiveExperimentalPipeline()
    {
        var executor = new FakeExecutor();
        var provider = new FakeStatusProvider(Snapshot(Eligible(), Software()));
        var bridge = CreateWithOptions(provider, executor, () => new RoutingExperimentOptions(
            new(RoutingStageMode.Enabled, RoutingStageMode.Enabled, RoutingStageMode.Enabled, SteamOutput: RoutingStageMode.Enabled),
            RoutingStageExperimentOptions.None));

        Assert.True((await bridge.Bridge.ReconcileAsync(CancellationToken.None)).Succeeded);
        var result = await bridge.Bridge.FailClosedAsync();

        Assert.True(result.Succeeded);
        Assert.Single(executor.RollbackPlans);
        Assert.Null(bridge.Session.ActiveSession);
    }

    [Fact]
    public async Task PublisherFaultInvokesTheActualRuntimeFailClosedPath()
    {
        var executor = new FakeExecutor();
        var provider = new FakeStatusProvider(Snapshot(Eligible(), Software()));
        var bridge = CreateWithOptions(provider, executor, () => new RoutingExperimentOptions(
            new(RoutingStageMode.Enabled, RoutingStageMode.Enabled, RoutingStageMode.Enabled, SteamOutput: RoutingStageMode.Enabled),
            RoutingStageExperimentOptions.None));
        Assert.True((await bridge.Bridge.ReconcileAsync(CancellationToken.None)).Succeeded);

        var ticks = new PublisherManualTicks();
        var runtime = new PublisherFailingRuntime();
        var failClosed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var publisher = new ClassicSteamControllerInputPublisher(
            new PublisherSnapshot(), runtime, 7, ticks,
            exception => _ = Task.Run(async () => { var result = await bridge.Bridge.FailClosedAsync(); if (result.Succeeded) failClosed.TrySetResult(); }));
        publisher.Start(); await Task.Yield(); ticks.Tick();

        await failClosed.Task.WaitAsync(TimeSpan.FromSeconds(1));
        await publisher.StopAsync();
        Assert.Single(executor.RollbackPlans);
        Assert.Null(bridge.Session.ActiveSession);
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
        Assert.True(await bridge.Bridge.ReconcileAfterRecoveryAsync(CancellationToken.None));

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
        var oldPlan = bridge.Session.ActiveSession!.Plan;
        Assert.True(await bridge.Bridge.ReconcileAfterRecoveryAsync(CancellationToken.None));

        Assert.Equal(2, executor.ExecutedPlans.Count);
        Assert.Single(executor.RollbackPlans);
        Assert.Equal(oldPlan, executor.RollbackPlans[0]);
        Assert.NotSame(oldPlan, bridge.Session.ActiveSession!.Plan);
    }

    [Fact]
    public async Task RecoveryRetirementFailureBlocksFreshCaptureAndEntry()
    {
        var executor = new FakeExecutor();
        executor.RollbackResults.Enqueue(new(false, RoutingStageKind.NativeMode, "CleanupFailed"));
        var provider = new FakeStatusProvider(Snapshot(Eligible(), Software()), Snapshot(Eligible(), Software()));
        var bridge = Create(provider, executor);

        await bridge.Bridge.ReconcileAsync(CancellationToken.None);
        Assert.False(await bridge.Bridge.ReconcileAfterRecoveryAsync(CancellationToken.None));

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

        Assert.False(await bridge.Bridge.ReconcileAfterRecoveryAsync(CancellationToken.None));
        Assert.NotNull(bridge.Session.PendingCleanup);
        Assert.Equal(1, provider.CaptureCount);
        Assert.Single(executor.ExecutedPlans);

        executor.RollbackResults.Enqueue(new(true, null, "Success"));
        Assert.True(await bridge.Bridge.ReconcileAfterRecoveryAsync(CancellationToken.None));

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
        Assert.Null(bridge.Session.ActiveSession);
        Assert.Equal(1, provider.CaptureCount);
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

        var recovery = bridge.Bridge.ReconcileAfterRecoveryAsync(CancellationToken.None).AsTask();
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

        await Task.WhenAll(first, shutdown, queued);
        Assert.Equal(1, provider.CaptureCount);
        Assert.Single(executor.ExecutedPlans);
        Assert.Single(executor.RollbackPlans);
        Assert.Null(bridge.Session.ActiveSession);
        Assert.Equal(RoutingOperationalState.Passive, bridge.Session.CurrentState);
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

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => bridge.Bridge.ReconcileAfterRecoveryAsync(new CancellationToken(true)).AsTask());
    }

    private static (RoutingPipelineRuntimeCoordinator Bridge, RoutingPipelineSessionCoordinator Session) Create(FakeStatusProvider provider, FakeExecutor executor, params IRoutingRuntimeSessionBoundaryParticipant[] participants)
    {
        var session = new RoutingPipelineSessionCoordinator(new RoutingEnvironmentStrategyResolver(), executor);
        return (new RoutingPipelineRuntimeCoordinator(provider, session, participants), session);
    }

    private static (RoutingPipelineRuntimeCoordinator Bridge, RoutingPipelineSessionCoordinator Session) CreateWithOptions(FakeStatusProvider provider, FakeExecutor executor, Func<RoutingExperimentOptions> options)
    {
        var session = new RoutingPipelineSessionCoordinator(new RoutingEnvironmentStrategyResolver(), executor);
        return (new RoutingPipelineRuntimeCoordinator(provider, session, experimentOptionsProvider: options), session);
    }

    private static IReadOnlyList<ControllerSoftwareStatus> Software() =>
    [
        new(ControllerSoftwareKind.MsiCenterM, "MSI Center M", SoftwareInstallationStatus.Installed, SoftwareRuntimeStatus.Running, "Test"),
        new(ControllerSoftwareKind.ClawTweaks, "ClawTweaks", SoftwareInstallationStatus.NotInstalled, SoftwareRuntimeStatus.NotRunning, "Test"),
        new(ControllerSoftwareKind.HandheldCompanion, "Handheld Companion", SoftwareInstallationStatus.NotInstalled, SoftwareRuntimeStatus.NotRunning, "Test")
    ];

    private static SystemStatusSnapshot Snapshot(RoutingDecision decision, IReadOnlyList<ControllerSoftwareStatus> software) =>
        new(new("Test", "Test", "Test", []), null!, software, null!, null!, null!, null!, decision, null!, true);

    private static RoutingDecision Eligible() => new(RoutingDecisionKind.Eligible, RoutingDecisionReason.Eligible);

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

    private sealed class FakeExecutor : IRoutingPipelineExecutor
    {
        internal List<RoutingPipelinePlan> ExecutedPlans { get; } = [];
        internal List<RoutingPipelinePlan> RollbackPlans { get; } = [];
        internal Queue<RoutingPipelineRollbackResult> RollbackResults { get; } = [];
        internal bool BlockNextExecute { get; set; }
        internal bool BlockNextRollback { get; set; }
        internal TaskCompletionSource ExecuteStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
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
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
            return RoutingPipelineExecutionResult.Success();
        }

        public ValueTask<RoutingPipelineRollbackResult> RollbackAsync(RoutingPipelinePlan plan, CancellationToken cancellationToken)
        {
            RollbackPlans.Add(plan);
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
    private sealed class PublisherFailingRuntime : IViiperRuntime
    {
        public IReadOnlyCollection<uint> OwnedDeviceIds => [7]; public uint BusId => 1;
        public void Start() { } public uint CreateDevice() => 7; public bool SetNeutral(uint id) => true;
        public bool SetInput(uint id, byte[] report) => false;
        public ViiperDeviceRemovalResult RemoveDevice(uint bus, uint id) => new(true, true);
        public void StopIfUnused() { } public void Dispose() { }
    }
    private sealed class PublisherManualTicks : IInputReportTickSource
    {
        private readonly Queue<TaskCompletionSource<bool>> _waiters = new();
        public ValueTask<bool> WaitForTickAsync(CancellationToken token)
        { var waiter = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously); _waiters.Enqueue(waiter); token.Register(() => waiter.TrySetCanceled(token)); return new(waiter.Task); }
        public void Tick() { Assert.NotEmpty(_waiters); _waiters.Dequeue().TrySetResult(true); }
    }
}
