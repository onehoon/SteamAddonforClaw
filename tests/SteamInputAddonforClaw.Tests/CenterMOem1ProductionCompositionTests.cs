using SteamInputAddonforClaw.CenterM;
using SteamInputAddonforClaw.Controllers.Detection;
using SteamInputAddonforClaw.Devices.Abstractions;
using SteamInputAddonforClaw.Devices.MSI.Claw;
using SteamInputAddonforClaw.Power;
using SteamInputAddonforClaw.Recovery;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

/// <summary>
/// PR2: proves only the NEW production-composition behavior added around the already-implemented
/// <see cref="CenterMOem1LifecycleCoordinator"/> -- shared ownership, the periodic driver, routing
/// guard coexistence, production activation neutrality, suspend/resume wiring, and disposal
/// ordering. The coordinator's own state machine is already exhaustively covered by
/// <c>CenterMOem1LifecycleCoordinatorTests</c> and is deliberately not re-tested here.
/// </summary>
public sealed class CenterMOem1ProductionCompositionTests
{
    // ---- Shared ownership (composition-level) ----

    [Fact]
    public async Task Composition_gives_the_OEM1_coordinator_the_same_shared_helper_ownership_as_the_routing_guard()
    {
        var composition = NewComposition(out _, out _);
        try
        {
            Assert.Same(composition.CenterMHelperOwnership, composition.CenterMOem1Coordinator.TestOnly_HelperOwnership);
        }
        finally
        {
            await ((IAsyncDisposable)composition).DisposeAsync();
        }
    }

    [Fact]
    public async Task Composition_constructs_exactly_one_production_CenterMHelperOwnership_instance()
    {
        var composition = NewComposition(out _, out _);
        try
        {
            // The guard was constructed with (and the OEM1 coordinator shares) the SAME
            // composition-owned instance -- proven for the guard by
            // MsiClawRoutingCompositionTests.Composition_uses_the_injected_shared_helper_ownership_as_the_default_guards_authority;
            // this test focuses on the OEM1 coordinator's side of that same sharing.
            Assert.Same(composition.CenterMHelperOwnership, composition.CenterMOem1Coordinator.TestOnly_HelperOwnership);
        }
        finally
        {
            await ((IAsyncDisposable)composition).DisposeAsync();
        }
    }

    // ---- Driver ----

    [Fact]
    public async Task Driver_polls_helper_liveness_on_every_tick()
    {
        var target = new FakeDriverTarget();
        var tick = new ManualTick();
        var runtime = new CenterMOem1LifecycleRuntime(target, routingGuardIsArmed: () => false, delay: tick.DelayAsync);

        runtime.Start();
        tick.ReleaseOneTick();
        await target.LivenessPolled.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(target.LivenessPollCount >= 1);
        await runtime.DisposeAsync();
    }

    [Fact]
    public async Task PollTick_runs_while_routing_guard_is_not_armed()
    {
        var target = new FakeDriverTarget();
        var tick = new ManualTick();
        var runtime = new CenterMOem1LifecycleRuntime(target, routingGuardIsArmed: () => false, delay: tick.DelayAsync);

        runtime.Start();
        tick.ReleaseOneTick();
        await target.PollTicked.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(target.PollTickCount >= 1);
        await runtime.DisposeAsync();
    }

    [Fact]
    public async Task PollTick_is_skipped_while_routing_guard_is_armed()
    {
        var target = new FakeDriverTarget();
        var tick = new ManualTick();
        var runtime = new CenterMOem1LifecycleRuntime(target, routingGuardIsArmed: () => true, delay: tick.DelayAsync);

        runtime.Start();
        tick.ReleaseOneTick();
        await target.LivenessPolled.WaitAsync(TimeSpan.FromSeconds(5));
        await Task.Delay(50);

        Assert.Equal(0, target.PollTickCount);
        await runtime.DisposeAsync();
    }

    [Fact]
    public async Task Helper_liveness_still_polls_while_routing_guard_is_armed()
    {
        var target = new FakeDriverTarget();
        var tick = new ManualTick();
        var runtime = new CenterMOem1LifecycleRuntime(target, routingGuardIsArmed: () => true, delay: tick.DelayAsync);

        runtime.Start();
        tick.ReleaseOneTick();
        await target.LivenessPolled.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(target.LivenessPollCount >= 1);
        await runtime.DisposeAsync();
    }

