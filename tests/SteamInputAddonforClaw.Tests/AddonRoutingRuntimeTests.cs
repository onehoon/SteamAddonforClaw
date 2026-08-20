using SteamInputAddonforClaw.Controllers.Detection;
using SteamInputAddonforClaw.Devices;
using SteamInputAddonforClaw.Devices.Abstractions;
using SteamInputAddonforClaw.Devices.MSI.Claw;
using SteamInputAddonforClaw.Power;
using SteamInputAddonforClaw.Recovery;
using SteamInputAddonforClaw.Routing;
using SteamInputAddonforClaw.Status;
using SteamInputAddonforClaw.VirtualOutput.Viiper;
using SteamInputAddonforClaw.Input;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class AddonRoutingRuntimeTests
{
    [Fact]
    public async Task GameBar_foreground_active_route_requests_enter_once()
    {
        var enters = 0;
        var exits = 0;
        await AddonRoutingRuntime.HandleGameBarForegroundChangedCoreAsync(
            isForeground: true,
            steamOutputActive: true,
            xbox360PresentationOwned: false,
            enter: _ => { enters++; return Task.FromResult(true); },
            exit: _ => { exits++; return Task.FromResult(true); },
            CancellationToken.None);

        await AddonRoutingRuntime.HandleGameBarForegroundChangedCoreAsync(
            isForeground: true,
            steamOutputActive: true,
            xbox360PresentationOwned: true,
            enter: _ => { enters++; return Task.FromResult(true); },
            exit: _ => { exits++; return Task.FromResult(true); },
            CancellationToken.None);

        Assert.Equal(1, enters);
        Assert.Equal(0, exits);
    }

    [Fact]
    public async Task GameBar_foreground_inactive_route_is_a_no_op()
    {
        var enters = 0;
        var exits = 0;
        await AddonRoutingRuntime.HandleGameBarForegroundChangedCoreAsync(
            true,
            steamOutputActive: false,
            xbox360PresentationOwned: false,
            _ => { enters++; return Task.FromResult(true); },
            _ => { exits++; return Task.FromResult(true); },
            CancellationToken.None);

        Assert.Equal(0, enters);
        Assert.Equal(0, exits);
    }

    [Fact]
    public async Task GameBar_leaving_with_owned_xbox360_requests_exit_once()
    {
        var enters = 0;
        var exits = 0;
        await AddonRoutingRuntime.HandleGameBarForegroundChangedCoreAsync(
            false,
            steamOutputActive: true,
            xbox360PresentationOwned: true,
            _ => { enters++; return Task.FromResult(true); },
            _ => { exits++; return Task.FromResult(true); },
            CancellationToken.None);

        await AddonRoutingRuntime.HandleGameBarForegroundChangedCoreAsync(
            false,
            steamOutputActive: true,
            xbox360PresentationOwned: false,
            _ => { enters++; return Task.FromResult(true); },
            _ => { exits++; return Task.FromResult(true); },
            CancellationToken.None);

        Assert.Equal(0, enters);
        Assert.Equal(1, exits);
    }

    [Fact]
    public async Task GameBar_policy_forwards_cancellation_to_selected_primitive()
    {
        using var cancellation = new CancellationTokenSource();
        var observed = CancellationToken.None;
        await AddonRoutingRuntime.HandleGameBarForegroundChangedCoreAsync(
            true,
            steamOutputActive: true,
            xbox360PresentationOwned: false,
            token => { observed = token; return Task.FromResult(true); },
            _ => Task.FromResult(true),
            cancellation.Token);

        Assert.Equal(cancellation.Token, observed);
    }

    [Fact]
    public async Task Xbox360_entry_orders_deck_pause_query_attach_and_first_live_publish()
    {
        var trace = new List<string>();
        var ticks = new ManualTicks();
        var snapshot = new FakeSnapshot();
        var stateWrites = new List<Xbox360DeviceState>();

        var result = await AddonRoutingRuntime.EnterXbox360PresentationCoreAsync(
            snapshot,
            () => { trace.Add("DeckPause"); return Task.FromResult(true); },
            (out USBDeviceAttachmentState state) => { trace.Add("Query"); state = USBDeviceAttachmentState.Detached; return true; },
            () => { trace.Add("Attach"); return USBDeviceAttachResult.Success; },
            () => { trace.Add("Detach"); return USBDeviceDetachResult.Success; },
            state => { trace.Add("X360State"); stateWrites.Add(state); return true; },
            ticks,
            _ => trace.Add("Fault"),
            CancellationToken.None);

        Assert.NotNull(result.Publisher);
        ticks.Tick();
        await WaitForAsync(() => stateWrites.Count == 1);
        Assert.Equal(["DeckPause", "Query", "Attach"], trace.Take(3));
        Assert.Single(stateWrites);
        await result.Publisher!.StopAsync();
    }

    [Fact]
    public async Task Xbox360_entry_does_not_attach_when_deck_pause_fails()
    {
        var attachCalls = 0;
        var stateCalls = 0;
        var result = await AddonRoutingRuntime.EnterXbox360PresentationCoreAsync(
            new FakeSnapshot(),
            () => Task.FromResult(false),
            (out USBDeviceAttachmentState state) => { state = USBDeviceAttachmentState.Detached; return true; },
            () => { attachCalls++; return USBDeviceAttachResult.Success; },
            () => USBDeviceDetachResult.Success,
            _ => { stateCalls++; return true; },
            null,
            _ => { },
            CancellationToken.None);

        Assert.Null(result.Publisher);
        Assert.Null(result.FailureReason);
        Assert.Equal(0, attachCalls);
        Assert.Equal(0, stateCalls);
    }

    [Theory]
    [InlineData(false, 0)]
    [InlineData(true, 1)]
    [InlineData(true, 2)]
    public async Task Xbox360_entry_requires_a_successful_detached_attachment_query(bool queryResult, int stateValue)
    {
        var attachCalls = 0;
        var result = await AddonRoutingRuntime.EnterXbox360PresentationCoreAsync(
            new FakeSnapshot(),
            () => Task.FromResult(true),
            (out USBDeviceAttachmentState state) => { state = (USBDeviceAttachmentState)stateValue; return queryResult; },
            () => { attachCalls++; return USBDeviceAttachResult.Success; },
            () => USBDeviceDetachResult.Success,
            _ => true,
            null,
            _ => { },
            CancellationToken.None);

        Assert.Null(result.Publisher);
        Assert.NotNull(result.FailureReason);
        Assert.Equal(0, attachCalls);
    }

    [Theory]
    [InlineData((int)USBDeviceAttachResult.RetryableFailure)]
    [InlineData((int)USBDeviceAttachResult.UnsafeOutcomeUnknown)]
    [InlineData((int)USBDeviceAttachResult.Invalid)]
    public async Task Xbox360_entry_does_not_start_publisher_after_attach_failure(int attachResultValue)
    {
        var attachResult = (USBDeviceAttachResult)attachResultValue;
        var starts = 0;
        var result = await AddonRoutingRuntime.EnterXbox360PresentationCoreAsync(
            new FakeSnapshot(),
            () => Task.FromResult(true),
            (out USBDeviceAttachmentState state) => { state = USBDeviceAttachmentState.Detached; return true; },
            () => { starts++; return attachResult; },
            () => USBDeviceDetachResult.Success,
            _ => true,
            null,
            _ => { },
            CancellationToken.None);

        Assert.Null(result.Publisher);
        Assert.Contains($"Xbox360Attach{attachResult}", result.FailureReason);
        Assert.Equal(1, starts);
    }

    [Theory]
    [InlineData((int)USBDeviceDetachResult.Success)]
    [InlineData((int)USBDeviceDetachResult.RetryableFailure)]
    public async Task Xbox360_publisher_start_failure_detaches_only_xbox360_and_preserves_cleanup_result(int detachResultValue)
    {
        var trace = new List<string>();
        var snapshot = new FakeSnapshot();
        var detachResult = (USBDeviceDetachResult)detachResultValue;
        var result = await AddonRoutingRuntime.EnterXbox360PresentationCoreAsync(
            snapshot,
            () => { trace.Add("DeckPause"); return Task.FromResult(true); },
            (out USBDeviceAttachmentState state) => { trace.Add("Query"); state = USBDeviceAttachmentState.Detached; return true; },
            () => { trace.Add("Attach"); return USBDeviceAttachResult.Success; },
            () => { trace.Add("Detach"); return detachResult; },
            _ => true,
            null,
            _ => trace.Add("UnexpectedFault"),
            CancellationToken.None,
            createPublisher: () =>
            {
                var publisher = new CanonicalXbox360InputPublisher(snapshot, _ => true, fault: _ => { });
                publisher.WorkerThreadStartOverrideForTests = _ =>
                    throw new InvalidOperationException("publisher start failed");
                return publisher;
            });

        Assert.Null(result.Publisher);
        Assert.Contains("Xbox360PublisherStartFailed:InvalidOperationException", result.FailureReason);
        Assert.Contains($"Detach={detachResult}", result.FailureReason);
        Assert.Equal(["DeckPause", "Query", "Attach", "Detach"], trace);
        Assert.DoesNotContain("UnexpectedFault", trace);
    }

    [Fact]
    public async Task Xbox360_exit_stops_before_detach_and_resumes_deck_after_detach()
    {
        var trace = new List<string>();
        var writes = 0;
        var ticks = new ManualTicks();
        var snapshot = new FakeSnapshot();
        var publisher = new CanonicalXbox360InputPublisher(snapshot, _ => { writes++; trace.Add("X360State"); return true; }, ticks);
        publisher.Start();

        var result = await AddonRoutingRuntime.ExitXbox360PresentationCoreAsync(
            publisher,
            async () => { await publisher.StopAsync(); trace.Add("Stop"); },
            () => { trace.Add("Detach"); return USBDeviceDetachResult.Success; },
            () => { trace.Add("DeckResume"); return Task.FromResult(true); },
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.True(result.PublisherReleased);
        Assert.Equal(["Stop", "Detach", "DeckResume"], trace);
        var writesAfterStop = writes;
        Assert.Throws<InvalidOperationException>(() => ticks.Tick());
        Assert.Equal(writesAfterStop, writes);
    }

    [Fact]
    public async Task Xbox360_exit_stop_failure_never_detaches_resumes_or_clears_ownership()
    {
        var publisher = new CanonicalXbox360InputPublisher(new FakeSnapshot(), _ => true, new ManualTicks());
        var detachCalls = 0;
        var resumeCalls = 0;
        var result = await AddonRoutingRuntime.ExitXbox360PresentationCoreAsync(
            publisher,
            () => Task.FromException(new TimeoutException("worker did not stop")),
            () => { detachCalls++; return USBDeviceDetachResult.Success; },
            () => { resumeCalls++; return Task.FromResult(true); },
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.False(result.PublisherReleased);
        Assert.Contains("Xbox360PublisherStopFailed:TimeoutException", result.FailureReason);
        Assert.Equal(0, detachCalls);
        Assert.Equal(0, resumeCalls);
    }

    [Theory]
    [InlineData((int)USBDeviceDetachResult.RetryableFailure)]
    [InlineData((int)USBDeviceDetachResult.UnsafeOutcomeUnknown)]
    [InlineData((int)USBDeviceDetachResult.Invalid)]
    public async Task Xbox360_exit_detach_failure_preserves_ownership_and_does_not_resume(int detachResultValue)
    {
        var publisher = new CanonicalXbox360InputPublisher(new FakeSnapshot(), _ => true, new ManualTicks());
        var resumeCalls = 0;
        var detachResult = (USBDeviceDetachResult)detachResultValue;
        var result = await AddonRoutingRuntime.ExitXbox360PresentationCoreAsync(
            publisher,
            () => Task.CompletedTask,
            () => detachResult,
            () => { resumeCalls++; return Task.FromResult(true); },
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.False(result.PublisherReleased);
        Assert.Contains($"Xbox360Detach{detachResult}", result.FailureReason);
        Assert.Equal(0, resumeCalls);
    }

    [Fact]
    public async Task Xbox360_exit_deck_resume_failure_does_not_request_x360_cleanup()
    {
        var publisher = new CanonicalXbox360InputPublisher(new FakeSnapshot(), _ => true, new ManualTicks());
        var detachCalls = 0;
        var result = await AddonRoutingRuntime.ExitXbox360PresentationCoreAsync(
            publisher,
            () => Task.CompletedTask,
            () => { detachCalls++; return USBDeviceDetachResult.Success; },
            () => Task.FromResult(false),
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.True(result.PublisherReleased);
        Assert.Null(result.FailureReason);
        Assert.Equal(1, detachCalls);
    }

    [Fact]
    public async Task Xbox360_exit_without_presentation_is_a_no_op_failure()
    {
        var result = await AddonRoutingRuntime.ExitXbox360PresentationCoreAsync(
            null,
            () => Task.CompletedTask,
            () => throw new InvalidOperationException("must not detach"),
            () => throw new InvalidOperationException("must not resume"),
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.False(result.PublisherReleased);
    }

    [Fact]
    public async Task Xbox360_retirement_stops_before_detach_without_resuming_deck()
    {
        var trace = new List<string>();
        var publisher = new CanonicalXbox360InputPublisher(new FakeSnapshot(), _ => true, new ManualTicks());
        var result = await AddonRoutingRuntime.RetireXbox360PresentationCoreAsync(
            publisher,
            () => { trace.Add("Stop"); return Task.CompletedTask; },
            () => { trace.Add("Detach"); return USBDeviceDetachResult.Success; },
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.True(result.PublisherReleased);
        Assert.Equal(["Stop", "Detach"], trace);
    }

    [Fact]
    public async Task Xbox360_retirement_stop_failure_blocks_detach_and_release()
    {
        var detachCalls = 0;
        var publisher = new CanonicalXbox360InputPublisher(new FakeSnapshot(), _ => true, new ManualTicks());
        var result = await AddonRoutingRuntime.RetireXbox360PresentationCoreAsync(
            publisher,
            () => Task.FromException(new TimeoutException("worker did not stop")),
            () => { detachCalls++; return USBDeviceDetachResult.Success; },
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.False(result.PublisherReleased);
        Assert.Equal(0, detachCalls);
    }

    [Theory]
    [InlineData((int)USBDeviceDetachResult.RetryableFailure)]
    [InlineData((int)USBDeviceDetachResult.UnsafeOutcomeUnknown)]
    [InlineData((int)USBDeviceDetachResult.Invalid)]
    public async Task Xbox360_retirement_detach_classification_retains_publisher(int resultValue)
    {
        var publisher = new CanonicalXbox360InputPublisher(new FakeSnapshot(), _ => true, new ManualTicks());
        var result = await AddonRoutingRuntime.RetireXbox360PresentationCoreAsync(
            publisher,
            () => Task.CompletedTask,
            () => (USBDeviceDetachResult)resultValue,
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.False(result.PublisherReleased);
        Assert.Contains($"Xbox360Detach{(USBDeviceDetachResult)resultValue}", result.FailureReason);
    }

    [Fact]
    public async Task Xbox360_retirement_detach_exception_retains_publisher()
    {
        var publisher = new CanonicalXbox360InputPublisher(new FakeSnapshot(), _ => true, new ManualTicks());
        var result = await AddonRoutingRuntime.RetireXbox360PresentationCoreAsync(
            publisher,
            () => Task.CompletedTask,
            () => throw new InvalidOperationException("detach failed"),
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.False(result.PublisherReleased);
        Assert.Contains("Xbox360DetachThrew=InvalidOperationException", result.FailureReason);
    }

    [Fact]
    public async Task Shutdown_retires_xbox360_before_coordinator_without_deck_resume()
    {
        var trace = new List<string>();
        var publisher = new CanonicalXbox360InputPublisher(new FakeSnapshot(), _ => true, new ManualTicks());
        var cleared = false;
        var result = await AddonRoutingRuntime.ShutdownAfterXbox360RetirementAsync(
            publisher,
            () => { trace.Add("Stop"); return Task.CompletedTask; },
            () => { trace.Add("Detach"); return USBDeviceDetachResult.Success; },
            () => { cleared = true; trace.Add("ClearOwner"); },
            () => { trace.Add("CoordinatorShutdown"); return Task.FromResult(true); },
            CancellationToken.None);

        Assert.True(result);
        Assert.True(cleared);
        Assert.Equal(["Stop", "Detach", "ClearOwner", "CoordinatorShutdown"], trace);
    }

    [Fact]
    public async Task Shutdown_does_not_enter_coordinator_when_xbox360_retirement_fails()
    {
        var coordinatorCalls = 0;
        var publisher = new CanonicalXbox360InputPublisher(new FakeSnapshot(), _ => true, new ManualTicks());
        var result = await AddonRoutingRuntime.ShutdownAfterXbox360RetirementAsync(
            publisher,
            () => Task.FromException(new TimeoutException()),
            () => USBDeviceDetachResult.Success,
            () => throw new InvalidOperationException("must retain owner"),
            () => { coordinatorCalls++; return Task.FromResult(true); },
            CancellationToken.None);

        Assert.False(result);
        Assert.Equal(0, coordinatorCalls);
    }

    [Fact]
    public async Task Shutdown_without_xbox360_presentation_preserves_coordinator_behavior()
    {
        var coordinatorCalls = 0;
        var result = await AddonRoutingRuntime.ShutdownAfterXbox360RetirementAsync(
            null,
            () => throw new InvalidOperationException(),
            () => throw new InvalidOperationException(),
            () => throw new InvalidOperationException(),
            () => { coordinatorCalls++; return Task.FromResult(true); },
            CancellationToken.None);

        Assert.True(result);
        Assert.Equal(1, coordinatorCalls);
    }

    private static async Task WaitForAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (!condition())
        {
            if (DateTime.UtcNow >= deadline) throw new TimeoutException();
            await Task.Yield();
        }
    }

    private sealed class FakeSnapshot : IControllerStateSnapshotSource
    {
        public ControllerState LatestState => new(new AuxiliaryButtonState([false, false]));
    }

    private sealed class ManualTicks : IInputReportTickSource
    {
        private readonly Queue<TaskCompletionSource<bool>> _waiters = new();
        public ValueTask<bool> WaitForTickAsync(CancellationToken token)
        {
            var waiter = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            _waiters.Enqueue(waiter);
            token.Register(() => waiter.TrySetCanceled(token));
            return new(waiter.Task);
        }
        public void Tick()
        {
            while (_waiters.Count > 0)
            {
                if (_waiters.Dequeue().TrySetResult(true)) return;
            }
            throw new InvalidOperationException("No live tick waiter.");
        }
    }

    [Theory]
    [InlineData(false, 0, false)]
    [InlineData(true, 1, false)]
    [InlineData(true, 2, false)]
    [InlineData(true, 0, true)]
    public void Persistent_viiper_initialization_requires_supported_safe_startup(bool hardwareSupported, int safety, bool expected) =>
        Assert.Equal(expected, AddonRoutingRuntime.CanInitializeViiper(hardwareSupported, (RecoverySafety)safety));

    [Fact]
    public void Missing_viiper_module_makes_only_steam_output_unavailable()
    {
        Assert.Null(AddonRoutingRuntime.TryLoadViiper(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".dll")));
    }
    [Fact]
    public async Task Create_returns_null_for_an_adapter_with_no_available_routing_composition()
    {
        // Mirrors HandheldRoutingCompositionFactory's own unavailable/passive result -- an
        // adapter the factory does not recognize must yield no runtime at all, not a fallback.
        var runtime = AddonRoutingRuntime.Create(
            new FakeUnsupportedAdapter(),
            new FakeStatusProvider(),
            new AddonOwnedVirtualDeviceTracker(),
            new RecoveryManager(new MemoryJournalStore()),
            new PowerMutationGate(initiallyOpen: true),
            new RecoverySafetyState(RecoverySafety.Safe),
        new DefaultOem1MappingPreference(),
            hardwareSupported: true);

        Assert.Null(runtime);
        if (runtime is not null) await runtime.DisposeAsync();
    }

    [Fact]
    public async Task Create_returns_a_runtime_for_the_supplied_MsiClawDeviceAdapter()
    {
        var runtime = CreateMsiRuntime();
        Assert.NotNull(runtime);

        // AddonRoutingRuntime's documented lifecycle precondition is ShutdownAsync() (plus any
        // other external orchestration referencing the runtime) before DisposeAsync() -- observe
        // that ordering here rather than disposing straight from Create(), even though nothing in
        // this construction-only test would currently surface a violation.
        await runtime.ShutdownAsync();
        await runtime.DisposeAsync();
    }

    [Fact]
    public async Task Facade_reflects_the_underlying_runtime_state_before_any_routing_activity()
    {
        var runtime = CreateMsiRuntime();
        Assert.NotNull(runtime);
        try
        {
            Assert.False(runtime.IsSafetySessionActive);
            Assert.False(runtime.HasOwnedRecoveryBoundary);
            Assert.False(runtime.HasResidualSessionState);

            var status = runtime.CaptureStatus();
            Assert.True(status.Available);
            Assert.Equal(RoutingOperationalState.Passive, status.OperationalState);
            Assert.False(status.SteamOutputActive);
            Assert.False(status.NativeDirectInputActive);
        }
        finally
        {
            await runtime.ShutdownAsync();
            await runtime.DisposeAsync();
        }
    }

    [Fact]
    public async Task IPowerSuspendParticipant_forwards_to_the_owned_routing_coordinator()
    {
        var runtime = CreateMsiRuntime();
        Assert.NotNull(runtime);
        try
        {
            // Same participant name RoutingPipelineRuntimeCoordinator itself reports, proving
            // this is a forwarding view and not a distinct participant identity.
            Assert.Equal("RoutingPipelineRuntime", runtime.Name);
        }
        finally
        {
            await runtime.ShutdownAsync();
            await runtime.DisposeAsync();
        }
    }

    [Fact]
    public async Task ReconcileSafelyAsync_requests_a_status_refresh_exactly_once_on_success()
    {
        var runtime = CreateMsiRuntime(new FakeStatusProvider(Snapshot(WaitingForSteam())));
        Assert.NotNull(runtime);
        try
        {
            var refreshCount = 0;
            await runtime.ReconcileSafelyAsync(() => refreshCount++);

            Assert.Equal(1, refreshCount);
        }
        finally
        {
            await runtime.ShutdownAsync();
            await runtime.DisposeAsync();
        }
    }

    [Fact]
    public async Task Unavailable_viiper_skips_eligible_new_forward_route_before_pipeline_entry()
    {
        var status = new FakeStatusProvider(Snapshot(new RoutingDecision(RoutingDecisionKind.Eligible, RoutingDecisionReason.Eligible)));
        var runtime = CreateMsiRuntime(status, hardwareSupported: false);
        Assert.NotNull(runtime);
        try
        {
            var refreshCount = 0;
            await runtime.ReconcileSafelyAsync(() => refreshCount++);
            Assert.Equal(0, status.CaptureCalls);
            Assert.False(runtime.HasResidualSessionState);
            Assert.Equal(1, refreshCount);
        }
        finally
        {
            Assert.True(await runtime.ShutdownAsync());
            await runtime.DisposeAsync();
        }
    }

    [Fact]
    public async Task Unavailable_viiper_does_not_suppress_existing_residual_cleanup_boundary()
    {
        var status = new FakeStatusProvider(Snapshot(WaitingForSteam()));
        var runtime = CreateMsiRuntime(status, hardwareSupported: false);
        Assert.NotNull(runtime);
        try
        {
            // Model the coordinator's already-owned in-flight cleanup boundary. This is a
            // test-only state injection; the production predicate still reads the coordinator's
            // authoritative HasResidualSessionState property.
            var coordinatorField = typeof(AddonRoutingRuntime).GetField("_coordinator",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
            var coordinator = coordinatorField.GetValue(runtime)!;
            var operationField = coordinator.GetType().GetField("_transitionOperationCount",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
            operationField.SetValue(coordinator, 1);

            var refreshCount = 0;
            await runtime.ReconcileSafelyAsync(() => refreshCount++);

            // An unavailable owner may block a brand-new forward route, but must not skip the
            // coordinator when residual process-owned cleanup is present.
            Assert.Equal(1, status.CaptureCalls);
            Assert.Equal(1, refreshCount);
        }
        finally
        {
            var coordinatorField = typeof(AddonRoutingRuntime).GetField("_coordinator",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
            var coordinator = coordinatorField.GetValue(runtime!)!;
            var operationField = coordinator.GetType().GetField("_transitionOperationCount",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
            operationField.SetValue(coordinator, 0);
            Assert.True(await runtime!.ShutdownAsync());
            await runtime.DisposeAsync();
        }
    }

    [Fact]
    public async Task ReconcileSafelyAsync_contains_an_unexpected_exception_and_still_refreshes_status()
    {
        var runtime = CreateMsiRuntime(new FakeStatusProvider(throwOnCapture: true));
        Assert.NotNull(runtime);
        try
        {
            var refreshCount = 0;

            // Must not propagate: the production failure policy (log, latch safety fault,
            // fail closed, log any rollback failure) runs entirely inside ReconcileSafelyAsync.
            await runtime.ReconcileSafelyAsync(() => refreshCount++);

            Assert.Equal(1, refreshCount);
        }
        finally
        {
            await runtime.ShutdownAsync();
            await runtime.DisposeAsync();
        }
    }

    [Fact]
    public async Task ReconcileSafelyAsync_treats_caller_requested_cancellation_as_non_faulting()
    {
        var runtime = CreateMsiRuntime(new FakeStatusProvider(Snapshot(WaitingForSteam())));
        Assert.NotNull(runtime);
        try
        {
            var refreshCount = 0;

            // A pre-cancelled token is observed by the coordinator's own transition gate wait,
            // not by the status provider -- this must not be treated as a routing failure.
            await runtime.ReconcileSafelyAsync(() => refreshCount++, new CancellationToken(true));

            Assert.Equal(1, refreshCount);
        }
        finally
        {
            await runtime.ShutdownAsync();
            await runtime.DisposeAsync();
        }
    }

    [Fact]
    public async Task ReconcileSafelyAsync_cannot_enter_the_routing_coordinator_until_OEM1_activation_resolves()
    {
        // Review fix (BLOCKER): InitializeRuntimeAsync's await of Oem1ActivationTask only orders the
        // caller-driven startup path -- AddonRuntimeHost's SteamSessionRuntime.StateChanged
        // subscription is wired earlier and can fire a real event-driven reconcile (via
        // ReconcileSafelyAsync) while OEM1 activation is still in flight. Since the OEM1 coordinator
        // and the routing guard share the same helper ownership, every normal reconcile entry point
        // must wait behind the same one-shot activation task before the routing coordinator can run.
        var runtime = CreateMsiRuntime(new FakeStatusProvider(Snapshot(WaitingForSteam())));
        Assert.NotNull(runtime);
        try
        {
            var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            runtime.TestOnly_SetOem1ActivationTask(gate.Task);

            var refreshCount = 0;
            var reconcileTask = runtime.ReconcileSafelyAsync(() => refreshCount++);

            await Task.Delay(50);
            Assert.False(reconcileTask.IsCompleted);
            Assert.Equal(0, refreshCount);

            gate.SetResult();
            await reconcileTask.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Equal(1, refreshCount);
        }
        finally
        {
            await runtime.ShutdownAsync();
            await runtime.DisposeAsync();
        }
    }

    private static AddonRoutingRuntime? CreateMsiRuntime(ISystemStatusProvider? statusProvider = null, bool hardwareSupported = true) => AddonRoutingRuntime.Create(
        new MsiClawDeviceAdapter(new EmptyDeviceEnumerator()),
        statusProvider ?? new FakeStatusProvider(),
        new AddonOwnedVirtualDeviceTracker(),
        new RecoveryManager(new MemoryJournalStore()),
        new PowerMutationGate(initiallyOpen: true),
        new RecoverySafetyState(RecoverySafety.Safe),
        new DefaultOem1MappingPreference(),
        hardwareSupported: hardwareSupported);

    private static SystemStatusSnapshot Snapshot(RoutingDecision decision) =>
        new(new("Test", "Test", "Test", []), null!, [], null!, null!, null!, decision, null!, true, false);

    private static RoutingDecision WaitingForSteam() => new(RoutingDecisionKind.WaitingForSteam, RoutingDecisionReason.SteamInactive);

    private sealed class EmptyDeviceEnumerator : IControllerDeviceEnumerator
    {
        public IReadOnlyList<ControllerDeviceInfo> EnumeratePresentDevices() => [];
    }

    private sealed class FakeUnsupportedAdapter : IHandheldDeviceAdapter
    {
        public HandheldDeviceDescriptor Descriptor { get; } = new(new HandheldDeviceId("fake.unsupported"), "Fake", "Unsupported", "Fake Unsupported Device");
        public AuxiliaryControlCatalog AuxiliaryControls { get; } = new([]);
        public IInternalControllerMatcher InternalControllerMatcher { get; } = new NeverMatcher();
        public INativeControllerStateManager? NativeState => null;
        public IHandheldDeviceModelResolver? ModelResolver => null;
        public DeviceProbeResult Probe(DeviceProbeContext context) => new(DeviceProbeStatus.NoMatch, "NotSupported");

        private sealed class NeverMatcher : IInternalControllerMatcher
        {
            public InternalControllerMatchResult Match(InternalControllerMatchContext context) => new(InternalControllerMatchStatus.NoMatch, "Fake");
        }
    }

    private sealed class FakeStatusProvider(SystemStatusSnapshot? snapshot = null, bool throwOnCapture = false) : ISystemStatusProvider
    {
        public int CaptureCalls { get; private set; }
        public Task<SystemStatusSnapshot> CaptureAsync(CancellationToken cancellationToken = default) => throwOnCapture
            ? throw new InvalidOperationException("Simulated status capture failure.")
            : Capture();

        private Task<SystemStatusSnapshot> Capture()
        {
            CaptureCalls++;
            return Task.FromResult(snapshot ?? throw new InvalidOperationException("Not exercised by construction-only tests."));
        }
    }

    private sealed class MemoryJournalStore : IRecoveryJournalStore
    {
        private RecoveryJournal? _journal;
        public string JournalPath => "memory";
        public bool Exists() => _journal is not null;
        public string ReadText() => System.Text.Json.JsonSerializer.Serialize(_journal);
        public void WriteNew(RecoveryJournal journal) => _journal = journal;
        public void ReplaceExisting(RecoveryJournal journal) { if (_journal is null) throw new IOException(); _journal = journal; }
        public void Delete() => _journal = null;
    }

    /// <summary>Default OEM1 mapping; these tests are about routing/host lifecycle, not mapping.</summary>
    private sealed class DefaultOem1MappingPreference : SteamInputAddonforClaw.Settings.IOem1MappingPreference
    {
        public SteamInputAddonforClaw.Contracts.Oem1.Oem1MappingSettings Oem1Mapping => SteamInputAddonforClaw.Contracts.Oem1.Oem1MappingSettings.Default;
        public event EventHandler? Oem1MappingChanged { add { } remove { } }
    }
}
