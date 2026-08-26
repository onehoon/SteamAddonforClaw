using SteamInputAddonforClaw.Controllers.Detection;
using SteamInputAddonforClaw.Power;
using SteamInputAddonforClaw.VirtualOutput.Viiper;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

[Collection("AppLog")]
public sealed class PowerTransitionTests
{
    [Fact]
    public async Task Resume_observer_runs_only_for_the_first_accepted_resume_cycle()
    {
        var observed = 0;
        var coordinator = new PowerTransitionCoordinator(new PowerMutationGate(false), new RecoverySafetyState(RecoverySafety.Unsafe), [],
            hasIncompleteRecovery: () => false,
            resumeObserved: () => observed++);

        var notification = new PowerNotificationObservation(18, PowerSignal.ResumeAutomatic, DateTimeOffset.UtcNow, 1, 1, 0, 0, true);
        await coordinator.HandleAsync(notification);
        await coordinator.HandleAsync(notification);

        Assert.Equal(1, observed);
        await coordinator.DisposeAsync();
    }

    [Fact]
    public async Task CleanResume_NoResidualNoJournal_EstablishesBaselineAndOpensGate()
    {
        var gate = new PowerMutationGate(false);
        var recovery = new RecoverySafetyState(RecoverySafety.Unsafe);
        var baselineCalls = 0;
        var coordinator = new PowerTransitionCoordinator(gate, recovery, [],
            hasIncompleteRecovery: () => false,
            establishBaseline: _ => { baselineCalls++; return Task.FromResult(true); });

        await coordinator.HandleAsync(new(18, PowerSignal.ResumeAutomatic, DateTimeOffset.UtcNow, 1, 1, 0, 0, true));

        Assert.Equal(1, baselineCalls);
        Assert.True(gate.IsOpen);
        Assert.Equal(RecoverySafety.Safe, recovery.Current);
        Assert.Equal(PowerTransitionState.Awake, coordinator.State);
    }

    [Fact]
    public async Task EstablishBaseline_runs_entirely_before_the_power_gate_opens_and_afterRecovery_runs_only_once_open()
    {
        // PR2 review fix (BLOCKER): PowerTransitionCoordinator opens PowerMutationGate as soon as
        // establishBaseline succeeds -- strictly BEFORE afterRecovery ever runs. AddonRuntimeHost's
        // OEM1 auxiliary resume reconcile therefore has to run INSIDE establishBaseline (while the
        // gate is still closed to normal routing re-entry), not inside afterRecovery (where it could
        // already be racing an already-open gate). This test proves the exact mechanism that
        // ordering choice depends on, directly against PowerTransitionCoordinator.
        var gate = new PowerMutationGate(false);
        var recovery = new RecoverySafetyState(RecoverySafety.Unsafe);
        var gateOpenDuringBaseline = true;
        var gateOpenDuringAfterRecovery = false;
        var coordinator = new PowerTransitionCoordinator(gate, recovery, [],
            hasIncompleteRecovery: () => false,
            establishBaseline: _ =>
            {
                gateOpenDuringBaseline = gate.IsOpen;
                return Task.FromResult(true);
            },
            afterRecovery: _ =>
            {
                gateOpenDuringAfterRecovery = gate.IsOpen;
                return Task.FromResult(true);
            });

        await coordinator.HandleAsync(new(18, PowerSignal.ResumeAutomatic, DateTimeOffset.UtcNow, 1, 1, 0, 0, true));

        Assert.False(gateOpenDuringBaseline);
        Assert.True(gateOpenDuringAfterRecovery);
        Assert.True(gate.IsOpen);
    }

    [Fact]
    public async Task Preserved_recovery_runs_post_commit_callback_only_after_safe_recovery_is_committed()
    {
        var gate = new PowerMutationGate(false);
        var recovery = new RecoverySafetyState(RecoverySafety.Unsafe);
        var callbackCalls = 0;
        var callbackSawSafe = false;
        var callbackSawOpenGate = false;
        await using var coordinator = new PowerTransitionCoordinator(
            gate,
            recovery,
            [],
            hasIncompleteRecovery: () => false,
            hasPreservedRoutingSession: () => true,
            reconcilePreservedRoutingSession: _ => Task.FromResult(true),
            afterPreservedRecoveryCommit: () =>
            {
                callbackCalls++;
                callbackSawSafe = recovery.Current == RecoverySafety.Safe;
                callbackSawOpenGate = gate.IsOpen;
                return Task.CompletedTask;
            });

        await coordinator.HandleAsync(new(18, PowerSignal.ResumeAutomatic, DateTimeOffset.UtcNow, 1, 1, 0, 0, true));

        Assert.Equal(1, callbackCalls);
        Assert.True(callbackSawSafe);
        Assert.True(callbackSawOpenGate);
        Assert.Equal(RecoverySafety.Safe, recovery.Current);
        Assert.True(gate.IsOpen);
    }

