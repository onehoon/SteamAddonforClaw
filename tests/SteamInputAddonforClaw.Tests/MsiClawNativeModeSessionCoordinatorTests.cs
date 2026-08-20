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
    public async Task FailClosed_restore_failure_keeps_power_gate_open_and_marks_recovery_unsafe()
    {
        var devices = new FakeDeviceEnumerator(MsiClawNativeMode.XInput);
        var modeController = new FakeModeController(devices) { FailRestore = true };
        var recoverySafety = new RecoverySafetyState(RecoverySafety.Safe);
        var gate = new PowerMutationGate(initiallyOpen: true);
        await using var coordinator = CreateCoordinator(devices, modeController, gate, recoverySafety);

        Assert.True(await coordinator.ObserveRoutingDecisionAsync(Eligible(), 1));
        await Assert.ThrowsAsync<IOException>(() => coordinator.FailClosedAsync("CanonicalRoutingReconciliationFailed"));

        Assert.True(gate.IsOpen);
        Assert.Equal(RecoverySafety.Unsafe, recoverySafety.Current);
        Assert.Contains(MsiClawNativeMode.XInput, modeController.Targets);
    }

    [Fact]
    public async Task FailClosedWithSuccessfulRestore_keeps_power_gate_and_recovery_safety_open()
    {
        var devices = new FakeDeviceEnumerator(MsiClawNativeMode.XInput);
        var recoverySafety = new RecoverySafetyState(RecoverySafety.Safe);
        var gate = new PowerMutationGate(initiallyOpen: true);
        await using var coordinator = CreateCoordinator(devices, new FakeModeController(devices), gate, recoverySafety);

        Assert.True(await coordinator.ObserveRoutingDecisionAsync(Eligible(), 1));
        await coordinator.FailClosedAsync("CanonicalRoutingReconciliationFailed");

        Assert.Equal(MsiClawNativeMode.XInput, devices.Mode);
        Assert.True(gate.IsOpen);
        Assert.Equal(RecoverySafety.Safe, recoverySafety.Current);
    }

    [Theory]
    [InlineData((int)RecoverySafety.Unsafe)]
    [InlineData((int)RecoverySafety.Indeterminate)]
    public async Task Recovery_safety_not_safe_blocks_new_forward_mutation(int safetyValue)
    {
        var devices = new FakeDeviceEnumerator(MsiClawNativeMode.XInput);
        var modeController = new FakeModeController(devices);
        var recoverySafety = new RecoverySafetyState((RecoverySafety)safetyValue);
        await using var coordinator = CreateCoordinator(devices, modeController, recoverySafety: recoverySafety);

        var result = await coordinator.EnterForPipelineAsync(CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("RecoverySafetyNotSafe", result.Reason);
        Assert.Empty(modeController.Targets);
        Assert.False(coordinator.HasOwnedRecoveryBoundary);
    }

    [Fact]
    public async Task Failed_restore_remains_retryable_without_closing_power_gate()
    {
        var devices = new FakeDeviceEnumerator(MsiClawNativeMode.XInput);
        var modeController = new FakeModeController(devices) { FailRestore = true };
        var gate = new PowerMutationGate(initiallyOpen: true);
        var recoverySafety = new RecoverySafetyState(RecoverySafety.Safe);
        await using var coordinator = CreateCoordinator(devices, modeController, gate, recoverySafety);

        Assert.True((await coordinator.EnterForPipelineAsync(CancellationToken.None)).Succeeded);
        await Assert.ThrowsAsync<IOException>(() => coordinator.ExitForPipelineAsync(CancellationToken.None));
        Assert.True(gate.IsOpen);
        Assert.Equal(RecoverySafety.Unsafe, recoverySafety.Current);
        Assert.True(coordinator.HasOwnedRecoveryBoundary);

        modeController.FailRestore = false;
        Assert.True(await coordinator.ExitForPipelineAsync(CancellationToken.None));
        Assert.True(gate.IsOpen);
        Assert.Equal(RecoverySafety.Safe, recoverySafety.Current);
        Assert.False(coordinator.HasOwnedRecoveryBoundary);
        Assert.Equal([MsiClawNativeMode.DirectInput, MsiClawNativeMode.XInput, MsiClawNativeMode.XInput], modeController.Targets);
    }

    [Fact]
    public async Task Native_recovery_does_not_clear_an_unrelated_recovery_safety_change()
    {
        var devices = new FakeDeviceEnumerator(MsiClawNativeMode.XInput);
        var modeController = new FakeModeController(devices) { FailRestore = true };
        var recoverySafety = new RecoverySafetyState(RecoverySafety.Safe);
        await using var coordinator = CreateCoordinator(devices, modeController, recoverySafety: recoverySafety);

        Assert.True((await coordinator.EnterForPipelineAsync(CancellationToken.None)).Succeeded);
        await Assert.ThrowsAsync<IOException>(() => coordinator.ExitForPipelineAsync(CancellationToken.None));
        recoverySafety.Set(RecoverySafety.Unsafe);
        modeController.FailRestore = false;

        Assert.True(await coordinator.ExitForPipelineAsync(CancellationToken.None));
        Assert.Equal(RecoverySafety.Unsafe, recoverySafety.Current);
    }

    [Fact]
    public async Task Repeated_native_failure_cannot_reclaim_another_owner_unsafe_state()
    {
        var devices = new FakeDeviceEnumerator(MsiClawNativeMode.XInput);
        var modeController = new FakeModeController(devices) { FailRestore = true };
        var recoverySafety = new RecoverySafetyState(RecoverySafety.Safe);
        await using var coordinator = CreateCoordinator(devices, modeController, recoverySafety: recoverySafety);

        Assert.True((await coordinator.EnterForPipelineAsync(CancellationToken.None)).Succeeded);
        await Assert.ThrowsAsync<IOException>(() => coordinator.ExitForPipelineAsync(CancellationToken.None));
        recoverySafety.Set(RecoverySafety.Unsafe);
        await Assert.ThrowsAsync<IOException>(() => coordinator.ExitForPipelineAsync(CancellationToken.None));
        modeController.FailRestore = false;

        Assert.True(await coordinator.ExitForPipelineAsync(CancellationToken.None));
        Assert.Equal(RecoverySafety.Unsafe, recoverySafety.Current);
    }

    [Fact]
    public async Task Suspend_cleanup_window_allows_owned_native_restore_without_forward_permission()
    {
        var devices = new FakeDeviceEnumerator(MsiClawNativeMode.XInput);
        var modeController = new FakeModeController(devices);
        var gate = new PowerMutationGate(initiallyOpen: true);
        await using var coordinator = CreateCoordinator(devices, modeController, gate);

        Assert.True((await coordinator.EnterForPipelineAsync(CancellationToken.None)).Succeeded);
        var writesBeforeBarrier = modeController.Targets.Count;
        gate.EnterNewCycleBarrier(out _, out _);

        Assert.False((await coordinator.EnterForPipelineAsync(CancellationToken.None)).Succeeded);
        Assert.True(await coordinator.ExitForPipelineAsync(CancellationToken.None));
        Assert.True(modeController.Targets.Count > writesBeforeBarrier);
        Assert.False(coordinator.HasOwnedRecoveryBoundary);
    }

    [Fact]
    public async Task Suspend_cleanup_window_does_not_allow_new_native_entry()
    {
        var devices = new FakeDeviceEnumerator(MsiClawNativeMode.XInput);
        var modeController = new FakeModeController(devices);
        var gate = new PowerMutationGate(initiallyOpen: true);
        gate.EnterNewCycleBarrier(out _, out _);
        await using var coordinator = CreateCoordinator(devices, modeController, gate);

        var result = await coordinator.EnterForPipelineAsync(CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("PowerGateClosed", result.Reason);
        Assert.Empty(modeController.Targets);
    }

    [Fact]
    public async Task Stale_power_epoch_during_restore_cannot_complete_recovery_and_fresh_epoch_can_retry()
    {
        var devices = new FakeDeviceEnumerator(MsiClawNativeMode.XInput);
        var gate = new PowerMutationGate(initiallyOpen: true);
        var recoverySafety = new RecoverySafetyState(RecoverySafety.Safe);
        var modeController = new FakeModeController(devices) { AfterRestoreApplied = () => gate.EnterNewCycleBarrier(out _, out _) };
        await using var coordinator = CreateCoordinator(devices, modeController, gate, recoverySafety);

        Assert.True((await coordinator.EnterForPipelineAsync(CancellationToken.None)).Succeeded);
        Assert.False(await coordinator.ExitForPipelineAsync(CancellationToken.None));
        Assert.Equal(RecoverySafety.Unsafe, recoverySafety.Current);
        Assert.True(coordinator.HasOwnedRecoveryBoundary);

        modeController.AfterRestoreApplied = null;
        gate.OpenAfterRecovery();
        Assert.True(await coordinator.ExitForPipelineAsync(CancellationToken.None));
        Assert.Equal(RecoverySafety.Safe, recoverySafety.Current);
        Assert.False(coordinator.HasOwnedRecoveryBoundary);
    }

    [Fact]
    public async Task Native_recovery_does_not_mark_global_safe_while_other_journal_mutation_remains()
    {
        var devices = new FakeDeviceEnumerator(MsiClawNativeMode.XInput);
        var modeController = new FakeModeController(devices) { FailRestore = true };
        var recoverySafety = new RecoverySafetyState(RecoverySafety.Safe);
        var store = new MemoryJournalStore();
        var recovery = new RecoveryManager(store);
        var native = new MsiClawNativeStateManager(devices, modeController);
        await using var coordinator = new MsiClawNativeModeSessionCoordinator(native, recovery, new PowerMutationGate(initiallyOpen: true), recoverySafety);

        Assert.True((await coordinator.EnterForPipelineAsync(CancellationToken.None)).Succeeded);
        Assert.Equal(RecoveryStatus.Success, recovery.RecordHidHideWhitelistAddition(coordinator.CurrentRecoverySessionId!.Value, "C:\\Addon.exe").Status);
        await Assert.ThrowsAsync<IOException>(() => coordinator.ExitForPipelineAsync(CancellationToken.None));
        modeController.FailRestore = false;

        Assert.True(await coordinator.ExitForPipelineAsync(CancellationToken.None));
        Assert.True(recovery.HasIncompleteRecovery);
        Assert.Equal(RecoverySafety.Unsafe, recoverySafety.Current);
        Assert.Equal("RecoverySafetyNotSafe", (await coordinator.EnterForPipelineAsync(CancellationToken.None)).Reason);
    }

    [Fact]
    public async Task Successful_fail_close_latches_forward_mutation_until_steam_session_ends()
    {
        var devices = new FakeDeviceEnumerator(MsiClawNativeMode.XInput);
        await using var coordinator = CreateCoordinator(devices, new FakeModeController(devices));

        Assert.True((await coordinator.EnterForPipelineAsync(CancellationToken.None)).Succeeded);
        await coordinator.FailClosedAsync("PipelineFailure");

        Assert.False(await coordinator.ConvergeAfterRoutingCleanupAsync());
        var blocked = await coordinator.EnterForPipelineAsync(CancellationToken.None);
        Assert.False(blocked.Succeeded);
        Assert.Equal("RoutingFaultLatched", blocked.Reason);
        Assert.True(await coordinator.OnSteamSessionEndedAsync(CancellationToken.None));
        Assert.True((await coordinator.EnterForPipelineAsync(CancellationToken.None)).Succeeded);
    }

    [Fact]
    public async Task Complete_cleanup_converges_owned_unsafe_state_and_fault_latch()
    {
        var devices = new FakeDeviceEnumerator(MsiClawNativeMode.XInput);
        var modeController = new FakeModeController(devices) { FailRestore = true };
        var recoverySafety = new RecoverySafetyState(RecoverySafety.Safe);
        var recovery = new RecoveryManager(new MemoryJournalStore());
        var native = new MsiClawNativeStateManager(devices, modeController);
        await using var coordinator = new MsiClawNativeModeSessionCoordinator(
            native, recovery, new PowerMutationGate(initiallyOpen: true), recoverySafety);

        Assert.True((await coordinator.EnterForPipelineAsync(CancellationToken.None)).Succeeded);
        var recoverySessionId = coordinator.CurrentRecoverySessionId!.Value;
        Assert.Equal(RecoveryStatus.Success, recovery.RecordHidHideWhitelistAddition(recoverySessionId, "C:\\Addon.exe").Status);
        await Assert.ThrowsAsync<IOException>(() => coordinator.FailClosedAsync("PipelineFailure"));
        modeController.FailRestore = false;
        Assert.True(await coordinator.ExitForPipelineAsync(CancellationToken.None));
        Assert.Equal(RecoverySafety.Unsafe, recoverySafety.Current);

        Assert.Equal(RecoveryStatus.Success, recovery.CompleteHidHideWhitelistAddition(recoverySessionId, "C:\\Addon.exe").Status);
        Assert.True(await coordinator.ConvergeAfterRoutingCleanupAsync());
        Assert.Equal(RecoverySafety.Safe, recoverySafety.Current);
        Assert.True((await coordinator.EnterForPipelineAsync(CancellationToken.None)).Succeeded);
    }

    [Fact]
    public async Task Native_only_cleanup_preserves_owned_version_until_final_convergence()
    {
        var devices = new FakeDeviceEnumerator(MsiClawNativeMode.XInput);
        var modeController = new FakeModeController(devices) { FailRestore = true };
        var recoverySafety = new RecoverySafetyState(RecoverySafety.Safe);
        await using var coordinator = CreateCoordinator(devices, modeController, recoverySafety: recoverySafety);

        Assert.True((await coordinator.EnterForPipelineAsync(CancellationToken.None)).Succeeded);
        await Assert.ThrowsAsync<IOException>(() => coordinator.FailClosedAsync("NativeFailure"));
        modeController.FailRestore = false;
        Assert.True(await coordinator.ExitForPipelineAsync(CancellationToken.None));
        Assert.Equal(RecoverySafety.Safe, recoverySafety.Current);

        Assert.True(await coordinator.ConvergeAfterRoutingCleanupAsync());
        Assert.True((await coordinator.EnterForPipelineAsync(CancellationToken.None)).Succeeded);
    }

    [Fact]
    public async Task Ordinary_safe_runtime_fault_remains_latched_until_steam_boundary()
    {
        var devices = new FakeDeviceEnumerator(MsiClawNativeMode.XInput);
        var recoverySafety = new RecoverySafetyState(RecoverySafety.Safe);
        await using var coordinator = CreateCoordinator(devices, new FakeModeController(devices), recoverySafety: recoverySafety);

        Assert.True((await coordinator.EnterForPipelineAsync(CancellationToken.None)).Succeeded);
        await coordinator.FailClosedAsync("RuntimeFault");

        Assert.False(await coordinator.ConvergeAfterRoutingCleanupAsync());
        Assert.Equal("RoutingFaultLatched", (await coordinator.EnterForPipelineAsync(CancellationToken.None)).Reason);
        Assert.True(await coordinator.OnSteamSessionEndedAsync(CancellationToken.None));
        Assert.True((await coordinator.EnterForPipelineAsync(CancellationToken.None)).Succeeded);
    }

    [Fact]
    public async Task Convergence_refuses_while_recovery_journal_remains()
    {
        var devices = new FakeDeviceEnumerator(MsiClawNativeMode.XInput);
        var recoverySafety = new RecoverySafetyState(RecoverySafety.Safe);
        var recovery = new RecoveryManager(new MemoryJournalStore());
        var native = new MsiClawNativeStateManager(devices, new FakeModeController(devices));
        await using var coordinator = new MsiClawNativeModeSessionCoordinator(
            native, recovery, new PowerMutationGate(initiallyOpen: true), recoverySafety);

        Assert.True((await coordinator.EnterForPipelineAsync(CancellationToken.None)).Succeeded);
        var sessionId = coordinator.CurrentRecoverySessionId!.Value;
        Assert.Equal(RecoveryStatus.Success, recovery.RecordHidHideWhitelistAddition(sessionId, "C:\\Addon.exe").Status);
        await coordinator.FailClosedAsync("PipelineFailure");

        Assert.True(recovery.HasIncompleteRecovery);
        Assert.False(await coordinator.ConvergeAfterRoutingCleanupAsync());
    }

    [Fact]
    public async Task Convergence_accepts_newer_safe_recovery_commit_for_owned_stale_claim()
    {
        var devices = new FakeDeviceEnumerator(MsiClawNativeMode.XInput);
        var modeController = new FakeModeController(devices) { FailRestore = true };
        var recoverySafety = new RecoverySafetyState(RecoverySafety.Safe);
        var recovery = new RecoveryManager(new MemoryJournalStore());
        var native = new MsiClawNativeStateManager(devices, modeController);
        await using var coordinator = new MsiClawNativeModeSessionCoordinator(
            native, recovery, new PowerMutationGate(initiallyOpen: true), recoverySafety);

        Assert.True((await coordinator.EnterForPipelineAsync(CancellationToken.None)).Succeeded);
        var sessionId = coordinator.CurrentRecoverySessionId!.Value;
        Assert.Equal(RecoveryStatus.Success, recovery.RecordHidHideWhitelistAddition(sessionId, "C:\\Addon.exe").Status);
        await Assert.ThrowsAsync<IOException>(() => coordinator.FailClosedAsync("PipelineFailure"));
        modeController.FailRestore = false;
        Assert.True(await coordinator.ExitForPipelineAsync(CancellationToken.None));
        Assert.Equal(RecoverySafety.Unsafe, recoverySafety.Current);

        Assert.Equal(RecoveryStatus.Success, recovery.CompleteHidHideWhitelistAddition(sessionId, "C:\\Addon.exe").Status);
        recoverySafety.Set(RecoverySafety.Safe);
        Assert.True(await coordinator.ConvergeAfterRoutingCleanupAsync());
        Assert.True((await coordinator.EnterForPipelineAsync(CancellationToken.None)).Succeeded);
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
    public async Task CanonicalCleanupAfterResumeClearsOwnershipAndAllowsFreshNativeEntry()
    {
        // The canonical pipeline rollback (ExitForPipelineAsync), not a resume-only recovery
        // replay hook, is now what a residual-cleanup retry on resume invokes to retire a
        // pre-suspend NativeMode session and clear ownership for fresh re-entry.
        var devices = new FakeDeviceEnumerator(MsiClawNativeMode.XInput);
        var modeController = new FakeModeController(devices);
        var store = new MemoryJournalStore();
        var recovery = new RecoveryManager(store);
        var gate = new PowerMutationGate(initiallyOpen: true);
        var native = new MsiClawNativeStateManager(devices, modeController);
        await using var coordinator = new MsiClawNativeModeSessionCoordinator(native, recovery, gate, new RecoverySafetyState(RecoverySafety.Safe));

        Assert.True((await coordinator.EnterForPipelineAsync(CancellationToken.None)).Succeeded);
        Assert.NotNull(coordinator.CurrentRecoverySessionId);
        Assert.True(await coordinator.ExitForPipelineAsync(CancellationToken.None));
        Assert.False(coordinator.HasOwnedRecoveryBoundary);
        Assert.Null(coordinator.CurrentRecoverySessionId);
        Assert.False(recovery.HasIncompleteRecovery);
        Assert.True((await coordinator.EnterForPipelineAsync(CancellationToken.None)).Succeeded);
        Assert.Equal([MsiClawNativeMode.DirectInput, MsiClawNativeMode.XInput, MsiClawNativeMode.DirectInput], modeController.Targets);
        Assert.True(await coordinator.ExitForPipelineAsync(CancellationToken.None));
    }

    [Fact]
    public async Task CanonicalCleanupFailureAfterResumePreservesOwnershipAndBlocksEntry()
    {
        // If the canonical cleanup retry itself cannot retire the owned session (e.g. the
        // native restore fails), ownership must remain latched so a journal still exists as
        // fail-closed evidence -- there is no separate resume replay path to fall back on.
        var devices = new FakeDeviceEnumerator(MsiClawNativeMode.XInput);
        var modeController = new FakeModeController(devices) { FailRestore = true };
        var gate = new PowerMutationGate(initiallyOpen: true);
        await using var coordinator = CreateCoordinator(devices, modeController, gate);

        Assert.True((await coordinator.EnterForPipelineAsync(CancellationToken.None)).Succeeded);
        Assert.NotNull(coordinator.CurrentRecoverySessionId);
        await Assert.ThrowsAsync<IOException>(() => coordinator.ExitForPipelineAsync(CancellationToken.None));
        Assert.True(coordinator.HasOwnedRecoveryBoundary);
        var reentry = await coordinator.EnterForPipelineAsync(CancellationToken.None);
        Assert.False(reentry.Succeeded);
        Assert.Equal("AlreadyActive", reentry.Reason);
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
    public async Task RoutingFaultLatch_BlocksForwardEntryUntilSteamSessionBoundaryWithoutDirectRestore()
    {
        var devices = new FakeDeviceEnumerator(MsiClawNativeMode.XInput);
        var modeController = new FakeModeController(devices);
        await using var coordinator = CreateCoordinator(devices, modeController);

        Assert.True((await coordinator.EnterForPipelineAsync(CancellationToken.None)).Succeeded);
        await coordinator.LatchRoutingFaultAsync("CanonicalRoutingReconciliationFailed");

        Assert.True(coordinator.IsActive);
        Assert.Equal(MsiClawNativeMode.DirectInput, devices.Mode);
        Assert.True(await coordinator.ExitForPipelineAsync(CancellationToken.None));

        var blocked = await coordinator.EnterForPipelineAsync(CancellationToken.None);
        Assert.False(blocked.Succeeded);
        Assert.Equal("RoutingFaultLatched", blocked.Reason);

        Assert.True(await coordinator.OnSteamSessionEndedAsync(CancellationToken.None));
        Assert.True((await coordinator.InspectForPipelineAsync(CancellationToken.None)).Succeeded);
    }

    [Fact]
    public async Task EnterWithValidSnapshotButWeakIdentityFailsBeforeMutation()
    {
        var devices = new WeakIdentityEnumerator();
        var modeController = new NeverModeController();
        await using var coordinator = CreateCoordinatorFor(devices, modeController);

        var result = await coordinator.EnterForPipelineAsync(CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("PhysicalIdentityNotStrong", result.Reason);
        Assert.Equal(0, modeController.CallCount);
        Assert.False(coordinator.HasOwnedRecoveryBoundary);
    }

    [Fact]
    public async Task PipelineEnterCarriesSentinelPhysicalDeviceKeyToModeSwitch()
    {
        var devices = new SentinelDeviceEnumerator();
        var modeController = new RecordingModeController();
        await using var coordinator = CreateCoordinatorFor(devices, modeController);

        Assert.True((await coordinator.InspectForPipelineAsync(CancellationToken.None)).Succeeded);
        var result = await coordinator.EnterForPipelineAsync(CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Single(modeController.Identities);
        Assert.Equal("USB\\VID_0DB0\\CLAW_A", modeController.Identities[0].PhysicalDeviceKey);
        Assert.Equal(MsiClawIdentityConfidence.Strong, modeController.Identities[0].Confidence);
    }

    [Fact]
    public async Task IRoutingSafetySession_view_observes_the_same_state_and_forwards_FailClosedAsync()
    {
        var devices = new FakeDeviceEnumerator(MsiClawNativeMode.XInput);
        var modeController = new FakeModeController(devices);
        var recoverySafety = new RecoverySafetyState(RecoverySafety.Safe);
        var gate = new PowerMutationGate(initiallyOpen: true);
        await using var coordinator = CreateCoordinator(devices, modeController, gate, recoverySafety);
        IRoutingSafetySession safetySession = coordinator;

        Assert.True(await coordinator.ObserveRoutingDecisionAsync(Eligible(), 1));

        // The generic view must observe the exact same underlying state as the concrete type --
        // not a copy or a separately tracked value.
        Assert.Equal(coordinator.IsActive, safetySession.IsActive);
        Assert.Equal(coordinator.HasOwnedRecoveryBoundary, safetySession.HasOwnedRecoveryBoundary);
        Assert.Equal(coordinator.CurrentRecoverySessionId, safetySession.CurrentRecoverySessionId);

        await safetySession.FailClosedAsync("CanonicalRoutingReconciliationFailed");

        // FailClosedAsync called through the interface must reach the same fail-close
        // implementation as calling the concrete method directly: mode restored, power gate
        // still open, recovery safety still Safe.
        Assert.Equal(MsiClawNativeMode.XInput, devices.Mode);
        Assert.True(gate.IsOpen);
        Assert.Equal(RecoverySafety.Safe, recoverySafety.Current);
    }

    private static MsiClawNativeModeSessionCoordinator CreateCoordinator(
        FakeDeviceEnumerator devices,
        FakeModeController modeController,
        PowerMutationGate? gate = null,
        RecoverySafetyState? recoverySafety = null)
    {
        var store = new MemoryJournalStore();
        var recovery = new RecoveryManager(store);
        var native = new MsiClawNativeStateManager(devices, modeController);
        return new(native, recovery, gate ?? new PowerMutationGate(initiallyOpen: true), recoverySafety ?? new RecoverySafetyState(RecoverySafety.Safe));
    }

    private static MsiClawNativeModeSessionCoordinator CreateCoordinatorFor(IControllerDeviceEnumerator devices, IMsiClawModeController modeController)
    {
        var recovery = new RecoveryManager(new MemoryJournalStore());
        return new(new MsiClawNativeStateManager(devices, modeController), recovery, new PowerMutationGate(initiallyOpen: true), new RecoverySafetyState(RecoverySafety.Safe));
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
        public Action? AfterRestoreApplied { get; set; }
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
            if (target == MsiClawNativeMode.XInput)
                AfterRestoreApplied?.Invoke();
            devices.Mode = target;
            return new(MsiClawModeTransitionStatus.Succeeded, target == MsiClawNativeMode.XInput ? MsiClawNativeMode.DirectInput : MsiClawNativeMode.XInput,
                target, null, target == MsiClawNativeMode.XInput ? MsiClawHardware.XInputProductId : MsiClawHardware.DirectInputProductId,
                true, true, true, true, true, 1, "test");
        }

        public void ReleaseFirstSwitch() => _releaseFirstSwitch.SetResult();
    }

    private sealed class WeakIdentityEnumerator : IControllerDeviceEnumerator
    {
        public IReadOnlyList<ControllerDeviceInfo> EnumeratePresentDevices() =>
        [new("HID\\MSI_CLAW", Guid.Parse("00000000-0000-0000-ffff-ffffffffffff"), "PARENT", [], "HID", [], [], "HIDClass", null, null, MsiClawHardware.VendorId, MsiClawHardware.XInputProductId, true)];
    }

    private sealed class SentinelDeviceEnumerator : IControllerDeviceEnumerator
    {
        private static readonly Guid Sentinel = Guid.Parse("00000000-0000-0000-ffff-ffffffffffff");
        public IReadOnlyList<ControllerDeviceInfo> EnumeratePresentDevices() =>
        [
            new("USB\\VID_0DB0&PID_1901&MI_00\\A", Sentinel, "USB\\PARENT", ["USB\\VID_0DB0&PID_1901\\CLAW_A"],
                "HID", [], [], "HIDClass", null, null, MsiClawHardware.VendorId, MsiClawHardware.XInputProductId, true,
                UsagePage: 0xFFA0, Usage: 0x0001)
        ];
    }

    private sealed class RecordingModeController : IMsiClawModeController
    {
        public List<MsiClawPhysicalIdentity> Identities { get; } = [];
        public Task<MsiClawModeTransitionResult> SwitchModeAsync(MsiClawNativeMode target, MsiClawPhysicalIdentity expectedIdentity, CancellationToken cancellationToken)
        {
            Identities.Add(expectedIdentity);
            return Task.FromResult(new MsiClawModeTransitionResult(MsiClawModeTransitionStatus.Succeeded,
                MsiClawNativeMode.XInput, target, MsiClawHardware.XInputProductId,
                target == MsiClawNativeMode.XInput ? MsiClawHardware.XInputProductId : MsiClawHardware.DirectInputProductId,
                true, true, true, true, true, 1, "test"));
        }
    }

    private sealed class NeverModeController : IMsiClawModeController
    {
        public int CallCount { get; private set; }
        public Task<MsiClawModeTransitionResult> SwitchModeAsync(MsiClawNativeMode target, MsiClawPhysicalIdentity expectedIdentity, CancellationToken cancellationToken)
        {
            CallCount++;
            throw new InvalidOperationException("mode mutation must not be reached");
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
