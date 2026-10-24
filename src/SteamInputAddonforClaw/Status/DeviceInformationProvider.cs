using System.Management;
using SteamInputAddonforClaw.Devices;
using SteamInputAddonforClaw.Devices.Abstractions;

namespace SteamInputAddonforClaw.Status;

internal sealed class WindowsDeviceInformationProvider : IDeviceInformationProvider
{
    private readonly Func<IReadOnlyList<string>> _captureGpuModels;
    private readonly object _gpuModelsGate = new();
    private IReadOnlyList<string>? _cachedGpuModels;

    internal WindowsDeviceInformationProvider(Func<IReadOnlyList<string>>? captureGpuModels = null) =>
        _captureGpuModels = captureGpuModels ?? CaptureGpuModels;

    public DeviceStatusSnapshot Capture(WindowsDeviceIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(identity);
        IReadOnlyList<string> gpus;
        try { gpus = CaptureGpuModelsOnce(); }
        catch { gpus = ["Unknown"]; }
        return new(
            string.IsNullOrWhiteSpace(identity.Manufacturer) ? "Unknown" : identity.Manufacturer,
            string.IsNullOrWhiteSpace(identity.SystemProductName) ? "Unknown" : identity.SystemProductName,
            string.IsNullOrWhiteSpace(identity.BaseBoardProduct) ? "Unknown" : identity.BaseBoardProduct,
            gpus.Count == 0 ? ["Unknown"] : gpus);
    }

    private IReadOnlyList<string> CaptureGpuModelsOnce()
    {
        lock (_gpuModelsGate)
        {
            if (_cachedGpuModels is not null) return _cachedGpuModels;
            var captured = _captureGpuModels() ?? throw new InvalidOperationException("GPU capture returned no result.");
            _cachedGpuModels = captured.ToArray();
            return _cachedGpuModels;
        }
    }

    internal static bool IsSupportedGpuName(string name) => name.Contains("Intel", StringComparison.OrdinalIgnoreCase)
        || name.Contains("AMD", StringComparison.OrdinalIgnoreCase)
        || name.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase);

    internal static bool IsPhysicalGpu(string? name, string? pnpDeviceId) =>
        !string.IsNullOrWhiteSpace(name) && IsSupportedGpuName(name) &&
        !((pnpDeviceId ?? "").StartsWith("ROOT\\DISPLAY", StringComparison.OrdinalIgnoreCase));

    private static IReadOnlyList<string> CaptureGpuModels()
    {
        using var searcher = new ManagementObjectSearcher("SELECT Name, PNPDeviceID FROM Win32_VideoController");
        return searcher.Get().Cast<ManagementObject>()
            .Select(controller => (Name: controller["Name"]?.ToString(), PnpDeviceId: controller["PNPDeviceID"]?.ToString()))
            .Where(controller => IsPhysicalGpu(controller.Name, controller.PnpDeviceId))
            .Select(controller => controller.Name!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}
