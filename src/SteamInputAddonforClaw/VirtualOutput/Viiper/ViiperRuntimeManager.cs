namespace SteamInputAddonforClaw.VirtualOutput.Viiper;

internal sealed class ViiperRuntimeManager : IDisposable
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

    internal ViiperRuntimeManager(string dllPath, Func<string, IViiperNativeApi>? apiFactory = null)
    {
        if (!Path.IsPathFullyQualified(dllPath)) throw new ArgumentException("The VIIPER path must be absolute.", nameof(dllPath));
        _dllPath = dllPath;
        _apiFactory = apiFactory ?? ViiperNativeApi.Load;
    }

    internal IReadOnlyCollection<uint> OwnedDeviceIds => _devices;
    internal uint BusId => _busId ?? throw new InvalidOperationException("The VIIPER bus is not running.");

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
        catch { api.Dispose(); throw; }
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
        for (var candidate = _nextBusCandidate; candidate < uint.MaxValue; candidate++)
        {
            if (_api!.CreateBus(candidate) == 0) { _busId = candidate; _nextBusCandidate = candidate + 1; return; }
        }
        throw new InvalidOperationException(_api!.GetLastError() ?? "VIIPER bus creation failed.");
    }

    internal bool SetNeutral(uint deviceId)
    {
        var report = new byte[ClassicSteamControllerReportBuilder.ReportLength];
        ClassicSteamControllerReportBuilder.Write(report, 0, new ClassicSteamControllerInput(false, false));
        return _api is not null && _devices.Contains(deviceId) && _api.SetInput(BusId, deviceId, report) == 0;
    }

    internal bool RemoveDevice(uint busId, uint deviceId)
    {
        if (_api is null || _busId != busId || !_devices.Contains(deviceId)) return false;
        var result = _api.RemoveDevice(busId, deviceId) == 0;
        if (result && _devices.Count == 1)
        {
            if (_api.RemoveBus(busId) != 0) return false;
            _devices.Remove(deviceId);
            _busId = null;
            return true;
        }
        if (result) _devices.Remove(deviceId);
        return result;
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
