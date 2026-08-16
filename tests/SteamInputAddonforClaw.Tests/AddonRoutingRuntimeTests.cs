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
            new RecoverySafetyState(RecoverySafety.Safe));

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

    private static AddonRoutingRuntime? CreateMsiRuntime(ISystemStatusProvider? statusProvider = null) => AddonRoutingRuntime.Create(
        new MsiClawDeviceAdapter(new EmptyDeviceEnumerator()),
        statusProvider ?? new FakeStatusProvider(),
        new AddonOwnedVirtualDeviceTracker(),
        new RecoveryManager(new MemoryJournalStore()),
        new PowerMutationGate(initiallyOpen: true),
        new RecoverySafetyState(RecoverySafety.Safe));

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
        public Task<SystemStatusSnapshot> CaptureAsync(CancellationToken cancellationToken = default) => throwOnCapture
            ? throw new InvalidOperationException("Simulated status capture failure.")
            : Task.FromResult(snapshot ?? throw new InvalidOperationException("Not exercised by construction-only tests."));
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
}