    [Fact]
    public async Task JournalRemainsAfterCanonicalCleanup_FailsClosedWithoutReplayOrBaseline()
    {
        var gate = new PowerMutationGate(false);
        var recovery = new RecoverySafetyState(RecoverySafety.Unsafe);
        var baselineCalls = 0;
        var afterRecoveryCalls = 0;
        var coordinator = new PowerTransitionCoordinator(gate, recovery, [],
            afterRecovery: _ => { afterRecoveryCalls++; return Task.FromResult(true); },
            hasIncompleteRecovery: () => true,
            establishBaseline: _ => { baselineCalls++; return Task.FromResult(true); });

        await coordinator.HandleAsync(new(18, PowerSignal.ResumeAutomatic, DateTimeOffset.UtcNow, 1, 1, 0, 0, true));

        Assert.Equal(0, baselineCalls);
        Assert.Equal(0, afterRecoveryCalls);
        Assert.False(gate.IsOpen);
        Assert.Equal(RecoverySafety.Unsafe, recovery.Current);
        Assert.Equal(PowerTransitionState.Unsafe, coordinator.State);
    }

    [Fact]
    public async Task ResidualRoutingCleanup_RetriedBeforeJournalCheckAndBaseline()
    {
        var calls = new List<string>();
        var gate = new PowerMutationGate(false);
        var gateOpenDuringCleanup = true;
        var coordinator = new PowerTransitionCoordinator(gate, new RecoverySafetyState(RecoverySafety.Unsafe), [],
            hasIncompleteRecovery: () => false,
            establishBaseline: _ => { calls.Add("Baseline"); return Task.FromResult(true); },
            hasResidualRoutingCleanup: () => true,
            retryResidualRoutingCleanup: _ =>
            {
                calls.Add("Cleanup");
                gateOpenDuringCleanup = gate.IsOpen;
                return Task.FromResult(true);
            });

        await coordinator.HandleAsync(new(18, PowerSignal.ResumeAutomatic, DateTimeOffset.UtcNow, 1, 1, 0, 0, true));

        Assert.Equal(["Cleanup", "Baseline"], calls);
        Assert.False(gateOpenDuringCleanup);
        Assert.True(gate.IsOpen);
    }

    [Fact]
    public async Task ResidualRoutingCleanup_ClearsJournal_ThenNormalFreshResume()
    {
        var calls = new List<string>();
        var cleanupRan = false;
        var coordinator = new PowerTransitionCoordinator(new PowerMutationGate(false), new RecoverySafetyState(RecoverySafety.Unsafe), [],
            hasIncompleteRecovery: () => !cleanupRan,
            establishBaseline: _ => { calls.Add("Baseline"); return Task.FromResult(true); },
            hasResidualRoutingCleanup: () => true,
            retryResidualRoutingCleanup: _ =>
            {
                calls.Add("Cleanup");
                cleanupRan = true;
                return Task.FromResult(true);
            });

        await coordinator.HandleAsync(new(18, PowerSignal.ResumeAutomatic, DateTimeOffset.UtcNow, 1, 1, 0, 0, true));

        Assert.Equal(["Cleanup", "Baseline"], calls);
    }

    [Fact]
    public async Task ResidualRoutingCleanup_Failure_SkipsJournalCheckAndBaselineAndRemainsUnsafe()
    {
        var baselineCalls = 0;
        var gate = new PowerMutationGate(false);
        var recovery = new RecoverySafetyState(RecoverySafety.Unsafe);
        var coordinator = new PowerTransitionCoordinator(gate, recovery, [],
            hasIncompleteRecovery: () => true,
            establishBaseline: _ => { baselineCalls++; return Task.FromResult(true); },
            hasResidualRoutingCleanup: () => true,
            retryResidualRoutingCleanup: _ => Task.FromResult(false));

        await coordinator.HandleAsync(new(18, PowerSignal.ResumeAutomatic, DateTimeOffset.UtcNow, 1, 1, 0, 0, true));

        Assert.Equal(0, baselineCalls);
        Assert.False(gate.IsOpen);
        Assert.Equal(RecoverySafety.Unsafe, recovery.Current);
        Assert.Equal(PowerTransitionState.Unsafe, coordinator.State);
    }

