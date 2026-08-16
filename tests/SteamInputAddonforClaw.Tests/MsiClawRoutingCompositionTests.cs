using System.Text.Json;
using SteamInputAddonforClaw.Controllers.Detection;
using SteamInputAddonforClaw.Devices.Abstractions;
using SteamInputAddonforClaw.Devices.MSI.Claw;
using SteamInputAddonforClaw.Power;
using SteamInputAddonforClaw.Recovery;
using SteamInputAddonforClaw.Routing;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class MsiClawRoutingCompositionTests
{
    [Fact]
    public void SafetySession_ControllerStateSource_and_SessionBoundaryParticipant_are_the_same_owned_instances()
    {
        var devices = new FakeDeviceEnumerator(MsiClawNativeMode.XInput);
        var native = new MsiClawNativeStateManager(devices, new FakeModeController(devices));
        var composition = new MsiClawRoutingComposition(native, new RecoveryManager(new MemoryJournalStore()), new PowerMutationGate(initiallyOpen: true), new RecoverySafetyState(RecoverySafety.Safe));
        IHandheldRoutingComposition handheld = composition;

        Assert.Same(composition.NativeModeSession, handheld.SafetySession);
        Assert.Same(composition.PhysicalInputSource, handheld.ControllerStateSource);
        Assert.Same(composition.NativeModeSession, Assert.Single(handheld.SessionBoundaryParticipants));
    }

    [Fact]
    public async Task DisposeAsync_disposes_the_owned_native_mode_session()
    {
        var devices = new FakeDeviceEnumerator(MsiClawNativeMode.XInput);
        var native = new MsiClawNativeStateManager(devices, new FakeModeController(devices));
        var composition = new MsiClawRoutingComposition(native, new RecoveryManager(new MemoryJournalStore()), new PowerMutationGate(initiallyOpen: true), new RecoverySafetyState(RecoverySafety.Safe));

        Assert.True(await composition.NativeModeSession.ObserveRoutingDecisionAsync(new RoutingDecision(RoutingDecisionKind.Eligible, RoutingDecisionReason.Eligible), 1));
        Assert.True(composition.NativeModeSession.IsActive);

        await ((IAsyncDisposable)composition).DisposeAsync();

        // DisposeAsync must reach the owned NativeModeSession -- observable via IsActive flipping
        // false, the same state MsiClawNativeModeSessionCoordinator.DisposeAsync produces when
        // called directly (it stops native mode before releasing its gate).
        Assert.False(composition.NativeModeSession.IsActive);
    }

    [Fact]
    public async Task DisposeAsync_completes_without_throwing_when_the_physical_input_source_was_never_started()
    {
        // MsiClawRoutingComposition constructs its own MsiClawInputSource against the real
        // Vortice DirectInput enumerator factory (not fakeable without an invasive production
        // seam), so its disposal here is exercised only in the default never-started state --
        // the sequential two-line DisposeAsync implementation is otherwise reviewed by inspection.
        var devices = new FakeDeviceEnumerator(MsiClawNativeMode.XInput);
        var native = new MsiClawNativeStateManager(devices, new FakeModeController(devices));
        var composition = new MsiClawRoutingComposition(native, new RecoveryManager(new MemoryJournalStore()), new PowerMutationGate(initiallyOpen: true), new RecoverySafetyState(RecoverySafety.Safe));

        await ((IAsyncDisposable)composition).DisposeAsync();

        Assert.False(composition.PhysicalInputSource.IsRunning);
    }

    private sealed class FakeDeviceEnumerator(MsiClawNativeMode initialMode) : IControllerDeviceEnumerator
    {
        private readonly Guid _containerId = Guid.NewGuid();
        private readonly string _parent = "PCIROOT\\0";
        public MsiClawNativeMode Mode { get; set; } = initialMode;

        public IReadOnlyList<ControllerDeviceInfo> EnumeratePresentDevices() =>
        [new("HID\\MSI_CLAW", _containerId, _parent, [], "HID", [], [], "HIDClass", null, null,
            MsiClawHardware.VendorId, Mode == MsiClawNativeMode.XInput ? MsiClawHardware.XInputProductId : MsiClawHardware.DirectInputProductId, true)];
    }

    private sealed class FakeModeController(FakeDeviceEnumerator devices) : IMsiClawModeController
    {
        public Task<MsiClawModeTransitionResult> SwitchModeAsync(MsiClawNativeMode target, MsiClawPhysicalIdentity expectedIdentity, CancellationToken cancellationToken)
        {
            devices.Mode = target;
            return Task.FromResult(new MsiClawModeTransitionResult(MsiClawModeTransitionStatus.Succeeded,
                target == MsiClawNativeMode.XInput ? MsiClawNativeMode.DirectInput : MsiClawNativeMode.XInput,
                target, null, target == MsiClawNativeMode.XInput ? MsiClawHardware.XInputProductId : MsiClawHardware.DirectInputProductId,
                true, true, true, true, true, 1, "test"));
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
