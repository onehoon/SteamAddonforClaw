using SteamInputAddonforClaw.Controllers.Detection;
using SteamInputAddonforClaw.Devices;
using SteamInputAddonforClaw.Devices.Abstractions;
using SteamInputAddonforClaw.Devices.MSI.Claw;
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
    [Fact]
    public async Task Host_with_unavailable_routing_remains_valid_and_passive()
    {
        using var steamRuntime = new SteamSessionRuntime(new FakeBigPicturePreference());
        var host = new AddonRuntimeHost(steamRuntime, routingRuntime: null);

        Assert.False(host.IsRoutingAvailable);
        Assert.Equal(RoutingRuntimeStatusSnapshot.Unavailable, host.CaptureRoutingStatus());
        Assert.False(host.IsSafetySessionActive);
        Assert.False(host.HasOwnedRecoveryBoundary);
        Assert.False(host.HasResidualSessionState);

        // No fallback routing backend must appear, and neither operation must throw.
        await host.ReconcileAsync();
        Assert.True(await host.ReconcileFreshAfterResumeAsync(CancellationToken.None));

        await host.ShutdownRoutingAsync();
        await host.DisposeAsync();
    }

    [Fact]
    public async Task Host_republishes_Steam_state_transitions_to_subscribers()
    {
        using var steamRuntime = new SteamSessionRuntime(new FakeBigPicturePreference());
        var host = new AddonRuntimeHost(steamRuntime, routingRuntime: null);
        SteamSessionStateChangedEventArgs? observed = null;
        host.SteamSessionStateChanged += (_, args) => observed = args;

        steamRuntime.DeveloperTestModeState.SetEnabled(true);

        Assert.NotNull(observed);
        Assert.Equal(SteamSessionSource.DeveloperTest, observed.Current.Source);

        await host.ShutdownRoutingAsync();
        await host.DisposeAsync();
    }

    [Fact]
    public async Task Steam_state_transition_drives_exactly_one_normal_reconcile_and_exactly_one_status_refresh()
    {
        using var steamRuntime = new SteamSessionRuntime(new FakeBigPicturePreference());
        var statusProvider = new FakeStatusProvider(Snapshot(WaitingForSteam()));
        var routingRuntime = AddonRoutingRuntime.Create(
            new MsiClawDeviceAdapter(new EmptyDeviceEnumerator()),
            statusProvider,
            new AddonOwnedVirtualDeviceTracker(),
            new RecoveryManager(new MemoryJournalStore()),
            new PowerMutationGate(initiallyOpen: true),
            new RecoverySafetyState(RecoverySafety.Safe));
        Assert.NotNull(routingRuntime);

        var host = new AddonRuntimeHost(steamRuntime, routingRuntime);
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
            await host.ShutdownRoutingAsync();
            await host.DisposeAsync();
        }
    }

    [Fact]
    public async Task ReconcileFreshAfterResumeAsync_reconciles_exactly_once_refreshes_status_exactly_once_and_does_not_leave_suppression_stuck()
    {
        using var steamRuntime = new SteamSessionRuntime(new FakeBigPicturePreference());
        var statusProvider = new FakeStatusProvider(Snapshot(WaitingForSteam()));
        var routingRuntime = AddonRoutingRuntime.Create(
            new MsiClawDeviceAdapter(new EmptyDeviceEnumerator()),
            statusProvider,
            new AddonOwnedVirtualDeviceTracker(),
            new RecoveryManager(new MemoryJournalStore()),
            new PowerMutationGate(initiallyOpen: true),
            new RecoverySafetyState(RecoverySafety.Safe));
        Assert.NotNull(routingRuntime);

        var host = new AddonRuntimeHost(steamRuntime, routingRuntime);
        var refreshCount = 0;
        host.StatusRefreshRequested += (_, _) => Interlocked.Increment(ref refreshCount);

        try
        {
            // No Steam transition occurs during this window (nothing toggles state), so the
            // resume path itself must not fabricate an extra reconcile or refresh: exactly the
            // fresh reconcile (Begin -> ExecuteExplicitRefresh -> RunResumeFreshAsync -> Complete)
            // and its own single finally-guaranteed status refresh, nothing more -- proving
            // Begin/ExecuteExplicitRefresh/Complete/deferredReconcile are wired without an extra
            // hop and without double-refreshing.
            var succeeded = await host.ReconcileFreshAfterResumeAsync(CancellationToken.None);

            Assert.True(succeeded);
            Assert.Equal(1, refreshCount);
            Assert.Equal(1, statusProvider.CaptureCount);

            // Prove suppression was actually released (Complete() correctly wired to the real
            // ResumeFreshReconcileSuppression instance) rather than left permanently "owned": a
            // real Steam transition occurring strictly after resume completes must still reach a
            // normal reconcile, not be silently suppressed forever.
            var postResumeRefresh = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            host.StatusRefreshRequested += (_, _) => postResumeRefresh.TrySetResult();
            steamRuntime.DeveloperTestModeState.SetEnabled(true);
            await postResumeRefresh.Task.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Equal(2, refreshCount);
            Assert.Equal(2, statusProvider.CaptureCount);
        }
        finally
        {
            await host.ShutdownRoutingAsync();
            await host.DisposeAsync();
        }
    }

    [Fact]
    public async Task ReconcileFreshAfterResumeAsync_suppresses_a_transition_during_the_window_then_runs_exactly_one_deferred_reconcile()
    {
        using var steamRuntime = new SteamSessionRuntime(new FakeBigPicturePreference());
        var statusProvider = new SlowFirstCaptureStatusProvider(Snapshot(WaitingForSteam()));
        var routingRuntime = AddonRoutingRuntime.Create(
            new MsiClawDeviceAdapter(new EmptyDeviceEnumerator()),
            statusProvider,
            new AddonOwnedVirtualDeviceTracker(),
            new RecoveryManager(new MemoryJournalStore()),
            new PowerMutationGate(initiallyOpen: true),
            new RecoverySafetyState(RecoverySafety.Safe));
        Assert.NotNull(routingRuntime);

        var host = new AddonRuntimeHost(steamRuntime, routingRuntime);
        var refreshCount = 0;
        var secondRefresh = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        host.StatusRefreshRequested += (_, _) =>
        {
            if (Interlocked.Increment(ref refreshCount) == 2) secondRefresh.TrySetResult();
        };

        try
        {
            var resumeTask = host.ReconcileFreshAfterResumeAsync(CancellationToken.None);

            // By the time the fresh reconcile's first status capture has started, Begin() and
            // ExecuteExplicitRefresh() have already run synchronously -- suppression is owned.
            // A Steam transition now must be suppressed as "pending", not cause an immediate
            // extra normal reconcile.
            await statusProvider.FirstCaptureStarted.WaitAsync(TimeSpan.FromSeconds(5));
            steamRuntime.DeveloperTestModeState.SetEnabled(true);
            statusProvider.ReleaseFirstCapture();

            Assert.True(await resumeTask);

            // The suppressed transition must produce exactly one deferred normal reconcile once
            // the fresh reconcile completes -- fresh (1) + deferred (1) = 2, no extra reconcile.
            await secondRefresh.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await Task.Delay(TimeSpan.FromMilliseconds(50));

            Assert.Equal(2, refreshCount);
            Assert.Equal(2, statusProvider.CaptureCount);
        }
        finally
        {
            await host.ShutdownRoutingAsync();
            await host.DisposeAsync();
        }
    }

    [Fact]
    public async Task ReconcileFreshAfterResumeAsync_after_StopSteamObservation_does_not_touch_the_disposed_Steam_runtime()
    {
        var steamRuntime = new SteamSessionRuntime(new FakeBigPicturePreference());
        var statusProvider = new FakeStatusProvider(Snapshot(WaitingForSteam()));
        var routingRuntime = AddonRoutingRuntime.Create(
            new MsiClawDeviceAdapter(new EmptyDeviceEnumerator()),
            statusProvider,
            new AddonOwnedVirtualDeviceTracker(),
            new RecoveryManager(new MemoryJournalStore()),
            new PowerMutationGate(initiallyOpen: true),
            new RecoverySafetyState(RecoverySafety.Safe));
        Assert.NotNull(routingRuntime);

        var host = new AddonRuntimeHost(steamRuntime, routingRuntime);

        // Simulates a resume notification already queued in PowerTransitionCoordinator before
        // shutdown began, running only after App has stopped Steam observation (and disposed the
        // owned SteamSessionRuntime). ReconcileFreshAfterResumeAsync must not throw
        // ObjectDisposedException -- it must skip the Steam refresh and still complete fresh
        // routing reconciliation.
        host.StopSteamObservation();

        var succeeded = await host.ReconcileFreshAfterResumeAsync(CancellationToken.None);

        Assert.True(succeeded);
        Assert.Equal(1, statusProvider.CaptureCount);

        await host.ShutdownRoutingAsync();
        await host.DisposeAsync();
    }

    private sealed class FakeBigPicturePreference : ISteamBigPictureRoutingPreference
    {
        public bool RouteInSteamBigPicture => false;
        public event EventHandler? RouteInSteamBigPictureChanged { add { } remove { } }
    }

    private sealed class EmptyDeviceEnumerator : IControllerDeviceEnumerator
    {
        public IReadOnlyList<ControllerDeviceInfo> EnumeratePresentDevices() => [];
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

    /// <summary>Blocks the first CaptureAsync call until released, so a test can deterministically
    /// interject an action after the routing coordinator's fresh reconcile has actually started
    /// (i.e. after AddonRuntimeHost's synchronous Begin()/ExecuteExplicitRefresh have already run)
    /// without an arbitrary delay.</summary>
    private sealed class SlowFirstCaptureStatusProvider(SystemStatusSnapshot snapshot) : ISystemStatusProvider
    {
        private int _captureCount;
        private readonly TaskCompletionSource _firstCaptureStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _releaseFirstCapture = new(TaskCreationOptions.RunContinuationsAsynchronously);

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
}
