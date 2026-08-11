using SteamInputAddonforClaw.Controllers.Detection;
using SteamInputAddonforClaw.HidHide;
using SteamInputAddonforClaw.Recovery;
using SteamInputAddonforClaw.VirtualOutput.Viiper;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class ClassicSteamControllerOutputStageTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "ClawSteamOutputTests", Guid.NewGuid().ToString("N"));
    private readonly Guid _session = Guid.NewGuid();

    [Fact]
    public async Task SuccessfulCreationResolvesPnPAndSendsOneNeutralReport()
    {
        var runtime = new FakeRuntime();
        var stage = Create(runtime, new FakeEnumerator([[], [Device("owned")]]), new FakeHidHide());
        Assert.True((await stage.PrepareMutationAsync(CancellationToken.None)).Succeeded);
        var result = await stage.ExecuteMutationAsync(CancellationToken.None);
        Assert.True(result.Succeeded, result.Reason);
        Assert.Equal(1, runtime.NeutralReports);
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

    private ClassicSteamControllerOutputStage Create(FakeRuntime runtime, FakeEnumerator enumerator, FakeHidHide hid, TimeSpan? timeout = null)
    {
        Directory.CreateDirectory(_directory);
        var recovery = new RecoveryManager(new RecoveryJournalStore(Path.Combine(_directory, "recovery.json")), deviceEnumerator: enumerator, hidHideClient: hid);
        recovery.BeginDeviceNativeStateMutation(new(SteamInputAddonforClaw.Devices.Abstractions.NativeStateCaptureStatus.Success, null, "test"));
        // The stage requires an existing recovery session; seed a valid empty session directly.
        var journal = new RecoveryJournal(RecoveryManager.CurrentSchemaVersion, _session, DateTimeOffset.UtcNow, null, new());
        File.WriteAllText(Path.Combine(_directory, "recovery.json"), System.Text.Json.JsonSerializer.Serialize(journal));
        return new(runtime, enumerator, new(new ViiperVirtualDeviceIdentityPolicy()), new(), recovery, () => _session, hid, timeout, TimeSpan.FromMilliseconds(1));
    }

    private static ControllerDeviceInfo Device(string id) => new(id, Guid.Empty, null, [], "VIIPER", ["HID\\VID_28DE&PID_1102"], [], "HIDClass", null, "VIIPER", 0x28DE, 0x1102, true);
    public void Dispose() { if (Directory.Exists(_directory)) Directory.Delete(_directory, true); }

    private sealed class FakeEnumerator(IReadOnlyList<IReadOnlyList<ControllerDeviceInfo>> states) : IControllerDeviceEnumerator
    { private int _index; public IReadOnlyList<ControllerDeviceInfo> EnumeratePresentDevices() => states[Math.Min(_index++, states.Count - 1)]; }
    private sealed class FakeRuntime : IViiperRuntime
    { public int NeutralReports; public int RemovedDevices; public int CreatedDevices; public IReadOnlyCollection<uint> OwnedDeviceIds => [7]; public uint BusId => 1; public void Start() { } public uint CreateDevice() { CreatedDevices++; return 7; } public bool SetNeutral(uint id) { NeutralReports++; return true; } public bool RemoveDevice(uint bus, uint id) { RemovedDevices++; return true; } public void StopIfUnused() { } public void Dispose() { } }
    private sealed class FakeHidHide : IHidHideClient
    { public HidHideInspection Inspection { get; init; } = new(HidHideInspectionStatus.Available, new HashSet<string>()); public HidHideInspection Inspect() => Inspection; public bool AddApplication(string p) => true; public bool RemoveApplication(string p) => true; public bool AddHiddenDevice(string p) => true; public bool RemoveHiddenDevice(string p) => true; }
}
