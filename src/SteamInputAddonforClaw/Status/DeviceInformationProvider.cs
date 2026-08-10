using System.Management;
using SteamInputAddonforClaw.Devices.Abstractions;

namespace SteamInputAddonforClaw.Status;

internal sealed class WindowsDeviceInformationProvider : IDeviceInformationProvider
{
    public DeviceStatusSnapshot Capture(DeviceProbeContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        IReadOnlyList<string> gpus;
        try { gpus = CaptureGpuModels(); }
        catch { gpus = ["Unknown"]; }
        return new(
            string.IsNullOrWhiteSpace(context.SystemManufacturer) ? "Unknown" : context.SystemManufacturer,
            string.IsNullOrWhiteSpace(context.SystemProductName) ? "Unknown" : context.SystemProductName,
            string.IsNullOrWhiteSpace(context.BaseBoardProduct) ? "Unknown" : context.BaseBoardProduct,
            gpus.Count == 0 ? ["Unknown"] : gpus);
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
