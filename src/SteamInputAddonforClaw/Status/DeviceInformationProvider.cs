using Microsoft.Win32;
using System.Management;

namespace SteamInputAddonforClaw.Status;

internal sealed class WindowsDeviceInformationProvider : IDeviceInformationProvider
{
    public DeviceStatusSnapshot Capture()
    {
        try
        {
            using var bios = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\BIOS");
            var manufacturer = bios?.GetValue("SystemManufacturer") as string;
            var model = bios?.GetValue("SystemProductName") as string;
            var gpus = CaptureGpuModels();
            return new(string.IsNullOrWhiteSpace(manufacturer) ? "Unknown" : manufacturer, string.IsNullOrWhiteSpace(model) ? "Unknown" : model, gpus.Count == 0 ? ["Unknown"] : gpus);
        }
        catch
        {
            return new("Unknown", "Unknown", ["Unknown"]);
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
