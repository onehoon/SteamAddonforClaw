using System.Management;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using SteamInputAddonforClaw.Contracts.Frontend;
using SteamInputAddonforClaw.Controllers.Detection;
using SteamInputAddonforClaw.Devices.MSI.Claw;
using Windows.Management.Deployment;

namespace SteamInputAddonforClaw.Devices.MSI.LiveUpdate;

internal sealed record MsiUpdateInstalledPnpDevice(
    string DeviceId,
    IReadOnlyList<string> HardwareIds,
    IReadOnlyList<string> CompatibleIds,
    string? ClassName,
    string? FriendlyName,
    string? WmiDeviceClass,
    string? WmiDeviceName,
    string? DriverProvider,
    string? DriverVersion,
    string? InfName,
    ushort? VendorId,
    ushort? ProductId);

internal sealed record MsiUpdateInstalledSoftware(
    string RegistryKeyName,
    string? ProductCode,
    string? UpgradeCode,
    string? DisplayName,
    string? DisplayVersion,
    string? Publisher);

internal sealed record MsiUpdateInstalledPackage(string Name, string FamilyName, string Version);

internal sealed record MsiUpdateControllerFirmware(string Version,
    FrontendMsiLiveUpdateInstalledVersionConfidence Confidence);

internal sealed record MsiUpdateInstalledInventory(
    IReadOnlyList<string> BiosVersions,
    IReadOnlyList<MsiUpdateInstalledPnpDevice> PnpDevices,
    IReadOnlyList<MsiUpdateInstalledSoftware> Software,
    IReadOnlyList<MsiUpdateInstalledPackage> Packages,
    MsiUpdateControllerFirmware? ControllerFirmware,
    IReadOnlyList<string> Warnings)
{
    private const int MaximumInventoryEntries = 4096;
    private const int MaximumValueLength = 512;
    private const int MaximumWmiDurationSeconds = 15;

    internal static Task<MsiUpdateInstalledInventory> CaptureAsync(
        Func<CancellationToken, Task<string?>>? exactControllerFirmwareReader,
        CancellationToken cancellationToken) =>
        Task.Run(() => Capture(exactControllerFirmwareReader, cancellationToken), cancellationToken);

    private static MsiUpdateInstalledInventory Capture(
        Func<CancellationToken, Task<string?>>? exactControllerFirmwareReader,
        CancellationToken cancellationToken)
    {
        var warnings = new List<string>();
        var biosVersions = CaptureBiosVersions(warnings, cancellationToken);
        var presentDevices = CapturePresentDevices(warnings, cancellationToken);
        var pnpDevices = CaptureSignedDriverVersions(presentDevices, warnings, cancellationToken);
        var software = CaptureUninstallEntries(warnings, cancellationToken);
        var packages = CaptureAppxPackages(warnings, cancellationToken);
        var controllerFirmware = CaptureControllerRevision(presentDevices);

        if (exactControllerFirmwareReader is not null)
        {
            try
            {
                var exactVersion = exactControllerFirmwareReader(cancellationToken).GetAwaiter().GetResult();
                if (!string.IsNullOrWhiteSpace(exactVersion))
                    controllerFirmware = new(exactVersion, FrontendMsiLiveUpdateInstalledVersionConfidence.Exact);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                // The safe present-PnP revision, when available, remains the explicit heuristic fallback.
            }
        }

        return new(biosVersions, pnpDevices, software, packages, controllerFirmware,
            warnings.Distinct(StringComparer.Ordinal).Take(16).ToArray());
    }

    private static IReadOnlyList<string> CaptureBiosVersions(List<string> warnings, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\BIOS", writable: false);
            if (key?.GetValue("BIOSVersion") is string[] values)
                return values.Select(SafeValue).Where(value => value is not null).Cast<string>().Take(16).ToArray();
            if (key?.GetValue("BIOSVersion") is string single)
            {
                var safe = SafeValue(single);
                return safe is null ? [] : [safe];
            }
            warnings.Add("BiosUnavailable");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            warnings.Add("BiosReadFailed");
        }
        return [];
    }

    private static IReadOnlyList<ControllerDeviceInfo> CapturePresentDevices(List<string> warnings, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!OperatingSystem.IsWindows())
        {
            warnings.Add("PnPUnavailable");
            return [];
        }
        try
        {
            return new WindowsControllerDeviceEnumerator().EnumeratePresentDevices()
                .Where(device => device.Present)
                .Take(MaximumInventoryEntries)
                .ToArray();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            warnings.Add("PnPReadFailed");
            return [];
        }
    }

    private static IReadOnlyList<MsiUpdateInstalledPnpDevice> CaptureSignedDriverVersions(
        IReadOnlyList<ControllerDeviceInfo> presentDevices,
        List<string> warnings,
        CancellationToken cancellationToken)
    {
        var presentById = presentDevices.ToDictionary(device => device.InstanceId, StringComparer.OrdinalIgnoreCase);
        var rows = new Dictionary<string, (string? DeviceClass, string? DeviceName, string? Provider, string? Version, string? InfName)>(StringComparer.OrdinalIgnoreCase);
        if (OperatingSystem.IsWindows())
        {
            try
            {
                using var searcher = new ManagementObjectSearcher(
                    "SELECT DeviceID, DeviceClass, DeviceName, DriverProviderName, DriverVersion, InfName FROM Win32_PnPSignedDriver");
                searcher.Options.Timeout = TimeSpan.FromSeconds(MaximumWmiDurationSeconds);
                using var results = searcher.Get();
                foreach (ManagementObject item in results)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var deviceId = SafeValue(item["DeviceID"]?.ToString());
                    if (deviceId is null || !presentById.ContainsKey(deviceId)) continue;
                    rows[deviceId] = (
                        SafeValue(item["DeviceClass"]?.ToString()),
                        SafeValue(item["DeviceName"]?.ToString()),
                        SafeValue(item["DriverProviderName"]?.ToString()),
                        SafeValue(item["DriverVersion"]?.ToString()),
                        SafeValue(item["InfName"]?.ToString()));
                    if (rows.Count >= MaximumInventoryEntries) break;
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception)
            {
                warnings.Add("WmiDriverReadFailed");
            }
        }
        else
        {
            warnings.Add("WmiUnavailable");
        }

        return presentDevices.Take(MaximumInventoryEntries).Select(device =>
        {
            rows.TryGetValue(device.InstanceId, out var row);
            return new MsiUpdateInstalledPnpDevice(
                device.InstanceId,
                device.HardwareIds.Select(SafeValue).Where(value => value is not null).Cast<string>().Take(32).ToArray(),
                device.CompatibleIds.Select(SafeValue).Where(value => value is not null).Cast<string>().Take(32).ToArray(),
                SafeValue(device.ClassName), SafeValue(device.FriendlyName), row.DeviceClass, row.DeviceName,
                row.Provider, row.Version, row.InfName, device.VendorId, device.ProductId);
        }).ToArray();
    }

    private static IReadOnlyList<MsiUpdateInstalledSoftware> CaptureUninstallEntries(List<string> warnings, CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows())
        {
            warnings.Add("UninstallRegistryUnavailable");
            return [];
        }
        var entries = new List<MsiUpdateInstalledSoftware>();
        AddUninstallEntries(entries, RegistryHive.LocalMachine, RegistryView.Registry64, warnings, cancellationToken);
        AddUninstallEntries(entries, RegistryHive.LocalMachine, RegistryView.Registry32, warnings, cancellationToken);
        AddUninstallEntries(entries, RegistryHive.CurrentUser, RegistryView.Default, warnings, cancellationToken);
        return entries.Take(MaximumInventoryEntries).ToArray();
    }

    private static void AddUninstallEntries(List<MsiUpdateInstalledSoftware> entries, RegistryHive hive, RegistryView view,
        List<string> warnings, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var sourceName = hive == RegistryHive.LocalMachine ? "HklmUninstallReadFailed" : "HkcuUninstallReadFailed";
        try
        {
            using var root = RegistryKey.OpenBaseKey(hive, view);
            using var uninstall = root.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall", writable: false);
            if (uninstall is null) return;
            foreach (var keyName in uninstall.GetSubKeyNames().Take(MaximumInventoryEntries))
            {
                cancellationToken.ThrowIfCancellationRequested();
                using var key = uninstall.OpenSubKey(keyName, writable: false);
                if (key is null) continue;
                var productCode = ParseGuid(keyName) ?? ParseGuid(SafeValue(key.GetValue("ProductCode")?.ToString()));
                entries.Add(new(keyName, productCode, ParseGuid(SafeValue(key.GetValue("UpgradeCode")?.ToString())),
                    SafeValue(key.GetValue("DisplayName")?.ToString()), SafeValue(key.GetValue("DisplayVersion")?.ToString()),
                    SafeValue(key.GetValue("Publisher")?.ToString())));
                if (entries.Count >= MaximumInventoryEntries) break;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            warnings.Add(sourceName);
        }
    }

    private static IReadOnlyList<MsiUpdateInstalledPackage> CaptureAppxPackages(List<string> warnings, CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows())
        {
            warnings.Add("AppxUnavailable");
            return [];
        }
        try
        {
            var packages = new List<MsiUpdateInstalledPackage>();
            foreach (var package in new PackageManager().FindPackagesForUser(string.Empty))
            {
                cancellationToken.ThrowIfCancellationRequested();
                packages.Add(new(
                    SafeValue(package.Id.Name) ?? string.Empty,
                    SafeValue(package.Id.FamilyName) ?? string.Empty,
                    SafeValue(package.Id.Version.ToString()) ?? string.Empty));
                if (packages.Count >= MaximumInventoryEntries) break;
            }
            return packages;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            warnings.Add("AppxReadFailed");
            return [];
        }
    }

    private static MsiUpdateControllerFirmware? CaptureControllerRevision(IReadOnlyList<ControllerDeviceInfo> devices)
    {
        var revisions = devices
            .Where(device => device.VendorId == MsiClawHardware.VendorId
                && device.ProductId is MsiClawHardware.XInputProductId or MsiClawHardware.DirectInputProductId)
            .SelectMany(device => new[] { device.InstanceId }.Concat(device.HardwareIds))
            .Select(ParseRevision)
            .Where(revision => revision is not null)
            .Cast<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return revisions.Length == 1
            ? new(revisions[0], FrontendMsiLiveUpdateInstalledVersionConfidence.Heuristic)
            : null;
    }

    internal static string? ParseRevision(string? hardwareId)
    {
        if (string.IsNullOrWhiteSpace(hardwareId)) return null;
        var match = Regex.Match(hardwareId, @"(?:^|&)REV_([0-9A-F]{4})(?:&|$)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        return match.Success ? $"0x{match.Groups[1].Value.ToUpperInvariant()}" : null;
    }

    internal static string? ParseGuid(string? value) =>
        Guid.TryParse(value, out var parsed) ? parsed.ToString("D") : null;

    private static string? SafeValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > MaximumValueLength) return null;
        var safe = new string(value.Where(static character => !char.IsControl(character)).ToArray()).Trim();
        return safe.Length == 0 ? null : safe;
    }
}

