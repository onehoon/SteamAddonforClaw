using Microsoft.Win32;
using SteamInputAddonforClaw.Controllers.Detection;

namespace SteamInputAddonforClaw.Status;

internal sealed class WindowsDeviceInformationProvider(IControllerDeviceEnumerator deviceEnumerator) : IDeviceInformationProvider
{
    public DeviceStatusSnapshot Capture()
    {
        try
        {
            using var bios = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\BIOS");
            var manufacturer = bios?.GetValue("SystemManufacturer") as string;
            var model = bios?.GetValue("SystemProductName") as string;
            var gpus = deviceEnumerator.EnumeratePresentDevices()
                .Where(device => string.Equals(device.ClassName, "Display", StringComparison.OrdinalIgnoreCase))
                .Select(device => device.FriendlyName)
                .Where(name => !string.IsNullOrWhiteSpace(name) && !IsSoftwareAdapter(name!))
                .Cast<string>()
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            return new(string.IsNullOrWhiteSpace(manufacturer) ? "Unknown" : manufacturer, string.IsNullOrWhiteSpace(model) ? "Unknown" : model, gpus.Length == 0 ? ["Unknown"] : gpus);
        }
        catch
        {
            return new("Unknown", "Unknown", ["Unknown"]);
        }
    }

    private static bool IsSoftwareAdapter(string name) => name.Contains("Microsoft Basic Display", StringComparison.OrdinalIgnoreCase)
        || name.Contains("Remote Display", StringComparison.OrdinalIgnoreCase)
        || name.Contains("Software Adapter", StringComparison.OrdinalIgnoreCase);
}