    [Fact]
    public async Task NoResidualRoutingState_SkipsCleanupRetryAndGoesStraightToJournalCheck()
    {
        var calls = new List<string>();
        var coordinator = new PowerTransitionCoordinator(new PowerMutationGate(false), new RecoverySafetyState(RecoverySafety.Unsafe), [],
            hasIncompleteRecovery: () => false,
            establishBaseline: _ => { calls.Add("Baseline"); return Task.FromResult(true); },
            hasResidualRoutingCleanup: () => false,
            retryResidualRoutingCleanup: _ => { calls.Add("Cleanup"); return Task.FromResult(true); });

        await coordinator.HandleAsync(new(18, PowerSignal.ResumeAutomatic, DateTimeOffset.UtcNow, 1, 1, 0, 0, true));

        Assert.Equal(["Baseline"], calls);
    }

    [Fact]
    public async Task StaleResidualCleanupCompletion_DoesNotSealOrCommitOverANewerSuspendEpoch()
    {
        var gate = new PowerMutationGate(false);
        var recovery = new RecoverySafetyState(RecoverySafety.Unsafe);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var coordinator = new PowerTransitionCoordinator(gate, recovery, [],
            hasIncompleteRecovery: () => false,
            establishBaseline: _ => Task.FromResult(true),
            hasResidualRoutingCleanup: () => true,
            retryResidualRoutingCleanup: async _ =>
            {
                started.TrySetResult();
                return await release.Task;
            });

        var resume = coordinator.HandleAsync(new(18, PowerSignal.ResumeAutomatic, DateTimeOffset.UtcNow, 1, 1, 0, 0, true));
        await started.Task;

        // Simulate a newer suspend cycle (already applied by the watcher's own barrier logic)
        // arriving while the residual cleanup retry for the older resume is still in flight.
        gate.EnterNewCycleBarrier(out _, out var newerEpoch);
        coordinator.InvalidateForBarrier();
        release.TrySetResult(true);
        await resume;

        Assert.Equal(newerEpoch, gate.Epoch);
        Assert.False(gate.IsOpen);
        Assert.Equal(PowerTransitionState.Quiescing, coordinator.State);
        Assert.Equal(RecoverySafety.Indeterminate, recovery.Current);
        // The newer suspend cycle's own cleanup permission must still be intact -- the stale
        // resume's failed TrySealResumeCleanup must not have closed it.
        Assert.True(gate.TryAcquireCleanup(out _));
    }

    [Fact]
    public void Suspend_barrier_denies_forward_mutation_and_allows_cleanup_until_sealed()
    {
        var gate = new PowerMutationGate(true);
        gate.EnterNewCycleBarrier(out _, out var epoch);

        Assert.False(gate.IsOpen);
        Assert.False(gate.TryAcquire(out _));
        Assert.True(gate.TryAcquireCleanup(out var cleanup));
        Assert.True(gate.IsCurrentCleanup(cleanup));
        Assert.True(gate.TrySealSuspendCleanup(epoch));
        Assert.False(gate.TryAcquire(out _));
        Assert.False(gate.TryAcquireCleanup(out _));
    }

    [Fact]
    public void Generic_barrier_and_stale_suspend_seal_do_not_grant_cleanup_permission()
    {
        var gate = new PowerMutationGate(true);
        gate.EnterNewCycleBarrier(out _, out var suspendEpoch);
        gate.OpenAfterRecovery();
        Assert.True(gate.TryEnterBarrier(out _, out var newerEpoch));

        Assert.False(gate.TrySealSuspendCleanup(suspendEpoch));
        Assert.Equal(newerEpoch, gate.Epoch);
        Assert.False(gate.TryAcquire(out _));
        Assert.False(gate.TryAcquireCleanup(out _));
    }

    [Fact]
    public void Registration_failure_fails_closed()
    {
        var gate = new PowerMutationGate(true); var source = new FakeSource(false);
        var watcher = new PowerTransitionWatcher(source, gate, Coordinator(gate), () => { });
        Assert.False(watcher.Start()); Assert.False(gate.IsOpen); watcher.Dispose();
    }

