using System.Diagnostics;
using System.Globalization;
using System.Management;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using SteamInputAddonforClaw.Contracts.Frontend;

namespace SteamInputAddonforClaw.Devices.MSI.LiveUpdate;

internal enum MsiLiveUpdateCatalogState { Completed, PartialFailure, Unavailable, Failed }
internal enum MsiLiveUpdateCatalogCategory { Bios, Firmware, Driver, Utility }
internal enum MsiLiveUpdateCatalogCategoryState { Success, Empty, Failed }

internal sealed record MsiLiveUpdateCatalogRawDiagnosticFields(
    string DeviceIdKind,
    List<string> HardwareTypes,
    string GuidKind,
    string? SafeGuid,
    string UpgradeGuidKind,
    string? SafeUpgradeGuid,
    string CrcKind,
    string TypeKind,
    string? Type,
    string OtherKind,
    string? Other,
    string FileKind,
    string? FileExtension,
    string SiparamKind,
    string? Crc);

internal sealed record MsiLiveUpdateCatalogEntry(
    string Name,
    string? Version,
    string? ReleaseDate,
    string? Description,
    string? DeviceId = null,
    string? Guid = null,
    string? UpgradeGuid = null,
    string? PackageVersion = null,
    string? DriverVersion = null,
    string? InfName = null,
    MsiLiveUpdateCatalogRawDiagnosticFields? DiagnosticFields = null);
internal sealed record MsiLiveUpdateCatalogCategoryResult(
    MsiLiveUpdateCatalogCategory Category,
    MsiLiveUpdateCatalogCategoryState State,
    IReadOnlyList<MsiLiveUpdateCatalogEntry> Entries,
    string? ErrorKind = null);
internal sealed record MsiLiveUpdateCatalogQueryResult(
    MsiLiveUpdateCatalogState State,
    string? ProductTitle,
    string? CountryCode,
    string? OperatingSystem,
    IReadOnlyList<MsiLiveUpdateCatalogCategoryResult> Categories,
    DateTimeOffset? CheckedAtUtc,
    string? ErrorKind = null,
    string? HelperTokenElevation = null,
    string? ResolverDiagnostics = null);
internal sealed record MsiLiveUpdateCatalogRequest(string ProductTitle, string CountryCode, string OperatingSystem);

internal static class MsiLiveUpdateCatalogDevicePolicy
{
    internal static bool IsSupported(FrontendHardwareStatus status, string? family, string? model) =>
        status == FrontendHardwareStatus.Supported
        && string.Equals(family, "msi.claw", StringComparison.OrdinalIgnoreCase)
        && model is "msi.claw.a2vm.7" or "msi.claw.a2vm.8" or "msi.claw.cg3em";
}

internal static class MsiLiveUpdateCatalogRequestIdentity
{
    internal static bool TryCreate(string? wmiProductName, string? cultureName, int windowsBuild,
        out MsiLiveUpdateCatalogRequest? request, out string errorKind)
    {
        request = null;
        var productTitle = NormalizeProductTitle(wmiProductName);
        if (productTitle is null)
        {
            errorKind = "ProductTitleUnavailable";
            return false;
        }

        string country;
        try
        {
            var culture = CultureInfo.GetCultureInfo(cultureName ?? string.Empty);
            country = new RegionInfo(culture.Name).TwoLetterISORegionName.ToLowerInvariant();
            if (country.Length != 2 || !country.All(char.IsLetter)) throw new CultureNotFoundException();
        }
        catch (CultureNotFoundException)
        {
            errorKind = "CountryCodeUnavailable";
            return false;
        }
        catch (ArgumentException)
        {
            errorKind = "CountryCodeUnavailable";
            return false;
        }

        request = new MsiLiveUpdateCatalogRequest(productTitle, country, windowsBuild >= 22000 ? "win11" : "win10");
        errorKind = string.Empty;
        return true;
    }

    internal static string? NormalizeProductTitle(string? wmiProductName)
    {
        var value = wmiProductName?.Trim();
        if (string.IsNullOrWhiteSpace(value)) return null;
        var slash = value.IndexOfAny(['/', '\\']);
        if (slash >= 0) value = value[..slash].Trim();
        return string.IsNullOrWhiteSpace(value) || value.Length > 256 ? null : value;
    }
}

internal sealed class MsiLiveUpdateCatalogClient
{
    private static readonly TimeSpan HelperTimeout = TimeSpan.FromSeconds(125);
    private const int MaximumHelperOutputCharacters = 12 * 1024 * 1024;
    private readonly Func<string?> _productNameSource;
    private readonly Func<string?> _cultureNameSource;
    private readonly Func<int> _windowsBuildSource;
    private readonly Func<string?> _helperPathSource;
    private readonly Func<MsiLiveUpdateCatalogRequest, CancellationToken, Task<MsiLiveUpdateCatalogQueryResult>> _query;