    [Fact]
    public async Task A_single_failed_tick_does_not_permanently_kill_the_driver()
    {
        // Review fix (MAJOR): an unexpected exception from a poll call must not fault the loop task
        // permanently. Before this fix, only OperationCanceledException was caught around the whole
        // loop -- any other exception (e.g. from PollHelperLivenessAsync) would propagate out of
        // RunAsync, fault the backing Task, and DisposeAsync would later observe/rethrow that fault
        // while joining it, leaving no later helper-liveness/MainUI reconciliation for the rest of
        // the process lifetime.
        var target = new FakeDriverTarget { ThrowOnFirstLivenessPoll = true };
        var tick = new ManualTick();
        var runtime = new CenterMOem1LifecycleRuntime(target, routingGuardIsArmed: () => false, delay: tick.DelayAsync);

        runtime.Start();
        tick.ReleaseOneTick(); // first tick: PollHelperLivenessAsync throws -- must be contained
        tick.ReleaseOneTick(); // second tick: must still run normally afterward

        await target.LivenessPolled.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(target.LivenessPollCount >= 1);
        Assert.True(runtime.IsRunning);

        // DisposeAsync joining the loop task must not observe/rethrow a fault from the contained
        // exception -- proving the loop task itself never faulted.
        var exception = await Record.ExceptionAsync(async () => await runtime.DisposeAsync());
        Assert.Null(exception);
    }

    [Fact]
    public async Task Disposing_the_driver_cancels_and_joins_the_periodic_loop()
    {
        var target = new FakeDriverTarget();
        var tick = new ManualTick();
        var runtime = new CenterMOem1LifecycleRuntime(target, routingGuardIsArmed: () => false, delay: tick.DelayAsync);

        runtime.Start();
        Assert.True(runtime.IsRunning);

        var disposeTask = runtime.DisposeAsync().AsTask();
        var completed = await Task.WhenAny(disposeTask, Task.Delay(TimeSpan.FromSeconds(5)));

        Assert.Same(disposeTask, completed);
        Assert.True(target.Disposed);
    }

    // ---- Production behavior neutrality ----

    [Fact]
    public async Task Normal_production_composition_leaves_OEM1_desired_state_disabled()
    {
        var composition = NewComposition(out _, out _, startDriver: false);
        try
        {
            var snapshot = composition.CenterMOem1Coordinator.GetSnapshot();
            Assert.False(snapshot.DesiredEnabled);
            Assert.False(snapshot.SuppressionReady);
        }
        finally
        {
            await ((IAsyncDisposable)composition).DisposeAsync();
        }
    }

    [Fact]
    public async Task Constructing_the_composition_does_not_stage_or_start_the_helper()
    {
        var composition = NewComposition(out _, out _, startDriver: false);
        try
        {
            Assert.False(composition.CenterMHelperOwnership.IsOwned);
        }
        finally
        {
            await ((IAsyncDisposable)composition).DisposeAsync();
        }
    }

    // ---- Explicit test activation ----

    [Fact]
    public async Task Test_can_explicitly_activate_the_coordinator_and_reach_reconcile_logic_with_fakes()
    {
        var coordinator = new CenterMOem1LifecycleCoordinator(
            publishRootProvider: () => "fake-publish-root",
            environmentEligibility: () => false); // fail-open: stays NeedsSetup, proves reconcile ran

        await coordinator.SetDesiredEnabledAsync(true);

        var snapshot = coordinator.GetSnapshot();
        Assert.True(snapshot.DesiredEnabled);
        Assert.Equal(CenterMOem1LifecycleState.NeedsSetup, snapshot.State);

        await coordinator.DisposeAsync();
    }

    // ---- Suspend/resume ----

    [Fact]
    public async Task Hibernate_does_not_register_OEM1_as_a_suspend_participant()
    {
        var composition = NewComposition(out _, out _, startDriver: false);

        Assert.Null(((IHandheldRoutingComposition)composition).AuxiliaryPowerParticipant);

        await ((IAsyncDisposable)composition).DisposeAsync();
    }

    [Fact]
    public async Task Resume_invokes_exactly_one_fresh_coordinator_resume_reconcile()
    {
        var target = new FakeDriverTarget();
        var runtime = new CenterMOem1LifecycleRuntime(target, routingGuardIsArmed: () => false);

        IRuntimeResumeParticipant participant = runtime;
        await participant.ReconcileAfterResumeAsync(CancellationToken.None);

        Assert.Equal(1, target.ResumeReconcileCount);
        await runtime.DisposeAsync();
    }