    [Fact]
    public async Task Notification_completion_failure_is_observed_and_fails_closed()
    {
        var gate = new PowerMutationGate(true);
        var source = new FakeSource(true);
        var coordinator = Coordinator(gate);
        using var watcher = new PowerTransitionWatcher(source, gate, coordinator, () => { });
        Assert.True(watcher.Start());
        await coordinator.DisposeAsync();
        gate.OpenAfterRecovery();

        source.Raise(999);

        Assert.True(SpinWait.SpinUntil(() => !gate.IsOpen, TimeSpan.FromSeconds(5)));
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
    public async Task Suspend_seals_cleanup_window_after_success_and_failure()
    {
        var successGate = new PowerMutationGate(true);
        var success = Coordinator(successGate, new CountingParticipant());
        successGate.EnterNewCycleBarrier(out _, out var successEpoch);
        await success.HandleAsync(new(4, PowerSignal.Suspend, DateTimeOffset.UtcNow, 1, 1, 0, successEpoch, true));
        Assert.Equal(PowerTransitionState.Suspended, success.State);
        Assert.False(successGate.IsOpen);
        Assert.False(successGate.TryAcquireCleanup(out _));

        var failureGate = new PowerMutationGate(true);
        var failure = Coordinator(failureGate, new FailingParticipant());
        failureGate.EnterNewCycleBarrier(out _, out var failureEpoch);
        await failure.HandleAsync(new(4, PowerSignal.Suspend, DateTimeOffset.UtcNow, 1, 1, 0, failureEpoch, true));
        Assert.Equal(PowerTransitionState.Unsafe, failure.State);
        Assert.False(failureGate.IsOpen);
        Assert.False(failureGate.TryAcquireCleanup(out _));
    }

    [Fact]
    public async Task Earlier_participant_is_invoked_before_a_later_one_can_exhaust_the_shared_deadline()
    {
        // PR2 review fix (BLOCKER): every suspend participant shares ONE deadline, and the
        // coordinator stops calling further participants once no time remains. AddonRuntimeHost
        // relies on this exact mechanism to guarantee its OEM1 auxiliary lifecycle participant is
        // invoked before a slow/timed-out routing quiesce could consume the whole budget --
        // reproduced directly here without needing the full routing/VIIPER stack.
        var gate = new PowerMutationGate(true);
        var spy = new CountingParticipant();
        var slow = new BlockingParticipant();
        var coordinator = new PowerTransitionCoordinator(gate, new RecoverySafetyState(RecoverySafety.Safe), [spy, slow],
            suspendQuiesceBudget: TimeSpan.FromMilliseconds(3000));
        gate.EnterNewCycleBarrier(out _, out var epoch);

        await coordinator.HandleAsync(new(4, PowerSignal.Suspend, DateTimeOffset.UtcNow, 1, 1, 0, epoch, true));

        Assert.Equal(1, spy.QuiesceCount);
    }

    [Fact]
    public async Task An_expired_suspend_deadline_does_not_invoke_any_participant()
    {
        // Use an already-expired observed timestamp so this boundary test does not infer timer
        // ordering from scheduler timing. The coordinator must reject the suspend work before
        // invoking the first participant when the shared deadline has already elapsed.
        var gate = new PowerMutationGate(true);
        var spy = new CountingParticipant();
        var coordinator = new PowerTransitionCoordinator(gate, new RecoverySafetyState(RecoverySafety.Safe), [spy],
            suspendQuiesceBudget: TimeSpan.FromMilliseconds(3000));
        gate.EnterNewCycleBarrier(out _, out var epoch);

        var observedUtc = DateTimeOffset.UtcNow - TimeSpan.FromSeconds(4);
        await coordinator.HandleAsync(new(4, PowerSignal.Suspend, observedUtc, 1, 1, 0, epoch, true));

        Assert.Equal(0, spy.QuiesceCount);
    }

    [Fact]
    public async Task Stale_suspend_completion_does_not_commit_over_newer_quiescing_state()
    {
        var gate = new PowerMutationGate(true);
        var participant = new ControllableParticipant();
        var coordinator = Coordinator(gate, participant);
        gate.EnterNewCycleBarrier(out _, out var suspendEpoch);

        var suspend = coordinator.HandleAsync(new(4, PowerSignal.Suspend, DateTimeOffset.UtcNow, 1, 1, 0, suspendEpoch, true));
        await participant.Started.Task;

        gate.EnterNewCycleBarrier(out _, out var newerEpoch);
        coordinator.InvalidateForBarrier();
        participant.Release.TrySetResult(true);
        await suspend;

        Assert.Equal(newerEpoch, gate.Epoch);
        Assert.Equal(PowerTransitionState.Quiescing, coordinator.State);
        Assert.True(gate.TryAcquireCleanup(out _));
    }

    [Fact]
    public async Task Resume_failure_keeps_gate_closed_and_recovery_unsafe()
    {
        var gate = new PowerMutationGate(true); var recovery = new RecoverySafetyState(RecoverySafety.Safe);
        // Default hasIncompleteRecovery reports a journal remains, so resume must fail closed.
        var coordinator = new PowerTransitionCoordinator(gate, recovery, []);
        await coordinator.HandleAsync(new(18, PowerSignal.ResumeAutomatic, DateTimeOffset.UtcNow, 1, 1, 0, 0, true));
        Assert.Equal(PowerTransitionState.Unsafe, coordinator.State); Assert.Equal(RecoverySafety.Unsafe, recovery.Current); Assert.False(gate.IsOpen);
    }

    [Fact]
    public async Task StartupUnsafeProcess_ResumeRemainsPassiveWithoutBaseline()
    {
        var gate = new PowerMutationGate(false);
        var recovery = new RecoverySafetyState(RecoverySafety.Unsafe);
        var baselineCalls = 0;
        var coordinator = new PowerTransitionCoordinator(gate, recovery, [],
            establishBaseline: _ => { baselineCalls++; return Task.FromResult(true); },
            recoveryEnabled: false);

        await coordinator.HandleAsync(new(18, PowerSignal.ResumeAutomatic, DateTimeOffset.UtcNow, 1, 1, 0, 0, false));

        Assert.Equal(0, baselineCalls);
        Assert.Equal(RecoverySafety.Unsafe, recovery.Current);
        Assert.Equal(PowerTransitionState.Unsafe, coordinator.State);
        Assert.False(gate.IsOpen);
    }

    [Fact]
    public async Task Resume_automatic_then_resume_suspend_reconciles_once()
    {
        var gate = new PowerMutationGate(true); var calls = 0;
        var coordinator = new PowerTransitionCoordinator(gate, new RecoverySafetyState(RecoverySafety.Safe), [],
            hasIncompleteRecovery: () => false,
            establishBaseline: _ => { calls++; return Task.FromResult(true); });
        await coordinator.HandleAsync(new(18, PowerSignal.ResumeAutomatic, DateTimeOffset.UtcNow, 1, 1, 0, 0, true));
        await coordinator.HandleAsync(new(7, PowerSignal.ResumeSuspend, DateTimeOffset.UtcNow, 2, 1, 1, 1, false));
        Assert.Equal(1, calls);
    }

    [Theory]
    [InlineData(18u, 7u)]
    [InlineData(7u, 18u)]
    public async Task Watcher_resume_pair_reconciles_once_and_leaves_gate_open(uint firstResume, uint secondResume)
    {
        var gate = new PowerMutationGate(true); var calls = 0; var source = new FakeSource(true);
        var coordinator = new PowerTransitionCoordinator(gate, new RecoverySafetyState(RecoverySafety.Safe), [],
            hasIncompleteRecovery: () => false,
            establishBaseline: _ => { Interlocked.Increment(ref calls); return Task.FromResult(true); });
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
        var coordinator = new PowerTransitionCoordinator(gate, new RecoverySafetyState(RecoverySafety.Safe), [],
            hasIncompleteRecovery: () => false,
            establishBaseline: _ => Task.FromResult(Interlocked.Increment(ref calls) > 1));
        using var watcher = new PowerTransitionWatcher(source, gate, coordinator, () => { }); Assert.True(watcher.Start());
        source.Raise(4); Assert.True(SpinWait.SpinUntil(() => coordinator.State == PowerTransitionState.Suspended, TimeSpan.FromSeconds(5)));
        source.Raise(18); Assert.True(SpinWait.SpinUntil(() => coordinator.State == PowerTransitionState.Unsafe, TimeSpan.FromSeconds(5))); var firstEpoch = gate.Epoch;
        source.Raise(4); Assert.True(SpinWait.SpinUntil(() => gate.Epoch > firstEpoch, TimeSpan.FromSeconds(5)));
        source.Raise(18); Assert.True(SpinWait.SpinUntil(() => coordinator.State == PowerTransitionState.Awake && gate.IsOpen, TimeSpan.FromSeconds(5))); Assert.Equal(2, calls);
    }

    [Fact]
    public async Task Stale_resume_recovery_cannot_reopen_gate_after_a_new_suspend()
    {
        var gate = new PowerMutationGate(true); var recovery = new RecoverySafetyState(RecoverySafety.Safe);
        var pendingBaseline = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var coordinator = new PowerTransitionCoordinator(gate, recovery, [],
            hasIncompleteRecovery: () => false,
            establishBaseline: _ => pendingBaseline.Task);
        var resume = coordinator.HandleAsync(new(18, PowerSignal.ResumeAutomatic, DateTimeOffset.UtcNow, 1, 1, 0, 0, true));
        Assert.True(SpinWait.SpinUntil(() => coordinator.State == PowerTransitionState.Recovering, TimeSpan.FromSeconds(5)));
        gate.EnterNewCycleBarrier(out _, out var newEpoch); coordinator.InvalidateForBarrier(); pendingBaseline.SetResult(true); await resume;
        Assert.False(gate.IsOpen); Assert.Equal(newEpoch, gate.Epoch); Assert.NotEqual(RecoverySafety.Safe, recovery.Current); Assert.NotEqual(PowerTransitionState.Awake, coordinator.State);
    }

    [Fact]
    public async Task Stale_failed_resume_cannot_overwrite_a_new_suspend_barrier_state()
    {
        var gate = new PowerMutationGate(true); var recovery = new RecoverySafetyState(RecoverySafety.Safe);
        var pendingBaseline = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var coordinator = new PowerTransitionCoordinator(gate, recovery, [],
            hasIncompleteRecovery: () => false,
            establishBaseline: _ => pendingBaseline.Task);
        var resume = coordinator.HandleAsync(new(18, PowerSignal.ResumeAutomatic, DateTimeOffset.UtcNow, 1, 1, 0, 0, true));
        Assert.True(SpinWait.SpinUntil(() => coordinator.State == PowerTransitionState.Recovering, TimeSpan.FromSeconds(5)));
        gate.EnterNewCycleBarrier(out _, out var newEpoch); coordinator.InvalidateForBarrier(); pendingBaseline.SetResult(false); await resume;
        Assert.Equal(newEpoch, gate.Epoch); Assert.False(gate.IsOpen); Assert.Equal(RecoverySafety.Indeterminate, recovery.Current); Assert.Equal(PowerTransitionState.Quiescing, coordinator.State);
    }

    [Fact]
    public async Task Stale_post_resume_failure_preserves_new_suspend_cleanup_window()
    {
        var gate = new PowerMutationGate(false);
        var recovery = new RecoverySafetyState(RecoverySafety.Unsafe);
        var afterResume = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var coordinator = new PowerTransitionCoordinator(gate, recovery, [],
            afterRecovery: _ => afterResume.Task,
            hasIncompleteRecovery: () => false,
            establishBaseline: _ => Task.FromResult(true));
        var resume = coordinator.HandleAsync(new(18, PowerSignal.ResumeAutomatic, DateTimeOffset.UtcNow, 1, 1, 0, 0, true));
        Assert.True(SpinWait.SpinUntil(() => gate.IsOpen, TimeSpan.FromSeconds(5)));

        gate.EnterNewCycleBarrier(out _, out var suspendEpoch);
        coordinator.InvalidateForBarrier();
        Assert.True(gate.TryAcquireCleanup(out _));
        afterResume.SetResult(false);
        await resume;

        Assert.Equal(suspendEpoch, gate.Epoch);
        Assert.Equal(PowerTransitionState.Quiescing, coordinator.State);
        Assert.Equal(RecoverySafety.Indeterminate, recovery.Current);
        Assert.False(gate.IsOpen);
        Assert.True(gate.TryAcquireCleanup(out _));
    }

    [Fact]
    public async Task Resume_observation_cannot_adopt_a_newer_suspend_epoch()
    {
        var gate = new PowerMutationGate(true);
        var recovery = new RecoverySafetyState(RecoverySafety.Safe);
        var reconcileCalls = 0;
        gate.EnterNewCycleBarrier(out _, out var resumeEpoch);
        await using var coordinator = new PowerTransitionCoordinator(
            gate,
            recovery,
            [],
            hasIncompleteRecovery: () => false,
            hasPreservedRoutingSession: () => true,
            reconcilePreservedRoutingSession: _ =>
            {
                reconcileCalls++;
                return Task.FromResult(true);
            });
        var observation = new PowerNotificationObservation(
            18, PowerSignal.ResumeAutomatic, DateTimeOffset.UtcNow, 1,
            Environment.CurrentManagedThreadId, resumeEpoch, resumeEpoch, false);

        gate.EnterNewCycleBarrier(out _, out var newerSuspendEpoch);
        coordinator.InvalidateForBarrier();
        await coordinator.HandleAsync(observation);

        Assert.Equal(0, reconcileCalls);
        Assert.Equal(newerSuspendEpoch, gate.Epoch);
        Assert.False(gate.IsOpen);
        Assert.Equal(PowerTransitionState.Quiescing, coordinator.State);
        Assert.Equal(RecoverySafety.Indeterminate, recovery.Current);
    }

    [Fact]
    public async Task Duplicate_suspend_does_not_advance_epoch_or_quiesce_twice()
    {
        var gate = new PowerMutationGate(true); var source = new FakeSource(true); var participant = new CountingParticipant();
        var coordinator = Coordinator(gate, participant); using var watcher = new PowerTransitionWatcher(source, gate, coordinator, () => { }); Assert.True(watcher.Start());
        await watcher.ObserveAsync(4); var epoch = gate.Epoch; await watcher.ObserveAsync(4);
        Assert.Equal(epoch, gate.Epoch); Assert.Equal(1, participant.QuiesceCount);
    }

    // Suspend notifications raised through PowerTransitionWatcher/FakeSource flow through the
    // coordinator's async channel and a background Task.Run reader, so there is real (if
    // normally small) scheduling delay between the notification's ObservedUtc and when
    // HandleAsync actually starts processing it. Production's 1200ms quiesce budget is tight
    // enough that CI thread-pool contention can burn through it before any participant is even
    // invoked, well before the fake (already-synchronous) participant would ever time out on its
    // own. Widen it here -- it's an upper bound, not an expected duration, so this does not slow
    // down passing runs.
    private static readonly TimeSpan TestSuspendQuiesceBudget = TimeSpan.FromSeconds(10);
    private static PowerTransitionCoordinator Coordinator(PowerMutationGate gate, params IPowerSuspendParticipant[] participants) => new(gate, new RecoverySafetyState(RecoverySafety.Safe), participants, suspendQuiesceBudget: TestSuspendQuiesceBudget);
    private sealed class FakeSource(bool succeeds) : IPowerSuspendResumeNotificationSource
    {
        public event Action<uint>? Notification;
        public bool TryRegister(out int nativeError) { nativeError = succeeds ? 0 : 5; return succeeds; }
        public void Raise(uint code) => Notification?.Invoke(code);
        public void Dispose() { }
    }
    private sealed class BlockingParticipant : IPowerSuspendParticipant
    {
        public string Name => "blocked";
        public TaskCompletionSource Completed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<bool> QuiesceForSuspendAsync(DateTimeOffset deadline, long cycle, long epoch, CancellationToken cancellationToken) { try { await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken); } catch (OperationCanceledException) { } Completed.TrySetResult(); return false; }
    }
    private sealed class CountingParticipant : IPowerSuspendParticipant
    {
        public string Name => "counting"; public int QuiesceCount;
        public Task<bool> QuiesceForSuspendAsync(DateTimeOffset deadline, long cycle, long epoch, CancellationToken cancellationToken) { Interlocked.Increment(ref QuiesceCount); return Task.FromResult(true); }
    }
    private sealed class ControllableParticipant : IPowerSuspendParticipant
    {
        public string Name => "controllable";
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<bool> QuiesceForSuspendAsync(DateTimeOffset deadline, long cycle, long epoch, CancellationToken cancellationToken)
        {
            Started.TrySetResult();
            return await Release.Task;
        }
    }
    private sealed class FailingParticipant : IPowerSuspendParticipant
    {
        public string Name => "failing";
        public Task<bool> QuiesceForSuspendAsync(DateTimeOffset deadline, long cycle, long epoch, CancellationToken cancellationToken) => Task.FromResult(false);
    }
}