internal sealed record IntelDsaResolvedDriver(
    IntelDsaComponentFamily Family,
    string PackageId,
    string PackageName,
    string? LatestVersion,
    FrontendMsiLiveUpdateVersionKind VersionKind,
    string? ReleaseDate,
    MsiUpdateInstalledPnpDevice Device,
    string MatchReason);

internal static class IntelDsaHardwareMatcher
{
    internal static IReadOnlyList<IntelDsaResolvedDriver> Resolve(
        IntelDsaCatalogResult catalog,
        MsiUpdateInstalledInventory inventory,
        string operatingSystem)
    {
        if (!catalog.Succeeded) return [];
        var candidates = new List<(IntelDsaResolvedDriver Result, int Specificity, string? VersionForOrdering)>();
        foreach (var device in inventory.PnpDevices)
        {
            foreach (var package in catalog.Packages)
            {
                if (package.IsBeta != false || !IsOperatingSystemCompatible(package.OperatingSystems, operatingSystem)) continue;
                foreach (var component in package.Components)
                {
                    if (!IsOperatingSystemCompatible(component.OperatingSystems, operatingSystem)
                        || !TryGetFamily(package.Name, component.Category, device, out var family)) continue;
                    var specificity = component.DetectionValues
                        .Select(value => TryMatchDetectionValue(value, device.HardwareIds.Concat(device.CompatibleIds), out var score) ? score : -1)
                        .DefaultIfEmpty(-1)
                        .Max();
                    if (specificity < 0) continue;
                    var latestVersion = family == IntelDsaComponentFamily.Npu ? package.Version : component.Version;
                    candidates.Add((new(family, package.Id, package.Name, latestVersion,
                        family == IntelDsaComponentFamily.Npu
                            ? FrontendMsiLiveUpdateVersionKind.Package
                            : FrontendMsiLiveUpdateVersionKind.Driver,
                        package.ReleaseDate, device, $"{component.Category}:HardwareId"), specificity, latestVersion));
                }
            }
        }

        return candidates
            .GroupBy(candidate => (candidate.Result.Device.DeviceId, candidate.Result.Family), StringTupleComparer.Instance)
            .Select(SelectBestCandidate)
            .Where(result => result is not null)
            .Cast<IntelDsaResolvedDriver>()
            .ToArray();
    }

