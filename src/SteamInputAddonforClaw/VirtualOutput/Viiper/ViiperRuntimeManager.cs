namespace SteamInputAddonforClaw.VirtualOutput.Viiper;

internal interface IViiperRuntime : IDisposable
{
    IReadOnlyCollection<uint> OwnedDeviceIds { get; }
    uint BusId { get; }
    void Start();
    uint CreateDevice();
    bool SetNeutral(uint deviceId);
    bool SetInput(uint deviceId, byte[] report) => false;
    ViiperDeviceRemovalResult RemoveDevice(uint busId, uint deviceId);
    void StopIfUnused();
}

internal readonly record struct ViiperDeviceRemovalResult(bool DeviceRemoved, bool BusRemoved)
{
    internal static ViiperDeviceRemovalResult Failed { get; } = new(false, false);
}

internal sealed class ViiperRuntimeManager : IViiperRuntime
{
    internal const string ListenAddress = "127.0.0.1:3241";
    internal const string DeviceType = "steamcontroller";
    internal const ushort VendorId = 0x28DE;
    internal const ushort ProductId = 0x1102;

    private readonly Func<string, IViiperNativeApi> _apiFactory;
    private readonly string _dllPath;
    private IViiperNativeApi? _api;
    private uint? _busId;
    private uint _nextBusCandidate = 1;
    private readonly HashSet<uint> _devices = [];
    private bool _disposed;
    private const int MaxBusAllocationAttempts = 16;

    internal ViiperRuntimeManager(string dllPath, Func<string, IViiperNativeApi>? apiFactory = null)
    {
        if (!Path.IsPathFullyQualified(dllPath)) throw new ArgumentException("The VIIPER path must be absolute.", nameof(dllPath));
        _dllPath = dllPath;
        _apiFactory = apiFactory ?? ViiperNativeApi.Load;
    }

    internal IReadOnlyCollection<uint> OwnedDeviceIds => _devices;
    internal uint BusId => _busId ?? throw new InvalidOperationException("The VIIPER bus is not running.");

    IReadOnlyCollection<uint> IViiperRuntime.OwnedDeviceIds => OwnedDeviceIds;
    uint IViiperRuntime.BusId => BusId;
    void IViiperRuntime.Start() => Start();
    uint IViiperRuntime.CreateDevice() => CreateDevice();
    bool IViiperRuntime.SetNeutral(uint deviceId) => SetNeutral(deviceId);
    bool IViiperRuntime.SetInput(uint deviceId, byte[] report) => SetInput(deviceId, report);
    ViiperDeviceRemovalResult IViiperRuntime.RemoveDevice(uint busId, uint deviceId) => RemoveDevice(busId, deviceId);
    void IViiperRuntime.StopIfUnused() => StopIfUnused();

    internal void Start()
    {
        if (_api is not null) return;
        var api = _apiFactory(_dllPath);
        try
        {
            if (api.Initialize(ListenAddress) != 0) throw new InvalidOperationException(api.GetLastError() ?? "VIIPER initialization failed.");
            if (!api.GetDeviceTypes().Contains(DeviceType, StringComparer.OrdinalIgnoreCase)) throw new InvalidOperationException("VIIPER does not support steamcontroller.");
            _api = api;
        }
        catch
        {
            try { api.Shutdown(); } finally { api.Dispose(); }
            throw;
        }
    }

    internal uint CreateDevice()
    {
        Start();
        for (var attempt = 0; attempt < 2; attempt++)
        {
            EnsureBus();
            if (_api!.AddDevice(BusId, DeviceType, VendorId, ProductId, out var deviceId) == 0)
            {
                _devices.Add(deviceId);
                return deviceId;
            }

            // VIIPER invalidates a bus after an AddDevice failure. Dispose that
            // bus and retry once on a freshly allocated bus.
            var failedBus = _busId;
            _busId = null;
            if (failedBus is not null && _api.RemoveBus(failedBus.Value) != 0)
                throw new InvalidOperationException(_api.GetLastError() ?? "VIIPER failed to remove the invalid bus.");
        }

        throw new InvalidOperationException(_api!.GetLastError() ?? "VIIPER device creation failed after bus retry.");
    }

    private void EnsureBus()
    {
        if (_busId is not null) return;
        for (var attempt = 0; attempt < MaxBusAllocationAttempts; attempt++)
        {
            var candidate = _nextBusCandidate++;
            if (_api!.CreateBus(candidate) == 0) { _busId = candidate; _nextBusCandidate = candidate + 1; return; }
        }
        throw new InvalidOperationException(_api!.GetLastError() ?? "VIIPER bus creation failed.");
    }

    internal bool SetNeutral(uint deviceId)
    {
        var report = new byte[ClassicSteamControllerReportBuilder.ReportLength];
        ClassicSteamControllerReportBuilder.Write(report, 0, new ClassicSteamControllerInput(false, false));
        return SetInput(deviceId, report);
    }

    internal bool SetInput(uint deviceId, byte[] report) =>
        _api is not null && _busId is not null && _devices.Contains(deviceId) && report is not null && _api.SetInput(_busId.Value, deviceId, report) == 0;

    internal ViiperDeviceRemovalResult RemoveDevice(uint busId, uint deviceId)
    {
        if (_api is null || _busId != busId || !_devices.Contains(deviceId)) return ViiperDeviceRemovalResult.Failed;
        if (_api.RemoveDevice(busId, deviceId) != 0) return ViiperDeviceRemovalResult.Failed;

        _devices.Remove(deviceId);
        if (_devices.Count == 0)
        {
            var busRemoved = _api.RemoveBus(busId) == 0;
            if (busRemoved) _busId = null;
            return new(true, busRemoved);
        }

        return new(true, true);
    }

    internal void StopIfUnused()
    {
        if (_devices.Count != 0 || _api is null) return;
        _api.Shutdown();
        _api.Dispose();
        _api = null;
        _busId = null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_api is not null)
        {
            foreach (var device in _devices.ToArray()) _api.RemoveDevice(_busId ?? 0, device);
            if (_busId is { } bus) _api.RemoveBus(bus);
            _api.Shutdown();
            _api.Dispose();
        }
        _devices.Clear();
        _api = null;
        _busId = null;
    }
}
