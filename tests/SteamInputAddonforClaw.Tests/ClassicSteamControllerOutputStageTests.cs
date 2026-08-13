using SteamInputAddonforClaw.Controllers.Detection;
using SteamInputAddonforClaw.HidHide;
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
    public async Task SuccessfulCreationResolvesPnPAndSendsOneNeutralReport()
    {
        var runtime = new FakeRuntime();
        var stage = Create(runtime, new FakeEnumerator([[], [Device("owned")], []]), new FakeHidHide());
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
        var stage = Create(runtime, new FakeEnumerator([[], [Device("owned")], []]), new FakeHidHide { Inspection = new(HidHideInspectionStatus.ConfigurationUnavailable, new HashSet<string>()) });
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
        var stage = Create(runtime, new FakeEnumerator([[], [Device("owned")], []]), hidHide);
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
    public async Task SuccessfulResolutionEmitsNoIdentityDiagnosticDump()
    {
        var runtime = new FakeRuntime();
        var stage = Create(runtime, new FakeEnumerator([[], [Device("owned")], []]), new FakeHidHide());
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
    public async Task CreationAndSuspendAreSerializedWithInFlightCancellation()
    {
        var runtime = new FakeRuntime();
        var stage = Create(runtime, new FakeEnumerator([[], []]), new FakeHidHide(), TimeSpan.FromSeconds(5));
        await stage.PrepareMutationAsync(CancellationToken.None);

        var creation = stage.ExecuteMutationAsync(CancellationToken.None).AsTask();
        Assert.True(SpinWait.SpinUntil(() => runtime.CreatedDevices == 1, TimeSpan.FromSeconds(1)));
        var quiesced = await stage.QuiesceForSuspendAsync(DateTimeOffset.UtcNow.AddSeconds(1), 1, 1, CancellationToken.None);
        var result = await creation;

        Assert.True(quiesced);
        Assert.False(result.Succeeded);
        Assert.Equal(1, runtime.RemovedDevices);
    }

    [Fact]
    public async Task CallerCancellationDuringPnPWaitRollsBackIntentAndDevice()
    {
        var runtime = new FakeRuntime();
        var stage = Create(runtime, new FakeEnumerator([[], []]), new FakeHidHide(), TimeSpan.FromSeconds(5));
        using var cancellation = new CancellationTokenSource();
        await stage.PrepareMutationAsync(CancellationToken.None);

        var creation = stage.ExecuteMutationAsync(cancellation.Token).AsTask();
        Assert.True(SpinWait.SpinUntil(() => runtime.CreatedDevices == 1, TimeSpan.FromSeconds(1)));
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
    public async Task SuspendQuiescesOutputAndResumeRequiresFreshRecreation()
    {
        var runtime = new FakeRuntime();
        var stage = Create(runtime, new FakeEnumerator([[], [Device("owned")], []]), new FakeHidHide());
        await stage.PrepareMutationAsync(CancellationToken.None);
        Assert.True((await stage.ExecuteMutationAsync(CancellationToken.None)).Succeeded);
        Assert.True(await stage.QuiesceForSuspendAsync(DateTimeOffset.UtcNow.AddSeconds(1), 1, 1, CancellationToken.None));
        Assert.True(await stage.ReconcileAfterResumeAsync(1, 1, CancellationToken.None));
        Assert.Equal(1, runtime.RemovedDevices);
    }

    [Fact]
    public async Task BusCleanupFailureDoesNotBlockVerifiedDeviceMutationCompletion()
    {
        var runtime = new FakeRuntime { BusRemoved = false };
        var stage = Create(runtime, new FakeEnumerator([[], [Device("owned")], []]), new FakeHidHide());
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
        var stage = Create(runtime, new FakeEnumerator([[], [Device("owned")], []]), new FakeHidHide(), snapshot: new FakeSnapshot(), reportTicks: ticks);
        await stage.PrepareMutationAsync(CancellationToken.None);
        Assert.True((await stage.ExecuteMutationAsync(CancellationToken.None)).Succeeded);
        ticks.Tick(); await runtime.InputEntered.Task.WaitAsync(TimeSpan.FromSeconds(1));
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
        var stage = Create(runtime, new FakeEnumerator([[], [Device("owned")], []]), new FakeHidHide(), snapshot: new FakeSnapshot());
        await stage.PrepareMutationAsync(CancellationToken.None);
        var result = await stage.ExecuteMutationAsync(CancellationToken.None);
        Assert.False(result.Succeeded);
        Assert.DoesNotContain("Input", runtime.Trace);
    }

    [Fact]
    public async Task NeutralRejectionRetainsFailureOperationTimingAndLogsOnce()
    {
        var runtime = new FakeRuntime { NeutralAccepted = false };
        var stage = Create(runtime, new FakeEnumerator([[], [Device("owned")], []]), new FakeHidHide(), snapshot: new FakeSnapshot());
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
        var stage = Create(runtime, new FakeEnumerator([[], [Device("owned")], []]), new FakeHidHide(), snapshot: new FakeSnapshot());
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
        var stage = Create(runtime, new FakeEnumerator([[], [Device("owned")], []]), new FakeHidHide(), snapshot: new FakeSnapshot());
        var fault = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        stage.SetOutputFaultHandler(() => { fault.TrySetResult(); return ValueTask.CompletedTask; });
        await stage.PrepareMutationAsync(CancellationToken.None);
        Assert.True((await stage.ExecuteMutationAsync(CancellationToken.None)).Succeeded);
        await fault.Task.WaitAsync(TimeSpan.FromSeconds(1));
        Assert.True((await stage.RollbackMutationAsync(CancellationToken.None)).Succeeded);
    }

    private ClassicSteamControllerOutputStage Create(FakeRuntime runtime, FakeEnumerator enumerator, FakeHidHide hid, TimeSpan? timeout = null, bool storeWriteFailsAfterSeed = false, IControllerStateSnapshotSource? snapshot = null, IInputReportTickSource? reportTicks = null)
    {
        Directory.CreateDirectory(_directory);
        var store = new RecoveryJournalStore(Path.Combine(_directory, "recovery.json"));
        var recovery = new RecoveryManager(storeWriteFailsAfterSeed ? new FailingReplaceStore(store) : store, deviceEnumerator: enumerator, hidHideClient: hid);
        // The stage requires an existing recovery session; seed a valid empty session directly.
        var journal = new RecoveryJournal(RecoveryManager.CurrentSchemaVersion, _session, DateTimeOffset.UtcNow, null, new());
        File.WriteAllText(Path.Combine(_directory, "recovery.json"), System.Text.Json.JsonSerializer.Serialize(journal));
        return new(runtime, enumerator, new(new ViiperVirtualDeviceIdentityPolicy()), new(), recovery, () => _session, hid, snapshot ?? new FakeSnapshot(), timeout, TimeSpan.FromMilliseconds(1), reportTicks);
    }

    private static ControllerDeviceInfo Device(string id) => new(id, Guid.Empty, null, [], "VIIPER", ["HID\\VID_28DE&PID_1102"], [], "HIDClass", null, "VIIPER", 0x28DE, 0x1102, true);
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
    private sealed class FakeRuntime : IViiperRuntime
    {
        public List<string> Trace { get; } = [];
        public int NeutralReports; public int RemovedDevices; public int CreatedDevices; public bool CancelAfterStart; public bool BusRemoved = true; public bool NeutralAccepted = true; public bool InputAccepted = true; public bool BlockInput; public ViiperDeviceRemovalResult Removal = new(true, true);
        public TaskCompletionSource InputEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseInput { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public IReadOnlyCollection<uint> OwnedDeviceIds => CreatedDevices > RemovedDevices ? [7] : [];
        public uint BusId => 1;
        public void Start() { if (CancelAfterStart) throw new OperationCanceledException(); }
        public uint CreateDevice() { CreatedDevices++; return 7; }
        public bool SetNeutral(uint id) { Trace.Add("Neutral"); NeutralReports++; return NeutralAccepted; }
        public bool SetInput(uint id, byte[] report) { Trace.Add("Input"); InputEntered.TrySetResult(); if (BlockInput) ReleaseInput.Task.GetAwaiter().GetResult(); return InputAccepted; }
        public ViiperDeviceRemovalResult RemoveDevice(uint bus, uint id) { Trace.Add("Remove"); RemovedDevices++; return Removal with { BusRemoved = BusRemoved }; }
        public void StopIfUnused() { }
        public void Dispose() { }
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
