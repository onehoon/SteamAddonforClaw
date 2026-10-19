using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using SteamInputAddonforClaw.Contracts.Frontend;

namespace SteamInputAddonforClaw.Devices.MSI.LiveUpdate;

internal sealed record MsiUpdateDiagnosticSnapshotInput(
    string? HardwareModelId,
    MsiLiveUpdateCatalogQueryResult Msi,
    IntelDsaCatalogResult Intel,
    MsiUpdateInstalledInventory Inventory,
    FrontendMsiLiveUpdateCatalogSnapshot Composed,
    IReadOnlyList<MsiUpdateCatalogRowDiagnostic> MsiRows,
    IReadOnlyList<IntelDsaResolvedDriver> ResolvedIntelRows,
    DateTimeOffset CapturedAtUtc,
    string? AppVersion);

internal sealed record MsiUpdateDiagnosticSnapshotWriteResult(
    bool Succeeded,
    string? FileName = null,
    string? ErrorKind = null,
    string? RetentionErrorKind = null,
    int MsiRowCount = 0,
    int LocalPnpRowCount = 0,
    int IntelMatchCount = 0);

internal sealed class MsiUpdateDiagnosticSnapshotDocument
{
    public int SchemaVersion { get; init; } = 1;
    public DateTimeOffset CapturedAtUtc { get; init; }
    public string? AppVersion { get; init; }
    public string? HardwareModelId { get; init; }
    public string? MsiRequestModelName { get; init; }
    public string? OperatingSystem { get; init; }
    public string? CountryCode { get; init; }
    public required MsiUpdateDiagnosticSources Sources { get; set; }
    public required MsiUpdateDiagnosticCounts Counts { get; init; }
    public required MsiUpdateDiagnosticInventory Inventory { get; set; }
    public List<MsiUpdateDiagnosticCatalogRow> MsiRows { get; set; } = [];
    public List<MsiUpdateDiagnosticIntelMatch> IntelDsaMatches { get; set; } = [];
    public bool Truncated { get; set; }
    public required MsiUpdateDiagnosticTruncation Truncation { get; init; }
}

internal sealed record MsiUpdateDiagnosticSources(
    string MsiState,
    string? MsiErrorKind,
    List<MsiUpdateDiagnosticCategory> Categories,
    MsiUpdateDiagnosticIntelSource IntelDsa,
    List<string> InventoryWarnings);

internal sealed record MsiUpdateDiagnosticCategory(
    string Category,
    string State,
    string? ErrorKind,
    int OriginalCount,
    int CapturedCount);

internal sealed record MsiUpdateDiagnosticIntelSource(
    bool Succeeded,
    string? ErrorKind,
    DateTimeOffset? CatalogCreatedUtc,
    int ResolvedCount,
    int CapturedMatchCount);

internal sealed record MsiUpdateDiagnosticCounts(
    int OriginalMsiItems,
    int ExcludedByPolicy,
    int RetainedMsiRows,
    int SuppressedByIntelDsa,
    int AppendedIntelDsaRows);

internal sealed record MsiUpdateDiagnosticCatalogRow(
    string SourceCategory,
    int SourceIndex,
    string? Name,
    MsiUpdateDiagnosticOemFields Oem,
    MsiUpdateDiagnosticLatestVersion Latest,
    MsiUpdateDiagnosticInstalledVersion Installed,
    string UiDisposition,
    string? DispositionReason,
    MsiUpdateDiagnosticDuplicate? IntelDsaDuplicate);

internal sealed record MsiUpdateDiagnosticOemFields(
    string? Version,
    string? DriverVersion,
    string? PackageVersion,
    string? ReleaseDate,
    string? InfName,
    string DeviceIdKind,
    List<string> HardwareTypes,
    string GuidKind,
    string? Guid,
    string UpgradeGuidKind,
    string? UpgradeGuid,
    string CrcKind,
    string? Crc,
    string TypeKind,
    string? Type,
    string OtherKind,
    string? Other,
    string FileKind,
    string? FileExtension,
    string SiparamKind);

internal sealed record MsiUpdateDiagnosticLatestVersion(
    string? Value,
    string? Source,
    string? Kind,
    string? UnavailableReason);

internal sealed record MsiUpdateDiagnosticInstalledVersion(
    string? Value,
    string? Confidence,
    string? MatchOutcome,
    int? CandidateCount,
    int? VersionCount,
    string? UnavailableReason);

internal sealed record MsiUpdateDiagnosticDuplicate(
    string Outcome,
    int CandidateCount,
    int VersionCount);

internal sealed record MsiUpdateDiagnosticIntelMatch(
    string? PackageId,
    string Family,
    string? LatestVersion,
    string VersionKind,
    string? ReleaseDate,
    string? InstalledVersion,
    int? PnpRowId,
    List<string> HardwareTypes);

