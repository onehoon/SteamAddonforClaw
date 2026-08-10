using SteamInputAddonforClaw.Controllers.Detection;
using SteamInputAddonforClaw.Power;
using SteamInputAddonforClaw.VirtualOutput.Viiper;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class PowerTransitionTests
{
    [Fact]
    public void Registration_failure_fails_closed()
    {
        var gate = new PowerMutationGate(true); var source = new FakeSource(false);
        var watcher = new PowerTransitionWatcher(source, gate, Coordinator(gate), () => { });
        Assert.False(watcher.Start()); Assert.False(gate.IsOpen); watcher.Dispose();
    }

    [Fact]
    public void Suspend_callback_closes_gate_advances_epoch_and_cancels_without_waiting_for_quiesce()
    {
        var gate = new PowerMutationGate(true); var cancelled = 0; var source = new FakeSource(true);
        var participant = new BlockingParticipant(); var coordinator = Coordinator(gate, participant);
        using var watcher = new PowerTransitionWatcher(source, gate, coordinator, () => Interlocked.Increment(ref cancelled));
        Assert.True(watcher.Start()); var before = gate.Epoch;
        source.Raise(4);
        Assert.False(gate.IsOpen); Assert.True(gate.Epoch > before); Assert.Equal(1, cancelled); Assert.False(participant.Completed.Task.IsCompleted);
    }

    [Fact]
    public async Task Resume_failure_keeps_gate_closed_and_recovery_unsafe()
    {
        var gate = new PowerMutationGate(true); var recovery = new RecoverySafetyState(RecoverySafety.Safe);
        var coordinator = new PowerTransitionCoordinator(gate, recovery, _ => Task.FromResult(false), []);
        await coordinator.HandleAsync(new(18, PowerSignal.ResumeAutomatic, DateTimeOffset.UtcNow, 1, 1, 0, 1, true));
        Assert.Equal(PowerTransitionState.Unsafe, coordinator.State); Assert.Equal(RecoverySafety.Unsafe, recovery.Current); Assert.False(gate.IsOpen);
    }

    [Fact]
    public async Task Resume_automatic_then_resume_suspend_reconciles_once()
    {
        var gate = new PowerMutationGate(true); var calls = 0;
        var coordinator = new PowerTransitionCoordinator(gate, new RecoverySafetyState(RecoverySafety.Safe), _ => { calls++; return Task.FromResult(true); }, []);
        await coordinator.HandleAsync(new(18, PowerSignal.ResumeAutomatic, DateTimeOffset.UtcNow, 1, 1, 0, 1, true));
        await coordinator.HandleAsync(new(7, PowerSignal.ResumeSuspend, DateTimeOffset.UtcNow, 2, 1, 1, 1, false));
        Assert.Equal(1, calls);
    }

    [Theory]
    [InlineData(18u, 7u)]
    [InlineData(7u, 18u)]
    public async Task Watcher_resume_pair_reconciles_once_and_leaves_gate_open(uint firstResume, uint secondResume)
    {
        var gate = new PowerMutationGate(true); var calls = 0; var source = new FakeSource(true);
        var coordinator = new PowerTransitionCoordinator(gate, new RecoverySafetyState(RecoverySafety.Safe), _ => { Interlocked.Increment(ref calls); return Task.FromResult(true); }, []);
        using var watcher = new PowerTransitionWatcher(source, gate, coordinator, () => { }); Assert.True(watcher.Start());
        await watcher.ObserveAsync(4);
        await watcher.ObserveAsync(firstResume);
        await watcher.ObserveAsync(secondResume);
        Assert.Equal(1, calls); Assert.True(gate.IsOpen); Assert.Equal(PowerTransitionState.Awake, coordinator.State);
    }

    [Fact]
    public void Failed_resume_allows_a_new_suspend_cycle_and_recovery_retry()
    {
        var gate = new PowerMutationGate(true); var calls = 0; var source = new FakeSource(true);
        var coordinator = new PowerTransitionCoordinator(gate, new RecoverySafetyState(RecoverySafety.Safe), _ => Task.FromResult(Interlocked.Increment(ref calls) > 1), []);
        using var watcher = new PowerTransitionWatcher(source, gate, coordinator, () => { }); Assert.True(watcher.Start());
        source.Raise(4); Assert.True(SpinWait.SpinUntil(() => coordinator.State == PowerTransitionState.Suspended, TimeSpan.FromSeconds(1)));
        source.Raise(18); Assert.True(SpinWait.SpinUntil(() => coordinator.State == PowerTransitionState.Unsafe, TimeSpan.FromSeconds(1))); var firstEpoch = gate.Epoch;
        source.Raise(4); Assert.True(SpinWait.SpinUntil(() => gate.Epoch > firstEpoch, TimeSpan.FromSeconds(1)));
        source.Raise(18); Assert.True(SpinWait.SpinUntil(() => coordinator.State == PowerTransitionState.Awake && gate.IsOpen, TimeSpan.FromSeconds(1))); Assert.Equal(2, calls);
    }

    [Fact]
    public async Task Stale_resume_recovery_cannot_reopen_gate_after_a_new_suspend()
    {
        var gate = new PowerMutationGate(true); var recovery = new RecoverySafetyState(RecoverySafety.Safe);
        var pendingRecovery = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var coordinator = new PowerTransitionCoordinator(gate, recovery, _ => pendingRecovery.Task, []);
        var resume = coordinator.HandleAsync(new(18, PowerSignal.ResumeAutomatic, DateTimeOffset.UtcNow, 1, 1, 0, 1, true));
        Assert.True(SpinWait.SpinUntil(() => coordinator.State == PowerTransitionState.Recovering, TimeSpan.FromSeconds(1)));
        gate.EnterNewCycleBarrier(out _, out var newEpoch); coordinator.InvalidateForBarrier(); pendingRecovery.SetResult(true); await resume;
        Assert.False(gate.IsOpen); Assert.Equal(newEpoch, gate.Epoch); Assert.NotEqual(RecoverySafety.Safe, recovery.Current); Assert.NotEqual(PowerTransitionState.Awake, coordinator.State);
    }

    [Fact]
    public async Task Stale_failed_resume_cannot_overwrite_a_new_suspend_barrier_state()
    {
        var gate = new PowerMutationGate(true); var recovery = new RecoverySafetyState(RecoverySafety.Safe);
        var pendingRecovery = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var coordinator = new PowerTransitionCoordinator(gate, recovery, _ => pendingRecovery.Task, []);
        var resume = coordinator.HandleAsync(new(18, PowerSignal.ResumeAutomatic, DateTimeOffset.UtcNow, 1, 1, 0, 1, true));
        Assert.True(SpinWait.SpinUntil(() => coordinator.State == PowerTransitionState.Recovering, TimeSpan.FromSeconds(1)));
        gate.EnterNewCycleBarrier(out _, out var newEpoch); coordinator.InvalidateForBarrier(); pendingRecovery.SetResult(false); await resume;
        Assert.Equal(newEpoch, gate.Epoch); Assert.False(gate.IsOpen); Assert.Equal(RecoverySafety.Indeterminate, recovery.Current); Assert.Equal(PowerTransitionState.Quiescing, coordinator.State);
    }

    [Fact]
    public async Task Duplicate_suspend_does_not_advance_epoch_or_quiesce_twice()
    {
        var gate = new PowerMutationGate(true); var source = new FakeSource(true); var participant = new CountingParticipant();
        var coordinator = Coordinator(gate, participant); using var watcher = new PowerTransitionWatcher(source, gate, coordinator, () => { }); Assert.True(watcher.Start());
        await watcher.ObserveAsync(4); var epoch = gate.Epoch; await watcher.ObserveAsync(4);
        Assert.Equal(epoch, gate.Epoch); Assert.Equal(1, participant.QuiesceCount);
    }

    [Fact]
    public void Verified_absence_only_clears_uncertain_ownership()
    {
        var tracker = new AddonOwnedVirtualDeviceTracker(); var policy = new ViiperVirtualDeviceIdentityPolicy(); tracker.MarkOwnershipUncertain();
        Assert.True(tracker.ClearUncertaintyAfterVerifiedAbsence([], policy)); Assert.False(tracker.HasUncertainOwnership);
        tracker.MarkOwnershipUncertain();
        var candidate = new ControllerDeviceInfo("USB\\VID_28DE&PID_1102\\A", null, null, ["ROOT\\USBIP_WIN2\\UDE"], "HID", [], [], "HID", null, "usbip2_ude", 0x28DE, 0x1102, true);
        Assert.False(tracker.ClearUncertaintyAfterVerifiedAbsence([candidate], policy)); Assert.True(tracker.HasUncertainOwnership);
    }

    private static PowerTransitionCoordinator Coordinator(PowerMutationGate gate, params IPowerTransitionParticipant[] participants) => new(gate, new RecoverySafetyState(RecoverySafety.Safe), _ => Task.FromResult(true), participants);
    private sealed class FakeSource(bool succeeds) : IPowerSuspendResumeNotificationSource
    {
        public event Action<uint>? Notification;
        public bool TryRegister(out int nativeError) { nativeError = succeeds ? 0 : 5; return succeeds; }
        public void Raise(uint code) => Notification?.Invoke(code);
        public void Dispose() { }
    }
    private sealed class BlockingParticipant : IPowerTransitionParticipant
    {
        public string Name => "blocked";
        public TaskCompletionSource Completed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<bool> QuiesceForSuspendAsync(DateTimeOffset deadline, long cycle, long epoch, CancellationToken cancellationToken) { try { await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken); } catch (OperationCanceledException) { } Completed.TrySetResult(); return false; }
        public Task<bool> ReconcileAfterResumeAsync(long cycle, long epoch, CancellationToken cancellationToken) => Task.FromResult(true);
    }
    private sealed class CountingParticipant : IPowerTransitionParticipant
    {
        public string Name => "counting"; public int QuiesceCount;
        public Task<bool> QuiesceForSuspendAsync(DateTimeOffset deadline, long cycle, long epoch, CancellationToken cancellationToken) { Interlocked.Increment(ref QuiesceCount); return Task.FromResult(true); }
        public Task<bool> ReconcileAfterResumeAsync(long cycle, long epoch, CancellationToken cancellationToken) => Task.FromResult(true);
    }
}
