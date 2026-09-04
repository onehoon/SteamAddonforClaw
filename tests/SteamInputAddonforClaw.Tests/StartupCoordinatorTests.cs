using System.Text.Json;
using SteamInputAddonforClaw.Contracts.Frontend;
using SteamInputAddonforClaw.Startup;
using SteamInputAddonforClaw.Recovery;
using SteamInputAddonforClaw.Devices;
using SteamInputAddonforClaw.Devices.Abstractions;
using SteamInputAddonforClaw.Status;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class StartupCoordinatorTests
{
    [Fact]
    public async Task LiveBaselineRunsAfterUpdateAndStableStockEnvironment()
    {
        var events = new List<string>();
        var store = new FakeRecoveryJournalStore(events, exists: false);
        var coordinator = new StartupCoordinator(new FakeUpdateGate(events, UpdateGateResult.Continue),
            new FakeTopologyWaiter(events), new FakeProbeFactory(), new FakeHardwareEvaluator(), recoveryJournalStore: store, stockCenterMBaseline: new FakeBaseline(events));
        var result = await coordinator.RunAsync(CancellationToken.None);
        Assert.True(result.RecoverySafe);
        Assert.Equal(["UpdateGate", "TopologyWaiter", "Baseline", "Discard"], events);
        Assert.Equal(0, store.DeleteCallCount);
    }

    [Fact]
    public async Task StaleJournalIsDiscardedAfterSuccessfulBaseline()
    {
        var events = new List<string>();
        var store = new FakeRecoveryJournalStore(events, exists: true, existsAfterDelete: false);
        var coordinator = new StartupCoordinator(new FakeUpdateGate(events, UpdateGateResult.Continue),
            new FakeTopologyWaiter(events), new FakeProbeFactory(), new FakeHardwareEvaluator(), recoveryJournalStore: store, stockCenterMBaseline: new FakeBaseline(events));
        var result = await coordinator.RunAsync(CancellationToken.None);
        Assert.True(result.RecoverySafe);
        Assert.Equal(1, store.DeleteCallCount);
        Assert.Contains("Baseline", events);
    }

    [Fact]
    public async Task StaleJournalUnreadable_PreservesJournalAndBlocksRouting()
    {
        // The stale journal is now read as immutable ownership evidence for HidHide cleanup
        // (never for routing/native/VIIPER replay). An unreadable journal must therefore be
        // treated fail-closed: preserved, never deleted, routing stays passive.
        var events = new List<string>();
        var store = new FakeRecoveryJournalStore(events, exists: true, readTextThrows: true);
        var coordinator = new StartupCoordinator(new FakeUpdateGate(events, UpdateGateResult.Continue),
            new FakeTopologyWaiter(events), new FakeProbeFactory(), new FakeHardwareEvaluator(), recoveryJournalStore: store, stockCenterMBaseline: new FakeBaseline(events));

        var result = await coordinator.RunAsync(CancellationToken.None);

        Assert.False(result.RecoverySafe);
        Assert.Equal(0, store.DeleteCallCount);
    }

    [Fact]
    public async Task MalformedJournal_PreservesJournalAndBlocksRouting_NoHidHideCleanup()
    {
        var events = new List<string>();
        var store = new FakeRecoveryJournalStore(events, exists: true, malformedJson: true);
        var cleaner = new FakeStartupHidHideRecoveryCleaner();
        var coordinator = new StartupCoordinator(new FakeUpdateGate(events, UpdateGateResult.Continue),
            new FakeTopologyWaiter(events), new FakeProbeFactory(), new FakeHardwareEvaluator(),
            recoveryJournalStore: store, stockCenterMBaseline: new FakeBaseline(events), hidHideRecoveryCleaner: cleaner);

        var result = await coordinator.RunAsync(CancellationToken.None);

        Assert.False(result.RecoverySafe);
        Assert.Equal(0, store.DeleteCallCount);
        Assert.Equal(0, cleaner.CallCount);
    }

    [Fact]
    public async Task UnsupportedSchemaJournal_PreservesJournalAndBlocksRouting_NoHidHideCleanup()
    {
        // The Stock XInput baseline may already have succeeded; only the LoadJournal() schema
        // gate downstream of it decides whether the (unreadable-as-current-schema) journal is
        // trusted. It must fail closed rather than run HidHide cleanup against untrusted evidence.
        var events = new List<string>();
        var unsupportedSchemaJson = System.Text.Json.JsonSerializer.Serialize(new { SchemaVersion = RecoveryManager.CurrentSchemaVersion - 1 });
        var store = new FakeRecoveryJournalStore(events, exists: true, rawJson: unsupportedSchemaJson);
        var cleaner = new FakeStartupHidHideRecoveryCleaner();
        var coordinator = new StartupCoordinator(new FakeUpdateGate(events, UpdateGateResult.Continue),
            new FakeTopologyWaiter(events), new FakeProbeFactory(), new FakeHardwareEvaluator(),
            recoveryJournalStore: store, stockCenterMBaseline: new FakeBaseline(events), hidHideRecoveryCleaner: cleaner);

        var result = await coordinator.RunAsync(CancellationToken.None);

        Assert.Contains("Baseline", events);
        Assert.False(result.RecoverySafe);
        Assert.Equal(0, store.DeleteCallCount);
        Assert.Equal(0, cleaner.CallCount);
    }

    [Fact]
    public async Task ValidJournalWithoutHidHideEvidence_DoesNotInvokeCleaner_DiscardsAndSafe()
    {
        var events = new List<string>();
        var journal = new RecoveryJournal(RecoveryManager.CurrentSchemaVersion, Guid.NewGuid(), DateTimeOffset.UtcNow, null, new());
        var store = new FakeRecoveryJournalStore(events, exists: true, journal: journal);
        var cleaner = new FakeStartupHidHideRecoveryCleaner();
        var coordinator = new StartupCoordinator(new FakeUpdateGate(events, UpdateGateResult.Continue),
            new FakeTopologyWaiter(events), new FakeProbeFactory(), new FakeHardwareEvaluator(),
            recoveryJournalStore: store, stockCenterMBaseline: new FakeBaseline(events), hidHideRecoveryCleaner: cleaner);

        var result = await coordinator.RunAsync(CancellationToken.None);

        Assert.True(result.RecoverySafe);
        Assert.Equal(1, store.DeleteCallCount);
        Assert.Equal(0, cleaner.CallCount);
    }

    [Fact]
    public async Task ValidJournalWithOwnedHidHideResidue_InvokesCleaner_DiscardsAndSafe()
    {
        var events = new List<string>();
        var journal = new RecoveryJournal(RecoveryManager.CurrentSchemaVersion, Guid.NewGuid(), DateTimeOffset.UtcNow, null,
            new(HidHideDeviceAdditions: ["HID\\Claw"], ExecutableWhitelistAdditions: ["C:\\addon.exe"], OriginalHidHideActiveState: false));
        var store = new FakeRecoveryJournalStore(events, exists: true, journal: journal);
        var cleaner = new FakeStartupHidHideRecoveryCleaner();
        var coordinator = new StartupCoordinator(new FakeUpdateGate(events, UpdateGateResult.Continue),
            new FakeTopologyWaiter(events), new FakeProbeFactory(), new FakeHardwareEvaluator(),
            recoveryJournalStore: store, stockCenterMBaseline: new FakeBaseline(events), hidHideRecoveryCleaner: cleaner);

        var result = await coordinator.RunAsync(CancellationToken.None);

        Assert.True(result.RecoverySafe);
        Assert.Equal(1, store.DeleteCallCount);
        Assert.Equal(1, cleaner.CallCount);
        Assert.Equal(journal.RecoverySessionId, cleaner.LastJournal?.RecoverySessionId);
    }

    [Fact]
    public async Task HidHideCleanupFails_PreservesJournalAndUnsafe()
    {
        var events = new List<string>();
        var journal = new RecoveryJournal(RecoveryManager.CurrentSchemaVersion, Guid.NewGuid(), DateTimeOffset.UtcNow, null,
            new(HidHideDeviceAdditions: ["HID\\Claw"]));
        var store = new FakeRecoveryJournalStore(events, exists: true, journal: journal);
        var cleaner = new FakeStartupHidHideRecoveryCleaner(succeeds: false);
        var coordinator = new StartupCoordinator(new FakeUpdateGate(events, UpdateGateResult.Continue),
            new FakeTopologyWaiter(events), new FakeProbeFactory(), new FakeHardwareEvaluator(),
            recoveryJournalStore: store, stockCenterMBaseline: new FakeBaseline(events), hidHideRecoveryCleaner: cleaner);

        var result = await coordinator.RunAsync(CancellationToken.None);

        Assert.False(result.RecoverySafe);
        Assert.Equal(0, store.DeleteCallCount);
        Assert.Equal(1, cleaner.CallCount);
    }

    [Fact]
    public async Task HidHideEvidencePresentButNoCleanerConfigured_PreservesJournalAndUnsafe()
    {
        var events = new List<string>();
        var journal = new RecoveryJournal(RecoveryManager.CurrentSchemaVersion, Guid.NewGuid(), DateTimeOffset.UtcNow, null,
            new(ExecutableWhitelistAdditions: ["C:\\addon.exe"]));
        var store = new FakeRecoveryJournalStore(events, exists: true, journal: journal);
        var coordinator = new StartupCoordinator(new FakeUpdateGate(events, UpdateGateResult.Continue),
            new FakeTopologyWaiter(events), new FakeProbeFactory(), new FakeHardwareEvaluator(),
            recoveryJournalStore: store, stockCenterMBaseline: new FakeBaseline(events));

        var result = await coordinator.RunAsync(CancellationToken.None);

        Assert.False(result.RecoverySafe);
        Assert.Equal(0, store.DeleteCallCount);
    }

    [Fact]
    public async Task MixedJournal_CleansOnlyHidHideResidue_AndNeverReplaysNativeOrVirtualState()
    {
        var mutationId = Guid.NewGuid();
        var events = new List<string>();
        var snapshot = new DeviceNativeStateSnapshot(new HandheldDeviceId("test.device"), 1, DateTimeOffset.UtcNow, JsonSerializer.SerializeToElement(new { Mode = "DirectInput" }));
        var journal = new RecoveryJournal(RecoveryManager.CurrentSchemaVersion, Guid.NewGuid(), DateTimeOffset.UtcNow,
            snapshot,
            new(DeviceNativeStateChanged: true,
                HidHideDeviceAdditions: ["HID\\Claw"],
                ExecutableWhitelistAdditions: ["C:\\addon.exe"],
                OriginalHidHideActiveState: false,
                AddonOwnedVirtualDeviceEntries: [new(mutationId, "steamdeck", 0x28DE, 0x1205, [], ["USB\\VID_28DE&PID_1205\\owned"])]));
        var store = new FakeRecoveryJournalStore(events, exists: true, journal: journal);
        var cleaner = new FakeStartupHidHideRecoveryCleaner();
        var coordinator = new StartupCoordinator(new FakeUpdateGate(events, UpdateGateResult.Continue),
            new FakeTopologyWaiter(events), new FakeProbeFactory(), new FakeHardwareEvaluator(),
            recoveryJournalStore: store, stockCenterMBaseline: new FakeBaseline(events), hidHideRecoveryCleaner: cleaner);

        var result = await coordinator.RunAsync(CancellationToken.None);

        Assert.True(result.RecoverySafe);
        Assert.Equal(1, cleaner.CallCount);
        Assert.Equal(1, store.DeleteCallCount);
        // The cleaner only ever receives the journal + IHidHideClient (see
        // StartupHidHideRecoveryCleaner), so it structurally cannot replay native or VIIPER
        // state -- there is no such dependency for it to call.
    }

    [Fact]
    public async Task JournalDeletionThrows_DoesNotCrashAndBlocksRouting()
    {
        var events = new List<string>();
        var store = new FakeRecoveryJournalStore(events, exists: true, deleteThrows: true);
        var coordinator = new StartupCoordinator(new FakeUpdateGate(events, UpdateGateResult.Continue),
            new FakeTopologyWaiter(events), new FakeProbeFactory(), new FakeHardwareEvaluator(), recoveryJournalStore: store, stockCenterMBaseline: new FakeBaseline(events));

        var result = await coordinator.RunAsync(CancellationToken.None);

        Assert.True(result.ShouldStartRuntime);
        Assert.False(result.RecoverySafe);
        Assert.Equal(1, store.DeleteCallCount);
    }

    [Fact]
    public async Task JournalStillExistsAfterDeletion_BlocksRouting()
    {
        var events = new List<string>();
        var store = new FakeRecoveryJournalStore(events, exists: true, existsAfterDelete: true);
        var coordinator = new StartupCoordinator(new FakeUpdateGate(events, UpdateGateResult.Continue),
            new FakeTopologyWaiter(events), new FakeProbeFactory(), new FakeHardwareEvaluator(), recoveryJournalStore: store, stockCenterMBaseline: new FakeBaseline(events));

        var result = await coordinator.RunAsync(CancellationToken.None);

        Assert.False(result.RecoverySafe);
        Assert.Equal(1, store.DeleteCallCount);
    }

    [Fact]
    public async Task BaselineFailure_BlocksRoutingButStartsPassiveRuntime()
    {
        var events = new List<string>();
        var store = new FakeRecoveryJournalStore(events, exists: false);
        var coordinator = new StartupCoordinator(new FakeUpdateGate(events, UpdateGateResult.Continue),
            new FakeTopologyWaiter(events), new FakeProbeFactory(), new FakeHardwareEvaluator(), recoveryJournalStore: store, stockCenterMBaseline: new FakeBaseline(events, false));
        var result = await coordinator.RunAsync(CancellationToken.None);
        Assert.False(result.RecoverySafe);
        Assert.Equal(FrontendCenterMStartupState.Enabled, result.CenterMStartupState);
        Assert.Equal(["UpdateGate", "TopologyWaiter", "Baseline"], events);
        Assert.Equal(0, store.DeleteCallCount);
    }

    [Fact]
    public async Task BaselineFailureWithStaleJournalPresent_NeverDeletesJournal()
    {
        // Critical safety invariant: the stale journal must never be deleted before the
        // live Stock XInput baseline has been independently verified to succeed.
        var events = new List<string>();
        var store = new FakeRecoveryJournalStore(events, exists: true);
        var coordinator = new StartupCoordinator(new FakeUpdateGate(events, UpdateGateResult.Continue),
            new FakeTopologyWaiter(events), new FakeProbeFactory(), new FakeHardwareEvaluator(), recoveryJournalStore: store, stockCenterMBaseline: new FakeBaseline(events, false));

        var result = await coordinator.RunAsync(CancellationToken.None);

        Assert.False(result.RecoverySafe);
        Assert.Equal(0, store.DeleteCallCount);
        Assert.DoesNotContain("Discard", events);
    }

    [Fact]
    public async Task UpdateRestart_DoesNotRunBaselineOrJournalDiscard()
    {
        var events = new List<string>();
        var store = new FakeRecoveryJournalStore(events, exists: true);
        var coordinator = new StartupCoordinator(new FakeUpdateGate(events, UpdateGateResult.RestartScheduled),
            new ThrowingTopologyWaiter(), new ThrowingProbeFactory(), new ThrowingHardwareEvaluator(), recoveryJournalStore: store, stockCenterMBaseline: new FakeBaseline(events));

        var result = await coordinator.RunAsync(CancellationToken.None);

        Assert.False(result.ShouldStartRuntime);
        Assert.False(result.RecoverySafe);
        Assert.Equal(["UpdateGate"], events);
        Assert.Equal(0, store.DeleteCallCount);
    }

    [Fact]
    public async Task CancellationDuringBaseline_Propagates()
    {
        var events = new List<string>();
        using var cancellation = new CancellationTokenSource();
        var store = new FakeRecoveryJournalStore(events, exists: false);
        var coordinator = new StartupCoordinator(new FakeUpdateGate(events, UpdateGateResult.Continue),
            new FakeTopologyWaiter(events), new FakeProbeFactory(), new FakeHardwareEvaluator(),
            recoveryJournalStore: store, stockCenterMBaseline: new ThrowingBaseline(events, new OperationCanceledException(cancellation.Token)));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => coordinator.RunAsync(cancellation.Token));

        Assert.Equal(["UpdateGate", "TopologyWaiter", "Baseline"], events);
        Assert.Equal(0, store.DeleteCallCount);
    }

    [Fact]
    public async Task CanStartRuntimeAsync_WhenNoUpdateExists_WaitsForEnvironmentAfterUpdateGate()
    {
        var events = new List<string>();
        var store = new FakeRecoveryJournalStore(events, exists: false);
        var coordinator = new StartupCoordinator(
            new FakeUpdateGate(events, UpdateGateResult.Continue),
            new FakeTopologyWaiter(events), new FakeProbeFactory(), new FakeHardwareEvaluator(), recoveryJournalStore: store);

        var result = await coordinator.RunAsync(CancellationToken.None);

        Assert.True(result.ShouldStartRuntime);
        Assert.Equal(FrontendCenterMStartupState.Enabled, result.CenterMStartupState);
        Assert.False(result.RecoverySafe);
        Assert.Equal(["UpdateGate", "TopologyWaiter"], events);
        Assert.Equal(0, store.DeleteCallCount);
    }

    [Fact]
    public async Task CanStartRuntimeAsync_WhenUpdateIsScheduled_DoesNotInitializeEnvironment()
    {
        var events = new List<string>();
        var store = new FakeRecoveryJournalStore(events, exists: false);
        var coordinator = new StartupCoordinator(
            new FakeUpdateGate(events, UpdateGateResult.RestartScheduled),
            new FakeTopologyWaiter(events), new FakeProbeFactory(), new FakeHardwareEvaluator(), recoveryJournalStore: store);

        var result = await coordinator.RunAsync(CancellationToken.None);

        Assert.False(result.ShouldStartRuntime);
        Assert.Equal(["UpdateGate"], events);
        Assert.Equal(0, store.DeleteCallCount);
    }

    private sealed class FakeUpdateGate(List<string> events, UpdateGateResult result) : IUpdateGate
    {
        public Task<UpdateGateResult> RunAsync(CancellationToken cancellationToken)
        {
            events.Add("UpdateGate");
            return Task.FromResult(result);
        }
    }

    /// <summary>
    /// Startup-specific test double for <see cref="IRecoveryJournalStore"/>. StartupCoordinator
    /// reads the journal (via <see cref="RecoveryManager.LoadJournal"/>) only as immutable
    /// ownership evidence for HidHide cleanup -- it never replays routing/native/VIIPER state
    /// from it -- and it must never write/replace journal contents itself.
    /// </summary>
    private sealed class FakeRecoveryJournalStore(List<string> events, bool exists = false, bool deleteThrows = false, bool existsAfterDelete = false,
        RecoveryJournal? journal = null, bool readTextThrows = false, bool malformedJson = false, string? rawJson = null) : IRecoveryJournalStore
    {
        private bool _deleted;
        private int _existsCallCount;

        public int DeleteCallCount { get; private set; }
        public string JournalPath => "fake-recovery-journal.json";

        public bool Exists()
        {
            _existsCallCount++;
            if (_existsCallCount == 1) events.Add("Discard");
            return _deleted ? existsAfterDelete : exists;
        }

        public string ReadText()
        {
            if (readTextThrows) throw new IOException("read failed");
            if (malformedJson) return "{ not valid json";
            if (rawJson is not null) return rawJson;
            var effective = journal ?? new RecoveryJournal(RecoveryManager.CurrentSchemaVersion, Guid.NewGuid(), DateTimeOffset.UtcNow, null, new());
            return JsonSerializer.Serialize(effective);
        }


        public void Delete()
        {
            DeleteCallCount++;
            _deleted = true;
            if (deleteThrows) throw new IOException("delete failed");
        }
    }

    private sealed class FakeStartupHidHideRecoveryCleaner(bool succeeds = true, string reason = "cleaned") : IStartupHidHideRecoveryCleaner
    {
        public int CallCount { get; private set; }
        public RecoveryJournal? LastJournal { get; private set; }

        public bool TryClean(RecoveryJournal journal, out string outReason)
        {
            CallCount++;
            LastJournal = journal;
            outReason = reason;
            return succeeds;
        }
    }

    [Theory]
    [InlineData((int)HardwareCompatibilityStatus.Unsupported)]
    [InlineData((int)HardwareCompatibilityStatus.Indeterminate)]
    public async Task NonSupportedHardware_DoesNotEstablishStartupBoundary(int statusValue)
    {
        var status = (HardwareCompatibilityStatus)statusValue;
        var events = new List<string>();
        var store = new FakeRecoveryJournalStore(events, exists: false);
        // Indeterminate is retried by the hardware probe stabilization; this fake never
        // resolves, so use a non-waiting fake delay and a short timeout to avoid a real 5s wait.
        var coordinator = new StartupCoordinator(new FakeUpdateGate(events, UpdateGateResult.Continue), new ThrowingTopologyWaiter(),
            new FakeProbeFactory(), new FixedHardwareEvaluator(status), recoveryJournalStore: store, stockCenterMBaseline: new FakeBaseline(events),
            hardwareProbeTimeout: TimeSpan.FromMilliseconds(20), hardwareProbeDelay: (_, _) => Task.CompletedTask);

        var result = await coordinator.RunAsync(CancellationToken.None);

        Assert.False(result.RecoverySafe);
        Assert.DoesNotContain("Baseline", events);
        Assert.DoesNotContain("Discard", events);
        Assert.Equal(0, store.DeleteCallCount);
    }

    [Fact]
    public async Task StockReadinessIndeterminate_DoesNotEstablishStartupBoundary()
    {
        var events = new List<string>();
        var store = new FakeRecoveryJournalStore(events, exists: true);
        var coordinator = new StartupCoordinator(new FakeUpdateGate(events, UpdateGateResult.Continue), new FixedTopologyWaiter(events, ControllerTopologyReadiness.Indeterminate), new FakeProbeFactory(), new FakeHardwareEvaluator(),
            recoveryJournalStore: store, stockCenterMBaseline: new FakeBaseline(events));

        var result = await coordinator.RunAsync(CancellationToken.None);

        Assert.False(result.RecoverySafe);
        Assert.DoesNotContain("Baseline", events);
        Assert.DoesNotContain("Discard", events);
        Assert.Equal(0, store.DeleteCallCount);
    }

    [Fact]
    public async Task EnabledRoots_StableTopology_ReachesBaseline()
    {
        var events = new List<string>();
        var store = new FakeRecoveryJournalStore(events, exists: false);
        var coordinator = new StartupCoordinator(new FakeUpdateGate(events, UpdateGateResult.Continue),
            new FakeTopologyWaiter(events), new FakeProbeFactory(), new FakeHardwareEvaluator(),
            recoveryJournalStore: store, stockCenterMBaseline: new FakeBaseline(events));

        var result = await coordinator.RunAsync(CancellationToken.None);

        Assert.Equal(FrontendCenterMStartupState.Enabled, result.CenterMStartupState);
        Assert.Contains("Baseline", events);
        Assert.True(result.RecoverySafe);
    }

    private sealed class FakeBaseline(List<string> events, bool succeeded = true) : IStockCenterMStartupBaseline
    {
        public Task<StockCenterMStartupBaselineResult> EstablishAsync(CancellationToken cancellationToken)
        {
            events.Add("Baseline");
            return Task.FromResult(new StockCenterMStartupBaselineResult(succeeded, false, succeeded ? "test" : "failed"));
        }
    }

    private sealed class ThrowingBaseline(List<string> events, Exception exception) : IStockCenterMStartupBaseline
    {
        public Task<StockCenterMStartupBaselineResult> EstablishAsync(CancellationToken cancellationToken)
        {
            events.Add("Baseline");
            return Task.FromException<StockCenterMStartupBaselineResult>(exception);
        }
    }

    private sealed class FakeTopologyWaiter(List<string> events) : IControllerTopologyWaiter
    {
        public int Calls { get; private set; }

        public Task<ControllerTopologyReadiness> WaitUntilStableAsync(CancellationToken cancellationToken)
        {
            events.Add("TopologyWaiter");
            Calls++;
            return Task.FromResult(ControllerTopologyReadiness.Stable);
        }
    }

    private sealed class FixedTopologyWaiter(List<string> events, ControllerTopologyReadiness readiness) : IControllerTopologyWaiter
    {
        public Task<ControllerTopologyReadiness> WaitUntilStableAsync(CancellationToken cancellationToken)
        {
            events.Add("TopologyWaiter");
            return Task.FromResult(readiness);
        }
    }

    [Fact]
    public async Task HardwareIndeterminate_ThenSupported_RetriesAndContinues()
    {
        var events = new List<string>();
        var store = new FakeRecoveryJournalStore(events, exists: false);
        var evaluator = new SequencedHardwareEvaluator([
            new(HardwareCompatibilityStatus.Indeterminate, null, null, "test"),
            new(HardwareCompatibilityStatus.Supported, new("msi.claw"), new("msi.claw.cg3em"), "test")
        ]);
        var delay = new RecordingHardwareProbeDelay();
        var coordinator = new StartupCoordinator(new FakeUpdateGate(events, UpdateGateResult.Continue), new FakeTopologyWaiter(events),
            new FakeProbeFactory(), evaluator, recoveryJournalStore: store, stockCenterMBaseline: new FakeBaseline(events), hardwareProbeDelay: delay.DelayAsync);

        var result = await coordinator.RunAsync(CancellationToken.None);

        Assert.Equal(2, evaluator.CallCount);
        Assert.Equal([TimeSpan.FromMilliseconds(500)], delay.Delays);
        Assert.True(result.RecoverySafe);
    }

    [Fact]
    public async Task HardwareNoAdapterMatched_ThenSupported_RetriesAndContinues()
    {
        var events = new List<string>();
        var store = new FakeRecoveryJournalStore(events, exists: false);
        var evaluator = new SequencedHardwareEvaluator([
            new(HardwareCompatibilityStatus.Unsupported, null, null, "No handheld-device adapter matched."),
            new(HardwareCompatibilityStatus.Supported, new("msi.claw"), new("msi.claw.cg3em"), "test")
        ]);
        var delay = new RecordingHardwareProbeDelay();
        var coordinator = new StartupCoordinator(new FakeUpdateGate(events, UpdateGateResult.Continue), new FakeTopologyWaiter(events),
            new FakeProbeFactory(), evaluator, recoveryJournalStore: store, stockCenterMBaseline: new FakeBaseline(events), hardwareProbeDelay: delay.DelayAsync);

        var result = await coordinator.RunAsync(CancellationToken.None);

        Assert.Equal(2, evaluator.CallCount);
        Assert.Equal([TimeSpan.FromMilliseconds(500)], delay.Delays);
        Assert.True(result.RecoverySafe);
    }

    [Fact]
    public async Task HardwareTerminalUnsupported_DoesNotRetry()
    {
        var events = new List<string>();
        var store = new FakeRecoveryJournalStore(events, exists: false);
        var evaluator = new SequencedHardwareEvaluator([new(HardwareCompatibilityStatus.Unsupported, null, null, "MsiClawModelUnsupported")]);
        var delay = new RecordingHardwareProbeDelay();
        var coordinator = new StartupCoordinator(new FakeUpdateGate(events, UpdateGateResult.Continue), new ThrowingTopologyWaiter(),
            new FakeProbeFactory(), evaluator, recoveryJournalStore: store, stockCenterMBaseline: new FakeBaseline(events), hardwareProbeDelay: delay.DelayAsync);

        var result = await coordinator.RunAsync(CancellationToken.None);

        Assert.Equal(1, evaluator.CallCount);
        Assert.Empty(delay.Delays);
        Assert.Equal(HardwareCompatibilityStatus.Unsupported, result.HardwareStatus);
    }

    [Fact]
    public async Task HardwareSupportedOnFirstAttempt_NoDelay()
    {
        var events = new List<string>();
        var store = new FakeRecoveryJournalStore(events, exists: false);
        var delay = new RecordingHardwareProbeDelay();
        var coordinator = new StartupCoordinator(new FakeUpdateGate(events, UpdateGateResult.Continue), new FakeTopologyWaiter(events),
            new FakeProbeFactory(), new FakeHardwareEvaluator(), recoveryJournalStore: store, stockCenterMBaseline: new FakeBaseline(events), hardwareProbeDelay: delay.DelayAsync);

        var result = await coordinator.RunAsync(CancellationToken.None);

        Assert.Empty(delay.Delays);
        Assert.True(result.RecoverySafe);
    }

    [Fact]
    public async Task HardwareTransientNeverResolves_TimesOutPassive()
    {
        var events = new List<string>();
        var store = new FakeRecoveryJournalStore(events, exists: false);
        var evaluator = new SequencedHardwareEvaluator([new(HardwareCompatibilityStatus.Indeterminate, null, null, "test")], repeatLast: true);
        var coordinator = new StartupCoordinator(new FakeUpdateGate(events, UpdateGateResult.Continue), new ThrowingTopologyWaiter(),
            new FakeProbeFactory(), evaluator, recoveryJournalStore: store, stockCenterMBaseline: new FakeBaseline(events),
            hardwareProbeTimeout: TimeSpan.FromMilliseconds(20), hardwareProbeDelay: (_, _) => Task.CompletedTask);

        var result = await coordinator.RunAsync(CancellationToken.None);

        Assert.True(evaluator.CallCount > 1);
        Assert.False(result.RecoverySafe);
    }

    [Fact]
    public async Task CancellationDuringHardwareProbeStabilization_Propagates()
    {
        var events = new List<string>();
        var store = new FakeRecoveryJournalStore(events, exists: false);
        using var cancellation = new CancellationTokenSource();
        var evaluator = new SequencedHardwareEvaluator([new(HardwareCompatibilityStatus.Indeterminate, null, null, "test")], repeatLast: true);
        var coordinator = new StartupCoordinator(new FakeUpdateGate(events, UpdateGateResult.Continue), new ThrowingTopologyWaiter(),
            new FakeProbeFactory(), evaluator, recoveryJournalStore: store, stockCenterMBaseline: new FakeBaseline(events),
            hardwareProbeDelay: (_, token) => { cancellation.Cancel(); token.ThrowIfCancellationRequested(); return Task.CompletedTask; });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => coordinator.RunAsync(cancellation.Token));

        Assert.Equal(1, evaluator.CallCount);
    }

    // ---- PR4: authority-aware startup branch (work order sections 7-9/17/26) ----

    private static FrontendCenterMStartupSnapshot Roots(FrontendCenterMStartupState state) => new(state, false, false, false, null);

    [Fact]
    public async Task DisabledRoots_RunReadOnlyAdmission_NeverStockBaselineOrRecoveryMutation()
    {
        var events = new List<string>();
        // A stale journal is present: the Disabled path must not read, clean, or delete it.
        var store = new FakeRecoveryJournalStore(events, exists: true);
        var waiter = new FakeTopologyWaiter(events);
        var admission = new FakeDisabledBootAdmission(events, DisabledBootAdmissionOutcome.Ready);
        var coordinator = new StartupCoordinator(new FakeUpdateGate(events, UpdateGateResult.Continue),
            waiter, new FakeProbeFactory(), new FakeHardwareEvaluator(),
            recoveryJournalStore: store, stockCenterMBaseline: new FakeBaseline(events),
            disabledBootAdmission: admission, captureCenterMStartup: () => Roots(FrontendCenterMStartupState.Disabled));

        var result = await coordinator.RunAsync(CancellationToken.None);

        Assert.True(result.ShouldStartRuntime);
        Assert.True(result.DisabledBootAdmission!.IsReady);
        Assert.Equal(FrontendCenterMStartupState.Disabled, result.CenterMStartupState);
        Assert.NotEqual(FrontendCenterMStartupState.Enabled, result.CenterMStartupState);
        Assert.Equal(["UpdateGate", "TopologyWaiter", "Admission"], events);
        Assert.DoesNotContain("Baseline", events);
        Assert.DoesNotContain("Discard", events);
        Assert.Equal(0, store.DeleteCallCount);
        Assert.Equal(1, waiter.Calls);
    }

    [Fact]
    public async Task DisabledRoots_TopologyNotStable_BlocksBeforeEvaluatingFacts()
    {
        var events = new List<string>();
        var store = new FakeRecoveryJournalStore(events, exists: false);
        var admission = new FakeDisabledBootAdmission(events, DisabledBootAdmissionOutcome.Ready);
        var coordinator = new StartupCoordinator(new FakeUpdateGate(events, UpdateGateResult.Continue),
            new FixedTopologyWaiter(events, ControllerTopologyReadiness.Indeterminate),
            new FakeProbeFactory(), new FakeHardwareEvaluator(),
            recoveryJournalStore: store, stockCenterMBaseline: new FakeBaseline(events),
            disabledBootAdmission: admission, captureCenterMStartup: () => Roots(FrontendCenterMStartupState.Disabled));

        var result = await coordinator.RunAsync(CancellationToken.None);

        Assert.Equal(DisabledBootAdmissionOutcome.Blocked, result.DisabledBootAdmission!.Outcome);
        Assert.Equal(0, admission.EvaluateCount);
        Assert.True(result.ShouldStartRuntime); // mandatory Runtime stays alive
        Assert.NotEqual(FrontendCenterMStartupState.Enabled, result.CenterMStartupState);
        Assert.DoesNotContain("Baseline", events);
    }

    [Fact]
    public async Task DisabledRoots_AdmissionBlocked_KeepsRuntimeAliveWithNoMutation()
    {
        var events = new List<string>();
        var store = new FakeRecoveryJournalStore(events, exists: false);
        var coordinator = new StartupCoordinator(new FakeUpdateGate(events, UpdateGateResult.Continue),
            new FakeTopologyWaiter(events), new FakeProbeFactory(), new FakeHardwareEvaluator(),
            recoveryJournalStore: store, stockCenterMBaseline: new FakeBaseline(events),
            disabledBootAdmission: new FakeDisabledBootAdmission(events, DisabledBootAdmissionOutcome.Blocked),
            captureCenterMStartup: () => Roots(FrontendCenterMStartupState.Disabled));

        var result = await coordinator.RunAsync(CancellationToken.None);

        Assert.Equal(DisabledBootAdmissionOutcome.Blocked, result.DisabledBootAdmission!.Outcome);
        Assert.True(result.ShouldStartRuntime);
        Assert.DoesNotContain("Baseline", events);
        Assert.Equal(0, store.DeleteCallCount);
    }

    [Theory]
    [InlineData("Partial")]
    [InlineData("Unavailable")]
    public async Task PartialOrUnavailableRoots_SelectNoControllerOwner(string state)
    {
        var events = new List<string>();
        var store = new FakeRecoveryJournalStore(events, exists: false);
        var admission = new FakeDisabledBootAdmission(events, DisabledBootAdmissionOutcome.Ready);
        var coordinator = new StartupCoordinator(new FakeUpdateGate(events, UpdateGateResult.Continue),
            new FakeTopologyWaiter(events), new FakeProbeFactory(), new FakeHardwareEvaluator(),
            recoveryJournalStore: store, stockCenterMBaseline: new FakeBaseline(events),
            disabledBootAdmission: admission, captureCenterMStartup: () => Roots(Enum.Parse<FrontendCenterMStartupState>(state)));

        var result = await coordinator.RunAsync(CancellationToken.None);

        Assert.True(result.ShouldStartRuntime);
        Assert.Null(result.DisabledBootAdmission);
        Assert.Equal(Enum.Parse<FrontendCenterMStartupState>(state), result.CenterMStartupState);
        Assert.NotEqual(FrontendCenterMStartupState.Enabled, result.CenterMStartupState);
        Assert.Equal(0, admission.EvaluateCount);
        Assert.DoesNotContain("Baseline", events);
    }

    [Fact]
    public async Task EnabledRoots_RunExistingStockPath_AdmissionNeverEvaluated()
    {
        var events = new List<string>();
        var store = new FakeRecoveryJournalStore(events, exists: false);
        var admission = new FakeDisabledBootAdmission(events, DisabledBootAdmissionOutcome.Ready);
        var coordinator = new StartupCoordinator(new FakeUpdateGate(events, UpdateGateResult.Continue),
            new FakeTopologyWaiter(events), new FakeProbeFactory(), new FakeHardwareEvaluator(),
            recoveryJournalStore: store, stockCenterMBaseline: new FakeBaseline(events),
            disabledBootAdmission: admission, captureCenterMStartup: () => Roots(FrontendCenterMStartupState.Enabled));

        var result = await coordinator.RunAsync(CancellationToken.None);

        Assert.Contains("Baseline", events);
        Assert.Equal(0, admission.EvaluateCount);
        Assert.Equal(FrontendCenterMStartupState.Enabled, result.CenterMStartupState);
        Assert.Equal(FrontendCenterMStartupState.Enabled, result.CenterMStartupState);
        Assert.True(result.RecoverySafe);
    }

    [Fact]
    public async Task NoCaptureDelegate_PreservesLegacyStockPath()
    {
        var events = new List<string>();
        var store = new FakeRecoveryJournalStore(events, exists: false);
        var coordinator = new StartupCoordinator(new FakeUpdateGate(events, UpdateGateResult.Continue),
            new FakeTopologyWaiter(events), new FakeProbeFactory(), new FakeHardwareEvaluator(),
            recoveryJournalStore: store, stockCenterMBaseline: new FakeBaseline(events));

        var result = await coordinator.RunAsync(CancellationToken.None);

        Assert.Contains("Baseline", events);
        Assert.Equal(FrontendCenterMStartupState.Enabled, result.CenterMStartupState);
    }

    [Fact]
    public void DisabledBootAdmission_TakesNoPhysicalOwnershipDependency()
    {
        var parameterTypes = typeof(DisabledBootControllerAdmission)
            .GetConstructors().Single().GetParameters().Select(p => p.ParameterType.FullName ?? "");
        Assert.All(parameterTypes, name =>
        {
            Assert.DoesNotContain("DirectInput", name);
            Assert.DoesNotContain("Viiper", name, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("NativeState", name);
            Assert.DoesNotContain("Routing", name);
        });

        var source = File.ReadAllText(Path.Combine(RepoRoot(), "src/SteamInputAddonforClaw/Startup/DisabledBootControllerAdmission.cs"));
        foreach (var forbidden in new[] { "SwitchModeAsync", "MsiClawInputSource", "DirectInput", "ApplyDisabledModeBaseline", "AddHiddenDevice", "AttachXbox360", "AttachSteamDeck", "CanonicalViiperRuntime" })
            Assert.DoesNotContain(forbidden, source, StringComparison.Ordinal);
    }

    [Fact]
    public void RuntimeComposition_NeverComposesLegacyRouting_ButKeepsTheStockResumeBaselineGated()
    {
        var source = File.ReadAllText(Path.Combine(RepoRoot(), "src/SteamInputAddonforClaw/Runtime/AddonRuntimeComposition.cs"));
        // Full1902 Cleanup A: the legacy Steam-session routing authority graph is deleted -- the
        // runtime composition no longer references it at all.
        Assert.DoesNotContain("AddonRoutingRuntime", source, StringComparison.Ordinal);
        Assert.DoesNotContain("StartRoutingObservation(", source, StringComparison.Ordinal);
        // Section 11: the stock PID1901 resume baseline is still gated on the Center M Enabled
        // authority state -- independently, not via the removed legacy routing selection.
        Assert.Contains("!stockCenterMAuthority || stockCenterMBaseline is null", source, StringComparison.Ordinal);
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "SteamInputAddonforClaw.slnx"))) dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("repo root");
    }

    private sealed class FakeDisabledBootAdmission(List<string> events, DisabledBootAdmissionOutcome outcome) : IDisabledBootControllerAdmission
    {
        public int EvaluateCount { get; private set; }
        public DisabledBootControllerAdmissionResult Evaluate()
        {
            EvaluateCount++;
            events.Add("Admission");
            return outcome switch
            {
                DisabledBootAdmissionOutcome.Ready => DisabledBootControllerAdmissionResult.Ready,
                DisabledBootAdmissionOutcome.NotApplicable => DisabledBootControllerAdmissionResult.NotApplicable,
                _ => DisabledBootControllerAdmissionResult.Blocked("test"),
            };
        }
    }

    private sealed class SequencedHardwareEvaluator(IEnumerable<HardwareCompatibilityAssessment> results, bool repeatLast = false) : IHardwareCompatibilityEvaluator
    {
        private readonly Queue<HardwareCompatibilityAssessment> _results = new(results);
        private HardwareCompatibilityAssessment? _last;
        public int CallCount { get; private set; }

        public HardwareCompatibilityAssessment Evaluate(DeviceProbeContextCapture _)
        {
            CallCount++;
            if (_results.Count > 0) { _last = _results.Dequeue(); return _last; }
            if (repeatLast && _last is not null) return _last;
            throw new InvalidOperationException("SequencedHardwareEvaluator exhausted.");
        }
    }

    private sealed class RecordingHardwareProbeDelay
    {
        public List<TimeSpan> Delays { get; } = [];
        public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
        {
            Delays.Add(delay);
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }
    }

    private sealed class FakeProbeFactory : IWindowsDeviceProbeContextFactory { public DeviceProbeContextCapture Capture() => new(DeviceProbeCaptureStatus.Success, new DeviceProbeContext(), "test"); }
    private sealed class FakeHardwareEvaluator : IHardwareCompatibilityEvaluator { public HardwareCompatibilityAssessment Evaluate(DeviceProbeContextCapture _) => new(HardwareCompatibilityStatus.Supported, new("msi.claw"), new("msi.claw.cg3em"), "test"); }
    private sealed class FixedHardwareEvaluator(HardwareCompatibilityStatus status) : IHardwareCompatibilityEvaluator
    {
        public HardwareCompatibilityAssessment Evaluate(DeviceProbeContextCapture _) => new(status, null, null, "test");
    }
    private sealed class ThrowingTopologyWaiter : IControllerTopologyWaiter { public Task<ControllerTopologyReadiness> WaitUntilStableAsync(CancellationToken _) => throw new Xunit.Sdk.XunitException("Topology wait must not run after recovery failure."); }
    private sealed class ThrowingProbeFactory : IWindowsDeviceProbeContextFactory { public DeviceProbeContextCapture Capture() => throw new Xunit.Sdk.XunitException("Hardware probe must not run after recovery failure."); }
    private sealed class ThrowingHardwareEvaluator : IHardwareCompatibilityEvaluator { public HardwareCompatibilityAssessment Evaluate(DeviceProbeContextCapture _) => throw new Xunit.Sdk.XunitException("Hardware evaluator must not run after recovery failure."); }
}