internal sealed record MsiUpdateDiagnosticInventory(
    int LocalPnpTotalCount,
    int LocalPnpCapturedCount,
    List<MsiUpdateDiagnosticPnpRow> LocalPnp,
    int SoftwareTotalCount,
    int SoftwareCapturedCount,
    List<MsiUpdateDiagnosticSoftwareRow> Software,
    int AppxTotalCount,
    int AppxCapturedCount,
    List<MsiUpdateDiagnosticAppxRow> AppxPackages,
    List<string> BiosVersions,
    MsiUpdateDiagnosticControllerFirmware? ControllerFirmware);

internal sealed record MsiUpdateDiagnosticPnpRow(
    int RowId,
    string? DeviceClass,
    string? OsDriverClass,
    string? DriverProvider,
    string? DriverVersion,
    string? InfName,
    List<string> HardwareTypes,
    string SelectionReason,
    string? DeviceDescriptorOmittedReason);

internal sealed record MsiUpdateDiagnosticSoftwareRow(
    string? DisplayName,
    string? Publisher,
    string? DisplayVersion,
    string? ProductCode,
    string? UpgradeCode,
    string? RegistrySourceKind,
    List<string> MatchedCatalogNames);

internal sealed record MsiUpdateDiagnosticAppxRow(
    string? Name,
    string? FamilyName,
    string? Version,
    List<string> MatchedCatalogNames,
    string NameDisposition,
    string FamilyNameDisposition);

internal sealed record MsiUpdateDiagnosticControllerFirmware(string? Version, string Confidence);

internal sealed class MsiUpdateDiagnosticTruncation
{
    public int MsiRowsOmitted { get; set; }
    public int IntelMatchesOmitted { get; set; }
    public int LocalPnpRowsOmitted { get; set; }
    public int SoftwareRowsOmitted { get; set; }
    public int AppxRowsOmitted { get; set; }
    public int BiosVersionsOmitted { get; set; }
}

internal static class MsiUpdateDiagnosticSnapshotBuilder
{
    private const int MaximumMsiRows = 2_000;
    private const int MaximumIntelMatches = 256;
    private const int MaximumLocalPnpRows = 512;
    private const int MaximumSoftwareRows = 256;
    private const int MaximumAppxRows = 256;
    private const int MaximumSnapshotBytes = 2 * 1024 * 1024;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private static readonly Regex HardwareTokenPattern = new(
        @"^(?:VEN_[A-Z0-9]{3,8}|VID_[0-9A-F]{4}|DEV_[A-Z0-9]{2,8}|PID_[0-9A-F]{4}|SUBSYS_[0-9A-F]{8}|REV_[0-9A-F]{2,4}|MI_[0-9A-F]{2}|FUNC_[0-9A-F]{2}|CC_[0-9A-F]{6}|PNP[0-9A-F]{4}|INTC[0-9A-F]{4,8}|CID_[A-Z0-9]{4,12}|ACPI_[A-Z0-9]{3,8})$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private static readonly HashSet<string> HardwareBuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "PCI", "USB", "USBSTOR", "ACPI", "HDAUDIO", "INTELAUDIO", "BTHENUM", "ROOT", "SWD",
        "SCSI", "SD", "I2C", "GPIO", "DISPLAY", "HID", "HIDCLASS", "SYSTEM", "PROCESSOR",
        "MEDIA", "NET", "SWC", "UCM", "USB4", "NVME", "VMBUS", "VPCI"
    };

