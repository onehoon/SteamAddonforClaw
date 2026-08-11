using System.Text.Json;
using SteamInputAddonforClaw.Controllers.Detection;
using SteamInputAddonforClaw.Devices.Abstractions;
using SteamInputAddonforClaw.Devices.MSI.Claw;
using SteamInputAddonforClaw.Power;
using SteamInputAddonforClaw.Recovery;
using SteamInputAddonforClaw.Routing;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class MsiClawNativeModeSessionCoordinatorTests
{
    [Fact]
    public async Task NewerNonEligibleDecisionRestoresAfterOlderEligibleTransitionCompletes()
    {
        var devices = new FakeDeviceEnumerator(MsiClawNativeMode.XInput);
        var modeController = new FakeModeController(devices) { BlockFirstSwitch = true };
        await using var coordinator = CreateCoordinator(devices, modeController);

        var eligible = coordinator.ObserveRoutingDecisionAsync(Eligible(), 1);
        await modeController.FirstSwitchStarted.Task;
        var passive = coordinator.ObserveRoutingDecisionAsync(WaitingForSteam(), 2);

        modeController.ReleaseFirstSwitch();

        Assert.True(await eligible);
        Assert.True(await passive);
        Assert.Equal(MsiClawNativeMode.XInput, devices.Mode);
        Assert.Equal([MsiClawNativeMode.DirectInput, MsiClawNativeMode.XInput], modeController.Targets);
    }

    [Fact]
    public async Task StaleEligibleDecisionCannotReenterAfterNewerDecisionWasAccepted()
    {
        var devices = new FakeDeviceEnumerator(MsiClawNativeMode.XInput);
        await using var coordinator = CreateCoordinator(devices, new FakeModeController(devices));

        Assert.True(await coordinator.ObserveRoutingDecisionAsync(WaitingForSteam(), 2));
        Assert.False(await coordinator.ObserveRoutingDecisionAsync(Eligible(), 1));

        Assert.Equal(MsiClawNativeMode.XInput, devices.Mode);
    }

    [Fact]
    public async Task FailClosedRestoresAndClosesGateWhenRestoreFails()
    {
        var devices = new FakeDeviceEnumerator(MsiClawNativeMode.XInput);
        var modeController = new FakeModeController(devices) { FailRestore = true };
        var unsafeReason = string.Empty;
        var gate = new PowerMutationGate(initiallyOpen: true);
        await using var coordinator = CreateCoordinator(devices, modeController, gate, reason => unsafeReason = reason);

        Assert.True(await coordinator.ObserveRoutingDecisionAsync(Eligible(), 1));
        await Assert.ThrowsAsync<IOException>(() => coordinator.FailClosedAsync("CanonicalRoutingReconciliationFailed"));

        Assert.False(gate.IsOpen);
        Assert.Equal("CanonicalRoutingReconciliationFailed", unsafeReason);
        Assert.Contains(MsiClawNativeMode.XInput, modeController.Targets);
    }

    [Fact]
    public async Task FailClosedWithSuccessfulRestoreClosesGateAndMarksSafetyUnsafe()
    {
        var devices = new FakeDeviceEnumerator(MsiClawNativeMode.XInput);
        var unsafeReasons = new List<string>();
        var gate = new PowerMutationGate(initiallyOpen: true);
        await using var coordinator = CreateCoordinator(devices, new FakeModeController(devices), gate, unsafeReasons.Add);

        Assert.True(await coordinator.ObserveRoutingDecisionAsync(Eligible(), 1));
        await coordinator.FailClosedAsync("CanonicalRoutingReconciliationFailed");

        Assert.Equal(MsiClawNativeMode.XInput, devices.Mode);
        Assert.False(gate.IsOpen);
        Assert.Equal(["CanonicalRoutingReconciliationFailed"], unsafeReasons);
    }

    [Fact]
    public async Task JournalCreatedBeforeEnterFailureRetainsRecoveryOwnership()
    {
        var devices = new FakeDeviceEnumerator(MsiClawNativeMode.XInput);
        var modeController = new FakeModeController(devices) { FailEnter = true };
        await using var coordinator = CreateCoordinator(devices, modeController);

        await Assert.ThrowsAsync<IOException>(() => coordinator.ObserveRoutingDecisionAsync(Eligible(), 1));
        Assert.False(coordinator.IsActive);
        Assert.True(coordinator.HasOwnedRecoveryBoundary);
        var targetCount = modeController.Targets.Count;
        var reentry = await coordinator.EnterForPipelineAsync(CancellationToken.None);
        Assert.False(reentry.Succeeded);
        Assert.True(reentry.RequiresRollback);
        Assert.Equal("RecoveryBoundaryAlreadyOwned", reentry.Reason);
        Assert.Equal(targetCount, modeController.Targets.Count);
        Assert.True(await coordinator.ExitForPipelineAsync(CancellationToken.None));
        Assert.False(coordinator.HasOwnedRecoveryBoundary);
        Assert.Contains(MsiClawNativeMode.XInput, modeController.Targets);
    }

    [Fact]
    public async Task PipelineEnterConvertsPostJournalExceptionToTypedRollbackFailure()
    {
        var devices = new FakeDeviceEnumerator(MsiClawNativeMode.XInput);
        var modeController = new FakeModeController(devices) { FailEnter = true };
        await using var coordinator = CreateCoordinator(devices, modeController);

        var result = await coordinator.EnterForPipelineAsync(CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.True(result.RequiresRollback);
        Assert.Equal(nameof(IOException), result.Reason);
        Assert.True(coordinator.HasOwnedRecoveryBoundary);
        Assert.True(await coordinator.ExitForPipelineAsync(CancellationToken.None));
    }

    [Fact]
    public async Task PipelineEnterPreservesCancellationAndRecoveryOwnership()
    {
        var devices = new FakeDeviceEnumerator(MsiClawNativeMode.XInput);
        var modeController = new FakeModeController(devices) { ThrowEnterCancellation = true };
        await using var coordinator = CreateCoordinator(devices, modeController);
        using var cancellation = new CancellationTokenSource();
        modeController.AfterModeApplied = cancellation.Cancel;

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => coordinator.EnterForPipelineAsync(cancellation.Token));
        Assert.True(coordinator.HasOwnedRecoveryBoundary);
    }

    [Fact]
    public async Task PowerEpochChangeAfterEnterRetainsRecoveryOwnership()
    {
        var devices = new FakeDeviceEnumerator(MsiClawNativeMode.XInput);
        var gate = new PowerMutationGate(initiallyOpen: true);
        var modeController = new FakeModeController(devices);
        modeController.AfterModeApplied = () => gate.Close();
        await using var coordinator = CreateCoordinator(devices, modeController, gate);

        Assert.False(await coordinator.ObserveRoutingDecisionAsync(Eligible(), 1));
        Assert.False(coordinator.IsActive);
        Assert.True(coordinator.HasOwnedRecoveryBoundary);
        Assert.False(await coordinator.ExitForPipelineAsync(CancellationToken.None));
        gate.OpenAfterRecovery();
        Assert.True(await coordinator.ExitForPipelineAsync(CancellationToken.None));
        Assert.False(coordinator.HasOwnedRecoveryBoundary);
        Assert.Equal([MsiClawNativeMode.DirectInput, MsiClawNativeMode.XInput], modeController.Targets);
    }

    [Fact]
    public async Task ResumeAfterRecoveryClearsOwnershipAndAllowsFreshNativeEntry()
    {
        var devices = new FakeDeviceEnumerator(MsiClawNativeMode.XInput);
        var modeController = new FakeModeController(devices);
        var store = new MemoryJournalStore();
        var recovery = new RecoveryManager(store);
        var gate = new PowerMutationGate(initiallyOpen: true);
        var native = new MsiClawNativeStateManager(devices, modeController);
        await using var coordinator = new MsiClawNativeModeSessionCoordinator(native, recovery, gate);

        Assert.True((await coordinator.EnterForPipelineAsync(CancellationToken.None)).Succeeded);
        devices.Mode = MsiClawNativeMode.XInput;
        Assert.Equal(RecoveryStatus.Success, recovery.CompleteRecoverySession().Status);
        Assert.True(await coordinator.ReconcileAfterResumeAsync(1, gate.Epoch, CancellationToken.None));
        Assert.False(coordinator.HasOwnedRecoveryBoundary);
        Assert.True((await coordinator.EnterForPipelineAsync(CancellationToken.None)).Succeeded);
        Assert.Equal([MsiClawNativeMode.DirectInput, MsiClawNativeMode.DirectInput], modeController.Targets);
        Assert.True(await coordinator.ExitForPipelineAsync(CancellationToken.None));
    }

    [Fact]
    public async Task ResumeWithIncompleteRecoveryPreservesOwnershipAndBlocksEntry()
    {
        var devices = new FakeDeviceEnumerator(MsiClawNativeMode.XInput);
        var modeController = new FakeModeController(devices);
        var store = new MemoryJournalStore();
        var recovery = new RecoveryManager(store);
        var gate = new PowerMutationGate(initiallyOpen: true);
        var native = new MsiClawNativeStateManager(devices, modeController);
        await using var coordinator = new MsiClawNativeModeSessionCoordinator(native, recovery, gate);

        Assert.True((await coordinator.EnterForPipelineAsync(CancellationToken.None)).Succeeded);
        Assert.False(await coordinator.ReconcileAfterResumeAsync(1, gate.Epoch, CancellationToken.None));
        Assert.True(coordinator.HasOwnedRecoveryBoundary);
        var reentry = await coordinator.EnterForPipelineAsync(CancellationToken.None);
        Assert.False(reentry.Succeeded);
        Assert.Equal("RecoveryBoundaryAlreadyOwned", reentry.Reason);
    }

    [Fact]
    public async Task SteamSessionBoundaryDoesNotResetWhileNativeSessionIsActive()
    {
        var devices = new FakeDeviceEnumerator(MsiClawNativeMode.XInput);
        var modeController = new FakeModeController(devices);
        await using var coordinator = CreateCoordinator(devices, modeController);

        Assert.True((await coordinator.EnterForPipelineAsync(CancellationToken.None)).Succeeded);
        Assert.False(await coordinator.OnSteamSessionEndedAsync(CancellationToken.None));
        Assert.True(coordinator.HasOwnedRecoveryBoundary);
        Assert.True(await coordinator.ExitForPipelineAsync(CancellationToken.None));
        Assert.True(await coordinator.OnSteamSessionEndedAsync(CancellationToken.None));
    }

    [Fact]
    public async Task ExternalVetoResetsOnlyAtNextSteamSessionBoundary()
    {
        var devices = new FakeDeviceEnumerator(MsiClawNativeMode.XInput);
        var modeController = new FakeModeController(devices);
        var mutationAllowed = true;
        await using var coordinator = CreateCoordinator(
            devices,
            modeController,
            mutationAllowed: () => mutationAllowed);

        Assert.True((await coordinator.EnterForPipelineAsync(CancellationToken.None)).Succeeded);

        mutationAllowed = false;
        await WaitUntilAsync(
            () => !coordinator.IsActive && devices.Mode == MsiClawNativeMode.XInput,
            TimeSpan.FromSeconds(3));

        var blocked = await coordinator.EnterForPipelineAsync(CancellationToken.None);
        Assert.False(blocked.Succeeded);
        Assert.Equal("SessionVetoLatched", blocked.Reason);

        Assert.True(await coordinator.OnSteamSessionEndedAsync(CancellationToken.None));
        mutationAllowed = true;

        Assert.True((await coordinator.EnterForPipelineAsync(CancellationToken.None)).Succeeded);
        Assert.True(await coordinator.ExitForPipelineAsync(CancellationToken.None));
    }

    private static MsiClawNativeModeSessionCoordinator CreateCoordinator(
        FakeDeviceEnumerator devices,
        FakeModeController modeController,
        PowerMutationGate? gate = null,
        Action<string>? markUnsafe = null,
        Func<bool>? mutationAllowed = null)
    {
        var store = new MemoryJournalStore();
        var recovery = new RecoveryManager(store);
        var native = new MsiClawNativeStateManager(devices, modeController);
        return new(native, recovery, gate ?? new PowerMutationGate(initiallyOpen: true), mutationAllowed: mutationAllowed, markRecoveryUnsafe: markUnsafe);
    }

    private static async Task WaitUntilAsync(Func<bool> predicate, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (!predicate())
        {
            if (DateTime.UtcNow >= deadline)
                throw new TimeoutException("The expected native-mode safety transition did not occur.");
            await Task.Delay(20);
        }
    }

    private static RoutingDecision Eligible() => new(RoutingDecisionKind.Eligible, RoutingDecisionReason.Eligible);
    private static RoutingDecision WaitingForSteam() => new(RoutingDecisionKind.WaitingForSteam, RoutingDecisionReason.SteamInactive);

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
        public bool BlockFirstSwitch { get; set; }
        public bool FailRestore { get; set; }
        public bool FailEnter { get; set; }
        public bool ThrowEnterCancellation { get; set; }
        public Action? AfterModeApplied { get; set; }
        public TaskCompletionSource FirstSwitchStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public List<MsiClawNativeMode> Targets { get; } = [];
        private readonly TaskCompletionSource _releaseFirstSwitch = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _switchCount;

        public async Task<MsiClawModeTransitionResult> SwitchModeAsync(MsiClawNativeMode target, MsiClawPhysicalIdentity expectedIdentity, CancellationToken cancellationToken)
        {
            Targets.Add(target);
            if (Interlocked.Increment(ref _switchCount) == 1 && BlockFirstSwitch)
            {
                FirstSwitchStarted.SetResult();
                await _releaseFirstSwitch.Task.WaitAsync(cancellationToken);
            }
            if (target == MsiClawNativeMode.DirectInput)
            {
                devices.Mode = target;
                AfterModeApplied?.Invoke();
            }
            if (target == MsiClawNativeMode.DirectInput && FailEnter)
                throw new IOException("simulated enter failure");
            if (target == MsiClawNativeMode.DirectInput && ThrowEnterCancellation)
                throw new OperationCanceledException(cancellationToken);
            if (target == MsiClawNativeMode.XInput && FailRestore)
                throw new IOException("simulated restore failure");
            devices.Mode = target;
            return new(MsiClawModeTransitionStatus.Succeeded, target == MsiClawNativeMode.XInput ? MsiClawNativeMode.DirectInput : MsiClawNativeMode.XInput,
                target, null, target == MsiClawNativeMode.XInput ? MsiClawHardware.XInputProductId : MsiClawHardware.DirectInputProductId,
                true, true, true, true, 1, "test");
        }

        public void ReleaseFirstSwitch() => _releaseFirstSwitch.SetResult();
    }

    private sealed class MemoryJournalStore : IRecoveryJournalStore
    {
        private RecoveryJournal? _journal;
        public string JournalPath => "memory";
        public bool Exists() => _journal is not null;
        public string ReadText() => JsonSerializer.Serialize(_journal);
        public void WriteNew(RecoveryJournal journal) => _journal = journal;
        public void Delete() => _journal = null;
    }
}
