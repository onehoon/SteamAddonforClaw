using Microsoft.Win32;
using SteamInputAddonforClaw.Controllers.Detection;

namespace SteamInputAddonforClaw.Prerequisites;

internal sealed record UsbIpWin2ProbeResult(bool ServiceInstalled, bool DevicePresent, bool DriverUsable, bool FilterInstalled = false);

internal interface IUsbIpWin2DeviceProbe
{
    UsbIpWin2ProbeResult Probe();
}

internal sealed class UsbIpWin2PrerequisiteInspector(
    IUsbIpWin2DeviceProbe deviceProbe,
    IUsbIpWin2PackageProbe packageProbe)
{
    internal const string RootHardwareId = "ROOT\\USBIP_WIN2\\UDE";
    internal const string ServiceName = "usbip2_ude";
    internal const string FilterServiceName = "usbip2_filter";

    public PrerequisiteAssessment Inspect()
    {
        UsbIpWin2PackageState package;
        try
        {
            package = packageProbe.Inspect();
        }
        catch
        {
            return new(PrerequisiteKind.UsbIpWin2, PrerequisiteStatus.Indeterminate, "UsbIpWin2PackageInspectionFailed");
        }

        try
        {
            if (!package.InspectionSucceeded)
                return new(PrerequisiteKind.UsbIpWin2, PrerequisiteStatus.Indeterminate, "UsbIpWin2PackageInspectionFailed", package.Version);

            var result = deviceProbe.Probe();
            var runtimeEvidence = result.ServiceInstalled || result.DevicePresent || result.FilterInstalled;

            if (!package.PackageEntryPresent)
                return runtimeEvidence
                    ? new(PrerequisiteKind.UsbIpWin2, PrerequisiteStatus.Indeterminate, "UsbIpWin2VersionUnavailable")
                    : new(PrerequisiteKind.UsbIpWin2, PrerequisiteStatus.Missing, "UsbIpWin2DeviceMissing");

            if (string.IsNullOrWhiteSpace(package.Version))
                return new(PrerequisiteKind.UsbIpWin2, PrerequisiteStatus.Indeterminate, "UsbIpWin2VersionUnavailable", package.Version);
            if (!Version.TryParse(package.Version, out var version))
                return new(PrerequisiteKind.UsbIpWin2, PrerequisiteStatus.Indeterminate, "UsbIpWin2VersionMalformed", package.Version);
            if (version != UsbIpWin2PackageMetadata.BundledVersion)
                return new(PrerequisiteKind.UsbIpWin2, PrerequisiteStatus.Incompatible, "UsbIpWin2VersionUnsupported", package.Version);
            if (result.ServiceInstalled && result.DevicePresent && result.DriverUsable && result.FilterInstalled)
                return new(PrerequisiteKind.UsbIpWin2, PrerequisiteStatus.Ready, "UsbIpWin2DeviceReady", package.Version);
            return new(PrerequisiteKind.UsbIpWin2, PrerequisiteStatus.Unusable, "UsbIpWin2DeviceUnavailable", package.Version);
        }
        catch
        {
            return new(PrerequisiteKind.UsbIpWin2, PrerequisiteStatus.Indeterminate, "UsbIpWin2InspectionFailed");
        }
    }
}

internal sealed class WindowsUsbIpWin2DeviceProbe(IControllerDeviceEnumerator controllerDeviceEnumerator) : IUsbIpWin2DeviceProbe
{
    public UsbIpWin2ProbeResult Probe()
    {
        var devices = controllerDeviceEnumerator.EnumeratePresentDevices();
        var device = devices.FirstOrDefault(IsUsbIpWin2UdeDevice);
        using var serviceKey = Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Services\{UsbIpWin2PrerequisiteInspector.ServiceName}");
        using var filterKey = Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Services\{UsbIpWin2PrerequisiteInspector.FilterServiceName}");
        var serviceInstalled = serviceKey is not null;
        return new UsbIpWin2ProbeResult(
            serviceInstalled,
            device is not null,
            device?.Present == true && string.Equals(device.Service, UsbIpWin2PrerequisiteInspector.ServiceName, StringComparison.OrdinalIgnoreCase),
            filterKey is not null);
    }

    private static bool IsUsbIpWin2UdeDevice(ControllerDeviceInfo device) =>
        string.Equals(device.Service, UsbIpWin2PrerequisiteInspector.ServiceName, StringComparison.OrdinalIgnoreCase)
        || Matches(device.InstanceId)
        || device.HardwareIds.Any(Matches)
        || device.CompatibleIds.Any(Matches);

    private static bool Matches(string? value) => string.Equals(value, UsbIpWin2PrerequisiteInspector.RootHardwareId, StringComparison.OrdinalIgnoreCase)
        || value?.StartsWith(UsbIpWin2PrerequisiteInspector.RootHardwareId + "\\", StringComparison.OrdinalIgnoreCase) == true;
}