    internal static bool TryMatchDetectionValue(string detectionValue, IEnumerable<string> hardwareIds, out int specificity)
    {
        specificity = 0;
        if (!TryParseHardwarePattern(detectionValue, out var pattern)) return false;
        foreach (var hardwareId in hardwareIds)
        {
            if (!TryParseHardwarePattern(hardwareId, out var actual) || !string.IsNullOrEmpty(pattern.Bus)
                && !string.Equals(pattern.Bus, actual.Bus, StringComparison.OrdinalIgnoreCase)) continue;
            var requiredVendor = string.Equals(pattern.VendorKey, actual.VendorKey, StringComparison.OrdinalIgnoreCase)
                && string.Equals(pattern.VendorValue, actual.VendorValue, StringComparison.OrdinalIgnoreCase);
            var requiredDevice = string.Equals(pattern.DeviceKey, actual.DeviceKey, StringComparison.OrdinalIgnoreCase)
                && string.Equals(pattern.DeviceValue, actual.DeviceValue, StringComparison.OrdinalIgnoreCase);
            if (!requiredVendor || !requiredDevice) continue;

            var score = 2;
            var matched = true;
            foreach (var (key, expected) in pattern.OptionalSegments)
            {
                if (!actual.OptionalSegments.TryGetValue(key, out var actualValue) || !MatchSegment(expected, actualValue))
                {
                    matched = false;
                    break;
                }
                score++;
            }
            if (!matched) continue;
            specificity = score;
            return true;
        }
        return false;
    }

