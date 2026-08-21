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
    public async Task GameBar_foreground_true_always_requests_enter()
    {
        // The policy seam no longer pre-checks SteamOutputActive/ownership itself -- that snapshot
        // could go stale behind a concurrent presentation mutation. Enter alone decides
        // applicability, fresh, once it holds _presentationGate.
        var enters = 0;
        var exits = 0;
        await AddonRoutingRuntime.HandleGameBarForegroundChangedCoreAsync(
            isForeground: true,
            enter: _ => { enters++; return Task.FromResult(true); },
            exit: _ => { exits++; return Task.FromResult(true); },
            CancellationToken.None);

        Assert.Equal(1, enters);
        Assert.Equal(0, exits);
    }

    [Fact]
    public async Task GameBar_foreground_false_always_requests_exit()
    {
        var enters = 0;
        var exits = 0;
        await AddonRoutingRuntime.HandleGameBarForegroundChangedCoreAsync(
            isForeground: false,
            enter: _ => { enters++; return Task.FromResult(true); },
            exit: _ => { exits++; return Task.FromResult(true); },
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
        var result = await AddonRoutingRuntime.ShutdownCoreAsync(
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
        var result = await AddonRoutingRuntime.ShutdownCoreAsync(
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
        var result = await AddonRoutingRuntime.ShutdownCoreAsync(
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
        var runtime = CreateMsiRuntime(status, steamOutputReady: false);
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
        var runtime = CreateMsiRuntime(status, steamOutputReady: false);
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

    [Fact]
    public async Task Fresh_resume_converges_stale_owned_fault_before_reconcile_callback()
    {
        var status = new FakeStatusProvider(Snapshot(new RoutingDecision(RoutingDecisionKind.Eligible, RoutingDecisionReason.Eligible)));
        var safety = new RecoverySafetyState(RecoverySafety.Safe);
        var runtime = CreateMsiRuntime(status);
        Assert.NotNull(runtime);
        try
        {
            var unsafeVersion = safety.Set(RecoverySafety.Unsafe);
            var safetySession = typeof(AddonRoutingRuntime)
                .GetField("_safetySession", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                .GetValue(runtime)!;
            await ((IRoutingSafetySession)safetySession).LatchRoutingFaultAsync("OldSessionFault");
            var nativeCoordinator = safetySession.GetType();
            nativeCoordinator.GetField("_unsafeRecoveryVersion", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                .SetValue(safetySession, unsafeVersion);

            // Model PowerTransitionCoordinator's completed recovery boundary.
            safety.Set(RecoverySafety.Safe);

            Assert.False(await runtime.ReconcileFreshAfterResumeAsync(CancellationToken.None));
            Assert.Equal(RecoverySafety.Safe, safety.Current);
            var latched = (bool)nativeCoordinator.GetField("_routingFaultLatched", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                .GetValue(safetySession)!;
            Assert.False(latched);
            Assert.Equal(1, status.CaptureCalls);
        }
        finally
        {
            Assert.True(await runtime.ShutdownAsync());
            await runtime.DisposeAsync();
        }
    }

    private static AddonRoutingRuntime? CreateMsiRuntime(
        ISystemStatusProvider? statusProvider = null,
        bool steamOutputReady = true)
    {
        var runtime = AddonRoutingRuntime.Create(
            new MsiClawDeviceAdapter(new EmptyDeviceEnumerator()),
            statusProvider ?? new FakeStatusProvider(),
            new AddonOwnedVirtualDeviceTracker(),
            new RecoveryManager(new MemoryJournalStore()),
            new PowerMutationGate(initiallyOpen: true),
            new RecoverySafetyState(RecoverySafety.Safe),
            new DefaultOem1MappingPreference(),
            hardwareSupported: false);

        runtime?.TestOnly_SetSteamOutputReady(steamOutputReady);
        return runtime;
    }

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

/// <summary>
/// Exercises <see cref="AddonRoutingRuntime.RunGatedPresentationMutationAsync"/> directly -- the
/// exact primitive <c>EnterXbox360PresentationAsync</c>, <c>ExitXbox360PresentationAsync</c>, and
/// the outer-route X360 retirement callback all share -- against a real <see cref="SemaphoreSlim"/>
/// and deterministic TaskCompletionSource-controlled delegates. These tests prove only the
/// concurrency contract this primitive adds (mutual exclusion between presentation mutations, and
/// that a failure's fail-close callback never runs while the gate is still held); the existing
/// Enter/Exit/retirement lifecycle behavior is already covered elsewhere and is not duplicated
/// here. No <c>Task.Delay</c>/timing-based assertions.
/// </summary>
public sealed class AddonRoutingRuntimePresentationGateTests
{
    [Fact]
    public async Task ConcurrentMutationsDoNotOverlap()
    {
        var gate = new SemaphoreSlim(1, 1);
        var events = new List<string>();
        var firstStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var first = AddonRoutingRuntime.RunGatedPresentationMutationAsync(
            gate,
            async () =>
            {
                events.Add("FirstBegin");
                firstStarted.TrySetResult();
                await releaseFirst.Task;
                events.Add("FirstEnd");
                return (true, (string?)null);
            },
            failClosed: null,
            CancellationToken.None);

        await firstStarted.Task;

        // Issued while the gate is still held by `first`; its mutate delegate must not run until
        // `first` releases -- proven by event order below, not by elapsed time.
        var second = AddonRoutingRuntime.RunGatedPresentationMutationAsync(
            gate,
            () => { events.Add("SecondBegin"); return Task.FromResult((true, (string?)null)); },
            failClosed: null,
            CancellationToken.None);

        releaseFirst.TrySetResult();
        await Task.WhenAll(first, second);

        Assert.Equal(["FirstBegin", "FirstEnd", "SecondBegin"], events);
    }

    [Fact]
    public async Task InteractiveEnterFollowedByExitDoesNotOverlap()
    {
        // Same mechanism as ConcurrentMutationsDoNotOverlap, labeled to match the actual production
        // callers: an interactive Enter-shaped mutation held open, followed by an Exit-shaped one.
        var gate = new SemaphoreSlim(1, 1);
        var events = new List<string>();
        var enterStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseEnter = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var enter = AddonRoutingRuntime.RunGatedPresentationMutationAsync(
            gate,
            async () =>
            {
                events.Add("EnterBegin");
                enterStarted.TrySetResult();
                await releaseEnter.Task;
                events.Add("EnterEnd");
                return (true, (string?)null);
            },
            failClosed: null,
            CancellationToken.None);

        await enterStarted.Task;

        var exit = AddonRoutingRuntime.RunGatedPresentationMutationAsync(
            gate,
            () => { events.Add("ExitBegin"); events.Add("ExitEnd"); return Task.FromResult((true, (string?)null)); },
            failClosed: null,
            CancellationToken.None);

        releaseEnter.TrySetResult();
        await Task.WhenAll(enter, exit);

        Assert.Equal(["EnterBegin", "EnterEnd", "ExitBegin", "ExitEnd"], events);
    }

    [Fact]
    public async Task OuterRetirementWaitsForAnInProgressInteractiveMutation()
    {
        var gate = new SemaphoreSlim(1, 1);
        var events = new List<string>();
        var interactiveStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseInteractive = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var interactive = AddonRoutingRuntime.RunGatedPresentationMutationAsync(
            gate,
            async () =>
            {
                events.Add("InteractiveBegin");
                interactiveStarted.TrySetResult();
                await releaseInteractive.Task;
                events.Add("InteractiveEnd");
                return (true, (string?)null);
            },
            failClosed: null,
            CancellationToken.None);

        await interactiveStarted.Task;

        var outerRetirement = AddonRoutingRuntime.RunGatedPresentationMutationAsync(
            gate,
            () => { events.Add("OuterRetireStopDetach"); return Task.FromResult((true, (string?)null)); },
            failClosed: null,
            CancellationToken.None);

        releaseInteractive.TrySetResult();
        await Task.WhenAll(interactive, outerRetirement);

        Assert.Equal(["InteractiveBegin", "InteractiveEnd", "OuterRetireStopDetach"], events);
    }

    [Fact]
    public async Task InteractiveMutationWaitsForAnInProgressOuterRetirement()
    {
        var gate = new SemaphoreSlim(1, 1);
        var events = new List<string>();
        var retirementStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseRetirement = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var outerRetirement = AddonRoutingRuntime.RunGatedPresentationMutationAsync(
            gate,
            async () =>
            {
                events.Add("OuterRetireBegin");
                retirementStarted.TrySetResult();
                await releaseRetirement.Task;
                events.Add("OuterRetireEnd");
                return (true, (string?)null);
            },
            failClosed: null,
            CancellationToken.None);

        await retirementStarted.Task;

        var interactive = AddonRoutingRuntime.RunGatedPresentationMutationAsync(
            gate,
            () => { events.Add("InteractiveMutate"); return Task.FromResult((true, (string?)null)); },
            failClosed: null,
            CancellationToken.None);

        releaseRetirement.TrySetResult();
        await Task.WhenAll(outerRetirement, interactive);

        Assert.Equal(["OuterRetireBegin", "OuterRetireEnd", "InteractiveMutate"], events);
    }

    [Fact]
    public async Task FailedMutationReleasesTheGateBeforeInvokingFailClose()
    {
        // The critical deadlock regression: fail-close (and the outer-route retirement it drives
        // through RoutingPipelineRuntimeCoordinator.FailClosedAsync's beforeActiveSessionExit
        // callback, which itself needs this same gate) must never be awaited while the gate is
        // still held by the failed mutation.
        var gate = new SemaphoreSlim(1, 1);
        var events = new List<string>();

        var succeeded = await AddonRoutingRuntime.RunGatedPresentationMutationAsync(
            gate,
            () => { events.Add("PresentationFailure"); return Task.FromResult((false, (string?)"SimulatedFailure")); },
            failClosed: reason =>
            {
                // Not timing-based: a synchronous zero-timeout acquire either succeeds immediately
                // (gate free) or fails immediately (gate still held) -- no race window either way.
                Assert.True(gate.Wait(0));
                gate.Release();
                events.Add($"FailClosed:{reason}");
                return Task.CompletedTask;
            },
            CancellationToken.None);

        Assert.False(succeeded);
        Assert.Equal(["PresentationFailure", "FailClosed:SimulatedFailure"], events);
    }

    [Fact]
    public async Task SuccessfulMutationNeverInvokesFailClose()
    {
        var gate = new SemaphoreSlim(1, 1);
        var failCloseCalls = 0;

        var succeeded = await AddonRoutingRuntime.RunGatedPresentationMutationAsync(
            gate,
            () => Task.FromResult((true, (string?)null)),
            failClosed: _ => { failCloseCalls++; return Task.CompletedTask; },
            CancellationToken.None);

        Assert.True(succeeded);
        Assert.Equal(0, failCloseCalls);
    }

    [Fact]
    public async Task QueuedMutationObservesStateCommittedByThePreviousMutationRatherThanAPreWaitSnapshot()
    {
        var gate = new SemaphoreSlim(1, 1);
        var owned = false;
        var firstStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var first = AddonRoutingRuntime.RunGatedPresentationMutationAsync(
            gate,
            async () =>
            {
                firstStarted.TrySetResult();
                await releaseFirst.Task;
                owned = true; // commits ownership only just before releasing the gate
                return (true, (string?)null);
            },
            failClosed: null,
            CancellationToken.None);

        await firstStarted.Task;

        // Queued (and thus constructed) before the commit above -- if the mutate delegate captured
        // a snapshot instead of reading `owned` only once actually invoked (i.e. once it holds the
        // gate), this would observe the stale `false`.
        bool observedBySecond = false;
        var second = AddonRoutingRuntime.RunGatedPresentationMutationAsync(
            gate,
            () => { observedBySecond = owned; return Task.FromResult((true, (string?)null)); },
            failClosed: null,
            CancellationToken.None);

        releaseFirst.TrySetResult();
        await Task.WhenAll(first, second);

        Assert.True(observedBySecond);
    }

    [Fact]
    public async Task GateWaitRespectsCancellation()
    {
        var gate = new SemaphoreSlim(1, 1);
        var holderStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseHolder = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var holder = AddonRoutingRuntime.RunGatedPresentationMutationAsync(
            gate,
            async () => { holderStarted.TrySetResult(); await releaseHolder.Task; return (true, (string?)null); },
            failClosed: null,
            CancellationToken.None);

        await holderStarted.Task;

        using var cancellation = new CancellationTokenSource();
        var queued = AddonRoutingRuntime.RunGatedPresentationMutationAsync(
            gate,
            () => Task.FromResult((true, (string?)null)),
            failClosed: null,
            cancellation.Token);
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => queued);

        releaseHolder.TrySetResult();
        await holder;
    }

    [Fact]
    public async Task GameBarForegroundFalseArrivingBeforeEnterCommitsStillWaitsThenExits()
    {
        // Review regression: HandleGameBarForegroundChangedAsync must not decide applicability
        // from a pre-gate ownership snapshot. Here `enter`/`exit` are wired through
        // HandleGameBarForegroundChangedCoreAsync exactly as the production instance method wires
        // the real EnterXbox360PresentationAsync/ExitXbox360PresentationAsync -- both share one
        // real SemaphoreSlim and only commit/read `owned` once they actually hold it. A
        // foreground=false delivered while Enter is still in flight (i.e. before it has committed
        // ownership) must still result in Exit observing the committed publisher and running,
        // never being silently skipped because of a stale snapshot taken up front.
        var gate = new SemaphoreSlim(1, 1);
        var events = new List<string>();
        var owned = false;
        var enterStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseEnter = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        Func<CancellationToken, Task<bool>> enter = token => AddonRoutingRuntime.RunGatedPresentationMutationAsync(
            gate,
            async () =>
            {
                events.Add("EnterBegin");
                enterStarted.TrySetResult();
                await releaseEnter.Task;
                owned = true;
                events.Add("EnterCommit");
                return (true, (string?)null);
            },
            failClosed: null,
            token);

        Func<CancellationToken, Task<bool>> exit = token => AddonRoutingRuntime.RunGatedPresentationMutationAsync(
            gate,
            () =>
            {
                // Same fresh-inside-the-gate rule EnterXbox360PresentationAsync/
                // ExitXbox360PresentationAsync apply for real: only decide here, never before.
                if (!owned)
                {
                    events.Add("ExitSkipped_NotOwned");
                    return Task.FromResult((false, (string?)null));
                }
                events.Add("ExitBegin");
                owned = false;
                events.Add("ExitEnd");
                return Task.FromResult((true, (string?)null));
            },
            failClosed: null,
            token);

        var enterCall = AddonRoutingRuntime.HandleGameBarForegroundChangedCoreAsync(
            isForeground: true, enter, exit, CancellationToken.None);
        await enterStarted.Task;

        // foreground=false arrives while Enter is still mid-flight, before it has committed
        // ownership -- issued (not awaited) here, exactly like a second Game Bar event racing in.
        var exitCall = AddonRoutingRuntime.HandleGameBarForegroundChangedCoreAsync(
            isForeground: false, enter, exit, CancellationToken.None);

        releaseEnter.TrySetResult();
        await Task.WhenAll(enterCall, exitCall);

        Assert.Equal(["EnterBegin", "EnterCommit", "ExitBegin", "ExitEnd"], events);
        Assert.False(owned);
    }
}