    internal MsiLiveUpdateCatalogClient(
        Func<string?>? productNameSource = null,
        Func<string?>? cultureNameSource = null,
        Func<int>? windowsBuildSource = null,
        Func<string?>? helperPathSource = null,
        Func<MsiLiveUpdateCatalogRequest, CancellationToken, Task<MsiLiveUpdateCatalogQueryResult>>? query = null)
    {
        _productNameSource = productNameSource ?? ReadWmiProductName;
        _cultureNameSource = cultureNameSource ?? (() => CultureInfo.CurrentCulture.Name);
        _windowsBuildSource = windowsBuildSource ?? (() => Environment.OSVersion.Version.Build);
        _helperPathSource = helperPathSource ?? (() => Path.Combine(AppContext.BaseDirectory, "SteamInputAddonforClaw.MsiLiveUpdateHelper.exe"));
        _query = query ?? InvokeHelperAsync;
    }

    internal async Task<MsiLiveUpdateCatalogQueryResult> QueryAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        MsiLiveUpdateCatalogRequest? request;
        string errorKind;
        try
        {
            if (!MsiLiveUpdateCatalogRequestIdentity.TryCreate(
                    _productNameSource(), _cultureNameSource(), _windowsBuildSource(), out request, out errorKind))
                return Unavailable(errorKind);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return Unavailable("DeviceIdentityReadFailed");
        }

        try
        {
            return await _query(request!, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            return new(MsiLiveUpdateCatalogState.Failed, request!.ProductTitle, request.CountryCode,
                request.OperatingSystem, [], DateTimeOffset.UtcNow, SafeErrorKind(exception.GetType().Name));
        }
    }

    internal static string? ReadWmiProductName()
    {
        if (!OperatingSystem.IsWindows()) return null;
        using var searcher = new ManagementObjectSearcher("SELECT Name FROM Win32_ComputerSystemProduct");
        using var results = searcher.Get();
        var product = results.Cast<ManagementObject>().FirstOrDefault();
        return product?["Name"]?.ToString();
    }

    private async Task<MsiLiveUpdateCatalogQueryResult> InvokeHelperAsync(
        MsiLiveUpdateCatalogRequest request, CancellationToken cancellationToken)
    {
        var helperPath = _helperPathSource();
        if (string.IsNullOrWhiteSpace(helperPath) || !File.Exists(helperPath)) return Unavailable("HelperUnavailable", request);

        var startInfo = new ProcessStartInfo
        {
            FileName = helperPath,
            WorkingDirectory = AppContext.BaseDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        startInfo.ArgumentList.Add(request.ProductTitle);
        startInfo.ArgumentList.Add(request.CountryCode);
        startInfo.ArgumentList.Add(request.OperatingSystem);

        return await RunHelperProcessAsync(startInfo, request, HelperTimeout, cancellationToken).ConfigureAwait(false);
    }

    internal static async Task<MsiLiveUpdateCatalogQueryResult> RunHelperProcessAsync(
        ProcessStartInfo startInfo,
        MsiLiveUpdateCatalogRequest request,
        TimeSpan timeoutDuration,
        CancellationToken cancellationToken)
    {
        using var process = new Process { StartInfo = startInfo };
        try
        {
            if (!process.Start()) return Failed("HelperLaunchFailed", request);
        }
        catch (Exception exception)
        {
            return Failed(SafeErrorKind(exception.GetType().Name), request);
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(timeoutDuration);
        var stdout = ReadBoundedAsync(process.StandardOutput, MaximumHelperOutputCharacters, timeout.Token);
        var stderr = DrainBoundedAsync(process.StandardError, timeout.Token);
        try
        {
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            var output = await stdout.ConfigureAwait(false);
            await stderr.ConfigureAwait(false);
            if (process.ExitCode != 0) return FailedForHelperExit(process.ExitCode, request);
            return MsiLiveUpdateCatalogResultParser.ParseHelperOutput(output, request, DateTimeOffset.UtcNow);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            KillHelper(process);
            await IgnoreReaderCompletionAsync(stdout, stderr).ConfigureAwait(false);
            throw;
        }
        catch (OperationCanceledException)
        {
            KillHelper(process);
            await IgnoreReaderCompletionAsync(stdout, stderr).ConfigureAwait(false);
            return Failed("HelperTimedOut", request);
        }
        catch (Exception exception)
        {
            KillHelper(process);
            await IgnoreReaderCompletionAsync(stdout, stderr).ConfigureAwait(false);
            return Failed(SafeErrorKind(exception.GetType().Name), request);
        }
    }

    private static async Task IgnoreReaderCompletionAsync(Task stdout, Task stderr)
    {
        try { await Task.WhenAll(stdout, stderr).ConfigureAwait(false); }
        catch { }
    }

    private static async Task<string> ReadBoundedAsync(StreamReader reader, int maxCharacters, CancellationToken cancellationToken)
    {
        var buffer = new char[8192];
        var result = new StringBuilder(Math.Min(maxCharacters, 8192));
        var exceededLimit = false;
        while (true)
        {
            var count = await reader.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false);
            if (count == 0)
            {
                if (exceededLimit) throw new InvalidDataException("HelperOutputTooLarge");
                return result.ToString();
            }
            if (result.Length + count > maxCharacters)
            {
                exceededLimit = true;
                continue;
            }
            if (!exceededLimit) result.Append(buffer, 0, count);
        }
    }

    private static async Task DrainBoundedAsync(StreamReader reader, CancellationToken cancellationToken)
    {
        var buffer = new char[2048];
        var total = 0;
        var exceededLimit = false;
        while (true)
        {
            var count = await reader.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false);
            if (count == 0)
            {
                if (exceededLimit) throw new InvalidDataException("HelperDiagnosticOutputTooLarge");
                return;
            }
            total += count;
            exceededLimit |= total > 32 * 1024;
        }
    }

