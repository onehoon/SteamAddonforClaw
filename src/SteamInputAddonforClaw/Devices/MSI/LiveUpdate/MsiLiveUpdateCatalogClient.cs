using System.Diagnostics;
using System.Globalization;
using System.Management;
using System.Text;
using System.Text.Json;
using SteamInputAddonforClaw.Contracts.Frontend;

namespace SteamInputAddonforClaw.Devices.MSI.LiveUpdate;

internal enum MsiLiveUpdateCatalogState { Completed, PartialFailure, Unavailable, Failed }
internal enum MsiLiveUpdateCatalogCategory { Bios, Firmware, Driver, Utility }
internal enum MsiLiveUpdateCatalogCategoryState { Success, Empty, Failed }

internal sealed record MsiLiveUpdateCatalogEntry(string Name, string? Version, string? ReleaseDate, string? Description);
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
    string? ErrorKind = null);
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

    internal static MsiLiveUpdateCatalogQueryResult ParseHelperOutput(
        string output, MsiLiveUpdateCatalogRequest request, DateTimeOffset checkedAtUtc)
    {
        try
        {
            using var document = JsonDocument.Parse(output, new JsonDocumentOptions { MaxDepth = 24 });
            var root = document.RootElement;
            if (!TryGetString(root, "status", out var status)) return Failed("MalformedHelperResponse");
            var errorKind = TryGetString(root, "errorKind", out var error) ? SafeErrorKind(error) : null;
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
            return new(state, request.ProductTitle, request.CountryCode, request.OperatingSystem, mapped, checkedAtUtc);

            MsiLiveUpdateCatalogQueryResult Failed(string reason) =>
                new(MsiLiveUpdateCatalogState.Failed, request.ProductTitle, request.CountryCode, request.OperatingSystem, [], checkedAtUtc, reason);
            MsiLiveUpdateCatalogQueryResult Unavailable(string reason) =>
                new(MsiLiveUpdateCatalogState.Unavailable, request.ProductTitle, request.CountryCode, request.OperatingSystem, [], null, reason);
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
                entries.Add(new(name!, version, releaseDate, description));
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
                entry.Name, entry.Version, entry.ReleaseDate, entry.Description)).ToArray(),
            category.ErrorKind)).ToArray(),
        result.CheckedAtUtc,
        result.ErrorKind);
}