    private static readonly HashSet<string> GenericWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "DRIVER", "BIOS", "FIRMWARE", "UTILITY", "UTILITIES", "SOFTWARE", "UPDATE", "UPDATES",
        "INSTALLER", "INSTALLATION", "APPLICATION", "APP", "DEVICE", "WINDOWS", "SYSTEM", "PACKAGE",
        "CORPORATION", "CORP", "INC", "LTD", "LIMITED", "TECHNOLOGY", "PROCESSING"
    };
    private static readonly HashSet<string> KnownProviderTokens = new(StringComparer.OrdinalIgnoreCase)
    {
        "INTEL", "REALTEK", "GOODIX", "DTS", "MSI", "NVIDIA", "AMD", "QUALCOMM", "SYNAPTICS",
        "MEDIATEK", "CIRRUS", "MICROSOFT", "ELAN", "ASUS", "BROADCOM"
    };
    private static readonly Dictionary<string, string> ApprovedAppxPackageNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["MSI_Center_M"] = "MSI_Center_M",
        ["RealtekSemiconductorCorp.RealtekAudioControl"] = "RealtekSemiconductorCorp.RealtekAudioControl",
        ["DTSInc.DTSAudioProcessing"] = "DTSInc.DTSAudioProcessing"
    };

    internal static MsiUpdateDiagnosticSnapshotDocument Build(MsiUpdateDiagnosticSnapshotInput input)
    {
        var rowDiagnostics = input.MsiRows.ToDictionary(item => (item.Category, item.SourceIndex));
        var originalRows = input.Msi.Categories
            .SelectMany(category => category.Entries.Select((entry, index) => (category.Category, Entry: entry, Index: index)))
            .ToArray();
        var projectedMsiRows = originalRows.Select(row => ProjectMsiRow(row.Category, row.Index, row.Entry,
                input.Msi.Categories.First(category => category.Category == row.Category).State,
                rowDiagnostics.GetValueOrDefault((row.Category, row.Index))))
            .ToList();

        var pnp = ProjectPnp(input, out var pnpRowIds, out var pnpTotalCount);
        var software = ProjectSoftware(input, out var softwareTotalCount);
        var appx = ProjectAppx(input, out var appxTotalCount);
        var intelMatches = ProjectIntelMatches(input, pnpRowIds);
        var rowCap = Math.Min(projectedMsiRows.Count, MaximumMsiRows);
        var intelCap = Math.Min(intelMatches.Count, MaximumIntelMatches);
        var biosVersions = input.Inventory.BiosVersions.Select(SafeVersion).Where(value => value is not null)
            .Cast<string>().Take(16).ToList();
        var capturedMsiRows = projectedMsiRows.Take(MaximumMsiRows).ToList();
        var categoryStates = Enum.GetValues<MsiLiveUpdateCatalogCategory>().Select(category =>
        {
            var source = input.Msi.Categories.FirstOrDefault(item => item.Category == category);
            var capturedCount = capturedMsiRows.Count(row => row.SourceCategory == category.ToString());
            return new MsiUpdateDiagnosticCategory(category.ToString(), source?.State.ToString() ?? "Missing",
                SafeErrorKind(source?.ErrorKind), source?.Entries.Count ?? 0, capturedCount);
        }).ToList();
        var document = new MsiUpdateDiagnosticSnapshotDocument
        {
            CapturedAtUtc = input.CapturedAtUtc.ToUniversalTime(),
            AppVersion = SafeVersion(input.AppVersion),
            HardwareModelId = SafeSupportedModel(input.HardwareModelId),
            MsiRequestModelName = SafeCatalogName(input.Msi.ProductTitle),
            OperatingSystem = input.Msi.OperatingSystem is "win10" or "win11" ? input.Msi.OperatingSystem : null,
            CountryCode = IsCountryCode(input.Msi.CountryCode) ? input.Msi.CountryCode!.ToLowerInvariant() : null,
            Sources = new(input.Msi.State.ToString(), SafeErrorKind(input.Msi.ErrorKind), categoryStates,
                new(input.Intel.Succeeded, SafeErrorKind(input.Intel.ErrorKind), input.Intel.CatalogCreatedUtc,
                    intelMatches.Count, intelCap),
                input.Inventory.Warnings.Select(SafeErrorKind).Where(value => value is not null).Cast<string>()
                    .Distinct(StringComparer.Ordinal).Take(16).ToList()),
            Counts = new(originalRows.Length,
                input.MsiRows.Count(row => row.UiDisposition == "ExcludedByPolicy"),
                input.MsiRows.Count(row => row.UiDisposition == "Retained"),
                input.MsiRows.Count(row => row.UiDisposition == "SuppressedByIntelDsa"),
                input.Composed.Categories.SelectMany(category => category.Entries)
                    .Count(entry => entry.LatestVersionSource == FrontendMsiLiveUpdateVersionSource.IntelDsa)),
            Inventory = new(pnpTotalCount, Math.Min(pnp.Count, MaximumLocalPnpRows), pnp.Take(MaximumLocalPnpRows).ToList(),
                softwareTotalCount, Math.Min(software.Count, MaximumSoftwareRows), software.Take(MaximumSoftwareRows).ToList(),
                appxTotalCount, Math.Min(appx.Count, MaximumAppxRows), appx.Take(MaximumAppxRows).ToList(),
                biosVersions, input.Inventory.ControllerFirmware is { } firmware
                    ? new(SafeVersion(firmware.Version), firmware.Confidence.ToString()) : null),
            MsiRows = capturedMsiRows,
            IntelDsaMatches = intelMatches.Take(MaximumIntelMatches).ToList(),
            Truncation = new()
            {
                MsiRowsOmitted = projectedMsiRows.Count - rowCap,
                IntelMatchesOmitted = intelMatches.Count - intelCap,
                LocalPnpRowsOmitted = pnpTotalCount - Math.Min(pnp.Count, MaximumLocalPnpRows),
                SoftwareRowsOmitted = softwareTotalCount - Math.Min(software.Count, MaximumSoftwareRows),
                AppxRowsOmitted = appxTotalCount - Math.Min(appx.Count, MaximumAppxRows),
                BiosVersionsOmitted = Math.Max(0, input.Inventory.BiosVersions.Count - biosVersions.Count)
            }
        };

        document.Truncated = HasOmissions(document.Truncation);
        return document;
    }

    internal static byte[] SerializeWithinLimit(MsiUpdateDiagnosticSnapshotDocument document)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(document, JsonOptions);
        if (bytes.Length <= MaximumSnapshotBytes) return bytes;

        document.Truncated = true;
        var pnp = Trim(document.Inventory.LocalPnp, omitted => document.Truncation.LocalPnpRowsOmitted += omitted);
        document.Inventory = document.Inventory with
        {
            LocalPnp = pnp,
            LocalPnpCapturedCount = pnp.Count
        };
        bytes = JsonSerializer.SerializeToUtf8Bytes(document, JsonOptions);

        if (bytes.Length > MaximumSnapshotBytes)
        {
            var software = Trim(document.Inventory.Software, omitted => document.Truncation.SoftwareRowsOmitted += omitted);
            document.Inventory = document.Inventory with { Software = software, SoftwareCapturedCount = software.Count };
            bytes = JsonSerializer.SerializeToUtf8Bytes(document, JsonOptions);
        }

        if (bytes.Length > MaximumSnapshotBytes)
        {
            var appx = Trim(document.Inventory.AppxPackages, omitted => document.Truncation.AppxRowsOmitted += omitted);
            document.Inventory = document.Inventory with { AppxPackages = appx, AppxCapturedCount = appx.Count };
            bytes = JsonSerializer.SerializeToUtf8Bytes(document, JsonOptions);
        }

        if (bytes.Length > MaximumSnapshotBytes)
        {
            var intel = Trim(document.IntelDsaMatches, omitted => document.Truncation.IntelMatchesOmitted += omitted);
            document.IntelDsaMatches = intel;
            document.Sources = document.Sources with
            {
                IntelDsa = document.Sources.IntelDsa with { CapturedMatchCount = intel.Count }
            };
            bytes = JsonSerializer.SerializeToUtf8Bytes(document, JsonOptions);
        }

        while (bytes.Length > MaximumSnapshotBytes && document.MsiRows.Count > 1)
        {
            var before = document.MsiRows.Count;
            document.MsiRows = document.MsiRows.Take(Math.Max(1, before * 3 / 4)).ToList();
            document.Truncation.MsiRowsOmitted += before - document.MsiRows.Count;
            document.Sources = document.Sources with
            {
                Categories = document.Sources.Categories.Select(category => category with
                {
                    CapturedCount = document.MsiRows.Count(row => row.SourceCategory == category.Category)
                }).ToList()
            };
            bytes = JsonSerializer.SerializeToUtf8Bytes(document, JsonOptions);
        }

        if (bytes.Length > MaximumSnapshotBytes)
            throw new InvalidDataException("DiagnosticSnapshotSizeLimitExceeded");
        return bytes;
    }

    private static MsiUpdateDiagnosticCatalogRow ProjectMsiRow(MsiLiveUpdateCatalogCategory category, int sourceIndex,
        MsiLiveUpdateCatalogEntry entry, MsiLiveUpdateCatalogCategoryState categoryState,
        MsiUpdateCatalogRowDiagnostic? diagnostic)
    {
        var raw = entry.DiagnosticFields;
        var hardwareTypes = raw?.HardwareTypes ?? SafeHardwareTypes(entry.DeviceId);
        var deviceIdKind = raw?.DeviceIdKind ?? (entry.DeviceId is null ? "Missing" : "Valid");

        var mapped = diagnostic?.MappedEntry;
        var latestValue = SafeVersion(mapped?.Version);
        var latestUnavailableReason = latestValue is not null ? null
            : mapped?.Version is not null ? "RedactedUnsafeValue"
            : diagnostic?.UiDisposition switch
            {
                "ExcludedByPolicy" => "ExcludedByPolicy",
                "SuppressedByIntelDsa" => "SupersededByIntelDsa",
                _ when categoryState == MsiLiveUpdateCatalogCategoryState.Failed => "CategoryFailed",
                _ => LatestUnavailableReason(entry)
            };
        var installed = SafeVersion(mapped?.InstalledVersion);
        var installedUnavailableReason = installed is not null ? null
            : mapped?.InstalledVersion is not null ? "RedactedUnsafeValue"
            : diagnostic?.MatchOutcome ?? (diagnostic is null ? "NoComposerDiagnostic" : "NotEvaluatedByPolicy");
        var duplicate = diagnostic?.DuplicateOutcome is { } outcome
            ? new MsiUpdateDiagnosticDuplicate(outcome, diagnostic.DuplicateCandidateCount, diagnostic.DuplicateVersionCount)
            : null;

        return new(category.ToString(), sourceIndex, SafeCatalogName(entry.Name),
            new(SafeVersion(entry.Version), SafeVersion(entry.DriverVersion), SafeVersion(entry.PackageVersion),
                SafeReleaseDate(entry.ReleaseDate), SafeInfName(entry.InfName), deviceIdKind, hardwareTypes,
                raw?.GuidKind ?? (entry.Guid is null ? "Missing" : "Valid"), SafeGuid(raw?.SafeGuid ?? entry.Guid),
                raw?.UpgradeGuidKind ?? (entry.UpgradeGuid is null ? "Missing" : "Valid"), SafeGuid(raw?.SafeUpgradeGuid ?? entry.UpgradeGuid),
                raw?.CrcKind ?? "Missing", SafeVersion(raw?.Crc),
                raw?.TypeKind ?? "Missing", SafeOemScalar(raw?.Type),
                raw?.OtherKind ?? "Missing", SafeOemScalar(raw?.Other),
                raw?.FileKind ?? "Missing", raw?.FileExtension is { } extension ? SafeFileExtension(extension) : null,
                raw?.SiparamKind ?? "Missing"),
            new(latestValue, mapped?.LatestVersionSource.ToString(), mapped?.LatestVersionKind.ToString(), latestUnavailableReason),
            new(installed, mapped?.InstalledVersionConfidence?.ToString(), diagnostic?.MatchOutcome,
                diagnostic?.CandidateCount, diagnostic?.VersionCount, installedUnavailableReason),
            diagnostic?.UiDisposition ?? "Unknown", SafeErrorKind(diagnostic?.DispositionReason), duplicate);
    }

    private static string? LatestUnavailableReason(MsiLiveUpdateCatalogEntry entry)
    {
        var preferred = entry.DriverVersion ?? entry.PackageVersion ?? entry.Version;
        if (preferred is null) return "NoVersionFields";
        if (string.Equals(preferred, "UWP", StringComparison.OrdinalIgnoreCase)) return "UwpSentinel";
        return "NoUsableVersionValue";
    }

    private static List<MsiUpdateDiagnosticPnpRow> ProjectPnp(MsiUpdateDiagnosticSnapshotInput input,
        out Dictionary<string, int> rowIds, out int totalCount)
    {
        var allRows = input.Inventory.PnpDevices;
        var catalogEntries = input.Msi.Categories.SelectMany(category => category.Entries).ToArray();
        var catalogWords = catalogEntries.SelectMany(entry => Words(entry.Name).Concat(Words(entry.DiagnosticFields?.Type)))
            .Where(word => !GenericWords.Contains(word)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var catalogTypes = catalogEntries.Select(entry => NormalizeClass(entry.DiagnosticFields?.Type))
            .Where(value => value is not null).Cast<string>().ToHashSet(StringComparer.OrdinalIgnoreCase);
        var catalogHardwareTypes = catalogEntries.SelectMany(entry => SafeHardwareTypes(entry.DeviceId))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var intelDeviceIds = input.ResolvedIntelRows.Select(row => row.Device.DeviceId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        rowIds = new(StringComparer.OrdinalIgnoreCase);
        var selected = new List<(int RowId, MsiUpdateInstalledPnpDevice Device, List<string> HardwareTypes, string ProviderToken, string SelectionReason)>();

        for (var index = 0; index < allRows.Count; index++)
        {
            var device = allRows[index];
            var hardwareTypes = SafeHardwareTypes(device.DeviceId)
                .Concat(device.HardwareIds.SelectMany(SafeHardwareTypes))
                .Concat(device.CompatibleIds.SelectMany(SafeHardwareTypes))
                .Distinct(StringComparer.OrdinalIgnoreCase).Take(8).ToList();
            var identityMatch = hardwareTypes.Any(catalogHardwareTypes.Contains);
            var providerToken = ProviderEvidenceToken(device.DriverProvider, catalogWords);
            var classMatch = catalogTypes.Contains(NormalizeClass(device.ClassName) ?? string.Empty)
                || catalogTypes.Contains(NormalizeClass(device.WmiDeviceClass) ?? string.Empty);
            var resolvedDsaDevice = intelDeviceIds.Contains(device.DeviceId);
            if (!identityMatch && providerToken is null && !classMatch && !resolvedDsaDevice) continue;

            var reason = resolvedDsaDevice ? "ResolvedIntelDsaDevice"
                : identityMatch ? "CatalogHardwareIdentity"
                : providerToken is not null ? "CatalogProviderFamily"
                : "CatalogClassFamily";
            var rowId = index + 1;
            rowIds[device.DeviceId] = rowId;
            selected.Add((rowId, device, hardwareTypes, providerToken ?? SafeProvider(device.DriverProvider) ?? string.Empty, reason));
        }

        totalCount = selected.Count;
        return selected.Take(MaximumLocalPnpRows).Select(row => new MsiUpdateDiagnosticPnpRow(
            row.RowId,
            SafeClass(row.Device.ClassName),
            SafeClass(row.Device.WmiDeviceClass),
            row.ProviderToken.Length == 0 ? null : row.ProviderToken,
            SafeVersion(row.Device.DriverVersion),
            SafeInfName(row.Device.InfName),
            row.HardwareTypes,
            row.SelectionReason,
            string.IsNullOrWhiteSpace(row.Device.FriendlyName) && string.IsNullOrWhiteSpace(row.Device.WmiDeviceName)
                ? null : "PotentiallyUserAssigned"))
            .ToList();
    }

    private static List<MsiUpdateDiagnosticSoftwareRow> ProjectSoftware(MsiUpdateDiagnosticSnapshotInput input,
        out int totalCount)
    {
        var utilityNames = input.Msi.Categories
            .Where(category => category.Category == MsiLiveUpdateCatalogCategory.Utility)
            .SelectMany(category => category.Entries.Select((entry, index) => (entry, index)))
            .Where(row => input.MsiRows.FirstOrDefault(diagnostic => diagnostic.Category == MsiLiveUpdateCatalogCategory.Utility
                && diagnostic.SourceIndex == row.index)?.UiDisposition == "Retained")
            .Select(row => SafeCatalogName(row.entry.Name)).Where(value => value is not null).Cast<string>().ToArray();
        var selected = new List<MsiUpdateDiagnosticSoftwareRow>();
        foreach (var software in input.Inventory.Software)
        {
            var observedName = SafeCatalogName(software.DisplayName);
            if (observedName is null) continue;
            var matchedNames = utilityNames.Where(name => HasProductFamilyOverlap(name, observedName)).Take(4).ToList();
            if (matchedNames.Count == 0) continue;

            var exactTitle = matchedNames.FirstOrDefault(name => string.Equals(name, observedName, StringComparison.OrdinalIgnoreCase));
            var evidenceTokens = matchedNames.SelectMany(Words).Where(word => !GenericWords.Contains(word))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            selected.Add(new(exactTitle, ProviderEvidenceToken(software.Publisher, evidenceTokens),
                SafeVersion(software.DisplayVersion), SafeGuid(software.ProductCode), SafeGuid(software.UpgradeCode),
                software.RegistrySourceKind is "HKCU" or "HKLM32" or "HKLM64" ? software.RegistrySourceKind : null,
                matchedNames));
        }
        totalCount = selected.Count;
        return selected.Take(MaximumSoftwareRows).ToList();
    }

    private static List<MsiUpdateDiagnosticAppxRow> ProjectAppx(MsiUpdateDiagnosticSnapshotInput input,
        out int totalCount)
    {
        var catalogNames = input.Msi.Categories.SelectMany(category => category.Entries)
            .Select(entry => SafeCatalogName(entry.Name)).Where(value => value is not null).Cast<string>().ToArray();
        var selected = new List<MsiUpdateDiagnosticAppxRow>();
        foreach (var package in input.Inventory.Packages)
        {
            var rawName = SafePackageName(package.Name);
            var rawFamilyName = SafePackageName(package.FamilyName);
            if (rawName is null && rawFamilyName is null) continue;
            var packageText = string.Join(' ', new[] { rawName, rawFamilyName }.Where(value => value is not null));
            var matchedNames = catalogNames.Where(catalogName =>
                    HasProductFamilyOverlap(catalogName, packageText)
                    || IsCatalogPackageCandidate(catalogName, packageText))
                .Take(4).ToList();
            if (matchedNames.Count == 0) continue;

            var safeName = GetApprovedAppxPackageName(rawName);
            var safeFamilyName = SafeValidatedFamilyName(rawFamilyName, safeName);
            selected.Add(new(safeName, safeFamilyName, SafeVersion(package.Version), matchedNames,
                safeName is not null ? "Included" : rawName is null ? "MissingOrInvalid" : "RedactedUnrecognizedToken",
                safeFamilyName is not null ? "Included" : rawFamilyName is null ? "MissingOrInvalid" : "RedactedUnrecognizedToken"));
        }
        totalCount = selected.Count;
        return selected.Take(MaximumAppxRows).ToList();
    }

    private static string? GetApprovedAppxPackageName(string? packageName) =>
        packageName is not null && ApprovedAppxPackageNames.TryGetValue(packageName, out var approvedName)
            ? approvedName
            : null;

    private static string? SafeValidatedFamilyName(string? familyName, string? approvedPackageName)
    {
        if (familyName is null || approvedPackageName is null) return null;
        var prefix = approvedPackageName + "_";
        if (!familyName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return null;
        var publisherId = familyName[prefix.Length..];
        return Regex.IsMatch(publisherId, @"^[A-Za-z0-9]{8,20}$", RegexOptions.CultureInvariant) ? familyName : null;
    }

    private static bool IsCatalogPackageCandidate(string catalogName, string packageName)
    {
        var catalogTokens = Words(catalogName).Where(word => !GenericWords.Contains(word)).ToArray();
        if (catalogTokens.Length < 2) return false;
        var compactCatalogName = string.Concat(catalogTokens);
        var compactPackageName = string.Concat(Words(packageName));
        return compactPackageName.Contains(compactCatalogName, StringComparison.OrdinalIgnoreCase);
    }

    private static List<MsiUpdateDiagnosticIntelMatch> ProjectIntelMatches(MsiUpdateDiagnosticSnapshotInput input,
        IReadOnlyDictionary<string, int> pnpRowIds) => input.ResolvedIntelRows.Select(row =>
        {
            pnpRowIds.TryGetValue(row.Device.DeviceId, out var rowId);
            return new MsiUpdateDiagnosticIntelMatch(SafePackageName(row.PackageId), row.Family.ToString(),
                SafeVersion(row.LatestVersion), row.VersionKind.ToString(), SafeReleaseDate(row.ReleaseDate),
                SafeVersion(row.Device.DriverVersion), rowId == 0 ? null : rowId,
                SafeHardwareTypes(row.Device.DeviceId).Concat(row.Device.HardwareIds.SelectMany(SafeHardwareTypes))
                    .Concat(row.Device.CompatibleIds.SelectMany(SafeHardwareTypes))
                    .Distinct(StringComparer.OrdinalIgnoreCase).Take(8).ToList());
        }).ToList();

    internal static List<string> SafeHardwareTypes(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 512) return [];
        var components = value.Trim().Split('\\', 3);
        var bus = components.Length >= 2 && HardwareBuses.Contains(components[0]) ? components[0].ToUpperInvariant() : null;
        var identityPart = bus is not null ? components[1] : value.Trim();
        var tokens = identityPart.Split('&', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(token => token.ToUpperInvariant())
            .Where(token => HardwareTokenPattern.IsMatch(token))
            .Distinct(StringComparer.OrdinalIgnoreCase).Take(8).ToArray();
        if (tokens.Length == 0) return [];
        var identity = string.Join('&', tokens);
        return [bus is null ? identity : $"{bus}\\{identity}"];
    }

    private static string? SafeSupportedModel(string? value) => value is
        "msi.claw.a2vm.7" or "msi.claw.a2vm.8" or "msi.claw.cg3em" ? value : null;

    private static bool IsCountryCode(string? value) =>
        value is { Length: 2 } && value.All(character => character is >= 'A' and <= 'Z' or >= 'a' and <= 'z');

    private static string? SafeCatalogName(string? value) => SafeCatalogScalar(value, 128, allowSpace: true,
        allowedPunctuation: "._+-()&'");

    private static string? SafeCatalogScalar(string? value, int maximumLength, bool allowSpace,
        string allowedPunctuation = "._+-")
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > maximumLength) return null;
        var safe = value.Trim();
        return safe.Length > 0 && safe.All(character => char.IsLetterOrDigit(character)
            || allowSpace && character == ' ' || allowedPunctuation.Contains(character)) ? safe : null;
    }

    private static string? SafeVersion(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 128) return null;
        if (value.Contains("powershell", StringComparison.OrdinalIgnoreCase)
            || value.Contains("cmd.exe", StringComparison.OrdinalIgnoreCase)
            || value.Contains("msiexec", StringComparison.OrdinalIgnoreCase)
            || value.Contains("start-process", StringComparison.OrdinalIgnoreCase)
            || value.Contains("http", StringComparison.OrdinalIgnoreCase)) return null;
        return SafeCatalogScalar(value, 128, allowSpace: true);
    }

    private static string? SafeReleaseDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 128) return null;
        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed)
            ? parsed.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            : null;
    }

    private static string? SafeInfName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 512) return null;
        var separator = Math.Max(value.LastIndexOf('\\'), value.LastIndexOf('/'));
        var basename = separator >= 0 ? value[(separator + 1)..] : value;
        return basename.Length is > 0 and <= 96
            && basename.EndsWith(".inf", StringComparison.OrdinalIgnoreCase)
            && basename.All(character => char.IsLetterOrDigit(character) || character is '.' or '_' or '-')
            ? basename
            : null;
    }

    private static string? SafeFileExtension(string value) => value is ".zip" or ".cab" or ".msi" or ".exe" or ".appx" or ".msix" or ".7z" or ".rar" or ".inf" or ".sys" or ".bin"
        ? value : null;

    private static string? SafeGuid(string? value) => Guid.TryParse(value, out var guid) ? guid.ToString("D") : null;

    private static string? SafeErrorKind(string? value) => value is not null && value.Length <= 64
        && value.Length > 0 && value.All(character => char.IsLetterOrDigit(character) || character is '_' or '-')
        ? value : null;

    private static string? SafeClass(string? value) => value is not null && value.Length is > 0 and <= 64
        && value.All(character => char.IsLetterOrDigit(character) || character is '_' or '-' or '.')
        ? value : null;

    private static string? NormalizeClass(string? value) => SafeClass(value)?.ToUpperInvariant();

    private static string? SafePackageName(string? value) => value is not null && value.Length is > 0 and <= 128
        && value.All(character => char.IsLetterOrDigit(character) || character is '_' or '-' or '.')
        ? value : null;

    private static List<string> Words(string? value) => string.IsNullOrWhiteSpace(value)
        ? []
        : Regex.Split(value, "[^A-Za-z0-9]+", RegexOptions.CultureInvariant)
            .Where(token => token.Length is > 0 and <= 48).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    private static string? ProviderEvidenceToken(string? provider, IReadOnlySet<string> catalogWords) =>
        Words(provider).FirstOrDefault(word => !GenericWords.Contains(word) && catalogWords.Contains(word));

    private static string? SafeProvider(string? provider) =>
        Words(provider).FirstOrDefault(KnownProviderTokens.Contains);

    private static string? SafeOemScalar(string? value) =>
        SafeCatalogScalar(value, 64, allowSpace: false);

    private static bool HasProductFamilyOverlap(string catalogName, string installedName)
    {
        var catalogWords = Words(catalogName).Where(word => !GenericWords.Contains(word)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var installedWords = Words(installedName).Where(word => !GenericWords.Contains(word)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return catalogWords.Intersect(installedWords, StringComparer.OrdinalIgnoreCase).Count() >= 2;
    }

    private static bool HasOmissions(MsiUpdateDiagnosticTruncation truncation) =>
        truncation.MsiRowsOmitted + truncation.IntelMatchesOmitted + truncation.LocalPnpRowsOmitted
        + truncation.SoftwareRowsOmitted + truncation.AppxRowsOmitted + truncation.BiosVersionsOmitted > 0;

    private static List<T> Trim<T>(List<T> rows, Action<int> addOmitted)
    {
        var keep = rows.Count / 2;
        addOmitted(rows.Count - keep);
        return rows.Take(keep).ToList();
    }
}

internal sealed class MsiUpdateDiagnosticSnapshotStore
{
    private const int MaximumSnapshotBytes = 2 * 1024 * 1024;
    private const int MaximumRetainedFiles = 5;
    private static readonly Regex OwnedFileName = new(
        @"^msi-update-\d{8}-\d{6}\.\d{3}-[0-9a-f]{8}\.json$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };
    private readonly string _directory;

    internal MsiUpdateDiagnosticSnapshotStore(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        _directory = Path.GetFullPath(directory);
    }

    internal string DirectoryPath => _directory;

    internal async Task<MsiUpdateDiagnosticSnapshotWriteResult> WriteAsync(
        MsiUpdateDiagnosticSnapshotInput input, CancellationToken cancellationToken)
    {
        string? temporaryPath = null;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var document = MsiUpdateDiagnosticSnapshotBuilder.Build(input);
            cancellationToken.ThrowIfCancellationRequested();
            var bytes = MsiUpdateDiagnosticSnapshotBuilder.SerializeWithinLimit(document);
            cancellationToken.ThrowIfCancellationRequested();
            if (bytes.Length > MaximumSnapshotBytes)
                throw new InvalidDataException("DiagnosticSnapshotSizeLimitExceeded");

            Directory.CreateDirectory(_directory);
            var timestamp = DateTimeOffset.UtcNow;
            var suffix = Guid.NewGuid().ToString("N")[..8];
            var stem = $"msi-update-{timestamp:yyyyMMdd-HHmmss.fff}-{suffix}";
            var fileName = stem + ".json";
            var finalPath = Path.Combine(_directory, fileName);
            temporaryPath = Path.Combine(_directory, stem + ".tmp");

            await using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                64 * 1024, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporaryPath, finalPath);
            temporaryPath = null;

            string? retentionErrorKind = null;
            try { PruneOldSnapshots(); }
            catch (Exception exception) { retentionErrorKind = SafeExceptionKind(exception); }

            return new(true, fileName, RetentionErrorKind: retentionErrorKind,
                MsiRowCount: document.MsiRows.Count, LocalPnpRowCount: document.Inventory.LocalPnp.Count,
                IntelMatchCount: document.IntelDsaMatches.Count);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            DeleteTemporaryFile(temporaryPath);
            throw;
        }
        catch (Exception exception)
        {
            DeleteTemporaryFile(temporaryPath);
            return new(false, ErrorKind: SafeExceptionKind(exception));
        }
    }

    private void PruneOldSnapshots()
    {
        var files = Directory.EnumerateFiles(_directory, "msi-update-*.json", SearchOption.TopDirectoryOnly)
            .Where(path => OwnedFileName.IsMatch(Path.GetFileName(path)))
            .Select(path => (Path: path, LastWriteUtc: File.GetLastWriteTimeUtc(path)))
            .OrderByDescending(item => item.LastWriteUtc)
            .ThenByDescending(item => Path.GetFileName(item.Path), StringComparer.Ordinal)
            .ToArray();
        foreach (var item in files.Skip(MaximumRetainedFiles)) File.Delete(item.Path);
    }

    private static void DeleteTemporaryFile(string? path)
    {
        if (path is null) return;
        try { File.Delete(path); } catch { }
    }

    private static string SafeExceptionKind(Exception exception) =>
        new(exception.GetType().Name.Where(character => char.IsLetterOrDigit(character) || character is '_' or '-')
            .Take(64).ToArray());
}
