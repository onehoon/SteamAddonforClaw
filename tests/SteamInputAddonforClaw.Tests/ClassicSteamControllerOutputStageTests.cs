using SteamInputAddonforClaw.Controllers.Detection;
using SteamInputAddonforClaw.HidHide;
using SteamInputAddonforClaw.Power;
using SteamInputAddonforClaw.Recovery;
using SteamInputAddonforClaw.VirtualOutput.Viiper;
using SteamInputAddonforClaw.Devices.MSI.Claw;
using SteamInputAddonforClaw.Input;
using SteamInputAddonforClaw.Diagnostics;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

[Collection("AppLog")]
public sealed class ClassicSteamControllerOutputStageTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "ClawSteamOutputTests", Guid.NewGuid().ToString("N"));
    private readonly Guid _session = Guid.NewGuid();

    [Fact]
    public async Task CanonicalSessionPathUsesTypedPublisherAndCleanupOrder()
    {
        var session = new FakeCanonicalSession();
        var stage = CreateCanonical(session, new FakeEnumerator([[], [UsbIpHost(), Device("owned")], []]), new FakeHidHide());
        await stage.PrepareMutationAsync(CancellationToken.None);

        var created = await stage.ExecuteMutationAsync(CancellationToken.None);
        Assert.True(created.Succeeded, created.Reason);

        var rollback = await stage.RollbackMutationAsync(CancellationToken.None);
        Assert.True(rollback.Succeeded, rollback.Reason);
        Assert.Equal(["Start", "Neutral", "Remove", "CompleteCleanup", "Dispose"], session.Trace);
    }

    [Fact]
    public async Task CanonicalBusRemovalRetryDoesNotReplayDeviceRemoval()
    {
        var session = new FakeCanonicalSession { CleanupFailure = CanonicalPendingCleanupPhase.BusRemoval };
        var stage = CreateCanonical(session, new FakeEnumerator([[], [UsbIpHost(), Device("owned")], []]), new FakeHidHide());
        await stage.PrepareMutationAsync(CancellationToken.None);
        Assert.True((await stage.ExecuteMutationAsync(CancellationToken.None)).Succeeded);

        Assert.False((await stage.RollbackMutationAsync(CancellationToken.None)).Succeeded);
        Assert.True((await stage.RollbackMutationAsync(CancellationToken.None)).Succeeded);
        Assert.Equal(1, session.RemoveCalls);
        Assert.Equal(["Start", "Neutral", "Remove", "CompleteCleanup", "Retry:BusRemoval", "Dispose"], session.Trace);
    }

    [Fact]
    public async Task CanonicalServerCloseRetryDoesNotReplayDeviceRemoval()
    {
        var session = new FakeCanonicalSession { CleanupFailure = CanonicalPendingCleanupPhase.ServerClose };
        var stage = CreateCanonical(session, new FakeEnumerator([[], [UsbIpHost(), Device("owned")], []]), new FakeHidHide());
        await stage.PrepareMutationAsync(CancellationToken.None);
        Assert.True((await stage.ExecuteMutationAsync(CancellationToken.None)).Succeeded);

        Assert.False((await stage.RollbackMutationAsync(CancellationToken.None)).Succeeded);
        Assert.True((await stage.RollbackMutationAsync(CancellationToken.None)).Succeeded);
        Assert.Equal(1, session.RemoveCalls);
        Assert.Equal(["Start", "Neutral", "Remove", "CompleteCleanup", "Retry:ServerClose", "Dispose"], session.Trace);
    }

    [Fact]
    public async Task CanonicalFactoryFailureLeavesNoRecoveryBoundaryOrOwnershipUncertainty()
    {
        var stage = CreateCanonicalFactoryFailure(new FakeEnumerator([[]]), new FakeHidHide());

        var result = await stage.ExecuteMutationAsync(CancellationToken.None);
        var rollback = await stage.RollbackMutationAsync(CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.True(rollback.Succeeded);
        Assert.Equal(RecoveryStatus.Success, new RecoveryManager(new RecoveryJournalStore(Path.Combine(_directory, "factory-failure-recovery.json"))).LoadJournal().Status);
    }

    [Fact]
    public async Task SuccessfulCreationResolvesPnPAndSendsOneNeutralReport()
    {
        var runtime = new FakeRuntime();
        var stage = Create(runtime, new FakeEnumerator([[], [UsbIpHost(), Device("owned")], []]), new FakeHidHide());
        Assert.True((await stage.PrepareMutationAsync(CancellationToken.None)).Succeeded);
        var result = await stage.ExecuteMutationAsync(CancellationToken.None);
        Assert.True(result.Succeeded, result.Reason);
        Assert.Equal(1, runtime.NeutralReports);
        Assert.True((await stage.RollbackMutationAsync(CancellationToken.None)).Succeeded);
    }

    [Fact]
    public async Task HidHideInspectionFailureRollsBackAndLeavesNoOwnedRuntimeDevice()
    {
        var runtime = new FakeRuntime();
        var stage = Create(runtime, new FakeEnumerator([[], [UsbIpHost(), Device("owned")], []]), new FakeHidHide { Inspection = new(HidHideInspectionStatus.ConfigurationUnavailable, new HashSet<string>()) });
        await stage.PrepareMutationAsync(CancellationToken.None);
        var result = await stage.ExecuteMutationAsync(CancellationToken.None);
        Assert.False(result.Succeeded);
        Assert.Equal(1, runtime.RemovedDevices);
    }

    [Fact]
    public async Task PreExistingHidHideOutputBlockIsPreserved()
    {
        var runtime = new FakeRuntime();
        var hidHide = new FakeHidHide { Inspection = new(HidHideInspectionStatus.Available, new HashSet<string>(), ["owned"]) };
        var stage = Create(runtime, new FakeEnumerator([[], [UsbIpHost(), Device("owned")], []]), hidHide);
        await stage.PrepareMutationAsync(CancellationToken.None);

        var result = await stage.ExecuteMutationAsync(CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains("HidHideOutputAlreadyBlocked", result.Reason);
        Assert.Contains("owned", hidHide.Inspection.HiddenDeviceEntries!);
        Assert.Equal(1, runtime.RemovedDevices);
    }

    [Fact]
    public async Task PnPTimeoutRollsBackAddDeviceSuccess()
    {
        var runtime = new FakeRuntime();
        var stage = Create(runtime, new FakeEnumerator([[], []]), new FakeHidHide(), TimeSpan.FromMilliseconds(1));
        await stage.PrepareMutationAsync(CancellationToken.None);
        var result = await stage.ExecuteMutationAsync(CancellationToken.None);
        Assert.False(result.Succeeded);
        Assert.Equal(1, runtime.RemovedDevices);
    }

    [Fact]
    public async Task PnPTimeoutEmitsExactlyOneBoundedIdentityDiagnosticDumpNotOnePerPoll()
    {
        var runtime = new FakeRuntime();
        // ~100 polling iterations at the fixed 1ms poll interval used by the Create() helper.
        var stage = Create(runtime, new FakeEnumerator([[], []]), new FakeHidHide(), TimeSpan.FromMilliseconds(100));
        await stage.PrepareMutationAsync(CancellationToken.None);

        var result = await stage.ExecuteMutationAsync(CancellationToken.None);

        Assert.False(result.Succeeded);
        var log = File.ReadAllText(AppLog.CurrentLogFilePath);
        var occurrences = log.Split("ViiperIdentityDiagnosticSummary").Length - 1;
        Assert.Equal(1, occurrences);
    }

    [Fact]
    public async Task IdentityFailureRollbackFailsWhenPotentialGordonNodeStaysPresentAfterRemoval()
    {
        // The usbip-win2 host ancestor record is missing from the snapshot, so identity
        // resolution correctly fails closed (MissingUsbIpWin2Ancestor). But the 28DE:1102 node
        // that appeared during the attempt does NOT actually disappear after RemoveDevice() in
        // this fixture -- rollback's absence verification must catch that using the exact
        // InstanceId observed at failure time, not by re-running the same strict ownership
        // predicate that already rejected it (which would trivially report "no matching
        // candidate" -> false-positive absence).
        var gordon = Device("USB\\VID_28DE&PID_1102\\STAYS");
        var enumerator = new GordonPresenceEnumerator(gordon);
        var runtime = new FakeRuntime();
        var stage = Create(runtime, enumerator, new FakeHidHide(), TimeSpan.FromMilliseconds(50));
        await stage.PrepareMutationAsync(CancellationToken.None);

        var result = await stage.ExecuteMutationAsync(CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains("VirtualDevicePnPStillPresent", result.Reason);
        Assert.Equal(1, runtime.RemovedDevices);
    }

    [Fact]
    public async Task IdentityFailureRollbackSucceedsAfterPotentialGordonNodeDisappears()
    {
        var gordon = Device("USB\\VID_28DE&PID_1102\\DISAPPEARS");
        var enumerator = new GordonPresenceEnumerator(gordon);
        var runtime = new FakeRuntime { OnRemoveDeviceCalled = () => enumerator.DeviceRemoved = true };
        var stage = Create(runtime, enumerator, new FakeHidHide(), TimeSpan.FromMilliseconds(50));
        await stage.PrepareMutationAsync(CancellationToken.None);

        var result = await stage.ExecuteMutationAsync(CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains("Rollback=ClassicSteamControllerRemoved", result.Reason);
        Assert.DoesNotContain("VirtualDevicePnPStillPresent", result.Reason);
        Assert.Equal(1, runtime.RemovedDevices);
    }

    [Fact]
    public async Task SuccessfulResolutionEmitsNoIdentityDiagnosticDump()
    {
        var runtime = new FakeRuntime();
        var stage = Create(runtime, new FakeEnumerator([[], [UsbIpHost(), Device("owned")], []]), new FakeHidHide());
        await stage.PrepareMutationAsync(CancellationToken.None);

        var result = await stage.ExecuteMutationAsync(CancellationToken.None);

        Assert.True(result.Succeeded, result.Reason);
        var log = File.ReadAllText(AppLog.CurrentLogFilePath);
        Assert.DoesNotContain("ViiperIdentityDiagnosticSummary", log);
        await stage.RollbackMutationAsync(CancellationToken.None);
    }

    [Fact]
    public async Task ExecuteCancellationAfterIntentRollsBackRecordedMutation()
    {
        var runtime = new FakeRuntime { CancelAfterStart = true };
        var stage = Create(runtime, new FakeEnumerator([[], []]), new FakeHidHide());
        await stage.PrepareMutationAsync(CancellationToken.None);

        var result = await stage.ExecuteMutationAsync(CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains("SteamOutputCreationCancelled", result.Reason);
        Assert.Equal(RecoveryStatus.NoRecoveryNeeded, new RecoveryManager(new RecoveryJournalStore(Path.Combine(_directory, "recovery.json"))).LoadJournal().Status);
    }

    [Fact]
    public async Task RecoveryIntentWriteFailureDoesNotEnterCreatingRollbackBoundary()
    {
        var runtime = new FakeRuntime();
        var stage = Create(runtime, new FakeEnumerator([[]]), new FakeHidHide(), storeWriteFailsAfterSeed: true);
        await stage.PrepareMutationAsync(CancellationToken.None);

        var result = await stage.ExecuteMutationAsync(CancellationToken.None);
        var rollback = await stage.RollbackMutationAsync(CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("VirtualDeviceRecoveryIntentFailed", result.Reason);
        Assert.True(rollback.Succeeded);
        Assert.Equal(0, runtime.CreatedDevices);
    }

    [Fact]
    public async Task CallerCancellationDuringPnPWaitRollsBackIntentAndDevice()
    {
        var runtime = new FakeRuntime();
        var stage = Create(runtime, new FakeEnumerator([[], []]), new FakeHidHide(), TimeSpan.FromSeconds(5));
        using var cancellation = new CancellationTokenSource();
        await stage.PrepareMutationAsync(CancellationToken.None);

        var creation = stage.ExecuteMutationAsync(cancellation.Token).AsTask();
        Assert.True(SpinWait.SpinUntil(() => runtime.CreatedDevices == 1, TimeSpan.FromSeconds(5)));
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => creation);
        Assert.Equal(1, runtime.RemovedDevices);
        Assert.Equal(RecoveryStatus.NoRecoveryNeeded, new RecoveryManager(new RecoveryJournalStore(Path.Combine(_directory, "recovery.json"))).LoadJournal().Status);
    }

    [Fact]
    public async Task CancellationBoundaryStopsBeforeMutation()
    {
        var runtime = new FakeRuntime();
        var stage = Create(runtime, new FakeEnumerator([[]]), new FakeHidHide());
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(async () => await stage.PrepareMutationAsync(cancellation.Token));
        Assert.Equal(0, runtime.CreatedDevices);
    }

    [Fact]
    public async Task StageExposesNoIndependentPowerHooksAndOnlyThePipelineRollbackRemovesGordon()
    {
        // RoutingPipelineRuntimeCoordinator owns complete suspend teardown through the canonical
        // frozen-plan pipeline rollback. This stage must not independently participate in
        // suspend/resume power notifications -- that would be a duplicate suspend-ownership path
        // racing the pipeline rollback that also targets this stage. The only removal path is the
        // canonical pipeline rollback (RollbackMutationAsync).
        Assert.False(typeof(IPowerSuspendParticipant).IsAssignableFrom(typeof(ClassicSteamControllerOutputStage)));

        var runtime = new FakeRuntime();
        var stage = Create(runtime, new FakeEnumerator([[], [UsbIpHost(), Device("owned")], []]), new FakeHidHide());
        await stage.PrepareMutationAsync(CancellationToken.None);
        Assert.True((await stage.ExecuteMutationAsync(CancellationToken.None)).Succeeded);

        var rollback = await stage.RollbackMutationAsync(CancellationToken.None);
        Assert.True(rollback.Succeeded, rollback.Reason);
        Assert.Equal(1, runtime.RemovedDevices);
    }

    [Fact]
    public async Task BusCleanupFailureDoesNotBlockVerifiedDeviceMutationCompletion()
    {
        var runtime = new FakeRuntime { BusRemoved = false };
        var stage = Create(runtime, new FakeEnumerator([[], [UsbIpHost(), Device("owned")], []]), new FakeHidHide());
        await stage.PrepareMutationAsync(CancellationToken.None);
        Assert.True((await stage.ExecuteMutationAsync(CancellationToken.None)).Succeeded);

        var rollback = await stage.RollbackMutationAsync(CancellationToken.None);

        Assert.True(rollback.Succeeded);
        Assert.Equal(1, runtime.RemovedDevices);
        Assert.Equal(RecoveryStatus.NoRecoveryNeeded, new RecoveryManager(new RecoveryJournalStore(Path.Combine(_directory, "recovery.json"))).LoadJournal().Status);
    }

    [Fact]
    public async Task InactiveAndDoubleRollbackAreSuccessfulNoOps()
    {
        var runtime = new FakeRuntime();
        var stage = Create(runtime, new FakeEnumerator([[]]), new FakeHidHide());
        Assert.True((await stage.RollbackMutationAsync(CancellationToken.None)).Succeeded);
        await stage.PrepareMutationAsync(CancellationToken.None);
        var rollback = await stage.RollbackMutationAsync(CancellationToken.None);
        Assert.True(rollback.Succeeded);
        Assert.True((await stage.RollbackMutationAsync(CancellationToken.None)).Succeeded);
    }

    [Fact]
    public async Task LivePublisherStartsAfterNeutralAndStopsBeforeDeviceRemoval()
    {
        var runtime = new FakeRuntime();
        var ticks = new ManualTicks(); runtime.BlockInput = true;
        var stage = Create(runtime, new FakeEnumerator([[], [UsbIpHost(), Device("owned")], []]), new FakeHidHide(), snapshot: new FakeSnapshot(), reportTicks: ticks);
        await stage.PrepareMutationAsync(CancellationToken.None);
        Assert.True((await stage.ExecuteMutationAsync(CancellationToken.None)).Succeeded);
        ticks.Tick(); await runtime.InputEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var rollback = stage.RollbackMutationAsync(CancellationToken.None).AsTask();
        await Task.Yield(); Assert.Equal(0, runtime.RemovedDevices);
        runtime.ReleaseInput.TrySetResult();
        Assert.True((await rollback).Succeeded);
        Assert.True(runtime.Trace.IndexOf("Neutral") < runtime.Trace.IndexOf("Input"));
        Assert.True(runtime.Trace.LastIndexOf("Input") < runtime.Trace.IndexOf("Remove"));
    }

    [Fact]
    public async Task NeutralRejectionDoesNotStartPublisher()
    {
        var runtime = new FakeRuntime { NeutralAccepted = false };
        var stage = Create(runtime, new FakeEnumerator([[], [UsbIpHost(), Device("owned")], []]), new FakeHidHide(), snapshot: new FakeSnapshot());
        await stage.PrepareMutationAsync(CancellationToken.None);
        var result = await stage.ExecuteMutationAsync(CancellationToken.None);
        Assert.False(result.Succeeded);
        Assert.DoesNotContain("Input", runtime.Trace);
    }

    [Fact]
    public async Task NeutralRejectionRetainsFailureOperationTimingAndLogsOnce()
    {
        var runtime = new FakeRuntime { NeutralAccepted = false };
        var stage = Create(runtime, new FakeEnumerator([[], [UsbIpHost(), Device("owned")], []]), new FakeHidHide(), snapshot: new FakeSnapshot());
        await stage.PrepareMutationAsync(CancellationToken.None);

        var result = await stage.ExecuteMutationAsync(CancellationToken.None);

        Assert.False(result.Succeeded);
        var log = File.ReadAllText(AppLog.CurrentLogFilePath);
        Assert.Equal(1, log.Split("Event=SteamOutputCreationFailed", StringSplitOptions.None).Length - 1);
        Assert.Contains("FailedOperation=NeutralReport", log);
        Assert.Contains("NeutralReportMs=", log);
        Assert.Equal(1, runtime.RemovedDevices);
    }

    [Fact]
    public async Task RemoveDeviceFailureLogsRollbackTimingAndPreservesFailureResult()
    {
        var runtime = new FakeRuntime { Removal = new(false, false) };
        var stage = Create(runtime, new FakeEnumerator([[], [UsbIpHost(), Device("owned")], []]), new FakeHidHide(), snapshot: new FakeSnapshot());
        await stage.PrepareMutationAsync(CancellationToken.None);
        Assert.True((await stage.ExecuteMutationAsync(CancellationToken.None)).Succeeded);

        var result = await stage.RollbackMutationAsync(CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("VirtualDeviceRemoveFailed", result.Reason);
        var log = File.ReadAllText(AppLog.CurrentLogFilePath);
        Assert.Equal(1, log.Split("Event=SteamOutputRollbackFailed", StringSplitOptions.None).Length - 1);
        Assert.Contains("Reason=VirtualDeviceRemoveFailed", log);
        Assert.Contains("RemoveDeviceMs=", log);
    }

    [Fact]
    public async Task LivePublisherFaultRequestsOneFailClosedNotification()
    {
        var runtime = new FakeRuntime { InputAccepted = false };
        var stage = Create(runtime, new FakeEnumerator([[], [UsbIpHost(), Device("owned")], []]), new FakeHidHide(), snapshot: new FakeSnapshot());
        var fault = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        stage.SetOutputFaultHandler(() => { fault.TrySetResult(); return ValueTask.CompletedTask; });
        await stage.PrepareMutationAsync(CancellationToken.None);
        Assert.True((await stage.ExecuteMutationAsync(CancellationToken.None)).Succeeded);
        await fault.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True((await stage.RollbackMutationAsync(CancellationToken.None)).Succeeded);
    }

    private ClassicSteamControllerOutputStage Create(FakeRuntime runtime, IControllerDeviceEnumerator enumerator, FakeHidHide hid, TimeSpan? timeout = null, bool storeWriteFailsAfterSeed = false, IControllerStateSnapshotSource? snapshot = null, IInputReportTickSource? reportTicks = null)
    {
        Directory.CreateDirectory(_directory);
        var store = new RecoveryJournalStore(Path.Combine(_directory, "recovery.json"));
        var recovery = new RecoveryManager(storeWriteFailsAfterSeed ? new FailingReplaceStore(store) : store);
        // The stage requires an existing recovery session; seed a valid empty session directly.
        var journal = new RecoveryJournal(RecoveryManager.CurrentSchemaVersion, _session, DateTimeOffset.UtcNow, null, new());
        File.WriteAllText(Path.Combine(_directory, "recovery.json"), System.Text.Json.JsonSerializer.Serialize(journal));
        return new(runtime, enumerator, new(new ViiperVirtualDeviceIdentityPolicy()), new(), recovery, () => _session, hid, snapshot ?? new FakeSnapshot(), timeout, TimeSpan.FromMilliseconds(1), reportTicks);
    }

    private ClassicSteamControllerOutputStage CreateCanonical(FakeCanonicalSession session, IControllerDeviceEnumerator enumerator, FakeHidHide hid)
    {
        Directory.CreateDirectory(_directory);
        var store = new RecoveryJournalStore(Path.Combine(_directory, "canonical-recovery.json"));
        var journal = new RecoveryJournal(RecoveryManager.CurrentSchemaVersion, _session, DateTimeOffset.UtcNow, null, new());
        File.WriteAllText(store.JournalPath, System.Text.Json.JsonSerializer.Serialize(journal));
        return new(() => session, enumerator, new(new ViiperVirtualDeviceIdentityPolicy()), new(), new RecoveryManager(store), () => _session, hid, new FakeSnapshot(), TimeSpan.FromSeconds(1), TimeSpan.FromMilliseconds(1));
    }

    private ClassicSteamControllerOutputStage CreateCanonicalFactoryFailure(IControllerDeviceEnumerator enumerator, FakeHidHide hid)
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "factory-failure-recovery.json");
        var store = new RecoveryJournalStore(path);
        var journal = new RecoveryJournal(RecoveryManager.CurrentSchemaVersion, _session, DateTimeOffset.UtcNow, null, new());
        File.WriteAllText(path, System.Text.Json.JsonSerializer.Serialize(journal));
        return new(() => throw new InvalidOperationException("canonical DLL load failed"), enumerator, new(new ViiperVirtualDeviceIdentityPolicy()), new(), new RecoveryManager(store), () => _session, hid, new FakeSnapshot(), TimeSpan.FromSeconds(1), TimeSpan.FromMilliseconds(1));
    }

    private const string UsbIpHostInstanceId = "ROOT\\USB\\0000";
    private static ControllerDeviceInfo UsbIpHost() => new(UsbIpHostInstanceId, null, null, [], "ROOT", ["ROOT\\USBIP_WIN2\\UDE"], [], "System", null, "usbip2_ude", null, null, true);
    private static ControllerDeviceInfo Device(string id) => new(id, Guid.Empty, null, [UsbIpHostInstanceId], "USB", ["HID\\VID_28DE&PID_1102"], [], "HIDClass", null, null, 0x28DE, 0x1102, true);
    public ClassicSteamControllerOutputStageTests()
    {
        Directory.CreateDirectory(_directory);
        AppLog.DirectoryOverride = _directory;
        AppLog.MinimumLevelOverride = AppLogLevel.Debug;
    }

    public void Dispose()
    {
        AppLog.DirectoryOverride = null;
        AppLog.MinimumLevelOverride = AppLogLevel.Info;
        if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
    }

    private sealed class FakeEnumerator(IReadOnlyList<IReadOnlyList<ControllerDeviceInfo>> states) : IControllerDeviceEnumerator
    { private int _index; public IReadOnlyList<ControllerDeviceInfo> EnumeratePresentDevices() => states[Math.Min(_index++, states.Count - 1)]; }
    // Returns [] for the very first call (the "before" snapshot) and thereafter either [gordon]
    // or [] depending on DeviceRemoved, regardless of how many times WaitForIdentityAsync polls.
    private sealed class GordonPresenceEnumerator(ControllerDeviceInfo gordon) : IControllerDeviceEnumerator
    {
        private bool _beforeCallConsumed;
        public bool DeviceRemoved { get; set; }
        public IReadOnlyList<ControllerDeviceInfo> EnumeratePresentDevices()
        {
            if (!_beforeCallConsumed) { _beforeCallConsumed = true; return []; }
            return DeviceRemoved ? [] : [gordon];
        }
    }
    private sealed class FakeRuntime : IViiperRuntime
    {
        public List<string> Trace { get; } = [];
        public int NeutralReports; public int RemovedDevices; public int CreatedDevices; public bool CancelAfterStart; public bool BusRemoved = true; public bool NeutralAccepted = true; public bool InputAccepted = true; public bool BlockInput; public ViiperDeviceRemovalResult Removal = new(true, true);
        public Action? OnRemoveDeviceCalled;
        public TaskCompletionSource InputEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseInput { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public IReadOnlyCollection<uint> OwnedDeviceIds => CreatedDevices > RemovedDevices ? [7] : [];
        public uint BusId => 1;
        public void Start() { if (CancelAfterStart) throw new OperationCanceledException(); }
        public uint CreateDevice() { CreatedDevices++; return 7; }
        public bool SetNeutral(uint id) { Trace.Add("Neutral"); NeutralReports++; return NeutralAccepted; }
        public bool SetInput(uint id, byte[] report) { Trace.Add("Input"); InputEntered.TrySetResult(); if (BlockInput) ReleaseInput.Task.GetAwaiter().GetResult(); return InputAccepted; }
        public ViiperDeviceRemovalResult RemoveDevice(uint bus, uint id) { Trace.Add("Remove"); RemovedDevices++; OnRemoveDeviceCalled?.Invoke(); return Removal with { BusRemoved = BusRemoved }; }
        public void StopIfUnused() { }
        public void Dispose() { }
    }
    private sealed class FakeCanonicalSession : ICanonicalSteamControllerSession
    {
        public List<string> Trace { get; } = [];
        public CanonicalPendingCleanupPhase? CleanupFailure { get; init; }
        public int RemoveCalls { get; private set; }
        public CanonicalSteamControllerSessionState State { get; private set; } = CanonicalSteamControllerSessionState.Clean;
        public CanonicalPendingCleanupPhase PendingCleanupPhase { get; private set; }
        public uint? BusId => State == CanonicalSteamControllerSessionState.Clean ? null : 1;
        public uint? LogicalDeviceId => State == CanonicalSteamControllerSessionState.Clean ? null : 7;
        public bool Start() { Trace.Add("Start"); State = CanonicalSteamControllerSessionState.Active; return true; }
        public bool SetState(SteamControllerDeviceState state) => true;
        public bool SetNeutral() { Trace.Add("Neutral"); return true; }
        public bool RemoveDevice() { Trace.Add("Remove"); RemoveCalls++; State = CanonicalSteamControllerSessionState.DeviceRemoved; return true; }
        public bool RetryPendingCleanup()
        {
            Trace.Add($"Retry:{PendingCleanupPhase}");
            PendingCleanupPhase = CanonicalPendingCleanupPhase.None;
            State = CanonicalSteamControllerSessionState.Clean;
            return true;
        }
        public bool CompleteRuntimeCleanup()
        {
            Trace.Add("CompleteCleanup");
            if (CleanupFailure is { } failure)
            {
                PendingCleanupPhase = failure;
                State = CanonicalSteamControllerSessionState.CleanupPending;
                return false;
            }
            State = CanonicalSteamControllerSessionState.Clean;
            return true;
        }
        public void Dispose() => Trace.Add("Dispose");
    }
    private sealed class FakeSnapshot : IControllerStateSnapshotSource
    { public ControllerState LatestState => new(new AuxiliaryButtonState([false, false])); }
    private sealed class ManualTicks : IInputReportTickSource
    {
        private readonly Queue<TaskCompletionSource<bool>> _waiters = new();
        public ValueTask<bool> WaitForTickAsync(CancellationToken token)
        { var waiter = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously); _waiters.Enqueue(waiter); token.Register(() => waiter.TrySetCanceled(token)); return new(waiter.Task); }
        public void Tick() { Assert.NotEmpty(_waiters); _waiters.Dequeue().TrySetResult(true); }
    }
    private sealed class FakeHidHide : IHidHideClient
    { public HidHideInspection Inspection { get; init; } = new(HidHideInspectionStatus.Available, new HashSet<string>()); public HidHideInspection Inspect() => Inspection; public bool AddApplication(string p) => true; public bool RemoveApplication(string p) => true; public bool AddHiddenDevice(string p) => true; public bool RemoveHiddenDevice(string p) => true; }
    private sealed class FailingReplaceStore(RecoveryJournalStore inner) : IRecoveryJournalStore
    {
        public string JournalPath => inner.JournalPath;
        public bool Exists() => inner.Exists();
        public string ReadText() => inner.ReadText();
        public void WriteNew(RecoveryJournal value) => inner.WriteNew(value);
        public void ReplaceExisting(RecoveryJournal value) => throw new IOException("replace failed");
        public void Delete() => inner.Delete();
    }
}
