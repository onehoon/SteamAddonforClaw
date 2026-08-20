using SteamInputAddonforClaw.Controllers.Detection;
using SteamInputAddonforClaw.Devices;
using SteamInputAddonforClaw.Devices.Abstractions;
using SteamInputAddonforClaw.Devices.MSI.Claw;
using SteamInputAddonforClaw.Power;
using SteamInputAddonforClaw.Recovery;
using SteamInputAddonforClaw.Routing;
using SteamInputAddonforClaw.Status;
using SteamInputAddonforClaw.VirtualOutput.Viiper;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class AddonRoutingRuntimeTests
{
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