    private static void KillHelper(Process process)
    {
        try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
        catch { }
    }

    private static MsiLiveUpdateCatalogQueryResult Unavailable(string errorKind, MsiLiveUpdateCatalogRequest? request = null) =>
        new(MsiLiveUpdateCatalogState.Unavailable, request?.ProductTitle, request?.CountryCode,
            request?.OperatingSystem, [], null, SafeErrorKind(errorKind));

    private static MsiLiveUpdateCatalogQueryResult Failed(string errorKind, MsiLiveUpdateCatalogRequest request) =>
        new(MsiLiveUpdateCatalogState.Failed, request.ProductTitle, request.CountryCode,
            request.OperatingSystem, [], null, SafeErrorKind(errorKind));

    private static string SafeErrorKind(string value)
    {
        var safe = new string(value.Where(static character => char.IsLetterOrDigit(character) || character is '_' or '-').Take(64).ToArray());
        return safe.Length == 0 ? "RequestFailed" : safe;
    }

    internal static MsiLiveUpdateCatalogQueryResult FailedForHelperExit(int exitCode, MsiLiveUpdateCatalogRequest request)
    {
        if (exitCode == 0) throw new ArgumentOutOfRangeException(nameof(exitCode));
        return Failed("HelperProcessFailed", request);
    }
}

