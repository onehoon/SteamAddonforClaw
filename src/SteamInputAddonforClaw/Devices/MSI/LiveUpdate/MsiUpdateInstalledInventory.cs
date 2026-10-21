using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Management;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using Microsoft.Win32;
using SteamInputAddonforClaw.Contracts.Frontend;
using SteamInputAddonforClaw.Controllers.Detection;
using SteamInputAddonforClaw.Devices.MSI.Claw;

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
    string? Publisher,
    string? RegistrySourceKind = null);

internal sealed record MsiUpdateControllerFirmware(string Version,
    FrontendMsiLiveUpdateInstalledVersionConfidence Confidence);

internal sealed record MsiUpdateInstalledDriverPackage(
    string PublishedInfName,
    string OriginalInfName,
    string Provider,
    string Class,
    string Version);

internal sealed record MsiUpdateDriverStoreCapture(
    string Status,
    IReadOnlyList<MsiUpdateInstalledDriverPackage> Packages,
    string? Warning = null)
{
    internal static MsiUpdateDriverStoreCapture NotRequested { get; } = new("NotRequested", []);

    internal long? ElapsedMs { get; init; }
}

internal sealed record MsiUpdateInstalledInventory(
    IReadOnlyList<string> BiosVersions,
    IReadOnlyList<MsiUpdateInstalledPnpDevice> PnpDevices,
    IReadOnlyList<MsiUpdateInstalledSoftware> Software,
    MsiUpdateControllerFirmware? ControllerFirmware,
    IReadOnlyList<string> Warnings)
{
    private const int MaximumInventoryEntries = 4096;
    private const int MaximumValueLength = 512;
    private const int MaximumWmiDurationSeconds = 15;
    private const int MaximumDriverStoreRows = 128;
    internal const int MaximumDriverStoreOutputCharacters = 4 * 1024 * 1024;
    private const int MaximumDriverStoreErrorCharacters = 8 * 1024;
    internal const int DriverStoreTimeoutMilliseconds = 8_000;
    internal const int DriverStoreProcessCleanupGraceMilliseconds = 500;
    internal const int MaximumDriverStoreElapsedMilliseconds = 30_000;
    private static readonly TimeSpan DriverStoreTimeout = TimeSpan.FromMilliseconds(DriverStoreTimeoutMilliseconds);
    private static readonly TimeSpan DriverStoreProcessCleanupGrace = TimeSpan.FromMilliseconds(DriverStoreProcessCleanupGraceMilliseconds);
    private static readonly string[] DriverStoreOriginalInfAllowlist =
    [
        "mep_audio_component.inf",
        "IntcOED_OemLibPath.inf",
        "RtkSdcaXuMSI.inf",
        "ishheci.inf"
    ];

    internal MsiUpdateDriverStoreCapture DriverStore { get; init; } = MsiUpdateDriverStoreCapture.NotRequested;

    internal static Task<MsiUpdateInstalledInventory> CaptureAsync(
        Func<CancellationToken, Task<string?>>? exactControllerFirmwareReader,
        CancellationToken cancellationToken,
        string? hardwareModelId = null) =>
        Task.Run(() => Capture(exactControllerFirmwareReader, cancellationToken, hardwareModelId), cancellationToken);

    private static MsiUpdateInstalledInventory Capture(
        Func<CancellationToken, Task<string?>>? exactControllerFirmwareReader,
        CancellationToken cancellationToken,
        string? hardwareModelId)
    {
        var warnings = new List<string>();
        var biosVersions = CaptureBiosVersions(warnings, cancellationToken);
        var presentDevices = CapturePresentDevices(warnings, cancellationToken);
        var pnpDevices = CaptureSignedDriverVersions(presentDevices, warnings, cancellationToken);
        var software = CaptureUninstallEntries(warnings, cancellationToken);
        var controllerFirmware = CaptureControllerRevision(presentDevices);
        var driverStore = CaptureCg3emDriverStore(hardwareModelId, warnings, cancellationToken);

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

        return new(biosVersions, pnpDevices, software, controllerFirmware,
            warnings.Distinct(StringComparer.Ordinal).Take(16).ToArray())
        {
            DriverStore = driverStore
        };
    }

    internal static bool ShouldCaptureCg3emDriverStore(string? hardwareModelId) =>
        string.Equals(hardwareModelId, "msi.claw.cg3em", StringComparison.OrdinalIgnoreCase);

    internal static bool ShouldLogDriverStoreCapture(string? hardwareModelId, MsiUpdateDriverStoreCapture capture) =>
        ShouldCaptureCg3emDriverStore(hardwareModelId)
        && !string.Equals(capture.Status, "NotRequested", StringComparison.Ordinal);

    internal static bool IsAllowedDriverStoreOriginalInf(string? originalInfName) =>
        DriverStoreOriginalInfAllowlist.Contains(originalInfName ?? string.Empty, StringComparer.OrdinalIgnoreCase);

    internal static string GetDriverStoreCaptureDisposition(string? hardwareModelId, bool isWindows) =>
        !ShouldCaptureCg3emDriverStore(hardwareModelId)
            ? "NotRequested"
            : isWindows ? "Capture" : "UnsupportedPlatform";

    internal static string[] GetDriverStoreArguments(string? deviceClass = null)
    {
        if (string.IsNullOrWhiteSpace(deviceClass))
            return ["/enum-drivers", "/format", "xml"];

        if (deviceClass is not ("System" or "Extension" or "AudioProcessingObject"))
            throw new ArgumentOutOfRangeException(nameof(deviceClass));

        return ["/enum-drivers", "/class", deviceClass, "/devices", "/format", "xml"];
    }

    internal static MsiUpdateDriverStoreCapture ParseDriverStorePackagesXml(string? xml)
    {
        if (string.IsNullOrWhiteSpace(xml))
            return new("EmptyOutput", [], "Cg3emDriverStoreEmptyOutput");
        if (xml.Length > MaximumDriverStoreOutputCharacters)
            return new("OutputLimitExceeded", [], "Cg3emDriverStoreOutputLimitExceeded");

        try
        {
            using var textReader = new StringReader(xml);
            using var xmlReader = XmlReader.Create(textReader, new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                MaxCharactersInDocument = MaximumDriverStoreOutputCharacters,
                MaxCharactersFromEntities = 0,
                IgnoreComments = true
            });
            var document = XDocument.Load(xmlReader, LoadOptions.None);
            var driverElements = document.Root?.DescendantsAndSelf()
                ?? Enumerable.Empty<XElement>();
            driverElements = driverElements
                .Where(element => string.Equals(element.Name.LocalName, "Driver", StringComparison.OrdinalIgnoreCase));
            var parsed = new List<MsiUpdateInstalledDriverPackage>();
            foreach (var driver in driverElements)
            {
                var publishedInfName = GetDriverStoreAttribute(driver, "DriverName");
                var originalInfName = GetInfBasename(GetDriverStoreElementValue(driver, "OriginalName") ?? string.Empty);
                if (string.IsNullOrWhiteSpace(publishedInfName) || string.IsNullOrWhiteSpace(originalInfName))
                    return new("InvalidOutput", [], "Cg3emDriverStoreInvalidOutput");

                if (!IsAllowedDriverStoreOriginalInf(originalInfName))
                    continue;

                publishedInfName = publishedInfName.Trim();
                var provider = GetDriverStoreElementValue(driver, "ProviderName");
                var deviceClass = GetDriverStoreElementValue(driver, "ClassName");
                var rawVersion = GetDriverStoreElementValue(driver, "DriverVersion");
                if (!Regex.IsMatch(publishedInfName, @"^oem\d+\.inf$",
                        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)
                    || string.IsNullOrWhiteSpace(provider) || !IsSafeDriverStoreLabel(provider)
                    || string.IsNullOrWhiteSpace(deviceClass) || !IsSafeDriverStoreLabel(deviceClass)
                    || !TryParseDriverStoreVersion(rawVersion, out var version))
                    return new("InvalidOutput", [], "Cg3emDriverStoreInvalidOutput");

                if (parsed.Count >= MaximumDriverStoreRows)
                    return new("RowLimitExceeded", [], "Cg3emDriverStoreRowLimitExceeded");

                parsed.Add(new(publishedInfName, originalInfName, provider, deviceClass, version));
            }

            return new("Completed", parsed
                .OrderBy(package => package.OriginalInfName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(package => package.PublishedInfName, StringComparer.OrdinalIgnoreCase)
                .ToArray());
        }
        catch (XmlException)
        {
            return new("InvalidOutput", [], "Cg3emDriverStoreInvalidOutput");
        }
        catch (InvalidOperationException)
        {
            return new("InvalidOutput", [], "Cg3emDriverStoreInvalidOutput");
        }
    }

    private static MsiUpdateDriverStoreCapture CaptureCg3emDriverStore(
        string? hardwareModelId,
        List<string> warnings,
        CancellationToken cancellationToken)
    {
        var disposition = GetDriverStoreCaptureDisposition(hardwareModelId, OperatingSystem.IsWindows());
        if (disposition == "NotRequested")
            return MsiUpdateDriverStoreCapture.NotRequested;
        if (disposition == "UnsupportedPlatform")
        {
            warnings.Add("Cg3emDriverStoreUnsupportedPlatform");
            return new("UnsupportedPlatform", [], "Cg3emDriverStoreUnsupportedPlatform");
        }

        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var capture = CaptureDriverStoreProcessAsync(cancellationToken).GetAwaiter().GetResult();
            if (capture.Status != "Completed") warnings.Add(capture.Warning ?? "Cg3emDriverStoreUnavailable");
            return capture;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            warnings.Add("Cg3emDriverStoreUnavailable");
            return new("Unavailable", [], "Cg3emDriverStoreUnavailable");
        }
    }

    private static async Task<MsiUpdateDriverStoreCapture> CaptureDriverStoreProcessAsync(
        CancellationToken cancellationToken)
    {
        var startedAt = Stopwatch.GetTimestamp();
        try
        {
            var capture = await CaptureDriverStoreProcessCoreAsync(cancellationToken).ConfigureAwait(false);
            return WithDriverStoreElapsed(capture, Stopwatch.GetElapsedTime(startedAt));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return WithDriverStoreElapsed(
                new("Unavailable", [], "Cg3emDriverStoreCommandFailed"),
                Stopwatch.GetElapsedTime(startedAt));
        }
    }

    internal static MsiUpdateDriverStoreCapture WithDriverStoreElapsed(
        MsiUpdateDriverStoreCapture capture,
        TimeSpan elapsed)
    {
        var milliseconds = elapsed.TotalMilliseconds;
        var boundedMilliseconds = double.IsNaN(milliseconds) || milliseconds <= 0
            ? 0
            : milliseconds >= MaximumDriverStoreElapsedMilliseconds
                ? MaximumDriverStoreElapsedMilliseconds
                : (long)milliseconds;
        return capture with { ElapsedMs = boundedMilliseconds };
    }

    private static async Task<MsiUpdateDriverStoreCapture> CaptureDriverStoreProcessCoreAsync(
        CancellationToken cancellationToken)
    {
        var windowsDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var pnputilPath = Path.Combine(windowsDirectory, "System32", "pnputil.exe");
        var startInfo = new ProcessStartInfo
        {
            FileName = pnputilPath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = Environment.SystemDirectory
        };
        foreach (var argument in GetDriverStoreArguments())
            startInfo.ArgumentList.Add(argument);

        using var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        var standardOutput = new StringBuilder();
        var standardError = new StringBuilder();
        var outputLimitExceeded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var outputGate = new object();
        void AppendBounded(StringBuilder buffer, string? line, int maximum)
        {
            if (line is null) return;
            lock (outputGate)
            {
                if (buffer.Length + line.Length + Environment.NewLine.Length > maximum)
                {
                    outputLimitExceeded.TrySetResult();
                    return;
                }
                buffer.AppendLine(line);
            }
        }

        process.OutputDataReceived += (_, eventArgs) =>
            AppendBounded(standardOutput, eventArgs.Data, MaximumDriverStoreOutputCharacters);
        process.ErrorDataReceived += (_, eventArgs) =>
            AppendBounded(standardError, eventArgs.Data, MaximumDriverStoreErrorCharacters);
        if (!process.Start()) return new("Unavailable", [], "Cg3emDriverStoreStartFailed");

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(DriverStoreTimeout);
        var exitTask = process.WaitForExitAsync(timeoutSource.Token);
        try
        {
            var first = await Task.WhenAny(exitTask, outputLimitExceeded.Task).ConfigureAwait(false);
            if (first == outputLimitExceeded.Task)
            {
                await TryKillAndWaitForExitAsync(process).ConfigureAwait(false);
                return new("OutputLimitExceeded", [], "Cg3emDriverStoreOutputLimitExceeded");
            }

            await exitTask.ConfigureAwait(false);
            process.WaitForExit();
        }
        catch (OperationCanceledException)
        {
            await TryKillAndWaitForExitAsync(process).ConfigureAwait(false);
            if (cancellationToken.IsCancellationRequested) throw;
            return new("TimedOut", [], "Cg3emDriverStoreTimedOut");
        }

        string xml;
        lock (outputGate) xml = standardOutput.ToString();
        return ParseDriverStoreProcessResult(xml, process.ExitCode);
    }

    internal static MsiUpdateDriverStoreCapture ParseDriverStoreProcessResult(
        string? standardOutput,
        int exitCode,
        bool timedOut = false,
        bool outputLimitExceeded = false)
    {
        if (outputLimitExceeded)
            return new("OutputLimitExceeded", [], "Cg3emDriverStoreOutputLimitExceeded");
        if (timedOut)
            return new("TimedOut", [], "Cg3emDriverStoreTimedOut");
        if (exitCode != 0)
            return new("Unavailable", [], "Cg3emDriverStoreCommandFailed");
        return ParseDriverStorePackagesXml(standardOutput);
    }

    private static void TryKill(Process process)
    {
        try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
        catch (InvalidOperationException) { }
        catch (System.ComponentModel.Win32Exception) { }
    }

    private static async Task TryKillAndWaitForExitAsync(Process process)
    {
        TryKill(process);
        using var cleanupSource = new CancellationTokenSource(DriverStoreProcessCleanupGrace);
        try { await process.WaitForExitAsync(cleanupSource.Token).ConfigureAwait(false); }
        catch (OperationCanceledException) { }
        catch (InvalidOperationException) { }
        catch (System.ComponentModel.Win32Exception) { }
    }

    private static string? GetDriverStoreAttribute(XElement element, string name) =>
        element.Attributes().FirstOrDefault(attribute =>
            string.Equals(attribute.Name.LocalName, name, StringComparison.OrdinalIgnoreCase))?.Value.Trim();

    private static string? GetDriverStoreElementValue(XElement element, string name)
    {
        var value = element.Elements().FirstOrDefault(child =>
            string.Equals(child.Name.LocalName, name, StringComparison.OrdinalIgnoreCase))?.Value.Trim();
        return string.IsNullOrWhiteSpace(value) || value.Length > 256 ? null : value;
    }

    private static bool TryParseDriverStoreVersion(string? rawVersion, out string version)
    {
        version = string.Empty;
        if (string.IsNullOrWhiteSpace(rawVersion) || rawVersion.Length > 256) return false;

        var match = Regex.Match(rawVersion.Trim(), @"(?<version>\d+(?:\.\d+){1,3})$",
            RegexOptions.CultureInvariant);
        if (!match.Success || (match.Index > 0 && !char.IsWhiteSpace(rawVersion.Trim()[match.Index - 1])))
            return false;

        var candidate = match.Groups["version"].Value;
        var components = candidate.Split('.');
        if (components.Length is < 2 or > 4 || components.Any(component =>
                component.Length is < 1 or > 10
                || !uint.TryParse(component, NumberStyles.None, CultureInfo.InvariantCulture, out _)))
            return false;

        version = candidate;
        return true;
    }

    private static string GetInfBasename(string path)
    {
        var normalized = path.Replace('\\', '/');
        var separator = normalized.LastIndexOf('/');
        return (separator >= 0 ? normalized[(separator + 1)..] : normalized).Trim();
    }

    private static bool IsSafeDriverStoreLabel(string value) =>
        value.Length <= 128 && value.All(character => char.IsLetterOrDigit(character)
            || character is ' ' or '.' or '_' or '-' or '&' or '(' or ')');

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
                var sourceKind = hive == RegistryHive.CurrentUser ? "HKCU"
                    : view == RegistryView.Registry32 ? "HKLM32" : "HKLM64";
                entries.Add(new(keyName, productCode, ParseGuid(SafeValue(key.GetValue("UpgradeCode")?.ToString())),
                    SafeValue(key.GetValue("DisplayName")?.ToString()), SafeValue(key.GetValue("DisplayVersion")?.ToString()),
                    SafeValue(key.GetValue("Publisher")?.ToString()), sourceKind));
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

