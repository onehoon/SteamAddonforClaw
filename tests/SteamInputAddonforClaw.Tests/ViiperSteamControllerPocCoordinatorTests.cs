using SteamInputAddonforClaw.Controllers.Detection;
using SteamInputAddonforClaw.Devices;
using SteamInputAddonforClaw.Prerequisites;
using SteamInputAddonforClaw.Routing;
using SteamInputAddonforClaw.Status;
using SteamInputAddonforClaw.VirtualOutput.Viiper;
using SteamInputAddonforClaw.Power;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class ViiperSteamControllerPocCoordinatorTests
{
    [Fact]
    public async Task Unsupported_hardware_denies_before_native_load()
    {
        var loaded = 0;
        await using var coordinator = Create(new(HardwareCompatibilityStatus.Unsupported, null, null, "test"), new SequenceEnumerator([]), () => { loaded++; return new FakeApi(); });
        var result = await coordinator.StartAsync();
        Assert.False(result.Succeeded);
        Assert.Equal("SafetyGateDenied", result.Reason);
        Assert.Equal(0, loaded);
    }

    [Fact]
    public async Task Start_then_stop_uses_the_expected_native_lifecycle_once()
    {
        var device = Device("USB\\VID_28DE&PID_1102\\VIIPER");
        var api = new FakeApi();
        await using var coordinator = Create(new(HardwareCompatibilityStatus.Supported, null, null, "test"), new SequenceEnumerator([], [device], []), () => api);
        var started = await coordinator.StartAsync();
        var stopped = await coordinator.StopAsync();
        var stoppedAgain = await coordinator.StopAsync();
        Assert.True(started.Succeeded);
        Assert.True(stopped.Succeeded);
        Assert.True(stoppedAgain.Succeeded);
        Assert.Equal(1, api.InitializeCount);
        Assert.Equal(1, api.CreateBusCount);
        Assert.Equal(1, api.AddCount);
        Assert.Equal(1, api.FeedbackCount);
        Assert.True(api.SetInputCount > 0);
        Assert.Equal(1, api.RemoveCount);
        Assert.Equal(1, api.RemoveBusCount);
        Assert.Equal(1, api.ShutdownCount);
        Assert.True(api.Disposed);
    }

    [Fact]
    public async Task Report_pump_failure_enters_failed_state_and_attempts_cleanup()
    {
        var device = Device("USB\\VID_28DE&PID_1102\\VIIPER");
        var api = new FakeApi { FailInputAfter = 2 };
        await using var coordinator = Create(new(HardwareCompatibilityStatus.Supported, null, null, "test"), new SequenceEnumerator([], [device], []), () => api);
        Assert.True((await coordinator.StartAsync()).Succeeded);
        await Task.Delay(50);
        Assert.Equal(ViiperSteamControllerPocState.Failed, coordinator.State);
        Assert.True(api.RemoveCount > 0);
        Assert.False((await coordinator.StartAsync()).Succeeded);
    }

    [Fact]
    public async Task Remove_failure_marks_ownership_uncertain_and_denies_restart()
    {
        var device = Device("USB\\VID_28DE&PID_1102\\VIIPER"); var api = new FakeApi { RemoveResult = -1 }; var tracker = new AddonOwnedVirtualDeviceTracker();
        await using var coordinator = Create(new(HardwareCompatibilityStatus.Supported, null, null, "test"), new SequenceEnumerator([], [device]), () => api, tracker);
        Assert.True((await coordinator.StartAsync()).Succeeded); Assert.False((await coordinator.StopAsync()).Succeeded);
        Assert.True(tracker.HasUncertainOwnership); Assert.Equal(ViiperSteamControllerPocState.Failed, coordinator.State); Assert.False((await coordinator.StartAsync()).Succeeded);
    }

    [Fact]
    public async Task Feedback_failure_marks_ownership_uncertain_and_attempts_cleanup()
    {
        var device = Device("USB\\VID_28DE&PID_1102\\VIIPER"); var api = new FakeApi { FeedbackResult = -1 }; var tracker = new AddonOwnedVirtualDeviceTracker();
        await using var coordinator = Create(new(HardwareCompatibilityStatus.Supported, null, null, "test"), new SequenceEnumerator([], [device]), () => api, tracker);
        Assert.False((await coordinator.StartAsync()).Succeeded); Assert.True(tracker.HasUncertainOwnership); Assert.True(api.RemoveCount > 0);
    }

    [Theory]
    [InlineData(false, ExternalControllerAssessmentStatus.Clear)]
    [InlineData(true, ExternalControllerAssessmentStatus.ExternalPresent)]
    [InlineData(true, ExternalControllerAssessmentStatus.Indeterminate)]
    public async Task Active_safety_change_automatically_stops(bool steamActive, ExternalControllerAssessmentStatus external)
    {
        var device = Device("USB\\VID_28DE&PID_1102\\VIIPER");
        var api = new FakeApi();
        var provider = new MutableSnapshotProvider(Snapshot());
        var path = Path.GetTempFileName();
        await using var coordinator = new ViiperSteamControllerPocCoordinator(provider, new SequenceEnumerator([], [device], []), new AddonOwnedVirtualDeviceTracker(), path, _ => api);
        Assert.True((await coordinator.StartAsync()).Succeeded);
        provider.Current = Snapshot(steamActive, external);
        await Task.Delay(400);
        Assert.Equal(ViiperSteamControllerPocState.Stopped, coordinator.State);
        Assert.Equal(1, api.RemoveCount);
    }

    [Fact]
    public async Task Disappearance_timeout_marks_ownership_uncertain()
    {
        var device = Device("USB\\VID_28DE&PID_1102\\VIIPER"); var tracker = new AddonOwnedVirtualDeviceTracker(); var api = new FakeApi();
        await using var coordinator = Create(new(HardwareCompatibilityStatus.Supported, null, null, "test"), new SequenceEnumerator([], [device], [device]), () => api, tracker);
        Assert.True((await coordinator.StartAsync()).Succeeded); Assert.False((await coordinator.StopAsync()).Succeeded);
        Assert.True(tracker.HasUncertainOwnership); Assert.Equal(ViiperSteamControllerPocState.Failed, coordinator.State);
    }

    [Fact]
    public async Task Suspend_quiesce_uses_bounded_path_and_orders_native_teardown()
    {
        var device = Device("USB\\VID_28DE&PID_1102\\VIIPER"); var api = new FakeApi();
        await using var coordinator = Create(new(HardwareCompatibilityStatus.Supported, null, null, "test"), new SequenceEnumerator([], [device], []), () => api);
        Assert.True((await coordinator.StartAsync()).Succeeded);
        Assert.True(await coordinator.QuiesceForSuspendAsync(DateTimeOffset.UtcNow.AddSeconds(1), 4, 9, CancellationToken.None));
        Assert.True(api.Calls.IndexOf("RemoveDevice") > api.Calls.IndexOf("SetInput"));
        Assert.True(api.Calls.IndexOf("RemoveBus") > api.Calls.IndexOf("RemoveDevice"));
        Assert.True(api.Calls.IndexOf("Shutdown") > api.Calls.IndexOf("RemoveBus"));
        Assert.True(api.Calls.IndexOf("Dispose") > api.Calls.IndexOf("Shutdown"));
    }

    [Fact]
    public async Task Suspend_quiesce_does_not_wait_for_start_lifecycle_lock()
    {
        var gate = new PowerMutationGate(true); var api = new FakeApi(); var tracker = new AddonOwnedVirtualDeviceTracker();
        await using var coordinator = Create(new(HardwareCompatibilityStatus.Supported, null, null, "test"), new SequenceEnumerator([], [], []), () => api, tracker, gate);
        var start = coordinator.StartAsync();
        Assert.True(SpinWait.SpinUntil(() => api.AddCount == 1, TimeSpan.FromSeconds(1)));
        gate.TryEnterBarrier(out _, out _); coordinator.CancelLifecycle();
        Assert.False(await coordinator.QuiesceForSuspendAsync(DateTimeOffset.UtcNow.AddSeconds(1), 1, gate.Epoch, CancellationToken.None));
        var startResult = await start;
        Assert.Equal("PowerTransitionInvalidated", startResult.Reason); Assert.Equal(1, api.RemoveCount); Assert.True(tracker.HasUncertainOwnership);
    }

    [Fact]
    public async Task Suspend_cleanup_continues_after_remove_device_failure()
    {
        var device = Device("USB\\VID_28DE&PID_1102\\VIIPER"); var api = new FakeApi { RemoveResult = -1 };
        await using var coordinator = Create(new(HardwareCompatibilityStatus.Supported, null, null, "test"), new SequenceEnumerator([], [device]), () => api);
        Assert.True((await coordinator.StartAsync()).Succeeded);
        Assert.False(await coordinator.QuiesceForSuspendAsync(DateTimeOffset.UtcNow.AddSeconds(1), 3, 8, CancellationToken.None));
        Assert.Equal(1, api.RemoveCount); Assert.Equal(1, api.RemoveBusCount); Assert.Equal(1, api.ShutdownCount); Assert.True(api.Disposed);
    }

    [Fact]
    public async Task Already_admitted_native_operation_may_finish_before_suspend_cleanup()
    {
        var gate = new PowerMutationGate(true); var api = new FakeApi { BlockAddDevice = true };
        await using var coordinator = Create(new(HardwareCompatibilityStatus.Supported, null, null, "test"), new SequenceEnumerator([], [], []), () => api, powerGate: gate);
        var start = Task.Run(() => coordinator.StartAsync());
        Assert.True(api.AddDeviceStarted.Wait(TimeSpan.FromSeconds(1)));
        gate.TryEnterBarrier(out _, out _); coordinator.CancelLifecycle();
        var quiesce = coordinator.QuiesceForSuspendAsync(DateTimeOffset.UtcNow.AddSeconds(2), 7, gate.Epoch, CancellationToken.None);
        Assert.False(quiesce.IsCompleted);
        api.AllowAddDevice.Set();
        Assert.False(await quiesce);
        Assert.Equal("PowerTransitionInvalidated", (await start).Reason);
        Assert.Equal(1, api.AddCount); Assert.Equal(1, api.RemoveCount);
    }

    [Fact]
    public async Task Suspend_unverified_disappearance_marks_ownership_uncertain()
    {
        var device = Device("USB\\VID_28DE&PID_1102\\VIIPER"); var api = new FakeApi(); var tracker = new AddonOwnedVirtualDeviceTracker();
        await using var coordinator = Create(new(HardwareCompatibilityStatus.Supported, null, null, "test"), new SequenceEnumerator([], [device], [device]), () => api, tracker);
        Assert.True((await coordinator.StartAsync()).Succeeded);
        Assert.False(await coordinator.QuiesceForSuspendAsync(DateTimeOffset.UtcNow.AddSeconds(1), 11, 12, CancellationToken.None));
        Assert.True(tracker.HasUncertainOwnership);
    }

    [Fact]
    public async Task Suspend_during_pulse_leaves_neutral_state()
    {
        var device = Device("USB\\VID_28DE&PID_1102\\VIIPER"); var api = new FakeApi(); var gate = new PowerMutationGate(true);
        await using var coordinator = Create(new(HardwareCompatibilityStatus.Supported, null, null, "test"), new SequenceEnumerator([], [device], []), () => api, powerGate: gate);
        Assert.True((await coordinator.StartAsync()).Succeeded);
        var pulse = coordinator.PulseAsync(true, false);
        gate.TryEnterBarrier(out _, out _); coordinator.CancelLifecycle();
        Assert.Equal("PowerTransitionInvalidated", (await pulse).Reason);
        await coordinator.QuiesceForSuspendAsync(DateTimeOffset.UtcNow.AddSeconds(1), 13, gate.Epoch, CancellationToken.None);
        Assert.True(api.Calls.IndexOf("RemoveDevice") > api.Calls.LastIndexOf("SetInput"));
    }

    private static ViiperSteamControllerPocCoordinator Create(HardwareCompatibilityAssessment hardware, IControllerDeviceEnumerator enumerator, Func<IViiperNativeApi> factory, AddonOwnedVirtualDeviceTracker? tracker = null, PowerMutationGate? powerGate = null)
    {
        var snapshot = Snapshot(hardware);
        var path = Path.GetTempFileName();
        return new ViiperSteamControllerPocCoordinator(new SnapshotProvider(snapshot), enumerator, tracker ?? new AddonOwnedVirtualDeviceTracker(), path, _ => factory(), powerGate: powerGate);
    }

    private static SystemStatusSnapshot Snapshot(bool steamActive = true, ExternalControllerAssessmentStatus external = ExternalControllerAssessmentStatus.Clear) => Snapshot(new(HardwareCompatibilityStatus.Supported, null, null, "test"), steamActive, external);
    private static SystemStatusSnapshot Snapshot(HardwareCompatibilityAssessment hardware, bool steamActive = true, ExternalControllerAssessmentStatus external = ExternalControllerAssessmentStatus.Clear) => new(new("", "", "", []), hardware, [], new(ControllerEnvironmentCompatibilityStatus.Supported, ControllerEnvironmentCompatibilityReason.StockCenterMOnlySupported), new(new(PrerequisiteKind.HidHide, PrerequisiteStatus.Ready, ""), new(PrerequisiteKind.UsbIpWin2, PrerequisiteStatus.Ready, ""), new(PrerequisiteKind.Viiper, PrerequisiteStatus.Present, "")), new(steamActive, steamActive ? 1u : 0u), new(external, 0, []), new(RoutingDecisionKind.Eligible, RoutingDecisionReason.Eligible), new(AddonOperationalStatus.Ready, ""), true);

    private static ControllerDeviceInfo Device(string id) => new(id, null, null, ["ROOT\\USBIP_WIN2\\UDE"], "HID", ["HID_DEVICE_UP:0001_U:0005"], [], "HIDClass", null, "usbip2_ude", 0x28DE, 0x1102, true);
    private sealed class SnapshotProvider(SystemStatusSnapshot snapshot) : ISystemStatusProvider { public Task<SystemStatusSnapshot> CaptureAsync(CancellationToken cancellationToken = default) => Task.FromResult(snapshot); }
    private sealed class MutableSnapshotProvider(SystemStatusSnapshot snapshot) : ISystemStatusProvider { public SystemStatusSnapshot Current = snapshot; public Task<SystemStatusSnapshot> CaptureAsync(CancellationToken cancellationToken = default) => Task.FromResult(Current); }
    private sealed class SequenceEnumerator(params IReadOnlyList<ControllerDeviceInfo>[] snapshots) : IControllerDeviceEnumerator { private int _index; public IReadOnlyList<ControllerDeviceInfo> EnumeratePresentDevices() => snapshots[Math.Min(_index++, snapshots.Length - 1)]; }
    private sealed class FakeApi : IViiperNativeApi
    {
        public int InitializeCount; public int CreateBusCount; public int AddCount; public int FeedbackCount; public int SetInputCount; public int RemoveCount; public int RemoveBusCount; public int ShutdownCount; public bool Disposed; public int FailInputAfter; public int RemoveResult; public int FeedbackResult; public bool BlockAddDevice; public ManualResetEventSlim AddDeviceStarted { get; } = new(); public ManualResetEventSlim AllowAddDevice { get; } = new();
        public List<string> Calls { get; } = []; public int Initialize(string listenAddress) { Calls.Add("Initialize"); InitializeCount++; return 0; } public void Shutdown() { Calls.Add("Shutdown"); ShutdownCount++; } public int CreateBus(uint id) { Calls.Add("CreateBus"); CreateBusCount++; return 0; } public int RemoveBus(uint id) { Calls.Add("RemoveBus"); RemoveBusCount++; return 0; }
        public int AddDevice(uint bus, string type, ushort vid, ushort pid, out uint id) { Calls.Add("AddDevice"); AddCount++; AddDeviceStarted.Set(); if (BlockAddDevice) AllowAddDevice.Wait(); Assert.Equal("steamcontroller", type); Assert.Equal((ushort)0x28DE, vid); Assert.Equal((ushort)0x1102, pid); id = 3; return 0; }
        public int RemoveDevice(uint bus, uint id) { Calls.Add("RemoveDevice"); RemoveCount++; return RemoveResult; } public int SetInput(uint bus, uint id, byte[] report) { Calls.Add("SetInput"); SetInputCount++; return FailInputAfter > 0 && SetInputCount >= FailInputAfter ? -1 : 0; } public int SetFeedbackCallback(uint bus, uint id, Action<ReadOnlyMemory<byte>> callback) { Calls.Add("Feedback"); FeedbackCount++; return FeedbackResult; }
        public string[] GetDeviceTypes() => ["steamcontroller"]; public string? GetLastError() => null; public void Dispose() { Calls.Add("Dispose"); Disposed = true; }
    }
}
