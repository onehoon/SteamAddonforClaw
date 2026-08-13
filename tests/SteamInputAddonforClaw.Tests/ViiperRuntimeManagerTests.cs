using SteamInputAddonforClaw.VirtualOutput.Viiper;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class ViiperRuntimeManagerTests
{
    [Fact]
    public void AddDeviceFailureInvalidatesBusAndRetriesOnFreshBus()
    {
        var api = new FakeApi { AddResults = [1, 0] };
        using var runtime = new ViiperRuntimeManager(Path.GetFullPath("libVIIPER.dll"), _ => api);

        var deviceId = runtime.CreateDevice();

        Assert.Equal((uint)42, deviceId);
        Assert.Equal(new uint[] { 1, 2 }, api.CreatedBuses);
        Assert.Equal(new uint[] { 1 }, api.RemovedBuses);
        Assert.Equal((uint)2, runtime.BusId);
    }

    [Fact]
    public void CreateDeviceUsesExactClassicSteamControllerEndpoint()
    {
        var api = new FakeApi();
        using var runtime = new ViiperRuntimeManager(Path.GetFullPath("libVIIPER.dll"), _ => api);

        runtime.CreateDevice();

        Assert.Equal([ViiperRuntimeManager.ListenAddress], api.InitializeCalls);
        var call = Assert.Single(api.AddDeviceCalls);
        Assert.Equal((uint)1, call.BusId);
        Assert.Equal("steamcontroller", call.TypeName);
        Assert.Equal((ushort)0x28DE, call.VendorId);
        Assert.Equal((ushort)0x1102, call.ProductId);
    }

    [Fact]
    public void UnsupportedDeviceTypeFailsAfterInitializationShutdown()
    {
        var api = new FakeApi { DeviceTypes = ["xbox360"] };
        using var runtime = new ViiperRuntimeManager(Path.GetFullPath("libVIIPER.dll"), _ => api);

        var exception = Assert.Throws<InvalidOperationException>(() => runtime.Start());

        Assert.Equal("VIIPER does not support steamcontroller.", exception.Message);
        Assert.True(api.ShutdownCalled);
        Assert.True(api.Disposed);
    }

    [Fact]
    public void FailedBusRemovalDoesNotReportDeviceCleanupAsComplete()
    {
        var api = new FakeApi { RemoveBusResult = 1 };
        using var runtime = new ViiperRuntimeManager(Path.GetFullPath("libVIIPER.dll"), _ => api);
        var deviceId = runtime.CreateDevice();

        var removal = runtime.RemoveDevice(runtime.BusId, deviceId);

        Assert.True(removal.DeviceRemoved);
        Assert.False(removal.BusRemoved);
        Assert.DoesNotContain(deviceId, runtime.OwnedDeviceIds);
        Assert.Empty(runtime.OwnedDeviceIds);
    }

    [Fact]
    public void DeviceRemovalIsNotRetriedAfterBusCleanupFailure()
    {
        var api = new FakeApi { RemoveBusResult = 1 };
        using var runtime = new ViiperRuntimeManager(Path.GetFullPath("libVIIPER.dll"), _ => api);
        var deviceId = runtime.CreateDevice();

        var first = runtime.RemoveDevice(runtime.BusId, deviceId);
        var second = runtime.RemoveDevice(runtime.BusId, deviceId);

        Assert.True(first.DeviceRemoved);
        Assert.False(first.BusRemoved);
        Assert.False(second.DeviceRemoved);
        Assert.Equal([deviceId], api.RemovedDevices);
    }

    [Fact]
    public void SetInputUsesOwnedBusAndDeviceAndRejectsUnknownDevice()
    {
        var api = new FakeApi();
        using var runtime = new ViiperRuntimeManager(Path.GetFullPath("libVIIPER.dll"), _ => api);
        var deviceId = runtime.CreateDevice(); var report = new byte[64]; report[8] = 0x80;

        Assert.True(runtime.SetInput(deviceId, report));
        Assert.False(runtime.SetInput(deviceId + 1, report));
        var call = Assert.Single(api.InputCalls);
        Assert.Equal((uint)1, call.BusId); Assert.Equal(deviceId, call.DeviceId); Assert.Equal(report, call.Report);
    }

    [Fact]
    public void SetNeutralReusesThe64ByteInputSubmissionPath()
    {
        var api = new FakeApi(); using var runtime = new ViiperRuntimeManager(Path.GetFullPath("libVIIPER.dll"), _ => api);
        var deviceId = runtime.CreateDevice();
        Assert.True(runtime.SetNeutral(deviceId));
        var call = Assert.Single(api.InputCalls);
        Assert.Equal(64, call.Report.Length); Assert.Equal((byte)1, call.Report[0]); Assert.Equal((byte)0xB8, call.Report[62]); Assert.Equal((byte)0x0B, call.Report[63]);
    }

    [Fact]
    public void InactiveRuntimeRejectsInputWithoutCallingNativeApi()
    {
        var api = new FakeApi(); using var runtime = new ViiperRuntimeManager(Path.GetFullPath("libVIIPER.dll"), _ => api);
        Assert.False(runtime.SetInput(7, new byte[64]));
        Assert.Empty(api.InputCalls);
    }

    [Fact]
    public void NativeInputFailureIsReportedAsFailure()
    {
        var api = new FakeApi { SetInputResult = 1 }; using var runtime = new ViiperRuntimeManager(Path.GetFullPath("libVIIPER.dll"), _ => api);
        var deviceId = runtime.CreateDevice();
        Assert.False(runtime.SetInput(deviceId, new byte[64]));
    }

    [Fact]
    public void PersistentCreateBusFailureIsBounded()
    {
        var api = new FakeApi { CreateBusResult = 1 };
        using var runtime = new ViiperRuntimeManager(Path.GetFullPath("libVIIPER.dll"), _ => api);

        var exception = Assert.Throws<InvalidOperationException>(() => runtime.CreateDevice());

        Assert.Equal("fake error", exception.Message);
        Assert.Equal(16, api.CreatedBuses.Count);
        Assert.Empty(api.AddDeviceCalls);
    }

    [Fact]
    public void DisposeRemovesOwnedDevicesAndDisposesNativeRuntime()
    {
        var api = new FakeApi();
        var runtime = new ViiperRuntimeManager(Path.GetFullPath("libVIIPER.dll"), _ => api);
        var deviceId = runtime.CreateDevice();

        runtime.Dispose();

        Assert.Contains(deviceId, api.RemovedDevices);
        Assert.True(api.ShutdownCalled);
        Assert.True(api.Disposed);
    }

    [Fact]
    public void StopIfUnusedShutsDownAndDisposesLogicalApiWrapperWhenLastDeviceRemoved()
    {
        var api = new FakeApi();
        using var runtime = new ViiperRuntimeManager(Path.GetFullPath("libVIIPER.dll"), _ => api);
        var deviceId = runtime.CreateDevice();

        runtime.RemoveDevice(runtime.BusId, deviceId);
        runtime.StopIfUnused();

        Assert.True(api.ShutdownCalled);
        Assert.True(api.Disposed);
    }

    [Fact]
    public void RuntimeCanReinitializeAndCreateADeviceAfterStopIfUnused()
    {
        var factoryCallCount = 0;
        FakeApi? current = null;
        var runtime = new ViiperRuntimeManager(Path.GetFullPath("libVIIPER.dll"), _ =>
        {
            factoryCallCount++;
            current = new FakeApi();
            return current;
        });

        var firstDeviceId = runtime.CreateDevice();
        runtime.RemoveDevice(runtime.BusId, firstDeviceId);
        runtime.StopIfUnused();

        var secondDeviceId = runtime.CreateDevice();

        Assert.Equal(2, factoryCallCount);
        Assert.Equal((uint)42, secondDeviceId);
        Assert.Contains(secondDeviceId, runtime.OwnedDeviceIds);
        Assert.True(current!.InitializeCalls.Count >= 1);
        runtime.Dispose();
    }

    [Fact]
    public void RepeatedStartCreateRemoveStopCyclesDoNotCauseRepeatedNativeModuleLoads()
    {
        var loadCount = 0;
        var path = Path.GetFullPath(Path.Combine(Path.GetTempPath(), $"libVIIPER-{Guid.NewGuid():N}.dll"));
        nint FakeLoad(string p) { loadCount++; return new nint(loadCount); }

        for (var cycle = 0; cycle < 3; cycle++)
        {
            using var runtime = new ViiperRuntimeManager(path, p =>
            {
                var handle = ViiperNativeModuleCache.GetOrLoad(p, FakeLoad);
                return new FakeApi();
            });
            var deviceId = runtime.CreateDevice();
            runtime.RemoveDevice(runtime.BusId, deviceId);
            runtime.StopIfUnused();
        }

        Assert.Equal(1, loadCount);
    }

    [Fact]
    public void UnsupportedDeviceTypeFailureDoesNotInvalidateThePinnedNativeModule()
    {
        var loadCount = 0;
        var path = Path.GetFullPath(Path.Combine(Path.GetTempPath(), $"libVIIPER-{Guid.NewGuid():N}.dll"));
        nint FakeLoad(string p) { loadCount++; return new nint(loadCount); }

        using var failingRuntime = new ViiperRuntimeManager(path, p =>
        {
            ViiperNativeModuleCache.GetOrLoad(p, FakeLoad);
            return new FakeApi { DeviceTypes = ["xbox360"] };
        });
        Assert.Throws<InvalidOperationException>(() => failingRuntime.Start());

        using var succeedingRuntime = new ViiperRuntimeManager(path, p =>
        {
            ViiperNativeModuleCache.GetOrLoad(p, FakeLoad);
            return new FakeApi();
        });
        succeedingRuntime.CreateDevice();

        Assert.Equal(1, loadCount);
    }

    private sealed class FakeApi : IViiperNativeApi
    {
        public int[] AddResults { get; init; } = [0];
        public int CreateBusResult { get; init; }
        public int RemoveBusResult { get; init; }
        public int SetInputResult { get; init; }
        public List<uint> CreatedBuses { get; } = [];
        public List<uint> RemovedBuses { get; } = [];
        public List<uint> RemovedDevices { get; } = [];
        public List<(uint BusId, string TypeName, ushort VendorId, ushort ProductId)> AddDeviceCalls { get; } = [];
        public List<(uint BusId, uint DeviceId, byte[] Report)> InputCalls { get; } = [];
        public List<string> InitializeCalls { get; } = [];
        public string[] DeviceTypes { get; init; } = [ViiperRuntimeManager.DeviceType];
        public bool ShutdownCalled { get; private set; }
        public bool Disposed { get; private set; }
        private int _addIndex;

        public int Initialize(string listenAddress) { InitializeCalls.Add(listenAddress); return 0; }
        public void Shutdown() { ShutdownCalled = true; }
        public int CreateBus(uint busId) { CreatedBuses.Add(busId); return CreateBusResult; }
        public int RemoveBus(uint busId) { RemovedBuses.Add(busId); return RemoveBusResult; }
        public int AddDevice(uint busId, string typeName, ushort vendorId, ushort productId, out uint deviceId)
        {
            AddDeviceCalls.Add((busId, typeName, vendorId, productId));
            deviceId = 42;
            return AddResults[Math.Min(_addIndex++, AddResults.Length - 1)];
        }
        public int RemoveDevice(uint busId, uint deviceId) { RemovedDevices.Add(deviceId); return 0; }
        public int SetInput(uint busId, uint deviceId, byte[] report) { InputCalls.Add((busId, deviceId, report.ToArray())); return SetInputResult; }
        public int SetFeedbackCallback(uint busId, uint deviceId, Action<ReadOnlyMemory<byte>> callback) => 0;
        public string[] GetDeviceTypes() => DeviceTypes;
        public string? GetLastError() => "fake error";
        public void Dispose() { Disposed = true; }
    }
}
