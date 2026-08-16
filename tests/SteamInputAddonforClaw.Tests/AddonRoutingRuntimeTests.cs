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
        var adapter = new MsiClawDeviceAdapter(new EmptyDeviceEnumerator());

        await using var runtime = AddonRoutingRuntime.Create(
            adapter,
            new FakeStatusProvider(),
            new AddonOwnedVirtualDeviceTracker(),
            new RecoveryManager(new MemoryJournalStore()),
            new PowerMutationGate(initiallyOpen: true),
            new RecoverySafetyState(RecoverySafety.Safe));

        Assert.NotNull(runtime);
    }

    [Fact]
    public async Task Facade_reflects_the_underlying_runtime_state_before_any_routing_activity()
    {
        var adapter = new MsiClawDeviceAdapter(new EmptyDeviceEnumerator());

        await using var runtime = AddonRoutingRuntime.Create(
            adapter,
            new FakeStatusProvider(),
            new AddonOwnedVirtualDeviceTracker(),
            new RecoveryManager(new MemoryJournalStore()),
            new PowerMutationGate(initiallyOpen: true),
            new RecoverySafetyState(RecoverySafety.Safe));
        Assert.NotNull(runtime);

        Assert.False(runtime.IsSafetySessionActive);
        Assert.False(runtime.HasOwnedRecoveryBoundary);
        Assert.False(runtime.HasResidualSessionState);
        Assert.Equal(RoutingOperationalState.Passive, runtime.CurrentOperationalState);
        Assert.False(runtime.ActiveSessionHasSteamOutputEnabled);

        var status = runtime.CaptureStatus();
        Assert.True(status.Available);
        Assert.Equal(RoutingOperationalState.Passive, status.OperationalState);
        Assert.False(status.SteamOutputActive);
        Assert.False(status.NativeDirectInputActive);
    }

    [Fact]
    public async Task IPowerSuspendParticipant_forwards_to_the_owned_routing_coordinator()
    {
        var adapter = new MsiClawDeviceAdapter(new EmptyDeviceEnumerator());

        await using var runtime = AddonRoutingRuntime.Create(
            adapter,
            new FakeStatusProvider(),
            new AddonOwnedVirtualDeviceTracker(),
            new RecoveryManager(new MemoryJournalStore()),
            new PowerMutationGate(initiallyOpen: true),
            new RecoverySafetyState(RecoverySafety.Safe));
        Assert.NotNull(runtime);

        // Same participant name RoutingPipelineRuntimeCoordinator itself reports, proving this is
        // a forwarding view and not a distinct participant identity.
        Assert.Equal("RoutingPipelineRuntime", runtime.Name);
    }

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

    private sealed class FakeStatusProvider : ISystemStatusProvider
    {
        public Task<SystemStatusSnapshot> CaptureAsync(CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Not exercised by construction-only tests.");
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
