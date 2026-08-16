using System.Text.Json;
using SteamInputAddonforClaw.Controllers.Detection;
using SteamInputAddonforClaw.Devices;
using SteamInputAddonforClaw.Devices.Abstractions;
using SteamInputAddonforClaw.Devices.MSI.Claw;
using SteamInputAddonforClaw.Power;
using SteamInputAddonforClaw.Recovery;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class HandheldRoutingCompositionFactoryTests
{
    [Fact]
    public async Task Create_returns_a_composition_for_the_supplied_MsiClawDeviceAdapter()
    {
        var adapter = new MsiClawDeviceAdapter(new EmptyDeviceEnumerator());
        var factory = new HandheldRoutingCompositionFactory();

        await using var composition = factory.Create(adapter, new RecoveryManager(new MemoryJournalStore()), new PowerMutationGate(initiallyOpen: true), new RecoverySafetyState(RecoverySafety.Safe));

        Assert.NotNull(composition);
        Assert.NotNull(composition.SafetySession);
        Assert.NotNull(composition.ControllerStateSource);
        Assert.NotEmpty(composition.Stages);
        Assert.Single(composition.SessionBoundaryParticipants);
    }

    [Fact]
    public async Task Create_returns_null_for_an_adapter_the_factory_does_not_recognize()
    {
        var factory = new HandheldRoutingCompositionFactory();

        var composition = factory.Create(new FakeUnsupportedAdapter(), new RecoveryManager(new MemoryJournalStore()), new PowerMutationGate(initiallyOpen: true), new RecoverySafetyState(RecoverySafety.Safe));

        // No silent MSI fallback: an adapter the factory does not know how to build a runtime
        // composition for must yield no composition at all, not the MSI one.
        Assert.Null(composition);
        if (composition is not null) await composition.DisposeAsync();
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

    private sealed class MemoryJournalStore : IRecoveryJournalStore
    {
        private RecoveryJournal? _journal;
        public string JournalPath => "memory";
        public bool Exists() => _journal is not null;
        public string ReadText() => JsonSerializer.Serialize(_journal);
        public void WriteNew(RecoveryJournal journal) => _journal = journal;
        public void ReplaceExisting(RecoveryJournal journal) { if (_journal is null) throw new IOException(); _journal = journal; }
        public void Delete() => _journal = null;
    }
}
