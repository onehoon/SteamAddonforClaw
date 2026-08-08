using System.Diagnostics;
using SteamInputAddonforClaw.Controllers.Detection;
using Windows.Management.Deployment;

namespace SteamInputAddonforClaw.Diagnostics;

internal static class ClawTweaksCompatibilitySnapshotLogger
{
    private static readonly string[] ProcessTokens = ["CLAW", "CTW", "GAMEBAR", "XBOX", "VIIPER", "USBIP"];
    private static readonly string[] PackageTokens = ["CLAW", "CTW", "GAMEBAR", "XBOXGAMINGBAR"];
    private static readonly string[] DeviceTokens = ["VID_045E&PID_028E", "VID_0DB0", "VIIPER", "USBIP", "USB/IP", "XUSB", "VIGEM", "CLAWTWEAKS"];

    public static void LogAtStartup(IControllerDeviceEnumerator deviceEnumerator)
    {
        ArgumentNullException.ThrowIfNull(deviceEnumerator);
        AppLog.Info("CompatibilitySnapshot", "ClawTweaks compatibility snapshot started.");
        LogPackages();
        LogProcesses();
        LogRelevantDevices(deviceEnumerator);
        AppLog.Info("CompatibilitySnapshot", "ClawTweaks compatibility snapshot completed.");
    }

    private static void LogPackages()
    {
        try
        {
            var packages = new PackageManager().FindPackagesForUser(string.Empty)
                .Where(package => MatchesAny(package.Id.Name, PackageTokens)
                    || MatchesAny(package.Id.FamilyName, PackageTokens)
                    || MatchesAny(package.DisplayName, PackageTokens))
                .ToArray();

            AppLog.Info("CompatibilitySnapshot", "MSIX package registration inspection completed.", ("MatchCount", packages.Length));
            foreach (var package in packages)
            {
                AppLog.Info("CompatibilitySnapshot", "MSIX package candidate.",
                    ("Name", package.Id.Name),
                    ("FamilyName", package.Id.FamilyName),
                    ("FullName", package.Id.FullName),
                    ("DisplayName", package.DisplayName),
                    ("InstallLocation", package.InstalledLocation?.Path));
            }
        }
        catch (Exception exception)
        {
            AppLog.Warn("CompatibilitySnapshot", "MSIX package registration inspection failed.", exception, ("Action", "Continue"));
        }
    }

    private static void LogProcesses()
    {
        try
        {
            var processes = Process.GetProcesses()
                .Where(process => MatchesAny(process.ProcessName, ProcessTokens))
                .OrderBy(process => process.ProcessName, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            AppLog.Info("CompatibilitySnapshot", "Helper process inspection completed.", ("MatchCount", processes.Length));
            foreach (var process in processes)
            {
                try
                {
                    AppLog.Info("CompatibilitySnapshot", "Helper process candidate.",
                        ("ProcessName", process.ProcessName),
                        ("ProcessId", process.Id),
                        ("SessionId", process.SessionId),
                        ("ImagePath", TryGetProcessImagePath(process)));
                }
                catch (Exception exception)
                {
                    AppLog.Warn("CompatibilitySnapshot", "Helper process candidate inspection failed.", exception, ("ProcessId", process.Id), ("Action", "Continue"));
                }
                finally
                {
                    process.Dispose();
                }
            }
        }
        catch (Exception exception)
        {
            AppLog.Warn("CompatibilitySnapshot", "Helper process inspection failed.", exception, ("Action", "Continue"));
        }
    }

    private static void LogRelevantDevices(IControllerDeviceEnumerator deviceEnumerator)
    {
        try
        {
            var devices = deviceEnumerator.EnumeratePresentDevices()
                .Where(IsCompatibilityRelevant)
                .ToArray();

            AppLog.Info("CompatibilitySnapshot", "Compatibility PnP inspection completed.", ("MatchCount", devices.Length));
            foreach (var device in devices)
            {
                AppLog.Info("CompatibilitySnapshot", "Compatibility PnP device candidate.",
                    ("InstanceId", device.InstanceId),
                    ("ParentInstanceId", device.ParentInstanceId),
                    ("AncestorInstanceIds", string.Join(" | ", device.AncestorInstanceIds)),
                    ("ContainerId", device.ContainerId),
                    ("HardwareIds", string.Join(" | ", device.HardwareIds)),
                    ("CompatibleIds", string.Join(" | ", device.CompatibleIds)),
                    ("EnumeratorName", device.EnumeratorName),
                    ("ClassName", device.ClassName),
                    ("ClassGuid", device.ClassGuid),
                    ("Service", device.Service),
                    ("VID", device.VendorId),
                    ("PID", device.ProductId));
            }
        }
        catch (Exception exception)
        {
            AppLog.Warn("CompatibilitySnapshot", "Compatibility PnP inspection failed.", exception, ("Action", "Continue"));
        }
    }

    internal static bool IsCompatibilityRelevant(ControllerDeviceInfo device)
    {
        var identity = string.Join('\n', device.HardwareIds
            .Concat(device.CompatibleIds)
            .Append(device.InstanceId)
            .Append(device.ParentInstanceId ?? string.Empty)
            .Concat(device.AncestorInstanceIds)
            .Append(device.EnumeratorName ?? string.Empty)
            .Append(device.ClassName ?? string.Empty)
            .Append(device.Service ?? string.Empty));
        return MatchesAny(identity, DeviceTokens);
    }

    internal static bool MatchesAny(string? value, IReadOnlyList<string> tokens)
        => !string.IsNullOrWhiteSpace(value) && tokens.Any(token => value.Contains(token, StringComparison.OrdinalIgnoreCase));

    private static string? TryGetProcessImagePath(Process process)
    {
        try
        {
            return process.MainModule?.FileName;
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
        {
            AppLog.Debug("CompatibilitySnapshot", "Helper process image path is unavailable.", ("ProcessId", process.Id), ("Reason", exception.GetType().Name));
            return null;
        }
    }
}
