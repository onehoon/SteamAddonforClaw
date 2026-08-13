using System.Text.Json;
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
            new FakeEnvironmentDetector(events), new FakeEnvironmentWaiter(events), new FakeProbeFactory(), new FakeHardwareEvaluator(), recoveryJournalStore: store, stockCenterMBaseline: new FakeBaseline(events));
        var result = await coordinator.RunAsync(CancellationToken.None);
        Assert.True(result.RecoverySafe);
        Assert.Equal(["UpdateGate", "EnvironmentDetector", "EnvironmentWaiter", "Baseline", "Discard"], events);
        Assert.Equal(0, store.DeleteCallCount);
    }

    [Fact]
    public async Task StaleJournalIsDiscardedAfterSuccessfulBaseline()
    {
        var events = new List<string>();
        var store = new FakeRecoveryJournalStore(events, exists: true, existsAfterDelete: false);
        var coordinator = new StartupCoordinator(new FakeUpdateGate(events, UpdateGateResult.Continue),
            new FakeEnvironmentDetector(events), new FakeEnvironmentWaiter(events), new FakeProbeFactory(), new FakeHardwareEvaluator(), recoveryJournalStore: store, stockCenterMBaseline: new FakeBaseline(events));
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
            new FakeEnvironmentDetector(events), new FakeEnvironmentWaiter(events), new FakeProbeFactory(), new FakeHardwareEvaluator(), recoveryJournalStore: store, stockCenterMBaseline: new FakeBaseline(events));

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
            new FakeEnvironmentDetector(events), new FakeEnvironmentWaiter(events), new FakeProbeFactory(), new FakeHardwareEvaluator(),
            recoveryJournalStore: store, stockCenterMBaseline: new FakeBaseline(events), hidHideRecoveryCleaner: cleaner);

        var result = await coordinator.RunAsync(CancellationToken.None);

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
            new FakeEnvironmentDetector(events), new FakeEnvironmentWaiter(events), new FakeProbeFactory(), new FakeHardwareEvaluator(),
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
            new FakeEnvironmentDetector(events), new FakeEnvironmentWaiter(events), new FakeProbeFactory(), new FakeHardwareEvaluator(),
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
            new FakeEnvironmentDetector(events), new FakeEnvironmentWaiter(events), new FakeProbeFactory(), new FakeHardwareEvaluator(),
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
            new FakeEnvironmentDetector(events), new FakeEnvironmentWaiter(events), new FakeProbeFactory(), new FakeHardwareEvaluator(),
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
                AddonOwnedVirtualDeviceEntries: [new(mutationId, "steamcontroller", 0x28DE, 0x1102, [], ["USB\\VID_28DE&PID_1102\\owned"])]));
        var store = new FakeRecoveryJournalStore(events, exists: true, journal: journal);
        var cleaner = new FakeStartupHidHideRecoveryCleaner();
        var coordinator = new StartupCoordinator(new FakeUpdateGate(events, UpdateGateResult.Continue),
            new FakeEnvironmentDetector(events), new FakeEnvironmentWaiter(events), new FakeProbeFactory(), new FakeHardwareEvaluator(),
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
    public async Task ConflictingManagerDetected_DoesNotInvokeHidHideCleaner()
    {
        var events = new List<string>();
        var store = new FakeRecoveryJournalStore(events, exists: true);
        var cleaner = new FakeStartupHidHideRecoveryCleaner();
        var coordinator = new StartupCoordinator(new FakeUpdateGate(events, UpdateGateResult.Continue),
            new FixedEnvironmentDetector(events, new(ControllerEnvironmentMode.HHCManaged, ClawTweaksState.NotInstalled)), new ThrowingEnvironmentWaiter(),
            new FakeProbeFactory(), new FakeHardwareEvaluator(), recoveryJournalStore: store, stockCenterMBaseline: new FakeBaseline(events), hidHideRecoveryCleaner: cleaner);

        var result = await coordinator.RunAsync(CancellationToken.None);

        Assert.False(result.RecoverySafe);
        Assert.Equal(0, cleaner.CallCount);
        Assert.Equal(0, store.DeleteCallCount);
    }

    [Fact]
    public async Task JournalDeletionThrows_DoesNotCrashAndBlocksRouting()
    {
        var events = new List<string>();
        var store = new FakeRecoveryJournalStore(events, exists: true, deleteThrows: true);
        var coordinator = new StartupCoordinator(new FakeUpdateGate(events, UpdateGateResult.Continue),
            new FakeEnvironmentDetector(events), new FakeEnvironmentWaiter(events), new FakeProbeFactory(), new FakeHardwareEvaluator(), recoveryJournalStore: store, stockCenterMBaseline: new FakeBaseline(events));

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
            new FakeEnvironmentDetector(events), new FakeEnvironmentWaiter(events), new FakeProbeFactory(), new FakeHardwareEvaluator(), recoveryJournalStore: store, stockCenterMBaseline: new FakeBaseline(events));

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
            new FakeEnvironmentDetector(events), new FakeEnvironmentWaiter(events), new FakeProbeFactory(), new FakeHardwareEvaluator(), recoveryJournalStore: store, stockCenterMBaseline: new FakeBaseline(events, false));
        var result = await coordinator.RunAsync(CancellationToken.None);
        Assert.False(result.RecoverySafe);
        Assert.Equal(ControllerEnvironmentMode.StockCenterM, result.EnvironmentMode);
        Assert.Equal(["UpdateGate", "EnvironmentDetector", "EnvironmentWaiter", "Baseline"], events);
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
            new FakeEnvironmentDetector(events), new FakeEnvironmentWaiter(events), new FakeProbeFactory(), new FakeHardwareEvaluator(), recoveryJournalStore: store, stockCenterMBaseline: new FakeBaseline(events, false));

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
            new ThrowingEnvironmentDetector(), new ThrowingEnvironmentWaiter(), new ThrowingProbeFactory(), new ThrowingHardwareEvaluator(), recoveryJournalStore: store, stockCenterMBaseline: new FakeBaseline(events));

        var result = await coordinator.RunAsync(CancellationToken.None);

        Assert.False(result.ShouldStartRuntime);
        Assert.False(result.RecoverySafe);
        Assert.Equal(["UpdateGate"], events);
        Assert.Equal(0, store.DeleteCallCount);
    }

    [Fact]
    public async Task NonStockEnvironment_DoesNotRunBaselineOrJournalDiscard()
    {
        var events = new List<string>();
        var store = new FakeRecoveryJournalStore(events, exists: true);
        var coordinator = new StartupCoordinator(new FakeUpdateGate(events, UpdateGateResult.Continue),
            new FakeEnvironmentDetector(events, ClawTweaksState.InstalledInactive), new ThrowingEnvironmentWaiter(), new FakeProbeFactory(), new FakeHardwareEvaluator(),
            recoveryJournalStore: store, stockCenterMBaseline: new FakeBaseline(events));

        var result = await coordinator.RunAsync(CancellationToken.None);

        Assert.True(result.ShouldStartRuntime);
        Assert.DoesNotContain("Baseline", events);
        Assert.DoesNotContain("Discard", events);
        Assert.Equal(0, store.DeleteCallCount);
    }

    [Fact]
    public async Task CancellationDuringBaseline_Propagates()
    {
        var events = new List<string>();
        using var cancellation = new CancellationTokenSource();
        var store = new FakeRecoveryJournalStore(events, exists: false);
        var coordinator = new StartupCoordinator(new FakeUpdateGate(events, UpdateGateResult.Continue),
            new FakeEnvironmentDetector(events), new FakeEnvironmentWaiter(events), new FakeProbeFactory(), new FakeHardwareEvaluator(),
            recoveryJournalStore: store, stockCenterMBaseline: new ThrowingBaseline(events, new OperationCanceledException(cancellation.Token)));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => coordinator.RunAsync(cancellation.Token));

        Assert.Equal(["UpdateGate", "EnvironmentDetector", "EnvironmentWaiter", "Baseline"], events);
        Assert.Equal(0, store.DeleteCallCount);
    }

    [Fact]
    public async Task CanStartRuntimeAsync_WhenNoUpdateExists_WaitsForEnvironmentAfterUpdateGate()
    {
        var events = new List<string>();
        var store = new FakeRecoveryJournalStore(events, exists: false);
        var coordinator = new StartupCoordinator(
            new FakeUpdateGate(events, UpdateGateResult.Continue),
            new FakeEnvironmentDetector(events),
            new FakeEnvironmentWaiter(events), new FakeProbeFactory(), new FakeHardwareEvaluator(), recoveryJournalStore: store);

        var result = await coordinator.RunAsync(CancellationToken.None);

        Assert.True(result.ShouldStartRuntime);
        Assert.Equal(ControllerEnvironmentMode.StockCenterM, result.EnvironmentMode);
        Assert.False(result.RecoverySafe);
        Assert.Equal(["UpdateGate", "EnvironmentDetector", "EnvironmentWaiter"], events);
        Assert.Equal(0, store.DeleteCallCount);
    }

    [Fact]
    public async Task CanStartRuntimeAsync_WhenUpdateIsScheduled_DoesNotInitializeEnvironment()
    {
        var events = new List<string>();
        var store = new FakeRecoveryJournalStore(events, exists: false);
        var coordinator = new StartupCoordinator(
            new FakeUpdateGate(events, UpdateGateResult.RestartScheduled),
            new FakeEnvironmentDetector(events),
            new FakeEnvironmentWaiter(events), new FakeProbeFactory(), new FakeHardwareEvaluator(), recoveryJournalStore: store);

        var result = await coordinator.RunAsync(CancellationToken.None);

        Assert.False(result.ShouldStartRuntime);
        Assert.Equal(ControllerEnvironmentMode.Indeterminate, result.EnvironmentMode);
        Assert.Equal(["UpdateGate"], events);
        Assert.Equal(0, store.DeleteCallCount);
    }

    [Fact]
    public async Task RunAsync_WhenClawTweaksIsStarting_DoesNotPollOrWaitForStabilization()
    {
        var events = new List<string>();
        var store = new FakeRecoveryJournalStore(events, exists: false);
        var coordinator = new StartupCoordinator(
            new FakeUpdateGate(events, UpdateGateResult.Continue),
            new FakeEnvironmentDetector(events, ClawTweaksState.Starting),
            new FakeEnvironmentWaiter(events),
            new FakeProbeFactory(), new FakeHardwareEvaluator(), recoveryJournalStore: store);

        var result = await coordinator.RunAsync(CancellationToken.None);

        Assert.Equal(ControllerEnvironmentReadiness.Indeterminate, result.EnvironmentReadiness);
        Assert.Equal(ControllerEnvironmentMode.Indeterminate, result.EnvironmentMode);
        Assert.False(result.RecoverySafe);
        Assert.Equal(["UpdateGate", "EnvironmentDetector"], events);
        Assert.Equal(0, store.DeleteCallCount);
    }

    [Fact]
    public async Task RunAsync_WhenClawTweaksIsInstalledOrRunning_ReturnsPassiveWithoutReadinessWait()
    {
        var events = new List<string>();
        var store = new FakeRecoveryJournalStore(events, exists: true);
        var coordinator = new StartupCoordinator(
            new FakeUpdateGate(events, UpdateGateResult.Continue),
            new FakeEnvironmentDetector(events, ClawTweaksState.InstalledInactive),
            new FakeEnvironmentWaiter(events),
            new FakeProbeFactory(), new FakeHardwareEvaluator(), recoveryJournalStore: store);

        var result = await coordinator.RunAsync(CancellationToken.None);

        Assert.True(result.ShouldStartRuntime);
        Assert.Equal(ControllerEnvironmentReadiness.NotApplicable, result.EnvironmentReadiness);
        Assert.Equal(ControllerEnvironmentMode.Unsupported, result.EnvironmentMode);
        Assert.False(result.RecoverySafe);
        Assert.DoesNotContain("EnvironmentWaiter", events);
        Assert.Equal(0, store.DeleteCallCount);
    }

    [Fact]
    public async Task RunAsync_WhenClawTweaksIsInstalledInactive_IsUnsupportedAndPassive()
    {
        var events = new List<string>();
        var waiter = new FakeEnvironmentWaiter(events);
        var store = new FakeRecoveryJournalStore(events, exists: true);
        var coordinator = new StartupCoordinator(
            new FakeUpdateGate(events, UpdateGateResult.Continue),
            new FakeEnvironmentDetector(events, ClawTweaksState.InstalledInactive),
            waiter, new FakeProbeFactory(), new FakeHardwareEvaluator(), recoveryJournalStore: store);

        var result = await coordinator.RunAsync(CancellationToken.None);

        Assert.Equal(ControllerEnvironmentMode.Unsupported, result.EnvironmentMode);
        Assert.False(result.RecoverySafe);
        Assert.Empty(waiter.Modes);
        Assert.Equal(0, store.DeleteCallCount);
    }

    [Fact]
    public async Task RunAsync_WhenClawTweaksStateIsIndeterminate_SkipsReadinessChecks()
    {
        var events = new List<string>();
        var store = new FakeRecoveryJournalStore(events, exists: true);
        var coordinator = new StartupCoordinator(
            new FakeUpdateGate(events, UpdateGateResult.Continue),
            new FakeEnvironmentDetector(events, ClawTweaksState.Indeterminate),
            new FakeEnvironmentWaiter(events), new FakeProbeFactory(), new FakeHardwareEvaluator(), recoveryJournalStore: store);

        var result = await coordinator.RunAsync(CancellationToken.None);

        Assert.Equal(ControllerEnvironmentMode.Indeterminate, result.EnvironmentMode);
        Assert.Equal(ControllerEnvironmentReadiness.Indeterminate, result.EnvironmentReadiness);
        Assert.False(result.RecoverySafe);
        Assert.DoesNotContain("EnvironmentWaiter", events);
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
        RecoveryJournal? journal = null, bool readTextThrows = false, bool malformedJson = false) : IRecoveryJournalStore
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
            var effective = journal ?? new RecoveryJournal(RecoveryManager.CurrentSchemaVersion, Guid.NewGuid(), DateTimeOffset.UtcNow, null, new());
            return JsonSerializer.Serialize(effective);
        }

        public void WriteNew(RecoveryJournal journal) => throw new NotSupportedException("StartupCoordinator must never write recovery journal contents.");
        public void ReplaceExisting(RecoveryJournal journal) => throw new NotSupportedException("StartupCoordinator must never replace recovery journal contents.");

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

    [Fact]
    public async Task ClawTweaksMode_DoesNotRunStockBaselineOrJournalDiscard()
    {
        var events = new List<string>();
        var store = new FakeRecoveryJournalStore(events, exists: true);
        var coordinator = new StartupCoordinator(new FakeUpdateGate(events, UpdateGateResult.Continue),
            new FixedEnvironmentDetector(events, new(ControllerEnvironmentMode.ClawTweaks, ClawTweaksState.Active)), new ThrowingEnvironmentWaiter(),
            new FakeProbeFactory(), new FakeHardwareEvaluator(), recoveryJournalStore: store, stockCenterMBaseline: new FakeBaseline(events));

        var result = await coordinator.RunAsync(CancellationToken.None);

        Assert.Equal(ControllerEnvironmentMode.Unsupported, result.EnvironmentMode);
        Assert.False(result.RecoverySafe);
        Assert.DoesNotContain("Baseline", events);
        Assert.DoesNotContain("Discard", events);
        Assert.Equal(0, store.DeleteCallCount);
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
        var coordinator = new StartupCoordinator(new FakeUpdateGate(events, UpdateGateResult.Continue), new ThrowingEnvironmentDetector(), new ThrowingEnvironmentWaiter(),
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
        var coordinator = new StartupCoordinator(new FakeUpdateGate(events, UpdateGateResult.Continue), new FakeEnvironmentDetector(events),
            new FixedEnvironmentWaiter(events, ControllerEnvironmentReadiness.Indeterminate), new FakeProbeFactory(), new FakeHardwareEvaluator(),
            recoveryJournalStore: store, stockCenterMBaseline: new FakeBaseline(events));

        var result = await coordinator.RunAsync(CancellationToken.None);

        Assert.False(result.RecoverySafe);
        Assert.DoesNotContain("Baseline", events);
        Assert.DoesNotContain("Discard", events);
        Assert.Equal(0, store.DeleteCallCount);
    }

    [Theory]
    [InlineData((int)ControllerEnvironmentCompatibilityStatus.Indeterminate, (int)ControllerEnvironmentCompatibilityReason.MsiCenterMStarting)]
    [InlineData((int)ControllerEnvironmentCompatibilityStatus.Unsupported, (int)ControllerEnvironmentCompatibilityReason.MsiCenterMNotOperational)]
    public async Task StockEnvironment_ReachesBaselineRegardlessOfCenterMRuntimeState(int statusValue, int reasonValue)
    {
        // Policy: MSI Center M being fully Running is NOT a prerequisite for controller
        // ownership. When no conflicting controller manager is present (Mode == StockCenterM)
        // and the actual MSI hardware topology is Stable, startup must proceed to baseline even
        // if Center M itself is still Starting or NotRunning -- that is a Center M process-state
        // fact, not a controller ownership conflict, and must not gate hardware access.
        var events = new List<string>();
        var store = new FakeRecoveryJournalStore(events, exists: false);
        var coordinator = new StartupCoordinator(new FakeUpdateGate(events, UpdateGateResult.Continue),
            new FixedCompatibilityEnvironmentDetector(events, (ControllerEnvironmentCompatibilityStatus)statusValue, (ControllerEnvironmentCompatibilityReason)reasonValue),
            new FakeEnvironmentWaiter(events), new FakeProbeFactory(), new FakeHardwareEvaluator(),
            recoveryJournalStore: store, stockCenterMBaseline: new FakeBaseline(events));

        var result = await coordinator.RunAsync(CancellationToken.None);

        Assert.Equal(ControllerEnvironmentMode.StockCenterM, result.EnvironmentMode);
        Assert.Equal(ControllerEnvironmentReadiness.Stable, result.EnvironmentReadiness);
        Assert.Contains("Baseline", events);
        Assert.True(result.RecoverySafe);
    }

    [Fact]
    public async Task HandheldCompanionDetected_DoesNotRunStockBaselineOrJournalDiscard()
    {
        var events = new List<string>();
        var store = new FakeRecoveryJournalStore(events, exists: true);
        var coordinator = new StartupCoordinator(new FakeUpdateGate(events, UpdateGateResult.Continue),
            new FixedEnvironmentDetector(events, new(ControllerEnvironmentMode.HHCManaged, ClawTweaksState.NotInstalled)), new ThrowingEnvironmentWaiter(),
            new FakeProbeFactory(), new FakeHardwareEvaluator(), recoveryJournalStore: store, stockCenterMBaseline: new FakeBaseline(events));

        var result = await coordinator.RunAsync(CancellationToken.None);

        Assert.Equal(ControllerEnvironmentMode.HHCManaged, result.EnvironmentMode);
        Assert.False(result.RecoverySafe);
        Assert.DoesNotContain("Baseline", events);
        Assert.DoesNotContain("Discard", events);
        Assert.Equal(0, store.DeleteCallCount);
    }

    private sealed class FixedCompatibilityEnvironmentDetector(List<string> events, ControllerEnvironmentCompatibilityStatus status, ControllerEnvironmentCompatibilityReason reason) : IControllerEnvironmentAssessmentProvider
    {
        public ControllerEnvironmentAssessmentSnapshot Capture()
        {
            events.Add("EnvironmentDetector");
            return new([], new(ControllerManagerKind.None, ControllerManagerClassificationReason.NoThirdPartyControllerManager), new(status, reason));
        }
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

    private sealed class FakeEnvironmentWaiter(List<string> events) : IControllerEnvironmentWaiter
    {
        public List<ControllerEnvironmentMode> Modes { get; } = [];

        public Task<ControllerEnvironmentReadiness> WaitUntilStableAsync(ControllerEnvironmentMode mode, CancellationToken cancellationToken)
        {
            events.Add("EnvironmentWaiter");
            Modes.Add(mode);
            return Task.FromResult(ControllerEnvironmentReadiness.Stable);
        }
    }

    private sealed class FakeEnvironmentDetector(List<string> events, params ClawTweaksState[] states) : IControllerEnvironmentAssessmentProvider
    {
        private readonly Queue<ClawTweaksState> _states = new(states.Length == 0 ? [ClawTweaksState.NotInstalled] : states);

        public ControllerEnvironmentAssessmentSnapshot Capture()
        {
            events.Add("EnvironmentDetector");
            var state = _states.Count > 1 ? _states.Dequeue() : _states.Peek();
            var mode = state switch
            {
                ClawTweaksState.Active or ClawTweaksState.InstalledInactive => ControllerEnvironmentMode.Unsupported,
                ClawTweaksState.NotInstalled => ControllerEnvironmentMode.StockCenterM,
                _ => ControllerEnvironmentMode.Indeterminate
            };
            return Assessment(mode);
        }
    }

    private sealed class FixedEnvironmentWaiter(List<string> events, ControllerEnvironmentReadiness readiness) : IControllerEnvironmentWaiter
    {
        public Task<ControllerEnvironmentReadiness> WaitUntilStableAsync(ControllerEnvironmentMode mode, CancellationToken cancellationToken)
        {
            events.Add("EnvironmentWaiter");
            return Task.FromResult(readiness);
        }
    }

    private sealed class FixedEnvironmentDetector(List<string> events, ControllerEnvironment environment) : IControllerEnvironmentAssessmentProvider
    {
        public ControllerEnvironmentAssessmentSnapshot Capture() { events.Add("EnvironmentDetector"); return Assessment(environment.Mode); }
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
        var coordinator = new StartupCoordinator(new FakeUpdateGate(events, UpdateGateResult.Continue), new FakeEnvironmentDetector(events), new FakeEnvironmentWaiter(events),
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
        var coordinator = new StartupCoordinator(new FakeUpdateGate(events, UpdateGateResult.Continue), new FakeEnvironmentDetector(events), new FakeEnvironmentWaiter(events),
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
        var coordinator = new StartupCoordinator(new FakeUpdateGate(events, UpdateGateResult.Continue), new ThrowingEnvironmentDetector(), new ThrowingEnvironmentWaiter(),
            new FakeProbeFactory(), evaluator, recoveryJournalStore: store, stockCenterMBaseline: new FakeBaseline(events), hardwareProbeDelay: delay.DelayAsync);

        var result = await coordinator.RunAsync(CancellationToken.None);

        Assert.Equal(1, evaluator.CallCount);
        Assert.Empty(delay.Delays);
        Assert.Equal(ControllerEnvironmentMode.Unsupported, result.EnvironmentMode);
    }

    [Fact]
    public async Task HardwareSupportedOnFirstAttempt_NoDelay()
    {
        var events = new List<string>();
        var store = new FakeRecoveryJournalStore(events, exists: false);
        var delay = new RecordingHardwareProbeDelay();
        var coordinator = new StartupCoordinator(new FakeUpdateGate(events, UpdateGateResult.Continue), new FakeEnvironmentDetector(events), new FakeEnvironmentWaiter(events),
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
        var coordinator = new StartupCoordinator(new FakeUpdateGate(events, UpdateGateResult.Continue), new ThrowingEnvironmentDetector(), new ThrowingEnvironmentWaiter(),
            new FakeProbeFactory(), evaluator, recoveryJournalStore: store, stockCenterMBaseline: new FakeBaseline(events),
            hardwareProbeTimeout: TimeSpan.FromMilliseconds(20), hardwareProbeDelay: (_, _) => Task.CompletedTask);

        var result = await coordinator.RunAsync(CancellationToken.None);

        Assert.True(evaluator.CallCount > 1);
        Assert.Equal(ControllerEnvironmentMode.Indeterminate, result.EnvironmentMode);
        Assert.False(result.RecoverySafe);
    }

    [Fact]
    public async Task CancellationDuringHardwareProbeStabilization_Propagates()
    {
        var events = new List<string>();
        var store = new FakeRecoveryJournalStore(events, exists: false);
        using var cancellation = new CancellationTokenSource();
        var evaluator = new SequencedHardwareEvaluator([new(HardwareCompatibilityStatus.Indeterminate, null, null, "test")], repeatLast: true);
        var coordinator = new StartupCoordinator(new FakeUpdateGate(events, UpdateGateResult.Continue), new ThrowingEnvironmentDetector(), new ThrowingEnvironmentWaiter(),
            new FakeProbeFactory(), evaluator, recoveryJournalStore: store, stockCenterMBaseline: new FakeBaseline(events),
            hardwareProbeDelay: (_, token) => { cancellation.Cancel(); token.ThrowIfCancellationRequested(); return Task.CompletedTask; });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => coordinator.RunAsync(cancellation.Token));

        Assert.Equal(1, evaluator.CallCount);
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
    private sealed class ThrowingEnvironmentDetector : IControllerEnvironmentAssessmentProvider { public ControllerEnvironmentAssessmentSnapshot Capture() => throw new Xunit.Sdk.XunitException("Environment assessment must not run."); }
    private sealed class ThrowingEnvironmentWaiter : IControllerEnvironmentWaiter { public Task<ControllerEnvironmentReadiness> WaitUntilStableAsync(ControllerEnvironmentMode _, CancellationToken __) => throw new Xunit.Sdk.XunitException("Environment wait must not run after recovery failure."); }
    private sealed class ThrowingProbeFactory : IWindowsDeviceProbeContextFactory { public DeviceProbeContextCapture Capture() => throw new Xunit.Sdk.XunitException("Hardware probe must not run after recovery failure."); }
    private sealed class ThrowingHardwareEvaluator : IHardwareCompatibilityEvaluator { public HardwareCompatibilityAssessment Evaluate(DeviceProbeContextCapture _) => throw new Xunit.Sdk.XunitException("Hardware evaluator must not run after recovery failure."); }

    private static ControllerEnvironmentAssessmentSnapshot Assessment(ControllerEnvironmentMode mode)
    {
        var (manager, compatibility) = mode switch
        {
            ControllerEnvironmentMode.StockCenterM => (new ControllerManagerClassification(ControllerManagerKind.None, ControllerManagerClassificationReason.NoThirdPartyControllerManager), new ControllerEnvironmentCompatibilityAssessment(ControllerEnvironmentCompatibilityStatus.Supported, ControllerEnvironmentCompatibilityReason.StockCenterMOnlySupported)),
            ControllerEnvironmentMode.HHCManaged => (new ControllerManagerClassification(ControllerManagerKind.HandheldCompanion, ControllerManagerClassificationReason.HandheldCompanionDetected), new ControllerEnvironmentCompatibilityAssessment(ControllerEnvironmentCompatibilityStatus.Unsupported, ControllerEnvironmentCompatibilityReason.HandheldCompanionNotSupportedByCurrentVersion)),
            ControllerEnvironmentMode.Indeterminate => (new ControllerManagerClassification(ControllerManagerKind.Indeterminate, ControllerManagerClassificationReason.ControllerManagerStateIndeterminate), new ControllerEnvironmentCompatibilityAssessment(ControllerEnvironmentCompatibilityStatus.Indeterminate, ControllerEnvironmentCompatibilityReason.ControllerSoftwareStateIndeterminate)),
            _ => (new ControllerManagerClassification(ControllerManagerKind.ClawTweaks, ControllerManagerClassificationReason.ClawTweaksDetected), new ControllerEnvironmentCompatibilityAssessment(ControllerEnvironmentCompatibilityStatus.Unsupported, ControllerEnvironmentCompatibilityReason.ClawTweaksNotSupportedByCurrentVersion))
        };
        return new([], manager, compatibility);
    }
}