internal static class MsiLiveUpdateCatalogResultParser
{
    private const int MaximumCategoryEntries = 500;
    private const int MaximumResponseCharacters = 1024 * 1024;
    private static readonly (MsiLiveUpdateCatalogCategory Category, string WireName)[] Categories =
    [
        (MsiLiveUpdateCatalogCategory.Bios, "bios"),
        (MsiLiveUpdateCatalogCategory.Firmware, "firmware"),
        (MsiLiveUpdateCatalogCategory.Driver, "driver"),
        (MsiLiveUpdateCatalogCategory.Utility, "utility")
    ];
    private static readonly HashSet<string> DiagnosticHardwareBuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "PCI", "USB", "USBSTOR", "ACPI", "HDAUDIO", "INTELAUDIO", "BTHENUM", "ROOT", "SWD",
        "SCSI", "SD", "I2C", "GPIO", "DISPLAY", "HID", "HIDCLASS", "SYSTEM", "PROCESSOR",
        "MEDIA", "NET", "SWC", "UCM", "USB4", "NVME", "VMBUS", "VPCI"
    };

    internal static MsiLiveUpdateCatalogQueryResult ParseHelperOutput(
        string output, MsiLiveUpdateCatalogRequest request, DateTimeOffset checkedAtUtc)
    {
        try
        {
            using var document = JsonDocument.Parse(output, new JsonDocumentOptions { MaxDepth = 24 });
            var root = document.RootElement;
            string? helperTokenElevation = null;
            string? resolverDiagnostics = null;
            if (!TryGetString(root, "status", out var status)) return Failed("MalformedHelperResponse");
            var errorKind = TryGetString(root, "errorKind", out var error) ? SafeErrorKind(error) : null;
            helperTokenElevation = TryGetString(root, "helperTokenElevation", out var elevation)
                && (elevation is "Administrator" or "NotElevated" or "Unknown") ? elevation : null;
            resolverDiagnostics = TryGetString(root, "resolverDiagnostics", out var diagnostics)
                ? SafeResolverDiagnostics(diagnostics) : null;
            if (status == "Unavailable") return Unavailable(errorKind ?? "OemDllUnavailable");
            if (status != "Completed" || !TryGetProperty(root, "categories", out var categoriesElement)
                || categoriesElement.ValueKind != JsonValueKind.Array)
                return Failed(errorKind ?? "MalformedHelperResponse");

            var responses = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
            foreach (var categoryElement in categoriesElement.EnumerateArray())
            {
                if (!TryGetString(categoryElement, "category", out var category)
                    || !Categories.Any(candidate => string.Equals(candidate.WireName, category, StringComparison.OrdinalIgnoreCase))
                    || !TryGetProperty(categoryElement, "transportSucceeded", out var transport)
                    || transport.ValueKind is not (JsonValueKind.True or JsonValueKind.False)
                    || !responses.TryAdd(category, categoryElement))
                    return Failed("MalformedHelperResponse");
            }

            var mapped = new List<MsiLiveUpdateCatalogCategoryResult>(Categories.Length);
            foreach (var (category, wireName) in Categories)
            {
                if (!responses.TryGetValue(wireName, out var response))
                {
                    mapped.Add(FailedCategory(category, "CategoryMissing"));
                    continue;
                }

                if (!TryGetProperty(response, "transportSucceeded", out var transport)
                    || transport.ValueKind != JsonValueKind.True
                    || !TryGetString(response, "responseJson", out var json))
                {
                    var categoryError = TryGetString(response, "errorKind", out var kind) ? SafeErrorKind(kind) : "TransportFailed";
                    mapped.Add(FailedCategory(category, categoryError));
                    continue;
                }

                mapped.Add(ParseCategory(category, json));
            }

            var failedCount = mapped.Count(static item => item.State == MsiLiveUpdateCatalogCategoryState.Failed);
            var state = failedCount == 0
                ? MsiLiveUpdateCatalogState.Completed
                : failedCount == mapped.Count ? MsiLiveUpdateCatalogState.Failed : MsiLiveUpdateCatalogState.PartialFailure;
            return new(state, request.ProductTitle, request.CountryCode, request.OperatingSystem, mapped, checkedAtUtc,
                HelperTokenElevation: helperTokenElevation, ResolverDiagnostics: resolverDiagnostics);

            MsiLiveUpdateCatalogQueryResult Failed(string reason) =>
                new(MsiLiveUpdateCatalogState.Failed, request.ProductTitle, request.CountryCode, request.OperatingSystem,
                    [], checkedAtUtc, reason, helperTokenElevation, resolverDiagnostics);
            MsiLiveUpdateCatalogQueryResult Unavailable(string reason) =>
                new(MsiLiveUpdateCatalogState.Unavailable, request.ProductTitle, request.CountryCode, request.OperatingSystem,
                    [], null, reason, helperTokenElevation, resolverDiagnostics);
        }
        catch (JsonException)
        {
            return new(MsiLiveUpdateCatalogState.Failed, request.ProductTitle, request.CountryCode,
                request.OperatingSystem, [], checkedAtUtc, "MalformedHelperResponse");
        }
    }

    private static MsiLiveUpdateCatalogCategoryResult ParseCategory(MsiLiveUpdateCatalogCategory category, string json)
    {
        if (json.Length > MaximumResponseCharacters) return FailedCategory(category, "ResponseTooLarge");
        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 32 });
            var root = document.RootElement;
            if (root.ValueKind == JsonValueKind.False) return FailedCategory(category, "FalseSentinel");
            if (root.ValueKind == JsonValueKind.Object)
            {
                if (!TryGetProperty(root, "SoftwareRecord", out root)) return FailedCategory(category, "UnexpectedResponseShape");
                if (root.ValueKind == JsonValueKind.Object && TryGetProperty(root, "$values", out var values)) root = values;
            }
            if (root.ValueKind != JsonValueKind.Array) return FailedCategory(category, "UnexpectedResponseShape");
            if (root.GetArrayLength() > MaximumCategoryEntries) return FailedCategory(category, "TooManyCatalogEntries");

            var entries = new List<MsiLiveUpdateCatalogEntry>(root.GetArrayLength());
            foreach (var element in root.EnumerateArray())
            {
                if (element.ValueKind != JsonValueKind.Object || !TryGetAnyString(element, ["name", "title"], 512, out var name))
                    return FailedCategory(category, "InvalidCatalogEntry");
                TryGetAnyString(element, ["displayVersion", "version"], 128, out var version);
                TryGetAnyString(element, ["releaseDate", "release_date"], 128, out var releaseDate);
                TryGetAnyString(element, ["description"], 2048, out var description);
                TryGetAnyString(element, ["deviceID"], 512, out var deviceId);
                TryGetAnyString(element, ["GUID"], 64, out var guid);
                TryGetAnyString(element, ["upgradeGuid"], 64, out var upgradeGuid);
                TryGetAnyString(element, ["packageVersion"], 128, out var packageVersion);
                TryGetAnyString(element, ["driverVersion"], 128, out var driverVersion);
                TryGetAnyString(element, ["infName"], 128, out var infName);
                entries.Add(new(name!, version, releaseDate, description,
                    ValidateDeviceId(deviceId), ValidateGuid(guid), ValidateGuid(upgradeGuid),
                    packageVersion, driverVersion, ValidateInfName(infName), ReadDiagnosticFields(element, deviceId)));
            }
            return entries.Count == 0
                ? new(category, MsiLiveUpdateCatalogCategoryState.Empty, entries)
                : new(category, MsiLiveUpdateCatalogCategoryState.Success, entries);
        }
        catch (JsonException)
        {
            return FailedCategory(category, "MalformedCatalogResponse");
        }
    }

    private static string? ValidateDeviceId(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 512 || !value.Contains('\\')) return null;
        return value.All(static character => char.IsLetterOrDigit(character)
            || character is '\\' or '&' or '_' or '*' or '-' or '.' or ':') ? value : null;
    }

    private static string? ValidateGuid(string? value) =>
        Guid.TryParse(value, out var parsed) ? parsed.ToString("D") : null;

    private static string? ValidateInfName(string? value) =>
        value is not null && value.Length <= 128
            && value.All(static character => char.IsLetterOrDigit(character) || character is '_' or '-' or '.')
            ? value
            : null;

    private static MsiLiveUpdateCatalogRawDiagnosticFields ReadDiagnosticFields(JsonElement element, string? deviceId)
    {
        var type = ReadSafeScalar(element, ["Type"], 64, allowSpace: false);
        var other = ReadAllowlistedOther(element);
        var file = ClassifyFile(element);
        var crc = ReadSafeCrc(element);
        return new(
            ClassifyIdentityField(element, ["deviceID"], value => ValidateDeviceId(value) is not null),
            ReadHardwareTypes(deviceId),
            ClassifyIdentityField(element, ["GUID"], static value => Guid.TryParse(value, out _)),
            ReadGuid(element, ["GUID"]),
            ClassifyIdentityField(element, ["upgradeGuid", "Upgrade_Guid"], static value => Guid.TryParse(value, out _)),
            ReadGuid(element, ["upgradeGuid", "Upgrade_Guid"]),
            ClassifyValueField(element, ["Crc"], crc),
            ClassifyValueField(element, ["Type"], type),
            type,
            ClassifyOpaqueScalarField(element, ["Other"], other),
            other,
            file.Kind,
            file.Extension,
            ClassifyCommandField(element, ["Siparam"]),
            crc);
    }

    private static string ClassifyIdentityField(JsonElement element, string[] names, Func<string, bool> isValid)
    {
        if (!TryGetRawField(element, names, out var raw)) return "Missing";
        if (raw.ValueKind == JsonValueKind.Null) return "Null";
        if (raw.ValueKind != JsonValueKind.String) return JsonKindName(raw.ValueKind);
        var value = raw.GetString()?.Trim();
        if (string.IsNullOrEmpty(value)) return "Empty";
        if (string.Equals(value, "UWP", StringComparison.OrdinalIgnoreCase)) return "UwpSentinel";
        if (string.Equals(value, "NOT_FOUND", StringComparison.OrdinalIgnoreCase)) return "NotFoundSentinel";
        if (isValid(value)) return "Valid";
        if (names[0].Equals("deviceID", StringComparison.OrdinalIgnoreCase)
            && value.IndexOfAny(['/', ':', '?', '#']) < 0
            && value.Split('&', StringSplitOptions.RemoveEmptyEntries).Any(token => token.StartsWith("VEN_", StringComparison.OrdinalIgnoreCase)
                || token.StartsWith("VID_", StringComparison.OrdinalIgnoreCase)
                || token.StartsWith("DEV_", StringComparison.OrdinalIgnoreCase)
                || token.StartsWith("PID_", StringComparison.OrdinalIgnoreCase))) return "Partial";
        return "Rejected";
    }

    private static string? ReadGuid(JsonElement element, string[] names)
    {
        if (!TryGetRawField(element, names, out var raw) || raw.ValueKind != JsonValueKind.String) return null;
        return Guid.TryParse(raw.GetString(), out var guid) ? guid.ToString("D") : null;
    }

    private static List<string> ReadHardwareTypes(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 512) return [];
        var parts = value.Trim().Split('\\', 3);
        var bus = parts.Length > 1 && DiagnosticHardwareBuses.Contains(parts[0]) ? parts[0].ToUpperInvariant() : null;
        var identity = parts.Length > 1 ? parts[1] : value.Trim();
        var tokens = identity.Split('&', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(token => token.ToUpperInvariant())
            .Where(token => Regex.IsMatch(token,
                @"^(?:VEN_[0-9A-F]{4}|VID_[0-9A-F]{4}|DEV_[0-9A-F]{4}|PID_[0-9A-F]{4}|SUBSYS_[0-9A-F]{8}|REV_[0-9A-F]{2,4}|MI_[0-9A-F]{2}|FUNC_[0-9A-F]{2}|CC_[0-9A-F]{6}|PNP[0-9A-F]{4}|INTC[0-9A-F]{4,8}|CID_[A-Z0-9]{4,12}|ACPI_[A-Z0-9]{3,8})$",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            .Distinct(StringComparer.OrdinalIgnoreCase).Take(8).ToArray();
        if (tokens.Length == 0) return [];
        var normalized = string.Join('&', tokens);
        return [bus is null ? normalized : $"{bus}\\{normalized}"];
    }

    private static string ClassifyValueField(JsonElement element, string[] names, string? safeValue)
    {
        if (!TryGetRawField(element, names, out var raw)) return "Missing";
        if (raw.ValueKind == JsonValueKind.Null) return "Null";
        if (raw.ValueKind != JsonValueKind.String && raw.ValueKind != JsonValueKind.Number)
            return JsonKindName(raw.ValueKind);
        var value = raw.ValueKind == JsonValueKind.String ? raw.GetString()?.Trim() : raw.GetRawText();
        if (string.IsNullOrEmpty(value)) return "Empty";
        if (string.Equals(value, "UWP", StringComparison.OrdinalIgnoreCase)) return "UwpSentinel";
        if (string.Equals(value, "NOT_FOUND", StringComparison.OrdinalIgnoreCase)) return "NotFoundSentinel";
        return safeValue is null ? "RedactedValue" : "SafeScalar";
    }

    private static string ClassifyOpaqueScalarField(JsonElement element, string[] names, string? safeValue)
    {
        if (!TryGetRawField(element, names, out var raw)) return "Missing";
        if (raw.ValueKind == JsonValueKind.Null) return "Null";
        if (raw.ValueKind != JsonValueKind.String && raw.ValueKind != JsonValueKind.Number)
            return JsonKindName(raw.ValueKind);
        var value = raw.ValueKind == JsonValueKind.String ? raw.GetString()?.Trim() : raw.GetRawText();
        if (string.IsNullOrEmpty(value)) return "Empty";
        if (string.Equals(value, "UWP", StringComparison.OrdinalIgnoreCase)) return "UwpSentinel";
        if (string.Equals(value, "NOT_FOUND", StringComparison.OrdinalIgnoreCase)) return "NotFoundSentinel";
        return safeValue is null ? "RedactedValue" : "SafeScalar";
    }

    private static (string Kind, string? Extension) ClassifyFile(JsonElement element)
    {
        if (!TryGetRawField(element, ["File"], out var raw)) return ("Missing", null);
        if (raw.ValueKind == JsonValueKind.Null) return ("Null", null);
        if (raw.ValueKind != JsonValueKind.String) return (JsonKindName(raw.ValueKind), null);
        var value = raw.GetString()?.Trim();
        if (string.IsNullOrEmpty(value)) return ("Empty", null);
        if (string.Equals(value, "UWP", StringComparison.OrdinalIgnoreCase)) return ("UwpSentinel", null);
        if (string.Equals(value, "NOT_FOUND", StringComparison.OrdinalIgnoreCase)) return ("NotFoundSentinel", null);

        if (Uri.TryCreate(value, UriKind.Absolute, out var uri))
        {
            var extension = SafeFileExtension(uri.IsFile ? uri.LocalPath : uri.AbsolutePath);
            return (uri.Scheme.Equals("ms-windows-store", StringComparison.OrdinalIgnoreCase) ? "StoreLink" : "Url", extension);
        }

        var hasPath = value.Contains('\\') || value.Contains('/') || value.Contains(':');
        var fileExtension = SafeFileExtension(value);
        if (fileExtension is not null) return (ClassifyFileExtension(fileExtension), fileExtension);
        return (hasPath ? "RedactedPath" : "RedactedString", null);
    }

    private static string ClassifyCommandField(JsonElement element, string[] names)
    {
        if (!TryGetRawField(element, names, out var raw)) return "Missing";
        if (raw.ValueKind == JsonValueKind.Null) return "Null";
        if (raw.ValueKind != JsonValueKind.String) return JsonKindName(raw.ValueKind);
        var value = raw.GetString()?.Trim();
        if (string.IsNullOrEmpty(value)) return "Empty";
        if (string.Equals(value, "UWP", StringComparison.OrdinalIgnoreCase)) return "UwpSentinel";
        if (string.Equals(value, "NOT_FOUND", StringComparison.OrdinalIgnoreCase)) return "NotFoundSentinel";
        if (value.Any(char.IsWhiteSpace) || value.IndexOfAny(['\\', '/', ':', '"', '\'']) >= 0
            || value.Contains(".exe", StringComparison.OrdinalIgnoreCase)
            || value.Contains(".msi", StringComparison.OrdinalIgnoreCase)
            || value.Contains("powershell", StringComparison.OrdinalIgnoreCase))
            return "RedactedCommand";
        return IsSafeScalar(value, 64, allowSpace: false) ? "SafeToken" : "RedactedString";
    }

    private static string? ReadSafeScalar(JsonElement element, string[] names, int maximumLength, bool allowSpace)
    {
        if (!TryGetRawField(element, names, out var raw)) return null;
        string? value = raw.ValueKind switch
        {
            JsonValueKind.String => raw.GetString(),
            JsonValueKind.Number => raw.GetRawText(),
            _ => null
        };
        value = value?.Trim();
        return value is not null && IsSafeScalar(value, maximumLength, allowSpace) ? value : null;
    }

    private static string? ReadAllowlistedOther(JsonElement element)
    {
        if (!TryGetRawField(element, ["Other"], out var raw)
            || raw.ValueKind is not (JsonValueKind.String or JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False))
            return null;
        var value = raw.ValueKind == JsonValueKind.String ? raw.GetString()?.Trim() : raw.GetRawText();
        if (string.IsNullOrEmpty(value)) return null;
        return value.ToUpperInvariant() is "UWP" or "NOT_FOUND" or "BIOS" or "PPKG" or "DISM" or "TRUE" or "FALSE" or "0" or "1"
            ? value.ToUpperInvariant()
            : null;
    }

    private static string? ReadSafeCrc(JsonElement element)
    {
        if (!TryGetRawField(element, ["Crc"], out var raw)
            || raw.ValueKind is not (JsonValueKind.String or JsonValueKind.Number)) return null;
        var value = raw.ValueKind == JsonValueKind.String ? raw.GetString()?.Trim() : raw.GetRawText();
        if (string.IsNullOrEmpty(value)) return null;
        if (value.Equals("UWP", StringComparison.OrdinalIgnoreCase)
            || value.Equals("NOT_FOUND", StringComparison.OrdinalIgnoreCase)) return value.ToUpperInvariant();
        return Regex.IsMatch(value, @"^(?:[0-9A-F]{1,16}|[0-9]{1,10}(?:\.[0-9]{1,10}){1,4})$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant) ? value : null;
    }

    private static bool IsSafeScalar(string value, int maximumLength, bool allowSpace) =>
        value.Length is > 0 && value.Length <= maximumLength
        && value.All(character => char.IsLetterOrDigit(character)
            || character is '.' or '_' or '+' or '-'
            || allowSpace && character == ' ');

    private static string? SafeFileExtension(string value)
    {
        var query = value.IndexOfAny(['?', '#']);
        if (query >= 0) value = value[..query];
        var dot = value.LastIndexOf('.');
        if (dot < 0) return null;
        var extension = value[dot..].ToLowerInvariant();
        return extension is ".zip" or ".cab" or ".msi" or ".exe" or ".appx" or ".msix" or ".7z" or ".rar" or ".inf" or ".sys" or ".bin"
            ? extension
            : null;
    }

    private static string ClassifyFileExtension(string extension) => extension switch
    {
        ".zip" or ".cab" or ".7z" or ".rar" => "Archive",
        ".msi" or ".appx" or ".msix" => "Installer",
        ".exe" => "Executable",
        ".inf" or ".sys" or ".bin" => "DriverPayload",
        _ => "OtherFile"
    };

    private static bool TryGetRawField(JsonElement element, string[] names, out JsonElement value)
    {
        foreach (var name in names)
            if (TryGetProperty(element, name, out value)) return true;
        value = default;
        return false;
    }

    private static string JsonKindName(JsonValueKind kind) => kind switch
    {
        JsonValueKind.Number => "Number",
        JsonValueKind.True or JsonValueKind.False => "Boolean",
        JsonValueKind.Array => "Array",
        JsonValueKind.Object => "Object",
        _ => "WrongType"
    };

    private static string SafeResolverDiagnostics(string value)
    {
        for (var index = 0; index + 2 < value.Length; index++)
        {
            if (char.IsLetter(value[index]) && value[index + 1] == ':' && (value[index + 2] is '\\' or '/'))
                return "RejectedUnsafePath";
        }
        if (value.StartsWith("\\\\", StringComparison.Ordinal)) return "RejectedUnsafePath";

        var safe = new string(value.Where(static character => char.IsLetterOrDigit(character)
            || character is ' ' or '.' or '_' or '-' or '/' or '\\' or ':' or '=' or ';' or '|').Take(1024).ToArray());
        return safe.Length == 0 ? "Unavailable" : safe;
    }

    private static bool TryGetAnyString(JsonElement element, string[] names, int maxLength, out string? value)
    {
        foreach (var name in names)
        {
            if (TryGetProperty(element, name, out var property) && TrySafeString(property, maxLength, out value))
                return true;
        }
        value = null;
        return false;
    }

    private static bool TrySafeString(JsonElement element, int maxLength, out string? value)
    {
        value = element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number => element.GetRawText(),
            _ => null
        };
        if (value is null) return false;
        value = new string(value.Where(static character => !char.IsControl(character) || character is '\r' or '\n' or '\t').ToArray()).Trim();
        if (value.Length > maxLength) return false;
        if (value.Length == 0) { value = null; return false; }
        return true;
    }

    private static bool TryGetString(JsonElement element, string propertyName, out string value)
    {
        if (TryGetProperty(element, propertyName, out var property) && property.ValueKind == JsonValueKind.String)
        {
            value = property.GetString() ?? string.Empty;
            return true;
        }
        value = string.Empty;
        return false;
    }

    private static bool TryGetProperty(JsonElement element, string name, out JsonElement property)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var candidate in element.EnumerateObject())
                if (string.Equals(candidate.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    property = candidate.Value;
                    return true;
                }
        }
        property = default;
        return false;
    }

    private static MsiLiveUpdateCatalogCategoryResult FailedCategory(MsiLiveUpdateCatalogCategory category, string errorKind) =>
        new(category, MsiLiveUpdateCatalogCategoryState.Failed, [], errorKind);

    private static MsiLiveUpdateCatalogQueryResult Failed(string errorKind, MsiLiveUpdateCatalogRequest request, DateTimeOffset checkedAt) =>
        new(MsiLiveUpdateCatalogState.Failed, request.ProductTitle, request.CountryCode, request.OperatingSystem, [], checkedAt, errorKind);

    private static string SafeErrorKind(string value) =>
        new(value.Where(static character => char.IsLetterOrDigit(character) || character is '_' or '-').Take(64).ToArray());
}

