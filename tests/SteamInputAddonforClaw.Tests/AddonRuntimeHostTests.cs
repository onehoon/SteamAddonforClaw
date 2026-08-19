using SteamInputAddonforClaw.Controllers.Detection;
using SteamInputAddonforClaw.Devices;
using SteamInputAddonforClaw.Devices.Abstractions;
using SteamInputAddonforClaw.Devices.MSI.Claw;
using SteamInputAddonforClaw.Lifecycle;
using SteamInputAddonforClaw.Power;
using SteamInputAddonforClaw.Recovery;
using SteamInputAddonforClaw.Routing;
using SteamInputAddonforClaw.Runtime;
using SteamInputAddonforClaw.Settings;
using SteamInputAddonforClaw.Status;
using SteamInputAddonforClaw.Steam;
using SteamInputAddonforClaw.VirtualOutput.Viiper;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class AddonRuntimeHostTests
{
    [Theory]
    [InlineData("FinalStop", false)]
    [InlineData("CallbackClear", false)]
    [InlineData("Success", true)]
    public async Task Host_shutdown_disposes_routing_backend_only_after_canonical_success(string failureClass, bool shutdownSucceeded)
    {
        using var steamRuntime = new SteamSessionRuntime(new FakeSteamInputRoutingPreference());
        var disposed = 0;
        var host = new AddonRuntimeHost(steamRuntime, routingRuntime: null,
            new PowerMutationGate(initiallyOpen: true), new RecoverySafetyState(RecoverySafety.Safe), true,
            () => false, _ => Task.FromResult(false), routingShutdownOverride: () => Task.FromResult(shutdownSucceeded),
            routingDisposeOverride: () => { Assert.True(failureClass is "FinalStop" or "CallbackClear" or "Success"); disposed++; return ValueTask.CompletedTask; });

        await host.DisposeAsync();

        Assert.Equal(shutdownSucceeded ? 1 : 0, disposed);
    }

    [Fact]
    public async Task Host_with_unavailable_routing_remains_valid_and_passive()
    {
        using var steamRuntime = new SteamSessionRuntime(new FakeSteamInputRoutingPreference());
        var host = new AddonRuntimeHost(steamRuntime, routingRuntime: null,
            new PowerMutationGate(initiallyOpen: true), new RecoverySafetyState(RecoverySafety.Safe), recoverySafe: true,
            hasIncompleteRecovery: () => false, establishBaseline: _ => Task.FromResult(false));

        Assert.Equal(RoutingRuntimeStatusSnapshot.Unavailable, host.CaptureRoutingStatus());
        Assert.True(host.EvaluateUserTermination().CanTerminate);

        // No fallback routing backend must appear, and normal reconcile must not throw.
        await host.ReconcileAsync();

        await host.DisposeAsync();
    }

    [Fact]
    public async Task Host_republishes_Steam_state_transitions_to_subscribers()
    {
        using var steamRuntime = new SteamSessionRuntime(new FakeSteamInputRoutingPreference());
        var host = new AddonRuntimeHost(steamRuntime, routingRuntime: null,
            new PowerMutationGate(initiallyOpen: true), new RecoverySafetyState(RecoverySafety.Safe), recoverySafe: true,
            hasIncompleteRecovery: () => false, establishBaseline: _ => Task.FromResult(false));
        SteamSessionStateChangedEventArgs? observed = null;
        host.SteamSessionStateChanged += (_, args) => observed = args;

        steamRuntime.DeveloperTestModeState.SetEnabled(true);

        Assert.NotNull(observed);
        Assert.Equal(SteamSessionSource.DeveloperTest, observed.Current.Source);

        await host.DisposeAsync();
    }

    [Fact]
    public async Task Steam_state_transition_drives_exactly_one_normal_reconcile_and_exactly_one_status_refresh()
    {
        using var steamRuntime = new SteamSessionRuntime(new FakeSteamInputRoutingPreference());
        var statusProvider = new FakeStatusProvider(Snapshot(WaitingForSteam()));
        var powerGate = new PowerMutationGate(initiallyOpen: true);
        var recoverySafetyState = new RecoverySafetyState(RecoverySafety.Safe);
        var routingRuntime = CreateRoutingRuntime(statusProvider, powerGate, recoverySafetyState);
        Assert.NotNull(routingRuntime);

        var host = new AddonRuntimeHost(steamRuntime, routingRuntime,
            powerGate, recoverySafetyState, recoverySafe: true,
            hasIncompleteRecovery: () => false, establishBaseline: _ => Task.FromResult(false));
        var refreshCount = 0;
        var refreshRequested = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        host.StatusRefreshRequested += (_, _) =>
        {
            Interlocked.Increment(ref refreshCount);
            refreshRequested.TrySetResult();
        };

        try
        {
            // The state-change handler triggers the normal reconcile fire-and-forget; wait for
            // ReconcileSafelyAsync's finally-guaranteed status refresh rather than a fixed delay,
            // then settle briefly to catch a duplicate refresh/reconcile a bug could still cause.
            steamRuntime.DeveloperTestModeState.SetEnabled(true);
            await refreshRequested.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await Task.Delay(TimeSpan.FromMilliseconds(50));

            Assert.Equal(1, refreshCount);
            Assert.Equal(1, statusProvider.CaptureCount);
        }
        finally
        {
            await host.DisposeAsync();
        }
    }

    [Fact]
    public async Task ReconcileFreshAfterResumeAsync_reconciles_exactly_once_refreshes_status_exactly_once_and_does_not_leave_suppression_stuck()
    {
        using var steamRuntime = new SteamSessionRuntime(new FakeSteamInputRoutingPreference());
        var statusProvider = new FakeStatusProvider(Snapshot(WaitingForSteam()));
        var powerGate = new PowerMutationGate(initiallyOpen: true);
        var recoverySafetyState = new RecoverySafetyState(RecoverySafety.Safe);
        var routingRuntime = CreateRoutingRuntime(statusProvider, powerGate, recoverySafetyState);
        Assert.NotNull(routingRuntime);

        var source = new FakeSource(succeeds: true);
        var host = new AddonRuntimeHost(steamRuntime, routingRuntime,
            powerGate, recoverySafetyState, recoverySafe: true,
            hasIncompleteRecovery: () => false, establishBaseline: _ => Task.FromResult(true), notificationSource: source);
        var refreshCount = 0;
        host.StatusRefreshRequested += (_, _) => Interlocked.Increment(ref refreshCount);
        host.StartPowerObservation();

        try
        {
            // Drive a real suspend/resume pair through the notification source -- the same
            // production path App used to wire up manually -- and confirm the resume fresh
            // reconcile (Begin -> ExecuteExplicitRefresh -> RunResumeFreshAsync -> Complete)
            // reaches Host's routing runtime exactly once, with exactly one status refresh.
            await source.RaiseAsync(4);
            await source.RaiseAsync(18);

            Assert.True(SpinWait.SpinUntil(() => statusProvider.CaptureCount >= 1, TimeSpan.FromSeconds(5)));
            await Task.Delay(TimeSpan.FromMilliseconds(50));

            Assert.Equal(1, refreshCount);
            Assert.Equal(1, statusProvider.CaptureCount);

            // Prove suppression was actually released (Complete() correctly wired) rather than
            // left permanently "owned": a real Steam transition occurring strictly after resume
            // completes must still reach a normal reconcile, not be silently suppressed forever.
            var postResumeRefresh = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            host.StatusRefreshRequested += (_, _) => postResumeRefresh.TrySetResult();
            steamRuntime.DeveloperTestModeState.SetEnabled(true);
            await postResumeRefresh.Task.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Equal(2, refreshCount);
            Assert.Equal(2, statusProvider.CaptureCount);
        }
        finally
        {
            await host.DisposeAsync();
        }
    }

    [Fact]
    public async Task Steam_transition_during_the_fresh_resume_reconcile_suppression_window_is_deferred_and_replayed_exactly_once()
    {
        // C5b2 regression, re-proven through the C5c Host-owned power boundary: a real Steam
        // state transition that lands strictly between the fresh resume reconcile's
        // ResumeFreshReconcileSuppression.Begin() and Complete() must be deferred (not fired
        // immediately, not dropped), then replayed as exactly one normal reconcile once the fresh
        // reconcile finishes -- never two independent/overlapping reconciles.
        using var steamRuntime = new SteamSessionRuntime(new FakeSteamInputRoutingPreference());
        var powerGate = new PowerMutationGate(initiallyOpen: true);
        var recoverySafetyState = new RecoverySafetyState(RecoverySafety.Safe);
        var statusProvider = new BlockingStatusProvider(Snapshot(WaitingForSteam()));
        var routingRuntime = CreateRoutingRuntime(statusProvider, powerGate, recoverySafetyState);
        Assert.NotNull(routingRuntime);

        var source = new FakeSource(succeeds: true);
        var host = new AddonRuntimeHost(steamRuntime, routingRuntime,
            powerGate, recoverySafetyState, recoverySafe: true,
            hasIncompleteRecovery: () => false, establishBaseline: _ => Task.FromResult(true), notificationSource: source);
        var refreshCount = 0;
        host.StatusRefreshRequested += (_, _) => Interlocked.Increment(ref refreshCount);
        host.StartPowerObservation();

        try
        {
            await source.RaiseAsync(4);
            await source.RaiseAsync(18);

            // The fresh reconcile has called Begin() and is now blocked inside its own status
            // capture -- exactly the window ResumeFreshReconcileSuppression exists to guard.
            await statusProvider.FirstCaptureStarted.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(1, statusProvider.CaptureCount);

            // A real Steam transition landing in that window must be suppressed (deferred), not
            // fire an extra, overlapping reconcile while the fresh one is still in flight.
            steamRuntime.DeveloperTestModeState.SetEnabled(true);
            Assert.Equal(1, statusProvider.CaptureCount);

            statusProvider.ReleaseFirstCapture();

            // Completion of the fresh reconcile must replay the deferred transition as exactly
            // one normal reconcile.
            Assert.True(SpinWait.SpinUntil(() => refreshCount >= 2, TimeSpan.FromSeconds(5)));
            await Task.Delay(TimeSpan.FromMilliseconds(50));

            Assert.Equal(2, statusProvider.CaptureCount);
            Assert.Equal(2, refreshCount);
        }
        finally
        {
            await host.DisposeAsync();
        }
    }

    [Fact]
    public async Task Resume_reconciles_the_OEM1_auxiliary_participant_before_and_independent_of_a_throwing_routing_reconcile()
    {
        // Review fix (BLOCKER): the OEM1 lifecycle's resume reconciliation must run BEFORE routing
        // re-enters, and independent of whether routing's own fresh reconcile throws --
        // RunResumeFreshAsync rethrows on failure, so before this fix the auxiliary participant was
        // never reached at all on this path. Drives the real production MSI Claw composition (real
        // CenterMOem1LifecycleCoordinator, real CenterMOem1LifecycleRuntime) through a real suspend/
        // resume pair, exactly like the existing resume tests above, but with a status provider that
        // throws during routing's own fresh reconcile.
        using var steamRuntime = new SteamSessionRuntime(new FakeSteamInputRoutingPreference());
        var statusProvider = new ThrowingStatusProvider();
        var powerGate = new PowerMutationGate(initiallyOpen: true);
        var recoverySafetyState = new RecoverySafetyState(RecoverySafety.Safe);
        var routingRuntime = CreateRoutingRuntime(statusProvider, powerGate, recoverySafetyState);
        Assert.NotNull(routingRuntime);
        var oem1Coordinator = ((MsiClawRoutingComposition)routingRuntime.TestOnly_Composition).CenterMOem1Coordinator;

        var source = new FakeSource(succeeds: true);
        var host = new AddonRuntimeHost(steamRuntime, routingRuntime,
            powerGate, recoverySafetyState, recoverySafe: true,
            hasIncompleteRecovery: () => false, establishBaseline: _ => Task.FromResult(true), notificationSource: source);
        host.StartPowerObservation();

        try
        {
            // Suspend first (the OEM1 auxiliary power participant's own QuiesceForSuspendAsync sets
            // LastReason to "SuspendQuiesced" -- deterministic ground truth before resume even
            // starts, so the check below can unambiguously attribute a later "ResumeReconcile" to
            // the resume path specifically, not to suspend).
            await source.RaiseAsync(4);
            Assert.True(SpinWait.SpinUntil(() => oem1Coordinator.GetSnapshot().LastReason == "SuspendQuiesced", TimeSpan.FromSeconds(5)));

            await source.RaiseAsync(18);

            // Routing's own fresh reconcile throws (ThrowingStatusProvider), which would previously
            // have prevented the auxiliary participant from ever being invoked at all (RunResumeFreshAsync
            // rethrows on failure). It must still run -- observable via the coordinator's own
            // internal resume reconcile reason -- and the host must not crash/propagate that
            // exception out of power notification handling (both notifications above completed
            // without this test itself throwing).
            Assert.True(SpinWait.SpinUntil(() => oem1Coordinator.GetSnapshot().LastReason == "ResumeReconcile", TimeSpan.FromSeconds(5)));

            // Confirms routing's own fresh reconcile was genuinely attempted (and threw) on this
            // same resume -- proving this is a real independence test, not a vacuous one.
            Assert.True(SpinWait.SpinUntil(() => statusProvider.WasCalled, TimeSpan.FromSeconds(5)));
        }
        finally
        {
            await host.DisposeAsync();
        }
    }

    [Fact]
    public async Task StartPowerObservation_opens_the_gate_when_registration_succeeds_and_recovery_is_safe()
    {
        using var steamRuntime = new SteamSessionRuntime(new FakeSteamInputRoutingPreference());
        var source = new FakeSource(succeeds: true);
        var powerGate = new PowerMutationGate();
        var host = new AddonRuntimeHost(steamRuntime, routingRuntime: null,
            powerGate, new RecoverySafetyState(RecoverySafety.Safe), recoverySafe: true,
            hasIncompleteRecovery: () => false, establishBaseline: _ => Task.FromResult(false), notificationSource: source);

        host.StartPowerObservation();

        Assert.Equal(1, source.RegisterCallCount);
        Assert.True(powerGate.IsOpen);

        await host.DisposeAsync();
    }

    [Fact]
    public async Task StartPowerObservation_leaves_the_gate_closed_when_registration_fails()
    {
        using var steamRuntime = new SteamSessionRuntime(new FakeSteamInputRoutingPreference());
        var source = new FakeSource(succeeds: false);
        var powerGate = new PowerMutationGate();
        var host = new AddonRuntimeHost(steamRuntime, routingRuntime: null,
            powerGate, new RecoverySafetyState(RecoverySafety.Safe), recoverySafe: true,
            hasIncompleteRecovery: () => false, establishBaseline: _ => Task.FromResult(false), notificationSource: source);

        host.StartPowerObservation();

        Assert.False(powerGate.IsOpen);

        await host.DisposeAsync();
    }

    [Fact]
    public async Task StartPowerObservation_leaves_the_gate_closed_when_recovery_is_unsafe_even_if_registration_succeeds()
    {
        using var steamRuntime = new SteamSessionRuntime(new FakeSteamInputRoutingPreference());
        var source = new FakeSource(succeeds: true);
        var powerGate = new PowerMutationGate();
        var host = new AddonRuntimeHost(steamRuntime, routingRuntime: null,
            powerGate, new RecoverySafetyState(RecoverySafety.Unsafe), recoverySafe: false,
            hasIncompleteRecovery: () => false, establishBaseline: _ => Task.FromResult(false), notificationSource: source);

        host.StartPowerObservation();

        Assert.False(powerGate.IsOpen);

        await host.DisposeAsync();
    }

    [Fact]
    public async Task EvaluateUserTermination_blocks_on_owned_live_recovery_mutation()
    {
        using var steamRuntime = new SteamSessionRuntime(new FakeSteamInputRoutingPreference());
        var recoverySafetyState = new RecoverySafetyState(RecoverySafety.Safe);
        var host = new AddonRuntimeHost(steamRuntime, routingRuntime: null,
            new PowerMutationGate(initiallyOpen: true), recoverySafetyState, recoverySafe: true,
            hasIncompleteRecovery: () => true, establishBaseline: _ => Task.FromResult(false));

        // recoverySafetyState.Current == Safe && hasIncompleteRecovery() == true -- the exact
        // existing conjunction UserTerminationGuard uses for RecoveryMutationOwned.
        var decision = host.EvaluateUserTermination();

        Assert.False(decision.CanTerminate);
        Assert.Equal(UserTerminationBlockReason.RecoveryMutationOwned, decision.Reason);

        await host.DisposeAsync();
    }

    [Fact]
    public async Task DisposeAsync_is_idempotent_and_a_post_disposal_notification_does_not_reenter_runtime_work()
    {
        using var steamRuntime = new SteamSessionRuntime(new FakeSteamInputRoutingPreference());
        var statusProvider = new FakeStatusProvider(Snapshot(WaitingForSteam()));
        var powerGate = new PowerMutationGate(initiallyOpen: true);
        var recoverySafetyState = new RecoverySafetyState(RecoverySafety.Safe);
        var routingRuntime = CreateRoutingRuntime(statusProvider, powerGate, recoverySafetyState);
        Assert.NotNull(routingRuntime);

        var source = new FakeSource(succeeds: true);
        var host = new AddonRuntimeHost(steamRuntime, routingRuntime,
            powerGate, recoverySafetyState, recoverySafe: true,
            hasIncompleteRecovery: () => false, establishBaseline: _ => Task.FromResult(false), notificationSource: source);
        host.StartPowerObservation();

        await host.DisposeAsync();
        await host.DisposeAsync(); // must not throw

        // A notification arriving after full disposal (PowerTransitionWatcher itself disposed)
        // must not re-enter routing/Steam work.
        await source.RaiseAsync(4);
        await Task.Delay(TimeSpan.FromMilliseconds(50));

        Assert.Equal(0, statusProvider.CaptureCount);
    }

    [Fact]
    public async Task Resume_notification_processed_after_PrepareForShutdown_does_not_touch_the_disposed_Steam_runtime()
    {
        var steamRuntime = new SteamSessionRuntime(new FakeSteamInputRoutingPreference());
        var statusProvider = new FakeStatusProvider(Snapshot(WaitingForSteam()));
        var powerGate = new PowerMutationGate(initiallyOpen: true);
        var recoverySafetyState = new RecoverySafetyState(RecoverySafety.Safe);
        var routingRuntime = CreateRoutingRuntime(statusProvider, powerGate, recoverySafetyState);
        Assert.NotNull(routingRuntime);

        var source = new FakeSource(succeeds: true);
        var host = new AddonRuntimeHost(steamRuntime, routingRuntime,
            powerGate, recoverySafetyState, recoverySafe: true,
            hasIncompleteRecovery: () => false, establishBaseline: _ => Task.FromResult(true), notificationSource: source);
        host.StartPowerObservation();

        // Simulates a resume notification already queued in PowerTransitionCoordinator before
        // App-level shutdown began, running only after Steam observation has been stopped (and
        // the owned SteamSessionRuntime disposed). This must not throw ObjectDisposedException --
        // the Steam refresh is skipped, and fresh routing reconciliation still completes.
        host.PrepareForShutdown();

        await source.RaiseAsync(4);
        await source.RaiseAsync(18);

        Assert.True(SpinWait.SpinUntil(() => statusProvider.CaptureCount >= 1, TimeSpan.FromSeconds(5)));

        await host.DisposeAsync();
    }

    /// <summary>Accepts the same PowerMutationGate/RecoverySafetyState instances the caller passes
    /// to AddonRuntimeHost, matching production composition where App constructs both once and
    /// threads them into AddonRoutingRuntime.Create and AddonRuntimeHost's constructor.</summary>
    private static AddonRoutingRuntime? CreateRoutingRuntime(ISystemStatusProvider statusProvider, PowerMutationGate powerGate, RecoverySafetyState recoverySafetyState) => AddonRoutingRuntime.Create(
        new MsiClawDeviceAdapter(new EmptyDeviceEnumerator()),
        statusProvider,
        new AddonOwnedVirtualDeviceTracker(),
        new RecoveryManager(new MemoryJournalStore()),
        powerGate,
        recoverySafetyState,
        new DefaultOem1MappingPreference(),
        hardwareSupported: true);

    private sealed class FakeSteamInputRoutingPreference : ISteamInputRoutingPreference
    {
        public bool SteamInputRoutingEnabled => true;
        public event EventHandler? SteamInputRoutingEnabledChanged { add { } remove { } }
    }

    private sealed class EmptyDeviceEnumerator : IControllerDeviceEnumerator
    {
        public IReadOnlyList<ControllerDeviceInfo> EnumeratePresentDevices() => [];
    }

    /// <summary>Always throws from CaptureAsync -- simulates routing's own fresh resume reconcile
    /// failing unexpectedly, to prove the OEM1 auxiliary resume participant still runs (and runs
    /// first) independent of that failure.</summary>
    private sealed class ThrowingStatusProvider : ISystemStatusProvider
    {
        private int _wasCalled;
        internal bool WasCalled => Volatile.Read(ref _wasCalled) != 0;

        public Task<SystemStatusSnapshot> CaptureAsync(CancellationToken cancellationToken = default)
        {
            Volatile.Write(ref _wasCalled, 1);
            throw new InvalidOperationException("Simulated routing status capture failure.");
        }
    }

    private sealed class FakeStatusProvider(SystemStatusSnapshot? snapshot = null) : ISystemStatusProvider
    {
        private int _captureCount;
        internal int CaptureCount => Volatile.Read(ref _captureCount);

        public Task<SystemStatusSnapshot> CaptureAsync(CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _captureCount);
            return Task.FromResult(snapshot ?? throw new InvalidOperationException("Not exercised."));
        }
    }

    /// <summary>A status provider whose first CaptureAsync call blocks until released, so a test
    /// can trigger work strictly inside a reconcile's in-flight window rather than approximating
    /// it with a fixed delay.</summary>
    private sealed class BlockingStatusProvider(SystemStatusSnapshot snapshot) : ISystemStatusProvider
    {
        private readonly TaskCompletionSource _firstCaptureStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _releaseFirstCapture = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _captureCount;
        internal int CaptureCount => Volatile.Read(ref _captureCount);
        internal Task FirstCaptureStarted => _firstCaptureStarted.Task;
        internal void ReleaseFirstCapture() => _releaseFirstCapture.TrySetResult();

        public async Task<SystemStatusSnapshot> CaptureAsync(CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref _captureCount) == 1)
            {
                _firstCaptureStarted.TrySetResult();
                await _releaseFirstCapture.Task.ConfigureAwait(false);
            }
            return snapshot;
        }
    }

    /// <summary>Mirrors PowerTransitionTests.cs's FakeSource: a real IPowerSuspendResumeNotificationSource
    /// so resume/suspend wiring is exercised through the same production path App used to wire up
    /// manually, not a bespoke Host-only shortcut.</summary>
    private sealed class FakeSource(bool succeeds) : IPowerSuspendResumeNotificationSource
    {
        private int _registerCallCount;
        internal int RegisterCallCount => Volatile.Read(ref _registerCallCount);

        public event Action<uint>? Notification;
        public bool TryRegister(out int nativeError) { Interlocked.Increment(ref _registerCallCount); nativeError = succeeds ? 0 : 5; return succeeds; }
        public void Raise(uint code) => Notification?.Invoke(code);
        public Task RaiseAsync(uint code) { Raise(code); return Task.CompletedTask; }
        public void Dispose() { }
    }

    private static SystemStatusSnapshot Snapshot(RoutingDecision decision) =>
        new(new("Test", "Test", "Test", []), null!, [], null!, null!, null!, decision, null!, true, false);

    private static RoutingDecision WaitingForSteam() => new(RoutingDecisionKind.WaitingForSteam, RoutingDecisionReason.SteamInactive);

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
        public bool CenterMAutoRunMutationPending => false;
        public event EventHandler? Oem1MappingChanged { add { } remove { } }
    }
}
