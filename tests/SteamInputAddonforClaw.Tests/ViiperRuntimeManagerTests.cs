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
    public void FailedBusRemovalDoesNotReportDeviceCleanupAsComplete()
    {
        var api = new FakeApi { RemoveBusResult = 1 };
        using var runtime = new ViiperRuntimeManager(Path.GetFullPath("libVIIPER.dll"), _ => api);
        var deviceId = runtime.CreateDevice();

        Assert.False(runtime.RemoveDevice(runtime.BusId, deviceId));
        Assert.Contains(deviceId, runtime.OwnedDeviceIds);
    }

    [Fact]
    public void DisposeRemovesOwnedDevicesAndDisposesNativeRuntime()
    {
        var api = new FakeApi();
        var runtime = new ViiperRuntimeManager(Path.GetFullPath("libVIIPER.dll"), _ => api);
        var deviceId = runtime.CreateDevice();

        runtime.Dispose();

        Assert.Contains(deviceId, api.RemovedDevices);
        Assert.True(api.Disposed);
    }

    private sealed class FakeApi : IViiperNativeApi
    {
        public int[] AddResults { get; init; } = [0];
        public int RemoveBusResult { get; init; }
        public List<uint> CreatedBuses { get; } = [];
        public List<uint> RemovedBuses { get; } = [];
        public List<uint> RemovedDevices { get; } = [];
        public bool Disposed { get; private set; }
        private int _addIndex;

        public int Initialize(string listenAddress) => 0;
        public void Shutdown() { }
        public int CreateBus(uint busId) { CreatedBuses.Add(busId); return 0; }
        public int RemoveBus(uint busId) { RemovedBuses.Add(busId); return RemoveBusResult; }
        public int AddDevice(uint busId, string typeName, ushort vendorId, ushort productId, out uint deviceId)
        {
            deviceId = 42;
            return AddResults[Math.Min(_addIndex++, AddResults.Length - 1)];
        }
        public int RemoveDevice(uint busId, uint deviceId) { RemovedDevices.Add(deviceId); return 0; }
        public int SetInput(uint busId, uint deviceId, byte[] report) => 0;
        public int SetFeedbackCallback(uint busId, uint deviceId, Action<ReadOnlyMemory<byte>> callback) => 0;
        public string[] GetDeviceTypes() => [ViiperRuntimeManager.DeviceType];
        public string? GetLastError() => "fake error";
        public void Dispose() { Disposed = true; }
    }
}