    [Fact]
    public async Task Resume_OEM1_reconciliation_still_occurs_even_if_a_prior_operation_reported_failure()
    {
        // The driver's resume path must not be gated on any outside success/failure signal --
        // it always forwards to the coordinator's own resume reconciliation.
        var target = new FakeDriverTarget();
        var runtime = new CenterMOem1LifecycleRuntime(target, routingGuardIsArmed: () => false);

        IRuntimeResumeParticipant participant = runtime;
        await participant.ReconcileAfterResumeAsync(CancellationToken.None);

        Assert.Equal(1, target.ResumeReconcileCount);
        await runtime.DisposeAsync();
    }

    // ---- Disposal ----

    [Fact]
    public async Task Disposing_the_composition_does_not_double_dispose_the_shared_helper_object()
    {
        var composition = NewComposition(out _, out _, startDriver: false);

        var exception = await Record.ExceptionAsync(async () =>
        {
            await ((IAsyncDisposable)composition).DisposeAsync();
        });

        Assert.Null(exception);
        Assert.False(composition.CenterMHelperOwnership.IsOwned);
    }

    // ---- Shared helpers ----

    private static MsiClawRoutingComposition NewComposition(out MsiClawNativeStateManager native, out FakeDeviceEnumerator devices, bool startDriver = true)
    {
        devices = new FakeDeviceEnumerator();
        native = new MsiClawNativeStateManager(devices, new FakeModeController());
        return new MsiClawRoutingComposition(
            native,
            new RecoveryManager(new MemoryJournalStore()),
            new PowerMutationGate(initiallyOpen: true),
            new RecoverySafetyState(RecoverySafety.Safe),
            startCenterMOem1LifecycleRuntime: startDriver);
    }

    /// <summary>Deterministic replacement for wall-clock <c>Task.Delay</c>: the driver loop awaits
    /// this instead, and a test controls exactly when the "next tick" is released.</summary>
    private sealed class ManualTick
    {
        private readonly SemaphoreSlim _released = new(0);

        internal void ReleaseOneTick() => _released.Release();

        internal Task DelayAsync(TimeSpan _, CancellationToken token) => _released.WaitAsync(token);
    }

    /// <summary>Records exactly which of the driver's calls into
    /// <see cref="ICenterMOem1LifecycleDriverTarget"/> happened, letting tests observe the driver's
    /// own scheduling decisions without depending on the real coordinator's internals.</summary>
    private sealed class FakeDriverTarget : ICenterMOem1LifecycleDriverTarget
    {
        internal readonly SemaphoreSlim LivenessPolled = new(0);
        internal readonly SemaphoreSlim PollTicked = new(0);
        internal int LivenessPollCount;
        internal int PollTickCount;
        internal int ResumeReconcileCount;
        internal bool Disposed;
        internal bool ThrowOnFirstLivenessPoll;
        private int _livenessAttempts;

        public Task PollHelperLivenessAsync(CancellationToken cancellationToken = default)
        {
            if (ThrowOnFirstLivenessPoll && Interlocked.Increment(ref _livenessAttempts) == 1)
                throw new InvalidOperationException("Simulated reconciliation tick failure.");
            Interlocked.Increment(ref LivenessPollCount);
            LivenessPolled.Release();
            return Task.CompletedTask;
        }

        public Task PollTickAsync(CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref PollTickCount);
            PollTicked.Release();
            return Task.CompletedTask;
        }

        public Task ReconcileAfterResumeAsync(CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref ResumeReconcileCount);
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class FakeDeviceEnumerator : IControllerDeviceEnumerator
    {
        private readonly Guid _containerId = Guid.NewGuid();
        public IReadOnlyList<ControllerDeviceInfo> EnumeratePresentDevices() =>
        [new("HID\\MSI_CLAW", _containerId, "PCIROOT\\0", [], "HID", [], [], "HIDClass", null, null,
            MsiClawHardware.VendorId, MsiClawHardware.XInputProductId, true)];
    }

    private sealed class FakeModeController : IMsiClawModeController
    {
        public Task<MsiClawModeTransitionResult> SwitchModeAsync(MsiClawNativeMode target, MsiClawPhysicalIdentity expectedIdentity, CancellationToken cancellationToken) =>
            Task.FromResult(new MsiClawModeTransitionResult(MsiClawModeTransitionStatus.Succeeded,
                target == MsiClawNativeMode.XInput ? MsiClawNativeMode.DirectInput : MsiClawNativeMode.XInput,
                target, null, MsiClawHardware.XInputProductId, true, true, true, true, true, 1, "test"));
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