    private static IntelDsaResolvedDriver? SelectBestCandidate(
        IGrouping<(string DeviceId, IntelDsaComponentFamily Family), (IntelDsaResolvedDriver Result, int Specificity, string? VersionForOrdering)> group)
    {
        var bestSpecificity = group.Max(candidate => candidate.Specificity);
        var best = group.Where(candidate => candidate.Specificity == bestSpecificity).ToArray();
        var versioned = best.Where(candidate => candidate.VersionForOrdering is not null).ToArray();
        if (versioned.Length == 0) return best[0].Result with { LatestVersion = null };
        if (versioned.Select(candidate => candidate.VersionForOrdering).Distinct(StringComparer.OrdinalIgnoreCase).Count() == 1)
            return versioned.OrderBy(candidate => candidate.Result.PackageId, StringComparer.Ordinal).First().Result;

        if (TrySelectNewestComparable(versioned, out var selected)) return selected.Result;
        return best[0].Result with { LatestVersion = null };
    }

    private static bool TrySelectNewestComparable(
        IReadOnlyList<(IntelDsaResolvedDriver Result, int Specificity, string? VersionForOrdering)> candidates,
        out (IntelDsaResolvedDriver Result, int Specificity, string? VersionForOrdering) selected)
    {
        selected = default;
        int[]? bestParts = null;
        var count = -1;
        foreach (var candidate in candidates)
        {
            if (!TryParseNumericVersion(candidate.VersionForOrdering, out var parts)) return false;
            if (count < 0) count = parts.Length;
            if (parts.Length != count) return false;
            if (bestParts is null || Compare(parts, bestParts) > 0)
            {
                bestParts = parts;
                selected = candidate;
            }
        }
        return bestParts is not null;
    }

    private static bool TryParseNumericVersion(string? value, out int[] parts)
    {
        parts = [];
        if (string.IsNullOrWhiteSpace(value)) return false;
        var segments = value.Split('.');
        if (segments.Length is < 2 or > 4) return false;
        var parsed = new int[segments.Length];
        for (var index = 0; index < segments.Length; index++)
            if (segments[index].Length is < 1 or > 10 || !int.TryParse(segments[index], out parsed[index]) || parsed[index] < 0)
                return false;
        parts = parsed;
        return true;
    }

    private static int Compare(IReadOnlyList<int> left, IReadOnlyList<int> right)
    {
        for (var index = 0; index < left.Count; index++)
        {
            var compared = left[index].CompareTo(right[index]);
            if (compared != 0) return compared;
        }
        return 0;
    }

