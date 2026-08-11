using SteamInputAddonforClaw.Routing;
using SteamInputAddonforClaw.Status;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class RoutingPipelineRuntimeCoordinatorTests
{
    [Fact]
    public async Task StockEligibleUsesCanonicalSnapshotAndStockBaseline()
    {
        var executor = new FakeExecutor();
        var provider = new FakeStatusProvider(Snapshot(Eligible(), Software()));
        var bridge = Create(provider, executor);

        var result = await bridge.Bridge.ReconcileAsync(CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Single(executor.ExecutedPlans);
        var plan = executor.ExecutedPlans.Single();
        Assert.Equal(RoutingStageMode.Enabled, plan.NativeMode);
        Assert.Equal(RoutingStageMode.Disabled, plan.PhysicalInput);
        Assert.NotNull(bridge.Session.ActiveSession);
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
    public async Task RecoveryCancellationPropagates()
    {
        var executor = new FakeExecutor();
        var provider = new FakeStatusProvider(Snapshot(Eligible(), Software()), Snapshot(Eligible(), Software()));
        var bridge = Create(provider, executor);
        await bridge.Bridge.ReconcileAsync(CancellationToken.None);
        provider.ThrowOnNextCapture = true;

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => bridge.Bridge.ReconcileAfterRecoveryAsync(new CancellationToken(true)).AsTask());
    }

    private static (RoutingPipelineRuntimeCoordinator Bridge, RoutingPipelineSessionCoordinator Session) Create(FakeStatusProvider provider, FakeExecutor executor)
    {
        var session = new RoutingPipelineSessionCoordinator(new RoutingEnvironmentStrategyResolver(), executor);
        return (new RoutingPipelineRuntimeCoordinator(provider, session), session);
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

        public Task<SystemStatusSnapshot> CaptureAsync(CancellationToken cancellationToken = default)
        {
            CaptureCount++;
            if (ThrowOnNextCapture)
            {
                ThrowOnNextCapture = false;
                throw new OperationCanceledException(cancellationToken);
            }
            return Task.FromResult(_snapshots.Count > 0 ? _snapshots.Dequeue() : snapshots[^1]);
        }
    }

    private sealed class FakeExecutor : IRoutingPipelineExecutor
    {
        internal List<RoutingPipelinePlan> ExecutedPlans { get; } = [];
        internal List<RoutingPipelinePlan> RollbackPlans { get; } = [];
        internal Queue<RoutingPipelineRollbackResult> RollbackResults { get; } = [];

        public ValueTask<RoutingPipelineExecutionResult> ExecuteAsync(RoutingPipelinePlan plan, CancellationToken cancellationToken)
        {
            ExecutedPlans.Add(plan);
            return ValueTask.FromResult(RoutingPipelineExecutionResult.Success());
        }

        public ValueTask<RoutingPipelineRollbackResult> RollbackAsync(RoutingPipelinePlan plan, CancellationToken cancellationToken)
        {
            RollbackPlans.Add(plan);
            return ValueTask.FromResult(RollbackResults.Count == 0 ? new RoutingPipelineRollbackResult(true, null, "Success") : RollbackResults.Dequeue());
        }
    }
}