internal sealed record MsiUpdateCatalogMatchDiagnostic(
    MsiLiveUpdateCatalogCategory Category,
    string? Name,
    string VersionSource,
    string Outcome,
    FrontendMsiLiveUpdateInstalledVersionConfidence? Confidence = null,
    int CandidateCount = 0,
    int VersionCount = 0,
    int FilteredCount = 0);

internal sealed record MsiUpdateCatalogRowDiagnostic(
    MsiLiveUpdateCatalogCategory Category,
    int SourceIndex,
    string UiDisposition,
    string? DispositionReason,
    FrontendMsiLiveUpdateCatalogEntry? MappedEntry,
    string? MatchOutcome,
    FrontendMsiLiveUpdateInstalledVersionConfidence? MatchConfidence,
    int CandidateCount,
    int VersionCount,
    string? DuplicateOutcome,
    int DuplicateCandidateCount,
    int DuplicateVersionCount);

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

    internal static bool HasComparableHardwareIdentity(string? value) =>
        !string.IsNullOrWhiteSpace(value) && TryParseHardwarePattern(value, out _);

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
    private static readonly (string Name, IntelDsaComponentFamily Family)[] KnownMsiIntelDriverNames =
    [
        ("Intel Graphics Driver", IntelDsaComponentFamily.Graphics),
        ("Intel Arc Display Driver", IntelDsaComponentFamily.Graphics),
        ("Intel Arc Graphics Driver", IntelDsaComponentFamily.Graphics),
        ("Intel Wireless LAN Driver", IntelDsaComponentFamily.Wireless),
        ("Intel Bluetooth Driver", IntelDsaComponentFamily.Bluetooth),
        ("Intel NPU Driver", IntelDsaComponentFamily.Npu)
    ];

    private static readonly string[] ExcludedUtilityNames =
    [
        "MSI App Player Installer CN",
        "Intel Graphics Software",
        "MSI App Player Installer"
    ];

    private const string MsiCenterMCatalogName = "MSI Center M";
    private const string MsiCenterMSdkDisplayName = "MSI Center M SDK";
    private const string VerifiedMsiPublisher = "MSI";
    private const string VerifiedMsiCorporationPublisher = "MSI Corporation";

    private static readonly string[] A2vmLegacySingleDeviceDriverNames =
    [
        "Goodix FingerPrinter Driver",
        "Intel Game Assist Driver",
        "Intel Dynamic Tuning Driver",
        "Intel Trusted Execution Technology Driver",
        "Intel PPM Provision Driver"
    ];

    private static readonly MsiCg3emDriverMapping[] Cg3emDriverMappings =
    [
        Mapping("Windows Studio Effects", ["SWC\\MEP_CAM&VEN_8086_DEV_B03E"], "Microsoft", ["SoftwareComponent"],
            driverStoreIdentity: new("dism_mep,audio,component.inf_AudioProcessingObject_Microsoft",
                "mep_audio_component.inf", "Microsoft Corporation", "AudioProcessingObject")),
        Mapping("Intel HID Event Filter", ["ACPI\\INTC10CC"], "Intel", ["HIDClass"]),
        Mapping("Integrated Sensor Solution", ["PCI\\VEN_8086&DEV_E345", "PCI\\VEN_8086&DEV_E445"],
            "Intel", ["System"], comparable: false,
            driverStoreIdentity: new("dism_ishheci.inf_System_Intel",
                "ishheci.inf", "Intel", "System")),
        Mapping("Intel Chipset Driver", ["PCI\\VEN_8086&DEV_E402", "PCI\\VEN_8086&DEV_E422", "PCI\\VEN_8086&DEV_E423"],
            "Intel", ["System"]),
        Mapping("Intel Platform Monitoring Technology Driver", ["PCI\\VEN_8086&DEV_B07D"], "Intel", ["System"]),
        Mapping("Intel Serial IO Driver", ["ACPI\\INTC105F", "PCI\\VEN_8086&DEV_E450"], "Intel", ["System"]),
        Mapping("Intel Dynamic Tuning Driver",
            [
                "SWC\\VID8086_DTT_0001", "SWC\\VID8086_DTT_0002", "SWC\\VID8086_DTT_0003", "SWC\\VID8086_DTT_0004",
                "SWC\\VID8086_DTTCFG_0001", "SWC\\VID8086_DTTCFG_0002", "SWC\\VID8086_DTTCFG_0003", "SWC\\VID8086_DTTCFG_0004",
                "SWC\\VID8086_DTTTECH_0002", "SWC\\VID8086_DTTTECH_0003", "SWC\\VID8086_DTTTECH_0004"
            ], "Intel", ["SoftwareComponent"]),
        Mapping("Intel IPF Driver",
            ["ACPI\\INTC10D4", "ACPI\\INTC10D5", "ACPI\\INTC10D6", "ACPI\\INTC10D9", "SWC\\VID8086_IPFEF_1.0"],
            "Intel", ["System", "SoftwareComponent"]),
        Mapping("Intel Management Engine Driver", ["PCI\\VEN_8086&DEV_E470"], "Intel", ["System"]),
        Mapping("Intel Trusted Execution Technology Driver", ["ACPI\\INTC1025"], "Intel", ["System"]),
        Mapping("Intel PPM Provision Driver", ["SWC\\VID8086_PPM_1008_1.0", "SWC\\VID8086_PPM_CORP_1008_1.0"],
            "Intel", ["SoftwareComponent"]),
        Mapping("Intel Game Assist Driver", ["SWC\\VID8086_DTT_GA_1.0"], "Intel", ["SoftwareComponent"]),
        Mapping("IntelliGo APO Driver", ["SWC\\VEN_IGO&CID_APO", "SWC\\VEN_IGO&AUDIOSERVICE"],
            "intelliGo", ["AudioProcessingObject", "SoftwareComponent"]),
        Mapping("Intel Smart Sound Technology driver", ["PCI\\VEN_8086&DEV_E428"], "Intel", ["System"]),
        Mapping("Realtek OED Driver",
            [
                "INTELAUDIO\\DSP_CTLR_DEV_E328&VEN_8086&DEV_0222&SUBSYS_15101462",
                "INTELAUDIO\\DSP_CTLR_DEV_E428&VEN_8086&DEV_0222&SUBSYS_15101462"
            ], "Realtek Semiconductor Corp.", ["Extension"],
            driverStoreIdentity: new("dism_intcoed,oemlibpath.inf_Extension_Realtek",
                "IntcOED_OemLibPath.inf", "Realtek Semiconductor Corp.", "Extension")),
        Mapping("Realtek Audio Driver",
            [
                "SOUNDWIRE\\SDCA_10&MAN_025D&FUNC_0721&TYPE_06&VER_01&ADR_01&LID_03&UID_00&SUBSYS_15101462",
                "SOUNDWIRE\\SDCA_10&MAN_025D&FUNC_0721&TYPE_03&VER_01&ADR_02&LID_03&UID_00&SUBSYS_15101462",
                "SOUNDWIRE\\SDCA_10&MAN_025D&FUNC_0721&TYPE_01&VER_01&ADR_04&LID_03&UID_00&SUBSYS_15101462"
            ], "Realtek", ["Extension"],
            driverStoreIdentity: new("dism_rtksdcaxumsi.inf_Extension_Realtek",
                "RtkSdcaXuMSI.inf", "Realtek", "Extension")),
        Mapping("DTS Driver", ["SWC\\VEN_DTSI&AID_DTSI2"], "DTS",
            ["SoftwareComponent", "AudioProcessingObject"],
            ["SWC\\VEN_DTSI&HID_DTSIAPO4xCONTROLLER", "SWC\\VEN_DTSI&HID_DTSIAPO4xSERVICE"]),
        Mapping("Goodix FingerPrinter Driver", ["USB\\VID_27C6&PID_6090"], "Goodix", ["Biometric"]),
        Mapping("Realtek Card Reader Driver", ["PCI\\VEN_10EC&DEV_5264"], "Realtek", ["MTD"])
    ];

    private sealed record MsiInstalledVersionResolution(
        string? Version,
        FrontendMsiLiveUpdateInstalledVersionConfidence? Confidence,
        string Outcome,
        int CandidateCount,
        int VersionCount,
        string? ComparisonVersion = null,
        bool IsCg3emMappedDriver = false,
        MsiUpdateInstalledSoftware? RegistryCandidate = null)
    {
        internal static MsiInstalledVersionResolution Matched(string version,
            FrontendMsiLiveUpdateInstalledVersionConfidence confidence, string outcome,
            int candidateCount, int versionCount = 1, MsiUpdateInstalledSoftware? registryCandidate = null) =>
            new(version, confidence, outcome, candidateCount, versionCount, RegistryCandidate: registryCandidate);

        internal static MsiInstalledVersionResolution Unknown(string outcome, int candidateCount = 0, int versionCount = 0) =>
            new(null, null, outcome, candidateCount, versionCount);

        internal static MsiInstalledVersionResolution Cg3emMapped(string? version, string? comparisonVersion,
            string outcome, int candidateCount, int versionCount, bool isComparable) =>
            new(version, version is null ? null : FrontendMsiLiveUpdateInstalledVersionConfidence.Exact,
                outcome, candidateCount, versionCount, isComparable ? comparisonVersion : null, true);
    }

    private sealed record MsiCg3emDriverMapping(
        string CatalogName,
        IReadOnlyList<string> PrimaryHardwareIds,
        string Provider,
        IReadOnlyList<string> DeviceClasses,
        IReadOnlyList<string> FallbackHardwareIds,
        bool Comparable,
        MsiCg3emDriverStoreIdentity? DriverStoreIdentity);

    private sealed record MsiCg3emDriverStoreIdentity(
        string OemPackageIdentity,
        string OriginalInfName,
        string Provider,
        string DeviceClass);

    private static MsiCg3emDriverMapping Mapping(string catalogName, string[] hardwareIds,
        string provider, string[] deviceClasses, string[]? fallbackHardwareIds = null, bool comparable = true,
        MsiCg3emDriverStoreIdentity? driverStoreIdentity = null) =>
        new(catalogName, hardwareIds, provider, deviceClasses, fallbackHardwareIds ?? [], comparable, driverStoreIdentity);

    internal static FrontendMsiLiveUpdateCatalogSnapshot Compose(
        MsiLiveUpdateCatalogQueryResult msi,
        IntelDsaCatalogResult intel,
        MsiUpdateInstalledInventory inventory,
        Action<IntelDsaResolvedDriver>? onIntelMatch = null,
        Action<MsiUpdateCatalogMatchDiagnostic>? onDiagnostic = null,
        Action<MsiUpdateCatalogRowDiagnostic>? onRowDiagnostic = null,
        Action<IReadOnlyList<IntelDsaResolvedDriver>>? onResolvedIntelMatches = null,
        string? hardwareModelId = null)
    {
        var msiSource = MapMsiSource(msi);
        var intelSource = new FrontendMsiLiveUpdateSourceSnapshot(
            intel.Succeeded ? FrontendMsiLiveUpdateSourceState.Completed : FrontendMsiLiveUpdateSourceState.Unavailable,
            intel.ErrorKind);
        var intelRows = IntelDsaHardwareMatcher.Resolve(intel,
            inventory, msi.OperatingSystem ?? CurrentWindowsVersion());
        onResolvedIntelMatches?.Invoke(intelRows);
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

            var sourceEntries = source.Entries.Select((entry, sourceIndex) => (Entry: entry, SourceIndex: sourceIndex)).ToList();
            var excludedEntries = sourceEntries
                .Select(row => (row.Entry, row.SourceIndex, Reason: GetPolicyExclusionReason(sourceCategory, row.Entry)))
                .Where(row => row.Reason is not null)
                .ToArray();
            foreach (var row in excludedEntries)
                onRowDiagnostic?.Invoke(new(sourceCategory, row.SourceIndex, "ExcludedByPolicy", row.Reason,
                    null, null, null, 0, 0, null, 0, 0));
            if (excludedEntries.Length > 0)
                onDiagnostic?.Invoke(new(sourceCategory, null, "MSI", "ExcludedByPolicy", FilteredCount: excludedEntries.Length));
            if (excludedEntries.Length > 0)
                sourceEntries = sourceEntries.Where(row => GetPolicyExclusionReason(sourceCategory, row.Entry) is null).ToList();

            var entries = new List<FrontendMsiLiveUpdateCatalogEntry>(sourceEntries.Count
                + (sourceCategory == MsiLiveUpdateCatalogCategory.Driver ? intelRows.Count : 0));
            foreach (var sourceRow in sourceEntries)
            {
                var entry = sourceRow.Entry;
                var duplicate = new MsiIntelDuplicateEvaluation(false, "NotApplicable", 0);
                if (sourceCategory == MsiLiveUpdateCatalogCategory.Driver)
                {
                    duplicate = EvaluateIntelDuplicate(entry, intelRows, intel.Succeeded);
                    if (duplicate.Outcome != "NotApplicable")
                        onDiagnostic?.Invoke(new(sourceCategory, entry.Name, "MSI/IntelDsa", duplicate.Outcome,
                            CandidateCount: duplicate.CandidateCount, VersionCount: duplicate.VersionCount));
                    if (duplicate.Suppress)
                    {
                        onRowDiagnostic?.Invoke(new(sourceCategory, sourceRow.SourceIndex, "SuppressedByIntelDsa",
                            duplicate.Outcome, null, null, null, 0, 0, duplicate.Outcome,
                            duplicate.CandidateCount, duplicate.VersionCount));
                        continue;
                    }
                }

                var mapped = MapMsiEntry(sourceCategory, entry, inventory, hardwareModelId, out var match);
                entries.Add(mapped);
                onRowDiagnostic?.Invoke(new(sourceCategory, sourceRow.SourceIndex, "Retained",
                    duplicate.Outcome == "NotApplicable" ? null : duplicate.Outcome,
                    mapped, match.Outcome, match.Confidence, match.CandidateCount, match.VersionCount,
                    duplicate.Outcome == "NotApplicable" ? null : duplicate.Outcome,
                    duplicate.CandidateCount, duplicate.VersionCount));
                if (sourceCategory is MsiLiveUpdateCatalogCategory.Driver or MsiLiveUpdateCatalogCategory.Utility)
                    onDiagnostic?.Invoke(new(sourceCategory, entry.Name, "MSI", match.Outcome, match.Confidence,
                        match.CandidateCount, match.VersionCount));
            }
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
        MsiUpdateInstalledInventory inventory,
        string? hardwareModelId,
        out MsiInstalledVersionResolution match)
    {
        var packageVersionDomain = IsCg3emDriverStorePackageRow(category, entry, hardwareModelId);
        var latestVersion = packageVersionDomain
            ? entry.PackageVersion
            : entry.DriverVersion ?? entry.PackageVersion ?? entry.Version;
        if (string.Equals(latestVersion, "UWP", StringComparison.OrdinalIgnoreCase)) latestVersion = null;
        var versionKind = packageVersionDomain
            ? !string.IsNullOrWhiteSpace(entry.PackageVersion)
                ? FrontendMsiLiveUpdateVersionKind.Package
                : FrontendMsiLiveUpdateVersionKind.Catalog
            : !string.IsNullOrWhiteSpace(entry.DriverVersion)
                ? FrontendMsiLiveUpdateVersionKind.Driver
                : !string.IsNullOrWhiteSpace(entry.PackageVersion)
                    ? FrontendMsiLiveUpdateVersionKind.Package
                    : FrontendMsiLiveUpdateVersionKind.Catalog;
        match = ResolveMsiInstalledVersion(category, entry, inventory, hardwareModelId);
        var comparisonStatus = ResolveMsiComparisonStatus(
            category, entry, inventory, match, latestVersion, versionKind, hardwareModelId);
        return new(entry.Name, latestVersion, entry.ReleaseDate, entry.Description,
            match.Version, FrontendMsiLiveUpdateVersionSource.Msi, versionKind, match.Confidence, comparisonStatus);
    }

    private static bool IsCg3emDriverStorePackageRow(
        MsiLiveUpdateCatalogCategory category,
        MsiLiveUpdateCatalogEntry entry,
        string? hardwareModelId) =>
        category == MsiLiveUpdateCatalogCategory.Driver
        && string.Equals(hardwareModelId, "msi.claw.cg3em", StringComparison.OrdinalIgnoreCase)
        && FindCg3emDriverMapping(entry.Name)?.DriverStoreIdentity is not null;

    private static FrontendMsiLiveUpdateComparisonStatus ResolveMsiComparisonStatus(
        MsiLiveUpdateCatalogCategory category,
        MsiLiveUpdateCatalogEntry entry,
        MsiUpdateInstalledInventory inventory,
        MsiInstalledVersionResolution match,
        string? latestVersion,
        FrontendMsiLiveUpdateVersionKind versionKind,
        string? hardwareModelId)
    {
        if (category == MsiLiveUpdateCatalogCategory.Bios)
            return CompareBiosVersions(entry, inventory, match, latestVersion);

        if (category == MsiLiveUpdateCatalogCategory.Firmware)
            return CompareControllerFirmwareVersions(entry, match, latestVersion, versionKind);

        if (category == MsiLiveUpdateCatalogCategory.Driver)
        {
            if (KnownMsiIntelDriverNames.Any(candidate =>
                    string.Equals(candidate.Name, entry.Name.Trim(), StringComparison.OrdinalIgnoreCase)))
                return FrontendMsiLiveUpdateComparisonStatus.Unknown;

            if (match.IsCg3emMappedDriver && match.Outcome == "Cg3emInstalledInfPackageMatch")
            {
                if (versionKind != FrontendMsiLiveUpdateVersionKind.Package
                    || string.IsNullOrWhiteSpace(entry.PackageVersion)
                    || !string.Equals(match.ComparisonVersion, entry.PackageVersion, StringComparison.Ordinal)
                    || !AreEqualNumericVersions(match.ComparisonVersion, entry.PackageVersion))
                    return FrontendMsiLiveUpdateComparisonStatus.Unknown;

                return CompareNumericVersions(match.Version, entry.PackageVersion);
            }

            if (match.IsCg3emMappedDriver)
                return string.IsNullOrWhiteSpace(match.Version) || string.IsNullOrWhiteSpace(match.ComparisonVersion)
                    ? FrontendMsiLiveUpdateComparisonStatus.Unknown
                    : CompareNumericVersions(match.Version, match.ComparisonVersion);

            // Preserve the previous comparison policy only for supported A2VM models.
            // Unmapped CG3EM rows must not fall through to these pre-mapping heuristics.
            if (hardwareModelId is not ("msi.claw.a2vm.7" or "msi.claw.a2vm.8"))
                return FrontendMsiLiveUpdateComparisonStatus.Unknown;

            var multiComponentStatus = CompareA2vmLegacyMultiComponentMsiDriver(
                entry, inventory, match, latestVersion, versionKind);
            if (multiComponentStatus != FrontendMsiLiveUpdateComparisonStatus.Unknown)
                return multiComponentStatus;

            if (match.Outcome is not ("ExactHardwareId" or "ExactInf")
                || match.Confidence != FrontendMsiLiveUpdateInstalledVersionConfidence.Exact
                || match.CandidateCount != 1
                || match.VersionCount != 1)
                return FrontendMsiLiveUpdateComparisonStatus.Unknown;

            if (!string.IsNullOrWhiteSpace(entry.DriverVersion))
                return versionKind == FrontendMsiLiveUpdateVersionKind.Driver
                    && string.Equals(latestVersion, entry.DriverVersion, StringComparison.Ordinal)
                    ? CompareNumericVersions(match.Version, entry.DriverVersion)
                    : FrontendMsiLiveUpdateComparisonStatus.Unknown;

            return CompareA2vmLegacySingleDeviceMsiDriver(entry, match, latestVersion, versionKind);
        }

        if (category == MsiLiveUpdateCatalogCategory.Utility)
        {
            if (IsMsiCenterMEntry(entry) && IsSupportedMsiCenterMModel(hardwareModelId))
                return CompareMsiCenterMRegistryDisplayVersion(entry, match, latestVersion, versionKind);

            return CompareExactRegistryDisplayVersion(entry, match, latestVersion, versionKind);
        }

        return FrontendMsiLiveUpdateComparisonStatus.Unknown;
    }

    private static FrontendMsiLiveUpdateComparisonStatus CompareControllerFirmwareVersions(
        MsiLiveUpdateCatalogEntry entry,
        MsiInstalledVersionResolution match,
        string? latestVersion,
        FrontendMsiLiveUpdateVersionKind versionKind)
    {
        if (!HasOwnedControllerFirmwareCatalogIdentity(entry)
            || versionKind != FrontendMsiLiveUpdateVersionKind.Catalog
            || string.IsNullOrWhiteSpace(entry.Version)
            || !string.Equals(latestVersion, entry.Version, StringComparison.Ordinal)
            || !string.IsNullOrWhiteSpace(entry.DriverVersion)
            || !string.IsNullOrWhiteSpace(entry.PackageVersion)
            || entry.Version.Split('.').Length != 2
            || match.Outcome != "ExactControllerIdentity"
            || match.Confidence != FrontendMsiLiveUpdateInstalledVersionConfidence.Exact
            || match.CandidateCount != 1
            || match.VersionCount != 1
            || !AreEqualNumericVersions(entry.Version, entry.Version)
            || !AreEqualNumericVersions(match.Version, entry.Version))
            return FrontendMsiLiveUpdateComparisonStatus.Unknown;

        return FrontendMsiLiveUpdateComparisonStatus.UpToDate;
    }

    private static FrontendMsiLiveUpdateComparisonStatus CompareA2vmLegacySingleDeviceMsiDriver(
        MsiLiveUpdateCatalogEntry entry,
        MsiInstalledVersionResolution match,
        string? latestVersion,
        FrontendMsiLiveUpdateVersionKind versionKind)
    {
        if (!A2vmLegacySingleDeviceDriverNames.Contains(entry.Name.Trim(), StringComparer.OrdinalIgnoreCase)
            || !HasA2vmLegacyPreciseMsiDriverIdentity(entry)
            || IsUwpCatalogEntry(entry))
            return FrontendMsiLiveUpdateComparisonStatus.Unknown;

        if (!string.IsNullOrWhiteSpace(entry.Version)
            && !string.IsNullOrWhiteSpace(entry.PackageVersion)
            && !AreEqualNumericVersions(entry.Version, entry.PackageVersion))
            return FrontendMsiLiveUpdateComparisonStatus.Unknown;

        var offeredVersion = entry.PackageVersion ?? entry.Version;
        var expectedKind = entry.PackageVersion is null
            ? FrontendMsiLiveUpdateVersionKind.Catalog
            : FrontendMsiLiveUpdateVersionKind.Package;
        if (string.IsNullOrWhiteSpace(offeredVersion)
            || versionKind != expectedKind
            || !string.Equals(latestVersion, offeredVersion, StringComparison.Ordinal))
            return FrontendMsiLiveUpdateComparisonStatus.Unknown;

        return CompareNumericVersions(match.Version, offeredVersion);
    }

    private static FrontendMsiLiveUpdateComparisonStatus CompareA2vmLegacyMultiComponentMsiDriver(
        MsiLiveUpdateCatalogEntry entry,
        MsiUpdateInstalledInventory inventory,
        MsiInstalledVersionResolution match,
        string? latestVersion,
        FrontendMsiLiveUpdateVersionKind versionKind)
    {
        var catalogHardwareIds = GetCatalogHardwareIds(entry);
        var preciseHardwareIds = catalogHardwareIds
            .Where(IsPreciseCatalogHardwareId).ToArray();
        if (!IsA2vmLegacyAudioDriverName(entry.Name)
            || preciseHardwareIds.Length == 0
            || !HasA2vmLegacySpecificAudioCatalogHardwareIdentity(entry.Name, preciseHardwareIds)
            || !string.IsNullOrWhiteSpace(entry.DriverVersion)
            || IsUwpCatalogEntry(entry)
            || match.Outcome != "ExactHardwareId"
            || match.Confidence != FrontendMsiLiveUpdateInstalledVersionConfidence.Heuristic
            || match.CandidateCount < 2
            || match.VersionCount != 1
            || string.IsNullOrWhiteSpace(match.Version))
            return FrontendMsiLiveUpdateComparisonStatus.Unknown;

        if (!string.IsNullOrWhiteSpace(entry.Version)
            && !string.IsNullOrWhiteSpace(entry.PackageVersion)
            && !AreEqualNumericVersions(entry.Version, entry.PackageVersion))
            return FrontendMsiLiveUpdateComparisonStatus.Unknown;

        var offeredVersion = entry.PackageVersion ?? entry.Version;
        var expectedKind = entry.PackageVersion is null
            ? FrontendMsiLiveUpdateVersionKind.Catalog
            : FrontendMsiLiveUpdateVersionKind.Package;
        if (string.IsNullOrWhiteSpace(offeredVersion)
            || versionKind != expectedKind
            || !string.Equals(latestVersion, offeredVersion, StringComparison.Ordinal))
            return FrontendMsiLiveUpdateComparisonStatus.Unknown;

        var candidates = inventory.PnpDevices
            .Where(device => preciseHardwareIds.Any(deviceId => IsMsiDeviceIdMatch(deviceId, device)))
            .GroupBy(device => device.DeviceId, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToArray();
        if (candidates.Length != match.CandidateCount
            || candidates.Length < 2
            || candidates.Any(device =>
                !HasA2vmLegacyExactCatalogHardwareId(preciseHardwareIds, device)
                || !HasA2vmLegacyAudioProviderAndClass(entry.Name, device)))
            return FrontendMsiLiveUpdateComparisonStatus.Unknown;

        var installedVersions = candidates.Select(device => device.DriverVersion).ToArray();
        if (installedVersions.Any(string.IsNullOrWhiteSpace)
            || installedVersions.Skip(1).Any(version => !AreEqualNumericVersions(installedVersions[0], version))
            || !AreEqualNumericVersions(match.Version, installedVersions[0])
            || !AreEqualNumericVersions(installedVersions[0], offeredVersion))
            return FrontendMsiLiveUpdateComparisonStatus.Unknown;

        return FrontendMsiLiveUpdateComparisonStatus.UpToDate;
    }

    private static bool IsA2vmLegacyAudioDriverName(string name) =>
        string.Equals(name.Trim(), "DTS Driver", StringComparison.OrdinalIgnoreCase)
        || string.Equals(name.Trim(), "IntelliGo APO Driver", StringComparison.OrdinalIgnoreCase);

    private static bool HasA2vmLegacySpecificAudioCatalogHardwareIdentity(
        string catalogName,
        IReadOnlyList<string> catalogHardwareIds)
    {
        var pattern = string.Equals(catalogName.Trim(), "DTS Driver", StringComparison.OrdinalIgnoreCase)
            ? @"^SWC\\VEN_DTSI&DEV_[A-Z0-9]{4,}$"
            : @"^SWC\\VEN_IGO&DEV_[A-Z0-9]{4,}$";
        return catalogHardwareIds.Any(deviceId => Regex.IsMatch(deviceId, pattern,
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant));
    }

    private static bool HasA2vmLegacyExactCatalogHardwareId(
        IReadOnlyList<string> catalogHardwareIds,
        MsiUpdateInstalledPnpDevice device) => catalogHardwareIds.Any(deviceId =>
        device.HardwareIds.Contains(deviceId, StringComparer.OrdinalIgnoreCase)
        || device.CompatibleIds.Contains(deviceId, StringComparer.OrdinalIgnoreCase));

    private static bool HasA2vmLegacyAudioProviderAndClass(string catalogName, MsiUpdateInstalledPnpDevice device)
    {
        var expectedProvider = string.Equals(catalogName.Trim(), "DTS Driver", StringComparison.OrdinalIgnoreCase)
            ? "DTS"
            : "IntelliGo";
        var deviceClass = FirstNonEmpty(device.WmiDeviceClass, device.ClassName);
        return string.Equals(device.DriverProvider, expectedProvider, StringComparison.OrdinalIgnoreCase)
            && (string.Equals(deviceClass, "SOFTWARECOMPONENT", StringComparison.OrdinalIgnoreCase)
                || string.Equals(deviceClass, "AUDIOPROCESSINGOBJECT", StringComparison.OrdinalIgnoreCase));
    }

    private static bool HasA2vmLegacyPreciseMsiDriverIdentity(MsiLiveUpdateCatalogEntry entry) =>
        !string.IsNullOrWhiteSpace(entry.InfName)
        || GetCatalogHardwareIds(entry).Any(IsPreciseCatalogHardwareId);

    private static bool IsA2vmLegacyIntelAcpiIdentity(string? deviceId) =>
        deviceId is not null
        && (Regex.IsMatch(deviceId, @"^ACPI\\VEN_INTC&DEV_[A-Z0-9]{4}$",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)
            || Regex.IsMatch(deviceId, @"^ACPI\\INTC[0-9A-F]{4}$",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant));

    private static FrontendMsiLiveUpdateComparisonStatus CompareBiosVersions(
        MsiLiveUpdateCatalogEntry entry,
        MsiUpdateInstalledInventory inventory,
        MsiInstalledVersionResolution match,
        string? latestVersion)
    {
        if (!IsBiosImage(entry)
            || string.IsNullOrWhiteSpace(entry.Version)
            || !string.Equals(latestVersion, entry.Version, StringComparison.Ordinal)
            || match.Outcome != "ExactBiosIdentity"
            || match.Confidence != FrontendMsiLiveUpdateInstalledVersionConfidence.Exact
            || match.CandidateCount != 1
            || !TryParseBiosVersion(entry.Version, out var offeredBoard, out var offeredRevision, out var offeredIsTest)
            || offeredIsTest)
            return FrontendMsiLiveUpdateComparisonStatus.Unknown;

        var boardPattern = new Regex(
            $@"(?<![A-Z0-9])(?<version>{Regex.Escape(offeredBoard)}\.[0-9A-F]{{1,8}}T?)(?![A-Z0-9])",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        var installedVersions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var biosValue in inventory.BiosVersions)
        {
            foreach (Match biosMatch in boardPattern.Matches(biosValue))
                installedVersions.Add(biosMatch.Groups["version"].Value);
        }

        if (installedVersions.Count != 1)
            return FrontendMsiLiveUpdateComparisonStatus.Unknown;

        var installedVersion = installedVersions.Single();
        if (!string.Equals(installedVersion, match.Version, StringComparison.OrdinalIgnoreCase)
            || !TryParseBiosVersion(installedVersion, out var installedBoard, out var installedRevision, out var installedIsTest)
            || installedIsTest
            || !string.Equals(installedBoard, offeredBoard, StringComparison.OrdinalIgnoreCase))
            return FrontendMsiLiveUpdateComparisonStatus.Unknown;

        return installedRevision < offeredRevision
            ? FrontendMsiLiveUpdateComparisonStatus.UpdateAvailable
            : FrontendMsiLiveUpdateComparisonStatus.UpToDate;
    }

    private static bool TryParseBiosVersion(string? value, out string board, out uint revision, out bool isTest)
    {
        board = string.Empty;
        revision = 0;
        isTest = false;
        if (string.IsNullOrWhiteSpace(value)) return false;

        var match = Regex.Match(value,
            @"^(?<board>[A-Z0-9]+IMS)\.(?<revision>[0-9A-F]{1,8})(?<test>T?)$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (!match.Success) return false;

        board = match.Groups["board"].Value;
        isTest = match.Groups["test"].Length > 0;
        return uint.TryParse(match.Groups["revision"].Value, NumberStyles.AllowHexSpecifier,
            CultureInfo.InvariantCulture, out revision);
    }

    private static FrontendMsiLiveUpdateComparisonStatus CompareExactRegistryDisplayVersion(
        MsiLiveUpdateCatalogEntry entry,
        MsiInstalledVersionResolution match,
        string? latestVersion,
        FrontendMsiLiveUpdateVersionKind versionKind)
    {
        if (versionKind != FrontendMsiLiveUpdateVersionKind.Catalog
            || string.IsNullOrWhiteSpace(entry.Version)
            || !string.Equals(latestVersion, entry.Version, StringComparison.Ordinal)
            || !string.IsNullOrWhiteSpace(entry.DriverVersion)
            || !string.IsNullOrWhiteSpace(entry.PackageVersion)
            || IsUwpCatalogEntry(entry)
            || match.Outcome != "MatchedRegistry"
            || match.Confidence != FrontendMsiLiveUpdateInstalledVersionConfidence.Exact
            || match.CandidateCount != 1
            || match.VersionCount != 1
            || match.RegistryCandidate is null
            || !string.Equals(match.RegistryCandidate.DisplayVersion, match.Version, StringComparison.Ordinal))
            return FrontendMsiLiveUpdateComparisonStatus.Unknown;

        return CompareNumericVersions(match.RegistryCandidate.DisplayVersion, entry.Version);
    }

    private static FrontendMsiLiveUpdateComparisonStatus CompareMsiCenterMRegistryDisplayVersion(
        MsiLiveUpdateCatalogEntry entry,
        MsiInstalledVersionResolution match,
        string? latestVersion,
        FrontendMsiLiveUpdateVersionKind versionKind)
    {
        if (match.Outcome is not ("MatchedRegistry" or "MatchedCenterMKnownNamePublisher")
            || match.Confidence != FrontendMsiLiveUpdateInstalledVersionConfidence.Exact
            || match.CandidateCount != 1
            || match.VersionCount != 1
            || match.RegistryCandidate is null
            || string.IsNullOrWhiteSpace(match.RegistryCandidate.DisplayVersion)
            || !string.Equals(match.RegistryCandidate.DisplayVersion, match.Version, StringComparison.Ordinal)
            || !string.IsNullOrWhiteSpace(entry.DriverVersion))
            return FrontendMsiLiveUpdateComparisonStatus.Unknown;

        if (entry.Version is not null && entry.PackageVersion is not null)
        {
            if (!AreEqualNumericVersions(entry.Version, entry.PackageVersion))
                return FrontendMsiLiveUpdateComparisonStatus.Unknown;
        }

        var comparableVersion = entry.PackageVersion ?? entry.Version;
        var expectedKind = entry.PackageVersion is not null
            ? FrontendMsiLiveUpdateVersionKind.Package
            : FrontendMsiLiveUpdateVersionKind.Catalog;
        if (string.IsNullOrWhiteSpace(comparableVersion)
            || versionKind != expectedKind
            || !AreEqualNumericVersions(comparableVersion, comparableVersion)
            || !AreEqualNumericVersions(latestVersion, comparableVersion))
            return FrontendMsiLiveUpdateComparisonStatus.Unknown;

        return CompareNumericVersions(match.RegistryCandidate.DisplayVersion, comparableVersion);
    }

    private static bool IsUwpCatalogEntry(MsiLiveUpdateCatalogEntry entry) =>
        IsUwpSentinel(entry.Version)
        || IsUwpSentinel(entry.PackageVersion)
        || IsUwpSentinel(entry.DriverVersion)
        || entry.DiagnosticFields is { } fields && (
            fields.DeviceIdKind == "UwpSentinel"
            || fields.GuidKind == "UwpSentinel"
            || fields.UpgradeGuidKind == "UwpSentinel"
            || fields.CrcKind == "UwpSentinel"
            || fields.TypeKind == "UwpSentinel"
            || fields.OtherKind == "UwpSentinel"
            || fields.FileKind == "UwpSentinel"
            || fields.SiparamKind == "UwpSentinel"
            || string.Equals(fields.FileKind, "StoreLink", StringComparison.OrdinalIgnoreCase)
            || string.Equals(fields.FileExtension, ".appx", StringComparison.OrdinalIgnoreCase)
            || string.Equals(fields.FileExtension, ".msix", StringComparison.OrdinalIgnoreCase));

    private static bool IsUwpSentinel(string? value) =>
        string.Equals(value?.Trim(), "UWP", StringComparison.OrdinalIgnoreCase);

    private static bool AreEqualNumericVersions(string? left, string? right) =>
        CompareNumericVersions(left, right) == FrontendMsiLiveUpdateComparisonStatus.UpToDate
        && CompareNumericVersions(right, left) == FrontendMsiLiveUpdateComparisonStatus.UpToDate;

    private static FrontendMsiLiveUpdateComparisonStatus CompareNumericVersions(string? installed, string? offered)
    {
        if (string.IsNullOrWhiteSpace(installed) || string.IsNullOrWhiteSpace(offered))
            return FrontendMsiLiveUpdateComparisonStatus.Unknown;

        var installedSegments = installed.Split('.');
        var offeredSegments = offered.Split('.');
        if (installedSegments.Length is < 2 or > 6 || installedSegments.Length != offeredSegments.Length)
            return FrontendMsiLiveUpdateComparisonStatus.Unknown;

        var installedParts = new uint[installedSegments.Length];
        var offeredParts = new uint[offeredSegments.Length];
        for (var index = 0; index < installedSegments.Length; index++)
        {
            if (!TryParseVersionComponent(installedSegments[index], out installedParts[index])
                || !TryParseVersionComponent(offeredSegments[index], out offeredParts[index]))
                return FrontendMsiLiveUpdateComparisonStatus.Unknown;
        }

        for (var index = 0; index < installedParts.Length; index++)
        {
            var comparison = installedParts[index].CompareTo(offeredParts[index]);
            if (comparison != 0)
                return comparison < 0
                    ? FrontendMsiLiveUpdateComparisonStatus.UpdateAvailable
                    : FrontendMsiLiveUpdateComparisonStatus.UpToDate;
        }

        return FrontendMsiLiveUpdateComparisonStatus.UpToDate;
    }

    private static bool TryParseVersionComponent(string value, out uint component)
    {
        component = 0;
        return value.Length is >= 1 and <= 10
            && value.All(static character => character is >= '0' and <= '9')
            && uint.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out component);
    }

    private static MsiInstalledVersionResolution ResolveMsiInstalledVersion(
        MsiLiveUpdateCatalogCategory category,
        MsiLiveUpdateCatalogEntry entry,
        MsiUpdateInstalledInventory inventory,
        string? hardwareModelId)
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
                    if (match.Success) return MsiInstalledVersionResolution.Matched(
                        match.Groups["version"].Value, FrontendMsiLiveUpdateInstalledVersionConfidence.Exact,
                        "ExactBiosIdentity", 1);
                }
            }
            return MsiInstalledVersionResolution.Unknown("NoMatchingPresentDevice");
        }

        if (category == MsiLiveUpdateCatalogCategory.Firmware && IsControllerFirmware(entry))
            return inventory.ControllerFirmware is { } controller
                ? MsiInstalledVersionResolution.Matched(controller.Version, controller.Confidence, "ExactControllerIdentity", 1)
                : MsiInstalledVersionResolution.Unknown("NoMatchingPresentDevice");

        if (category == MsiLiveUpdateCatalogCategory.Driver)
            return ResolveMsiDriverInstalledVersion(entry, inventory, hardwareModelId);

        if (category == MsiLiveUpdateCatalogCategory.Utility)
        {
            var registryMatches = inventory.Software.Where(installed =>
                entry.Guid is not null && (string.Equals(installed.ProductCode, entry.Guid, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(installed.RegistryKeyName, $"{{{entry.Guid}}}", StringComparison.OrdinalIgnoreCase))
                || entry.UpgradeGuid is not null && string.Equals(installed.UpgradeCode, entry.UpgradeGuid, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (registryMatches.Length == 1)
            {
                var candidate = registryMatches[0];
                return ResolveRegistryCandidate(candidate, "MatchedRegistry", 1);
            }
            if (registryMatches.Length > 1)
                return MsiInstalledVersionResolution.Unknown("AmbiguousRegistryMatches", registryMatches.Length,
                    CountDistinctVersions(registryMatches.Select(candidate => candidate.DisplayVersion)));

            if (IsMsiCenterMEntry(entry) && IsSupportedMsiCenterMModel(hardwareModelId))
                return ResolveMsiCenterMRegistryCandidate(entry, inventory.Software);

            return MsiInstalledVersionResolution.Unknown("Unmatched");
        }

        return MsiInstalledVersionResolution.Unknown("NotApplicable");
    }

    private static MsiInstalledVersionResolution ResolveMsiCenterMRegistryCandidate(
        MsiLiveUpdateCatalogEntry entry,
        IReadOnlyList<MsiUpdateInstalledSoftware> software)
    {
        var titleCandidates = software.Where(candidate => IsKnownMsiCenterMDisplayName(candidate.DisplayName))
            .ToArray();
        if (titleCandidates.Length == 0)
            return MsiInstalledVersionResolution.Unknown("CenterMNoKnownDisplayNameMatch");

        var conflictingIdentity = titleCandidates.Any(candidate =>
            candidate.ProductCode is not null
            || candidate.UpgradeCode is not null
            || HasConflictingRegistryIdentity(entry, candidate));
        if (conflictingIdentity)
            return MsiInstalledVersionResolution.Unknown("CenterMConflictingRegistryIdentity", titleCandidates.Length,
                CountDistinctVersions(titleCandidates.Select(candidate => candidate.DisplayVersion)));

        var verifiedPublisherCandidates = titleCandidates.Where(IsVerifiedMsiCenterMPublisher).ToArray();
        if (verifiedPublisherCandidates.Length == 0)
            return MsiInstalledVersionResolution.Unknown("CenterMUnverifiedPublisher", titleCandidates.Length,
                CountDistinctVersions(titleCandidates.Select(candidate => candidate.DisplayVersion)));
        if (verifiedPublisherCandidates.Length > 1)
            return MsiInstalledVersionResolution.Unknown("CenterMAmbiguousRegistryMatches", verifiedPublisherCandidates.Length,
                CountDistinctVersions(verifiedPublisherCandidates.Select(candidate => candidate.DisplayVersion)));

        return ResolveRegistryCandidate(verifiedPublisherCandidates[0], "MatchedCenterMKnownNamePublisher", 1);
    }

    private static MsiInstalledVersionResolution ResolveRegistryCandidate(
        MsiUpdateInstalledSoftware candidate,
        string outcome,
        int candidateCount)
    {
        if (string.IsNullOrWhiteSpace(candidate.DisplayVersion))
            return MsiInstalledVersionResolution.Unknown("MissingRegistryDisplayVersion", candidateCount, 0) with
            {
                RegistryCandidate = candidate
            };

        return MsiInstalledVersionResolution.Matched(candidate.DisplayVersion,
            FrontendMsiLiveUpdateInstalledVersionConfidence.Exact, outcome, candidateCount, 1, candidate);
    }

    private static bool HasConflictingRegistryIdentity(
        MsiLiveUpdateCatalogEntry entry,
        MsiUpdateInstalledSoftware candidate) =>
        entry.Guid is not null && !string.IsNullOrWhiteSpace(candidate.ProductCode)
            && !string.Equals(candidate.ProductCode, entry.Guid, StringComparison.OrdinalIgnoreCase)
        || entry.UpgradeGuid is not null && !string.IsNullOrWhiteSpace(candidate.UpgradeCode)
            && !string.Equals(candidate.UpgradeCode, entry.UpgradeGuid, StringComparison.OrdinalIgnoreCase)
        || entry.Guid is not null && string.IsNullOrWhiteSpace(candidate.ProductCode)
            && Guid.TryParse(candidate.RegistryKeyName, out var registryProductCode)
            && !string.Equals(registryProductCode.ToString("D"), entry.Guid, StringComparison.OrdinalIgnoreCase);

    private static bool IsKnownMsiCenterMDisplayName(string? displayName)
    {
        var name = displayName?.Trim();
        return string.Equals(name, MsiCenterMCatalogName, StringComparison.OrdinalIgnoreCase)
            || string.Equals(name, MsiCenterMSdkDisplayName, StringComparison.OrdinalIgnoreCase)
            || Regex.IsMatch(name ?? string.Empty, @"^MSI Center M\s+v?\d+(?:\.\d+){1,3}$",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private static bool IsVerifiedMsiCenterMPublisher(MsiUpdateInstalledSoftware candidate)
    {
        var publisher = candidate.Publisher?.Trim();
        return string.Equals(publisher, VerifiedMsiPublisher, StringComparison.OrdinalIgnoreCase)
            || string.Equals(publisher, VerifiedMsiCorporationPublisher, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsMsiCenterMEntry(MsiLiveUpdateCatalogEntry entry) =>
        string.Equals(entry.Name.Trim(), MsiCenterMCatalogName, StringComparison.OrdinalIgnoreCase);

    private static bool IsSupportedMsiCenterMModel(string? hardwareModelId) =>
        string.Equals(hardwareModelId, "msi.claw.cg3em", StringComparison.OrdinalIgnoreCase)
        || string.Equals(hardwareModelId, "msi.claw.a2vm.7", StringComparison.OrdinalIgnoreCase)
        || string.Equals(hardwareModelId, "msi.claw.a2vm.8", StringComparison.OrdinalIgnoreCase);

    private static MsiInstalledVersionResolution ResolveMsiDriverInstalledVersion(
        MsiLiveUpdateCatalogEntry entry,
        MsiUpdateInstalledInventory inventory,
        string? hardwareModelId)
    {
        if (string.Equals(hardwareModelId, "msi.claw.cg3em", StringComparison.Ordinal))
        {
            var cg3emMapping = FindCg3emDriverMapping(entry.Name);
            if (cg3emMapping is not null)
                return ResolveCg3emMappedDriver(entry, inventory, cg3emMapping);

            var isIntelDsaFallback = KnownMsiIntelDriverNames.Any(candidate =>
                string.Equals(candidate.Name, entry.Name.Trim(), StringComparison.OrdinalIgnoreCase));
            if (!isIntelDsaFallback)
                return MsiInstalledVersionResolution.Unknown("NoCg3emPackageMapping");
        }

        var catalogHardwareIds = GetCatalogHardwareIds(entry);
        if (IsA2vmModel(hardwareModelId) && catalogHardwareIds.Count > 0)
        {
            var validatedIds = catalogHardwareIds.Where(IsPreciseCatalogHardwareId).ToArray();
            var hardwareMatches = inventory.PnpDevices
                .Where(device => validatedIds.Any(deviceId => IsMsiDeviceIdMatch(deviceId, device)))
                .ToArray();
            if (hardwareMatches.Length > 0) return ResolveDriverCandidates(hardwareMatches, "ExactHardwareId");
            if (validatedIds.Length > 0)
                return MsiInstalledVersionResolution.Unknown("NoMatchingPresentDevice");
        }
        else if (!string.IsNullOrWhiteSpace(entry.DeviceId))
        {
            var hardwareMatches = inventory.PnpDevices.Where(device => IsMsiDeviceIdMatch(entry.DeviceId, device)).ToArray();
            if (hardwareMatches.Length > 0) return ResolveDriverCandidates(hardwareMatches, "ExactHardwareId");
            if (IntelDsaHardwareMatcher.HasComparableHardwareIdentity(entry.DeviceId))
                return MsiInstalledVersionResolution.Unknown("NoMatchingPresentDevice");
        }

        if (!string.IsNullOrWhiteSpace(entry.InfName))
        {
            var infMatches = inventory.PnpDevices.Where(device =>
                string.Equals(device.InfName, entry.InfName, StringComparison.OrdinalIgnoreCase)).ToArray();
            return infMatches.Length > 0
                ? ResolveDriverCandidates(infMatches, "ExactInf")
                : MsiInstalledVersionResolution.Unknown("NoMatchingPresentDevice");
        }

        if (string.Equals(entry.Name.Trim(), "Realtek Audio Driver", StringComparison.OrdinalIgnoreCase))
        {
            return hardwareModelId is "msi.claw.a2vm.7" or "msi.claw.a2vm.8"
                ? ResolveA2vmLegacyRealtekAudioDriverInstalledVersion(inventory)
                : MsiInstalledVersionResolution.Unknown("NoSupportedModelSpecificMapping");
        }

        var family = KnownMsiIntelDriverNames
            .FirstOrDefault(candidate => string.Equals(candidate.Name, entry.Name.Trim(), StringComparison.OrdinalIgnoreCase));
        if (family.Name is null) return MsiInstalledVersionResolution.Unknown("NoUsableIdentity");

        var familyMatches = inventory.PnpDevices.Where(device => MatchesKnownIntelFamily(family.Family, device))
            .GroupBy(device => device.DeviceId, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToArray();
        if (familyMatches.Length == 0) return MsiInstalledVersionResolution.Unknown("NoMatchingPresentDevice");
        if (familyMatches.Length != 1)
        {
            var distinctVersionCount = familyMatches.Select(device => device.DriverVersion)
                .Where(version => !string.IsNullOrWhiteSpace(version))
                .Distinct(StringComparer.OrdinalIgnoreCase).Count();
            return MsiInstalledVersionResolution.Unknown("AmbiguousMatches", familyMatches.Length, distinctVersionCount);
        }

        var familyDevice = familyMatches[0];
        if (string.IsNullOrWhiteSpace(familyDevice.DriverVersion))
            return MsiInstalledVersionResolution.Unknown("MissingDriverVersion", 1);
        return MsiInstalledVersionResolution.Matched(familyDevice.DriverVersion,
            FrontendMsiLiveUpdateInstalledVersionConfidence.Heuristic, "HeuristicFamily", 1, 1);
    }

    private static MsiCg3emDriverMapping? FindCg3emDriverMapping(string name) =>
        Cg3emDriverMappings.FirstOrDefault(mapping =>
            string.Equals(mapping.CatalogName, name.Trim(), StringComparison.OrdinalIgnoreCase));

    private static MsiInstalledVersionResolution ResolveCg3emMappedDriver(
        MsiLiveUpdateCatalogEntry entry,
        MsiUpdateInstalledInventory inventory,
        MsiCg3emDriverMapping mapping)
    {
        if (mapping.DriverStoreIdentity is { } driverStoreIdentity)
            return ResolveCg3emDriverStorePackage(entry, inventory, driverStoreIdentity);

        var comparisonVersion = ResolveCg3emComparisonVersion(entry, mapping);
        var primaryCandidates = ResolveCg3emMappedCandidates(entry, inventory, mapping.PrimaryHardwareIds, mapping);
        var fallbackCandidates = mapping.FallbackHardwareIds.Count > 0
            ? ResolveCg3emMappedCandidates(entry, inventory, mapping.FallbackHardwareIds, mapping)
            : [];
        var candidates = primaryCandidates.Length > 0 ? primaryCandidates : fallbackCandidates;

        if (candidates.Length == 0)
            return MsiInstalledVersionResolution.Cg3emMapped(null, comparisonVersion,
                "Cg3emMappedNoMatchingPresentDevice", 0, 0, mapping.Comparable);

        // The DTS core is authoritative when present, but a recognized controller/service
        // component with a conflicting version makes the package-level result ambiguous.
        var coherenceCandidates = primaryCandidates.Length > 0 && fallbackCandidates.Length > 0
            ? primaryCandidates.Concat(fallbackCandidates).ToArray()
            : candidates;
        var versions = coherenceCandidates.Select(candidate => candidate.DriverVersion).ToArray();
        if (versions.Any(string.IsNullOrWhiteSpace))
            return MsiInstalledVersionResolution.Cg3emMapped(null, comparisonVersion,
                "Cg3emMappedMissingDriverVersion", coherenceCandidates.Length, 0, mapping.Comparable);

        var installedVersion = candidates[0].DriverVersion!;
        if (!AreEqualNumericVersions(installedVersion, installedVersion)
            || versions.Any(version => !AreEqualNumericVersions(installedVersion, version)))
            return MsiInstalledVersionResolution.Cg3emMapped(null, comparisonVersion,
                "Cg3emMappedConflictingVersions", coherenceCandidates.Length,
                versions.Where(version => !string.IsNullOrWhiteSpace(version))
                    .Distinct(StringComparer.OrdinalIgnoreCase).Count(), mapping.Comparable);

        return MsiInstalledVersionResolution.Cg3emMapped(installedVersion, comparisonVersion,
            "Cg3emFixedPackageMapping", coherenceCandidates.Length, 1, mapping.Comparable);
    }

    private static MsiInstalledVersionResolution ResolveCg3emDriverStorePackage(
        MsiLiveUpdateCatalogEntry entry,
        MsiUpdateInstalledInventory inventory,
        MsiCg3emDriverStoreIdentity identity)
    {
        var packageVersion = entry.PackageVersion;
        if (string.IsNullOrWhiteSpace(entry.OemPackageIdentity)
            || !string.Equals(entry.OemPackageIdentity.Trim(), identity.OemPackageIdentity,
                StringComparison.OrdinalIgnoreCase))
            return MsiInstalledVersionResolution.Cg3emMapped(null, null,
                "Cg3emInvalidOemPackageIdentity", 0, 0, true);
        if (!string.Equals(inventory.DriverStore.Status, "Completed", StringComparison.Ordinal))
            return MsiInstalledVersionResolution.Cg3emMapped(null, packageVersion,
                "Cg3emDriverStoreUnavailable", 0, 0, true);

        var candidates = inventory.DriverStore.Packages
            .Where(package => string.Equals(package.OriginalInfName, identity.OriginalInfName,
                    StringComparison.OrdinalIgnoreCase)
                && string.Equals(package.Provider, identity.Provider, StringComparison.OrdinalIgnoreCase)
                && string.Equals(package.Class, identity.DeviceClass, StringComparison.OrdinalIgnoreCase))
            .OrderBy(package => package.PublishedInfName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (candidates.Length == 0)
            return MsiInstalledVersionResolution.Cg3emMapped(null, packageVersion,
                "NoMatchingInstalledDriverPackage", 0, 0, true);

        var installedVersion = candidates[0].Version;
        if (CompareNumericVersions(installedVersion, installedVersion)
                != FrontendMsiLiveUpdateComparisonStatus.UpToDate
            || candidates.Skip(1).Any(candidate =>
                CompareNumericVersions(installedVersion, candidate.Version)
                    != FrontendMsiLiveUpdateComparisonStatus.UpToDate
                || CompareNumericVersions(candidate.Version, installedVersion)
                    != FrontendMsiLiveUpdateComparisonStatus.UpToDate))
            return MsiInstalledVersionResolution.Cg3emMapped(null, packageVersion,
                "Cg3emConflictingDriverStorePackageVersions", candidates.Length,
                candidates.Select(candidate => candidate.Version).Distinct(StringComparer.OrdinalIgnoreCase).Count(), true);

        if (string.IsNullOrWhiteSpace(packageVersion)
            || CompareNumericVersions(packageVersion, packageVersion) != FrontendMsiLiveUpdateComparisonStatus.UpToDate)
            return MsiInstalledVersionResolution.Cg3emMapped(installedVersion, null,
                "Cg3emMissingOrInvalidPackageVersion", candidates.Length, 1, true);

        return MsiInstalledVersionResolution.Cg3emMapped(installedVersion, packageVersion,
            "Cg3emInstalledInfPackageMatch", candidates.Length, 1, true);
    }

    private static MsiUpdateInstalledPnpDevice[] ResolveCg3emMappedCandidates(
        MsiLiveUpdateCatalogEntry entry,
        MsiUpdateInstalledInventory inventory,
        IReadOnlyList<string> mappedHardwareIds,
        MsiCg3emDriverMapping mapping)
    {
        var mappedCandidates = inventory.PnpDevices
            .Where(device => MatchesCg3emDriverMapping(device, mappedHardwareIds, mapping))
            .GroupBy(device => device.DeviceId, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToArray();

        // Catalog identities may narrow a statically mapped package, but can never add
        // a device outside the EX INF-backed provider/class/ID family.
        var catalogHardwareIds = GetCatalogHardwareIds(entry);
        var preciseCatalogHardwareIds = catalogHardwareIds
            .Where(IsPreciseCatalogHardwareId).ToArray();
        if (preciseCatalogHardwareIds.Length > 0)
        {
            var exactDeviceMatches = mappedCandidates
                .Where(device => preciseCatalogHardwareIds.Any(deviceId => IsMsiDeviceIdMatch(deviceId, device)))
                .ToArray();
            if (exactDeviceMatches.Length > 0) return exactDeviceMatches;
        }

        if (!string.IsNullOrWhiteSpace(entry.InfName))
        {
            var exactInfMatches = mappedCandidates
                .Where(device => string.Equals(device.InfName, entry.InfName, StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (exactInfMatches.Length > 0) return exactInfMatches;
        }

        return mappedCandidates;
    }

    private static bool MatchesCg3emDriverMapping(
        MsiUpdateInstalledPnpDevice device,
        IReadOnlyList<string> mappedHardwareIds,
        MsiCg3emDriverMapping mapping)
    {
        var providerMatches = device.DriverProvider?.StartsWith(mapping.Provider, StringComparison.OrdinalIgnoreCase) == true;
        var classMatches = mapping.DeviceClasses.Any(expectedClass =>
            string.Equals(device.ClassName, expectedClass, StringComparison.OrdinalIgnoreCase)
            || string.Equals(device.WmiDeviceClass, expectedClass, StringComparison.OrdinalIgnoreCase));
        if (!providerMatches || !classMatches) return false;

        return mappedHardwareIds.Any(hardwareId =>
            string.Equals(device.DeviceId, hardwareId, StringComparison.OrdinalIgnoreCase)
            || device.HardwareIds.Contains(hardwareId, StringComparer.OrdinalIgnoreCase)
            || device.CompatibleIds.Contains(hardwareId, StringComparer.OrdinalIgnoreCase));
    }

    private static string? ResolveCg3emComparisonVersion(
        MsiLiveUpdateCatalogEntry entry,
        MsiCg3emDriverMapping mapping)
    {
        if (!mapping.Comparable || IsUwpCatalogEntry(entry)) return null;

        var offeredVersion = entry.DriverVersion ?? entry.Version;
        if (string.IsNullOrWhiteSpace(offeredVersion)
            || !AreEqualNumericVersions(offeredVersion, offeredVersion))
            return null;

        if (entry.DriverVersion is not null && entry.Version is not null
            && !AreEqualNumericVersions(entry.DriverVersion, entry.Version))
            return null;
        if (entry.PackageVersion is not null
            && !AreEqualNumericVersions(offeredVersion, entry.PackageVersion))
            return null;

        return offeredVersion;
    }

    private static MsiInstalledVersionResolution ResolveA2vmLegacyRealtekAudioDriverInstalledVersion(
        MsiUpdateInstalledInventory inventory)
    {
        var candidates = inventory.PnpDevices.Where(IsA2vmLegacyRealtekAudioCodec)
            .GroupBy(device => device.DeviceId, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToArray();
        if (candidates.Length == 0)
            return MsiInstalledVersionResolution.Unknown("NoMatchingPresentDevice");

        if (candidates.Length != 1)
        {
            var distinctVersionCount = candidates.Select(device => device.DriverVersion)
                .Where(version => !string.IsNullOrWhiteSpace(version))
                .Distinct(StringComparer.OrdinalIgnoreCase).Count();
            return MsiInstalledVersionResolution.Unknown("AmbiguousMatches", candidates.Length, distinctVersionCount);
        }

        var candidate = candidates[0];
        if (string.IsNullOrWhiteSpace(candidate.DriverVersion))
            return MsiInstalledVersionResolution.Unknown("MissingDriverVersion", 1);
        return MsiInstalledVersionResolution.Matched(candidate.DriverVersion,
            FrontendMsiLiveUpdateInstalledVersionConfidence.Heuristic, "HeuristicFamily", 1, 1);
    }

    private static bool IsA2vmLegacyRealtekAudioCodec(MsiUpdateInstalledPnpDevice device)
    {
        if (string.IsNullOrWhiteSpace(device.DriverProvider)
            || !device.DriverProvider.StartsWith("Realtek", StringComparison.OrdinalIgnoreCase)) return false;

        var deviceClass = FirstNonEmpty(device.WmiDeviceClass, device.ClassName);
        if (!string.Equals(deviceClass, "MEDIA", StringComparison.OrdinalIgnoreCase)) return false;

        var deviceName = FirstNonEmpty(device.WmiDeviceName, device.FriendlyName);
        if (!string.Equals(deviceName, "Realtek(R) Audio", StringComparison.OrdinalIgnoreCase)) return false;

        return device.HardwareIds.Any(IsA2vmLegacyRealtekAudioCodecHardwareId);
    }

    private static bool IsA2vmLegacyRealtekAudioCodecHardwareId(string hardwareId) =>
        Regex.IsMatch(hardwareId,
            @"^INTELAUDIO\\FUNC_01&VEN_10EC&DEV_0256(?:&|$)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static MsiInstalledVersionResolution ResolveDriverCandidates(
        IReadOnlyList<MsiUpdateInstalledPnpDevice> candidates,
        string exactOutcome)
    {
        var matchedDevices = candidates.GroupBy(device => device.DeviceId, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First()).ToArray();
        var versions = matchedDevices.Select(device => device.DriverVersion)
            .Where(version => !string.IsNullOrWhiteSpace(version))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (versions.Length == 0)
            return MsiInstalledVersionResolution.Unknown("MissingDriverVersion", matchedDevices.Length);
        if (versions.Length > 1)
            return MsiInstalledVersionResolution.Unknown("AmbiguousMatches", matchedDevices.Length, versions.Length);
        return MsiInstalledVersionResolution.Matched(versions[0]!,
            matchedDevices.Length == 1
                ? FrontendMsiLiveUpdateInstalledVersionConfidence.Exact
                : FrontendMsiLiveUpdateInstalledVersionConfidence.Heuristic,
            exactOutcome, matchedDevices.Length, versions.Length);
    }

    private static bool MatchesKnownIntelFamily(IntelDsaComponentFamily family, MsiUpdateInstalledPnpDevice device)
    {
        var provider = device.DriverProvider;
        if (string.IsNullOrWhiteSpace(provider) || !provider.StartsWith("Intel", StringComparison.OrdinalIgnoreCase)) return false;
        var deviceClass = FirstNonEmpty(device.WmiDeviceClass, device.ClassName);
        var deviceName = FirstNonEmpty(device.WmiDeviceName, device.FriendlyName);
        if (string.IsNullOrWhiteSpace(deviceName)) return false;

        return family switch
        {
            IntelDsaComponentFamily.Graphics => string.Equals(deviceClass, "DISPLAY", StringComparison.OrdinalIgnoreCase)
                && (ContainsToken(deviceName, "Arc") || ContainsPhrase(deviceName, "UHD Graphics")
                    || ContainsPhrase(deviceName, "Iris Xe")),
            IntelDsaComponentFamily.Wireless => string.Equals(deviceClass, "NET", StringComparison.OrdinalIgnoreCase)
                && (ContainsPhrase(deviceName, "Wi-Fi") || ContainsPhrase(deviceName, "Wireless")),
            IntelDsaComponentFamily.Bluetooth => string.Equals(deviceClass, "BLUETOOTH", StringComparison.OrdinalIgnoreCase)
                && ContainsPhrase(deviceName, "Bluetooth"),
            IntelDsaComponentFamily.Npu => (string.Equals(deviceClass, "SYSTEM", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(deviceClass, "PROCESSOR", StringComparison.OrdinalIgnoreCase))
                && (ContainsToken(deviceName, "NPU") || ContainsPhrase(deviceName, "AI Boost")),
            _ => false
        };
    }

    private static string? FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

    private static bool ContainsToken(string value, string token) =>
        Regex.IsMatch(value, $@"(?<![A-Z0-9]){Regex.Escape(token)}(?![A-Z0-9])",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static bool ContainsPhrase(string value, string phrase) =>
        value.Contains(phrase, StringComparison.OrdinalIgnoreCase);

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

    private static bool HasOwnedControllerFirmwareCatalogIdentity(MsiLiveUpdateCatalogEntry entry) =>
        IsControllerFirmware(entry)
        && (string.IsNullOrWhiteSpace(entry.DeviceId)
            || Regex.IsMatch(entry.DeviceId, @"(?:^|\\)VID_0DB0&PID_190[12](?:&|\\|$)",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant));

    private static FrontendMsiLiveUpdateCatalogEntry MapIntelEntry(IntelDsaResolvedDriver entry)
    {
        var installedVersion = entry.Device.DriverVersion;
        var deviceName = SafeDeviceName(entry.Device.WmiDeviceName ?? entry.Device.FriendlyName);
        var displayName = string.IsNullOrWhiteSpace(deviceName)
            ? entry.PackageName
            : $"{entry.PackageName} — {deviceName}";
        var exactHardwareMatch = entry.MatchReason.EndsWith(":HardwareId", StringComparison.Ordinal);
        var comparisonStatus = entry.Family switch
        {
            IntelDsaComponentFamily.Graphics or IntelDsaComponentFamily.Wireless or IntelDsaComponentFamily.Bluetooth
                when exactHardwareMatch && entry.VersionKind == FrontendMsiLiveUpdateVersionKind.Driver =>
                    CompareNumericVersions(installedVersion, entry.LatestVersion),
            IntelDsaComponentFamily.Npu
                when exactHardwareMatch
                    && entry.VersionKind == FrontendMsiLiveUpdateVersionKind.Package
                    && AreEqualNumericVersions(installedVersion, entry.LatestVersion) =>
                    FrontendMsiLiveUpdateComparisonStatus.UpToDate,
            _ => FrontendMsiLiveUpdateComparisonStatus.Unknown
        };
        return new(displayName, entry.LatestVersion, entry.ReleaseDate, null,
            installedVersion,
            FrontendMsiLiveUpdateVersionSource.IntelDsa,
            entry.VersionKind,
            installedVersion is null ? null : FrontendMsiLiveUpdateInstalledVersionConfidence.Exact,
            comparisonStatus);
    }

    private static string? SafeDeviceName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var safe = new string(value.Where(static character => !char.IsControl(character)).Take(160).ToArray()).Trim();
        return safe.Length == 0 ? null : safe;
    }

    private static MsiIntelDuplicateEvaluation EvaluateIntelDuplicate(
        MsiLiveUpdateCatalogEntry entry,
        IReadOnlyList<IntelDsaResolvedDriver> intelRows,
        bool intelQuerySucceeded)
    {
        var family = KnownMsiIntelDriverNames
            .FirstOrDefault(candidate => string.Equals(candidate.Name, entry.Name.Trim(), StringComparison.OrdinalIgnoreCase));
        if (family.Name is null) return new(false, "NotApplicable", 0);
        if (!intelQuerySucceeded) return new(false, "RetainedNoDsa", 0);

        var familyRows = intelRows.Where(candidate => candidate.Family == family.Family).ToArray();
        var distinctDevices = familyRows.Select(candidate => candidate.Device.DeviceId)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (distinctDevices.Length == 0) return new(false, "RetainedNoDsa", 0);

        var catalogHardwareIds = GetCatalogHardwareIds(entry);
        var preciseCatalogHardwareIds = catalogHardwareIds
            .Where(IsPreciseCatalogHardwareId).ToArray();
        if (preciseCatalogHardwareIds.Length > 0)
        {
            var identityMatches = familyRows.Where(candidate => preciseCatalogHardwareIds.Any(deviceId =>
                IsMsiDeviceIdMatch(deviceId, candidate.Device))).ToArray();
            if (identityMatches.Length > 0)
            {
                var matchedDeviceCount = identityMatches.Select(candidate => candidate.Device.DeviceId)
                    .Distinct(StringComparer.OrdinalIgnoreCase).Count();
                var matchedVersionCount = identityMatches.Select(candidate => candidate.LatestVersion)
                    .Where(version => !string.IsNullOrWhiteSpace(version))
                    .Distinct(StringComparer.OrdinalIgnoreCase).Count();
                return new(true, "SuppressedByExactIdentity", matchedDeviceCount, matchedVersionCount);
            }

            if (preciseCatalogHardwareIds.Length > 0)
                return new(false, "RetainedConflictingIdentity", distinctDevices.Length,
                    CountDistinctVersions(familyRows.Select(candidate => candidate.LatestVersion)));
        }

        return distinctDevices.Length == 1
            ? new(true, "SuppressedByUniqueDsaFamily", 1,
                CountDistinctVersions(familyRows.Select(candidate => candidate.LatestVersion)))
            : new(false, "RetainedAmbiguousFamily", distinctDevices.Length,
                CountDistinctVersions(familyRows.Select(candidate => candidate.LatestVersion)));
    }

    private static int CountDistinctVersions(IEnumerable<string?> versions) => versions
        .Where(version => !string.IsNullOrWhiteSpace(version))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .Count();

    private static IReadOnlyList<string> GetCatalogHardwareIds(MsiLiveUpdateCatalogEntry entry)
    {
        if (entry.CatalogHardwareIds is { Count: > 0 })
            return entry.CatalogHardwareIds;
        return string.IsNullOrWhiteSpace(entry.DeviceId) ? [] : [entry.DeviceId];
    }

    private static bool IsA2vmModel(string? hardwareModelId) =>
        string.Equals(hardwareModelId, "msi.claw.a2vm.7", StringComparison.OrdinalIgnoreCase)
        || string.Equals(hardwareModelId, "msi.claw.a2vm.8", StringComparison.OrdinalIgnoreCase);

    private static bool IsPreciseCatalogHardwareId(string hardwareId)
    {
        if (!MsiLiveUpdateCatalogResultParser.IsValidSingleHardwareId(hardwareId)) return false;
        var separator = hardwareId.IndexOf('\\');
        var identity = hardwareId[(separator + 1)..];
        return !Regex.IsMatch(identity, @"^(?:VEN_[A-Z0-9]{3,8}|VID_[0-9A-F]{4})$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private static bool IsMsiDeviceIdMatch(string deviceId, MsiUpdateInstalledPnpDevice device) =>
        string.Equals(device.DeviceId, deviceId, StringComparison.OrdinalIgnoreCase)
        || device.HardwareIds.Contains(deviceId, StringComparer.OrdinalIgnoreCase)
        || device.CompatibleIds.Contains(deviceId, StringComparer.OrdinalIgnoreCase)
        || IntelDsaHardwareMatcher.TryMatchDetectionValue(deviceId,
            device.HardwareIds.Concat(device.CompatibleIds), out _);

    private static string? GetPolicyExclusionReason(MsiLiveUpdateCatalogCategory category, MsiLiveUpdateCatalogEntry entry)
    {
        if (IsUwpCatalogEntry(entry)) return "UwpCatalogEntry";
        return category == MsiLiveUpdateCatalogCategory.Utility
            && ExcludedUtilityNames.Contains(entry.Name.Trim(), StringComparer.OrdinalIgnoreCase)
                ? "ExcludedUtility"
                : null;
    }

    private sealed record MsiIntelDuplicateEvaluation(bool Suppress, string Outcome, int CandidateCount, int VersionCount = 0);

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