internal static class MsiLiveUpdateCatalogContractMapper
{
    internal static FrontendMsiLiveUpdateCatalogSnapshot Map(MsiLiveUpdateCatalogQueryResult result) => new(
        result.State switch
        {
            MsiLiveUpdateCatalogState.Completed => FrontendMsiLiveUpdateCatalogState.Completed,
            MsiLiveUpdateCatalogState.PartialFailure => FrontendMsiLiveUpdateCatalogState.PartialFailure,
            MsiLiveUpdateCatalogState.Unavailable => FrontendMsiLiveUpdateCatalogState.Unavailable,
            _ => FrontendMsiLiveUpdateCatalogState.Failed
        },
        result.ProductTitle,
        result.CountryCode,
        result.OperatingSystem,
        result.Categories.Select(category => new FrontendMsiLiveUpdateCatalogCategorySnapshot(
            category.Category switch
            {
                MsiLiveUpdateCatalogCategory.Bios => FrontendMsiLiveUpdateCatalogCategory.Bios,
                MsiLiveUpdateCatalogCategory.Firmware => FrontendMsiLiveUpdateCatalogCategory.Firmware,
                MsiLiveUpdateCatalogCategory.Driver => FrontendMsiLiveUpdateCatalogCategory.Driver,
                _ => FrontendMsiLiveUpdateCatalogCategory.Utility
            },
            category.State switch
            {
                MsiLiveUpdateCatalogCategoryState.Success => FrontendMsiLiveUpdateCatalogCategoryState.Success,
                MsiLiveUpdateCatalogCategoryState.Empty => FrontendMsiLiveUpdateCatalogCategoryState.Empty,
                _ => FrontendMsiLiveUpdateCatalogCategoryState.Failed
            },
            category.Entries.Select(entry => new FrontendMsiLiveUpdateCatalogEntry(
                entry.Name, entry.DriverVersion ?? entry.PackageVersion
                    ?? (string.Equals(entry.Version, "UWP", StringComparison.OrdinalIgnoreCase) ? null : entry.Version),
                entry.ReleaseDate, entry.Description,
                LatestVersionSource: FrontendMsiLiveUpdateVersionSource.Msi,
                LatestVersionKind: !string.IsNullOrWhiteSpace(entry.DriverVersion)
                    ? FrontendMsiLiveUpdateVersionKind.Driver
                    : !string.IsNullOrWhiteSpace(entry.PackageVersion)
                        ? FrontendMsiLiveUpdateVersionKind.Package
                        : FrontendMsiLiveUpdateVersionKind.Catalog)).ToArray(),
            category.ErrorKind)).ToArray(),
        result.CheckedAtUtc,
        result.ErrorKind);
}