    private static bool IsOperatingSystemCompatible(IReadOnlyList<string> restrictions, string operatingSystem)
    {
        if (restrictions.Count == 0) return true;
        var recognized = restrictions.SelectMany(NormalizeWindowsVersions)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        return recognized.Length == 0 || recognized.Contains(operatingSystem, StringComparer.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<string> NormalizeWindowsVersions(string value)
    {
        var normalized = value.Replace(" ", string.Empty, StringComparison.Ordinal)
            .Replace("-", string.Empty, StringComparison.Ordinal)
            .Replace("_", string.Empty, StringComparison.Ordinal)
            .ToLowerInvariant();
        var result = new List<string>(2);
        if (normalized.Contains("windows10", StringComparison.Ordinal) || normalized.Contains("win10", StringComparison.Ordinal))
            result.Add("win10");
        if (normalized.Contains("windows11", StringComparison.Ordinal) || normalized.Contains("win11", StringComparison.Ordinal))
            result.Add("win11");
        return result;
    }

    private static bool TryGetFamily(string packageName, string category, MsiUpdateInstalledPnpDevice device,
        out IntelDsaComponentFamily family)
    {
        family = default;
        if (string.Equals(category, "Graphics", StringComparison.OrdinalIgnoreCase)
            && IsIntel(device) && IsDisplay(device))
        {
            family = IntelDsaComponentFamily.Graphics;
            return true;
        }
        if (string.Equals(category, "Wireless", StringComparison.OrdinalIgnoreCase)
            && IsIntel(device) && string.Equals(device.WmiDeviceClass ?? device.ClassName, "NET", StringComparison.OrdinalIgnoreCase))
        {
            family = IntelDsaComponentFamily.Wireless;
            return true;
        }
        if (string.Equals(category, "Bluetooth", StringComparison.OrdinalIgnoreCase)
            && IsIntelBluetoothDevice(device))
        {
            family = IntelDsaComponentFamily.Bluetooth;
            return true;
        }
        if (string.Equals(category, "OtherDrivers", StringComparison.OrdinalIgnoreCase)
            && packageName.Contains("NPU", StringComparison.OrdinalIgnoreCase)
            && HasPciVendorDevice(device, "VEN", "8086"))
        {
            family = IntelDsaComponentFamily.Npu;
            return true;
        }
        return false;
    }

    private static bool IsIntel(MsiUpdateInstalledPnpDevice device) =>
        HasPciVendorDevice(device, "VEN", "8086") || HasUsbVendorDevice(device, "VID", "8086");

    private static bool IsIntelBluetoothDevice(MsiUpdateInstalledPnpDevice device) =>
        (string.Equals(device.WmiDeviceClass ?? device.ClassName, "BLUETOOTH", StringComparison.OrdinalIgnoreCase)
            || device.DeviceId.Contains("BTHENUM", StringComparison.OrdinalIgnoreCase)
            || device.HardwareIds.Any(id => id.Contains("BTHENUM", StringComparison.OrdinalIgnoreCase))
            || HasUsbVendorDevice(device, "VID", "8087"))
        && (IsIntel(device) || HasUsbVendorDevice(device, "VID", "8087"));

    private static bool IsDisplay(MsiUpdateInstalledPnpDevice device) =>
        string.Equals(device.WmiDeviceClass ?? device.ClassName, "DISPLAY", StringComparison.OrdinalIgnoreCase)
        || device.HardwareIds.Any(id => id.Contains("CC_0300", StringComparison.OrdinalIgnoreCase)
            || id.Contains("CC_0302", StringComparison.OrdinalIgnoreCase));

    private static bool HasPciVendorDevice(MsiUpdateInstalledPnpDevice device, string vendorKey, string vendorValue) =>
        device.HardwareIds.Any(id => TryParseHardwarePattern(id, out var parsed)
            && string.Equals(parsed.Bus, "PCI", StringComparison.OrdinalIgnoreCase)
            && parsed.VendorKey == vendorKey && parsed.VendorValue == vendorValue && parsed.DeviceKey == "DEV");

    private static bool HasUsbVendorDevice(MsiUpdateInstalledPnpDevice device, string vendorKey, string vendorValue) =>
        device.HardwareIds.Any(id => TryParseHardwarePattern(id, out var parsed)
            && string.Equals(parsed.Bus, "USB", StringComparison.OrdinalIgnoreCase)
            && parsed.VendorKey == vendorKey && parsed.VendorValue == vendorValue && parsed.DeviceKey == "PID");

    private static bool MatchSegment(string expected, string actual)
    {
        if (!expected.Contains('*')) return string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase);
        if (expected.Count(character => character == '*') != 1) return false;
        var split = expected.Split('*');
        return actual.Length >= split[0].Length + split[1].Length
            && actual.StartsWith(split[0], StringComparison.OrdinalIgnoreCase)
            && actual.EndsWith(split[1], StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryParseHardwarePattern(string value, out HardwarePattern pattern)
    {
        pattern = default;
        if (string.IsNullOrWhiteSpace(value) || value.Length > 512) return false;
        var sections = value.Trim().Split('\\', StringSplitOptions.RemoveEmptyEntries);
        string? bus = null;
        var tokenSection = sections[^1];
        if (sections.Length > 1 && (sections[0].Equals("PCI", StringComparison.OrdinalIgnoreCase)
            || sections[0].Equals("USB", StringComparison.OrdinalIgnoreCase)))
            bus = sections[0].ToUpperInvariant();
        var tokens = tokenSection.Split('&', StringSplitOptions.RemoveEmptyEntries);
        string? vendorKey = null, vendorValue = null, deviceKey = null, deviceValue = null;
        var optional = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var token in tokens)
        {
            var separator = token.IndexOf('_');
            if (separator <= 0 || separator == token.Length - 1) continue;
            var key = token[..separator].ToUpperInvariant();
            var part = token[(separator + 1)..].ToUpperInvariant();
            if (key is "VEN" or "VID") { vendorKey = key; vendorValue = part; }
            else if (key is "DEV" or "PID") { deviceKey = key; deviceValue = part; }
            else optional[key] = part;
        }
        if (vendorKey is null || deviceKey is null || vendorValue is null || deviceValue is null
            || vendorValue.Length != 4 || deviceValue.Length != 4
            || vendorValue.Any(character => !Uri.IsHexDigit(character)) || deviceValue.Any(character => !Uri.IsHexDigit(character))
            || vendorValue.Contains('*') || deviceValue.Contains('*')) return false;
        pattern = new(bus, vendorKey, vendorValue, deviceKey, deviceValue, optional);
        return true;
    }

    private readonly record struct HardwarePattern(string? Bus, string VendorKey, string VendorValue,
        string DeviceKey, string DeviceValue, IReadOnlyDictionary<string, string> OptionalSegments);

    private sealed class StringTupleComparer : IEqualityComparer<(string DeviceId, IntelDsaComponentFamily Family)>
    {
        internal static StringTupleComparer Instance { get; } = new();
        public bool Equals((string DeviceId, IntelDsaComponentFamily Family) x, (string DeviceId, IntelDsaComponentFamily Family) y) =>
            x.Family == y.Family && string.Equals(x.DeviceId, y.DeviceId, StringComparison.OrdinalIgnoreCase);
        public int GetHashCode((string DeviceId, IntelDsaComponentFamily Family) value) =>
            HashCode.Combine(StringComparer.OrdinalIgnoreCase.GetHashCode(value.DeviceId), value.Family);
    }
}

internal static class MsiUpdateCatalogComposer
{
    internal static FrontendMsiLiveUpdateCatalogSnapshot Compose(
        MsiLiveUpdateCatalogQueryResult msi,
        IntelDsaCatalogResult intel,
        MsiUpdateInstalledInventory inventory,
        Action<IntelDsaResolvedDriver>? onIntelMatch = null)
    {
        var msiSource = MapMsiSource(msi);
        var intelSource = new FrontendMsiLiveUpdateSourceSnapshot(
            intel.Succeeded ? FrontendMsiLiveUpdateSourceState.Completed : FrontendMsiLiveUpdateSourceState.Unavailable,
            intel.ErrorKind);
        var intelRows = IntelDsaHardwareMatcher.Resolve(intel,
            inventory, msi.OperatingSystem ?? CurrentWindowsVersion());
        foreach (var match in intelRows.Take(16)) onIntelMatch?.Invoke(match);
        var categoryResults = new List<FrontendMsiLiveUpdateCatalogCategorySnapshot>(4);

        foreach (var (sourceCategory, frontendCategory) in new[]
        {
            (MsiLiveUpdateCatalogCategory.Bios, FrontendMsiLiveUpdateCatalogCategory.Bios),
            (MsiLiveUpdateCatalogCategory.Firmware, FrontendMsiLiveUpdateCatalogCategory.Firmware),
            (MsiLiveUpdateCatalogCategory.Driver, FrontendMsiLiveUpdateCatalogCategory.Driver),
            (MsiLiveUpdateCatalogCategory.Utility, FrontendMsiLiveUpdateCatalogCategory.Utility)
        })
        {
            var source = msi.Categories.FirstOrDefault(item => item.Category == sourceCategory);
            if (source is null)
            {
                if (sourceCategory == MsiLiveUpdateCatalogCategory.Driver && intelRows.Count > 0)
                {
                    categoryResults.Add(new(frontendCategory,
                        FrontendMsiLiveUpdateCatalogCategoryState.Success,
                        intelRows.Select(MapIntelEntry).ToArray(), "MsiCategoryMissing"));
                    continue;
                }

                categoryResults.Add(new(frontendCategory, FrontendMsiLiveUpdateCatalogCategoryState.Failed, [], "CategoryMissing"));
                continue;
            }

            var sourceEntries = source.Entries.ToList();
            if (sourceCategory == MsiLiveUpdateCatalogCategory.Driver && intel.Succeeded)
            {
                foreach (var resolved in intelRows)
                    sourceEntries.RemoveAll(entry => IsExactMsiDuplicate(entry, resolved));
            }
            var entries = sourceEntries.Select(entry => MapMsiEntry(sourceCategory, entry, inventory)).ToList();
            var categoryState = source.State switch
            {
                MsiLiveUpdateCatalogCategoryState.Success => FrontendMsiLiveUpdateCatalogCategoryState.Success,
                MsiLiveUpdateCatalogCategoryState.Empty => FrontendMsiLiveUpdateCatalogCategoryState.Empty,
                _ => FrontendMsiLiveUpdateCatalogCategoryState.Failed
            };
            var categoryErrorKind = source.ErrorKind;

            if (sourceCategory == MsiLiveUpdateCatalogCategory.Driver)
            {
                if (intel.Succeeded)
                {
                    foreach (var resolved in intelRows)
                    {
                        entries.Add(MapIntelEntry(resolved));
                    }
                    if (categoryState == FrontendMsiLiveUpdateCatalogCategoryState.Failed && entries.Count > 0)
                    {
                        categoryState = FrontendMsiLiveUpdateCatalogCategoryState.Success;
                        categoryErrorKind = null;
                    }
                    else if (entries.Count > 0)
                    {
                        categoryState = FrontendMsiLiveUpdateCatalogCategoryState.Success;
                    }
                }
            }

            categoryResults.Add(new(frontendCategory, categoryState, entries, categoryErrorKind));
        }

        var hasMsiSuccess = msi.State is MsiLiveUpdateCatalogState.Completed or MsiLiveUpdateCatalogState.PartialFailure;
        var hasAnyRemoteSuccess = hasMsiSuccess || intel.Succeeded;
        var hasPartialSource = msiSource.State != FrontendMsiLiveUpdateSourceState.Completed
            || intelSource.State != FrontendMsiLiveUpdateSourceState.Completed
            || inventory.Warnings.Count > 0
            || categoryResults.Any(category => category.State == FrontendMsiLiveUpdateCatalogCategoryState.Failed);
        var state = hasAnyRemoteSuccess
            ? hasPartialSource ? FrontendMsiLiveUpdateCatalogState.PartialFailure : FrontendMsiLiveUpdateCatalogState.Completed
            : msi.State == MsiLiveUpdateCatalogState.Unavailable
                ? FrontendMsiLiveUpdateCatalogState.Unavailable
                : FrontendMsiLiveUpdateCatalogState.Failed;
        var errorKind = hasPartialSource
            ? BuildCombinedError(msiSource, intelSource, inventory.Warnings)
            : null;

        return new(state, msi.ProductTitle, msi.CountryCode, msi.OperatingSystem ?? CurrentWindowsVersion(),
            categoryResults, DateTimeOffset.UtcNow, errorKind, msiSource, intelSource, inventory.Warnings);
    }

    private static FrontendMsiLiveUpdateCatalogEntry MapMsiEntry(
        MsiLiveUpdateCatalogCategory category,
        MsiLiveUpdateCatalogEntry entry,
        MsiUpdateInstalledInventory inventory)
    {
        var latestVersion = entry.DriverVersion ?? entry.PackageVersion ?? entry.Version;
        if (string.Equals(latestVersion, "UWP", StringComparison.OrdinalIgnoreCase)) latestVersion = null;
        var versionKind = !string.IsNullOrWhiteSpace(entry.DriverVersion)
            ? FrontendMsiLiveUpdateVersionKind.Driver
            : !string.IsNullOrWhiteSpace(entry.PackageVersion)
                ? FrontendMsiLiveUpdateVersionKind.Package
                : FrontendMsiLiveUpdateVersionKind.Catalog;
        var installed = ResolveMsiInstalledVersion(category, entry, inventory);
        return new(entry.Name, latestVersion, entry.ReleaseDate, entry.Description,
            installed?.Version, FrontendMsiLiveUpdateVersionSource.Msi, versionKind, installed?.Confidence);
    }

    private static (string Version, FrontendMsiLiveUpdateInstalledVersionConfidence Confidence)? ResolveMsiInstalledVersion(
        MsiLiveUpdateCatalogCategory category,
        MsiLiveUpdateCatalogEntry entry,
        MsiUpdateInstalledInventory inventory)
    {
        if (category == MsiLiveUpdateCatalogCategory.Bios && IsBiosImage(entry))
        {
            var version = entry.Version ?? entry.DriverVersion ?? entry.PackageVersion;
            var prefixMatch = version is null ? Match.Empty : Regex.Match(version,
                @"^(?<board>[A-Z0-9]+IMS)\.[0-9A-F]+T?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (prefixMatch.Success)
            {
                var board = Regex.Escape(prefixMatch.Groups["board"].Value);
                foreach (var installed in inventory.BiosVersions)
                {
                    var match = Regex.Match(installed, $@"(?<![A-Z0-9])(?<version>{board}\.[0-9A-F]+T?)(?![A-Z0-9])",
                        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
                    if (match.Success) return (match.Groups["version"].Value, FrontendMsiLiveUpdateInstalledVersionConfidence.Exact);
                }
            }
            return null;
        }

        if (category == MsiLiveUpdateCatalogCategory.Firmware && IsControllerFirmware(entry))
            return inventory.ControllerFirmware is { } controller ? (controller.Version, controller.Confidence) : null;

        if (category == MsiLiveUpdateCatalogCategory.Driver)
        {
            IEnumerable<MsiUpdateInstalledPnpDevice> matches;
            if (!string.IsNullOrWhiteSpace(entry.DeviceId))
            {
                matches = inventory.PnpDevices.Where(device =>
                    string.Equals(device.DeviceId, entry.DeviceId, StringComparison.OrdinalIgnoreCase)
                    || device.HardwareIds.Contains(entry.DeviceId, StringComparer.OrdinalIgnoreCase)
                    || device.CompatibleIds.Contains(entry.DeviceId, StringComparer.OrdinalIgnoreCase)
                    || IntelDsaHardwareMatcher.TryMatchDetectionValue(entry.DeviceId,
                        device.HardwareIds.Concat(device.CompatibleIds), out _));
            }
            else if (!string.IsNullOrWhiteSpace(entry.InfName))
            {
                matches = inventory.PnpDevices.Where(device =>
                    string.Equals(device.InfName, entry.InfName, StringComparison.OrdinalIgnoreCase));
            }
            else return null;

            var matchedDevices = matches.ToArray();
            var versions = matchedDevices.Select(device => device.DriverVersion)
                .Where(version => !string.IsNullOrWhiteSpace(version))
                .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            return versions.Length == 1
                ? (versions[0]!, matchedDevices.Length == 1
                    ? FrontendMsiLiveUpdateInstalledVersionConfidence.Exact
                    : FrontendMsiLiveUpdateInstalledVersionConfidence.Heuristic)
                : null;
        }

        if (category == MsiLiveUpdateCatalogCategory.Utility)
        {
            var registryVersions = inventory.Software.Where(installed =>
                entry.Guid is not null && (string.Equals(installed.ProductCode, entry.Guid, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(installed.RegistryKeyName, $"{{{entry.Guid}}}", StringComparison.OrdinalIgnoreCase))
                || entry.UpgradeGuid is not null && string.Equals(installed.UpgradeCode, entry.UpgradeGuid, StringComparison.OrdinalIgnoreCase))
                .Select(installed => installed.DisplayVersion)
                .Where(version => !string.IsNullOrWhiteSpace(version))
                .Cast<string>();
            var packageVersions = inventory.Packages.Where(package =>
                    string.Equals(entry.Name, package.Name, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(entry.Name, package.FamilyName, StringComparison.OrdinalIgnoreCase))
                .Select(package => package.Version)
                .Where(version => !string.IsNullOrWhiteSpace(version));
            var versions = registryVersions.Concat(packageVersions).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            return versions.Length == 1
                ? (versions[0]!, FrontendMsiLiveUpdateInstalledVersionConfidence.Exact)
                : null;
        }

        return null;
    }

    private static bool IsBiosImage(MsiLiveUpdateCatalogEntry entry)
    {
        if (entry.Name.Contains("ME FW", StringComparison.OrdinalIgnoreCase)
            || entry.Name.Contains("ME Update Tool", StringComparison.OrdinalIgnoreCase)
            || entry.Name.Contains("Management Engine", StringComparison.OrdinalIgnoreCase)) return false;
        return string.Equals(entry.Name.Trim(), "BIOS", StringComparison.OrdinalIgnoreCase)
            || entry.Name.Contains("BIOS", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsControllerFirmware(MsiLiveUpdateCatalogEntry entry) =>
        entry.DeviceId?.Contains("VID_0DB0&PID_1901", StringComparison.OrdinalIgnoreCase) == true
        || entry.DeviceId?.Contains("VID_0DB0&PID_1902", StringComparison.OrdinalIgnoreCase) == true
        || entry.Name.Contains("Controller", StringComparison.OrdinalIgnoreCase)
            && entry.Name.Contains("Firmware", StringComparison.OrdinalIgnoreCase);

    private static FrontendMsiLiveUpdateCatalogEntry MapIntelEntry(IntelDsaResolvedDriver entry)
    {
        var installedVersion = entry.Device.DriverVersion;
        var deviceName = SafeDeviceName(entry.Device.WmiDeviceName ?? entry.Device.FriendlyName);
        var displayName = string.IsNullOrWhiteSpace(deviceName)
            ? entry.PackageName
            : $"{entry.PackageName} — {deviceName}";
        return new(displayName, entry.LatestVersion, entry.ReleaseDate, null,
            installedVersion,
            FrontendMsiLiveUpdateVersionSource.IntelDsa,
            entry.VersionKind,
            installedVersion is null ? null : FrontendMsiLiveUpdateInstalledVersionConfidence.Exact);
    }

    private static string? SafeDeviceName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var safe = new string(value.Where(static character => !char.IsControl(character)).Take(160).ToArray()).Trim();
        return safe.Length == 0 ? null : safe;
    }

    private static bool IsExactMsiDuplicate(MsiLiveUpdateCatalogEntry entry, IntelDsaResolvedDriver intelEntry)
    {
        if (entry.DeviceId is null) return false;
        var sameDevice = string.Equals(entry.DeviceId, intelEntry.Device.DeviceId, StringComparison.OrdinalIgnoreCase)
            || intelEntry.Device.HardwareIds.Contains(entry.DeviceId, StringComparer.OrdinalIgnoreCase)
            || IntelDsaHardwareMatcher.TryMatchDetectionValue(entry.DeviceId,
                intelEntry.Device.HardwareIds.Concat(intelEntry.Device.CompatibleIds), out _);
        if (!sameDevice) return false;
        return intelEntry.Family switch
        {
            IntelDsaComponentFamily.Graphics => ContainsAny(entry.Name, "graphics", "display", "arc"),
            IntelDsaComponentFamily.Wireless => ContainsAny(entry.Name, "wireless", "wi-fi", "wifi"),
            IntelDsaComponentFamily.Bluetooth => entry.Name.Contains("bluetooth", StringComparison.OrdinalIgnoreCase),
            IntelDsaComponentFamily.Npu => ContainsAny(entry.Name, "npu", "ai boost"),
            _ => false
        };
    }

    private static bool ContainsAny(string value, params string[] candidates) =>
        candidates.Any(candidate => value.Contains(candidate, StringComparison.OrdinalIgnoreCase));

    private static FrontendMsiLiveUpdateSourceSnapshot MapMsiSource(MsiLiveUpdateCatalogQueryResult result)
    {
        var failedCategories = result.Categories.Where(category => category.State == MsiLiveUpdateCatalogCategoryState.Failed)
            .Select(category => $"{category.Category}{category.ErrorKind}").Take(4).ToArray();
        var state = result.State switch
        {
            MsiLiveUpdateCatalogState.Completed when failedCategories.Length == 0 => FrontendMsiLiveUpdateSourceState.Completed,
            MsiLiveUpdateCatalogState.Completed or MsiLiveUpdateCatalogState.PartialFailure => FrontendMsiLiveUpdateSourceState.PartialFailure,
            MsiLiveUpdateCatalogState.Unavailable => FrontendMsiLiveUpdateSourceState.Unavailable,
            _ => FrontendMsiLiveUpdateSourceState.Failed
        };
        return new(state, result.ErrorKind ?? (failedCategories.Length == 0 ? null : "CategoryFailure"));
    }

    private static string? BuildCombinedError(FrontendMsiLiveUpdateSourceSnapshot msi,
        FrontendMsiLiveUpdateSourceSnapshot intel, IReadOnlyList<string> warnings)
    {
        var parts = new List<string>(3);
        if (msi.State != FrontendMsiLiveUpdateSourceState.Completed) parts.Add("MsiSourcePartial");
        if (intel.State != FrontendMsiLiveUpdateSourceState.Completed) parts.Add("IntelSourcePartial");
        if (warnings.Count > 0) parts.Add("InstalledInventoryPartial");
        return parts.Count == 0 ? null : string.Join(';', parts);
    }

    private static string CurrentWindowsVersion() =>
        OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000) ? "win11" : "win10";
}
