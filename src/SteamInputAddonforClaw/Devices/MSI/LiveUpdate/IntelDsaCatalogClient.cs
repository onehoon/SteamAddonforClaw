using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace SteamInputAddonforClaw.Devices.MSI.LiveUpdate;

internal enum IntelDsaComponentFamily { Graphics, Wireless, Bluetooth, Npu }

internal sealed record IntelDsaComponent(string Category, string? Version, IReadOnlyList<string> DetectionValues,
    IReadOnlyList<string> OperatingSystems);

internal sealed record IntelDsaPackage(string Id, string Name, string? Version, string? ReleaseDate, string? PackageUrl, bool? IsBeta,
    IReadOnlyList<string> OperatingSystems, IReadOnlyList<IntelDsaComponent> Components);

internal sealed record IntelDsaCatalogResult(bool Succeeded, IReadOnlyList<IntelDsaPackage> Packages,
    string? ErrorKind = null, DateTimeOffset? CatalogCreatedUtc = null)
{
    internal static IntelDsaCatalogResult Failed(string error) => new(false, [], error);
}

internal sealed class IntelDsaCatalogClient
{
    internal static readonly Uri CatalogUri = new("https://dsadata.intel.com/data/en");
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(20);
    private const int MaximumCompressedBytes = 4 * 1024 * 1024;
    private const int MaximumJsonEntryBytes = 8 * 1024 * 1024;
    private const int MaximumZipEntries = 256;
    private const int MaximumPackages = 512;
    private const int MaximumComponents = 2048;
    private const int MaximumDetectionValuesPerComponent = 1_024;
    private static readonly HttpClient SharedHttpClient = new(new SocketsHttpHandler
    {
        AutomaticDecompression = DecompressionMethods.None,
        ConnectTimeout = TimeSpan.FromSeconds(8)
    }) { Timeout = Timeout.InfiniteTimeSpan };

    private readonly HttpClient _httpClient;
    private readonly TimeSpan _requestTimeout;

    internal IntelDsaCatalogClient(HttpClient? httpClient = null, TimeSpan? requestTimeout = null)
    {
        _httpClient = httpClient ?? SharedHttpClient;
        _requestTimeout = requestTimeout ?? RequestTimeout;
    }

    internal async Task<IntelDsaCatalogResult> QueryAsync(CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_requestTimeout);
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, CatalogUri);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/zip"));
            request.Headers.UserAgent.ParseAdd("SteamInputAddonforClaw/1.0 (+https://github.com/onehoon/SteamAddonforClaw)");
            using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token)
                .ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                return IntelDsaCatalogResult.Failed($"Http{(int)response.StatusCode}");
            if (response.Content.Headers.ContentLength is long length && length > MaximumCompressedBytes)
                return IntelDsaCatalogResult.Failed("ArchiveTooLarge");

            await using var responseStream = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
            var bytes = await ReadBoundedAsync(responseStream, MaximumCompressedBytes, timeout.Token).ConfigureAwait(false);
            return ParseArchive(bytes);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            return IntelDsaCatalogResult.Failed("TimedOut");
        }
        catch (HttpRequestException exception)
        {
            return IntelDsaCatalogResult.Failed(SafeError(exception.GetType().Name));
        }
        catch (InvalidDataException)
        {
            return IntelDsaCatalogResult.Failed("InvalidArchive");
        }
        catch (JsonException)
        {
            return IntelDsaCatalogResult.Failed("InvalidJson");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return IntelDsaCatalogResult.Failed(SafeError(exception.GetType().Name));
        }
    }

    internal static IntelDsaCatalogResult ParseArchive(ReadOnlyMemory<byte> bytes)
    {
        try { return ParseArchiveCore(bytes); }
        catch (InvalidDataException) { return IntelDsaCatalogResult.Failed("InvalidArchive"); }
        catch (JsonException) { return IntelDsaCatalogResult.Failed("InvalidJson"); }
    }

    private static IntelDsaCatalogResult ParseArchiveCore(ReadOnlyMemory<byte> bytes)
    {
        if (bytes.Length == 0) return IntelDsaCatalogResult.Failed("EmptyArchive");
        if (bytes.Length > MaximumCompressedBytes) return IntelDsaCatalogResult.Failed("ArchiveTooLarge");

        using var stream = new MemoryStream(bytes.ToArray(), writable: false);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);
        if (archive.Entries.Count > MaximumZipEntries) return IntelDsaCatalogResult.Failed("TooManyArchiveEntries");
        var configurationEntry = FindUniqueEntry(archive, "software-configurations.json");
        if (configurationEntry is null) return IntelDsaCatalogResult.Failed("CatalogEntryMissing");
        var json = ReadEntry(configurationEntry);
        using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 32, CommentHandling = JsonCommentHandling.Disallow });
        if (document.RootElement.ValueKind != JsonValueKind.Array
            || document.RootElement.GetArrayLength() > MaximumPackages)
            return IntelDsaCatalogResult.Failed("InvalidPackageList");

        var packages = new List<IntelDsaPackage>();
        var componentCount = 0;
        foreach (var packageElement in document.RootElement.EnumerateArray())
        {
            if (packageElement.ValueKind != JsonValueKind.Object) continue;
            var id = ReadBoundedScalar(packageElement, "Id", 128) ?? string.Empty;
            var name = ReadBoundedString(packageElement, "Name", 512);
            var version = ReadBoundedString(packageElement, "Version", 128);
            var releaseDate = ReadBoundedString(packageElement, "DisplayReleaseDate", 128);
            var packageUrl = ReadBoundedString(packageElement, "Url", 1024);
            if (!Uri.TryCreate(packageUrl, UriKind.Absolute, out var parsedUrl)
                || parsedUrl.Scheme != Uri.UriSchemeHttps)
                packageUrl = null;
            var isBeta = ReadOptionalBoolean(packageElement, "IsBeta");
            var operatingSystems = ReadOperatingSystems(packageElement);
            var components = new List<IntelDsaComponent>();
            if (TryGetProperty(packageElement, "Components", out var componentsElement)
                && componentsElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var componentElement in componentsElement.EnumerateArray())
                {
                    if (++componentCount > MaximumComponents) return IntelDsaCatalogResult.Failed("TooManyComponents");
                    if (componentElement.ValueKind != JsonValueKind.Object) continue;
                    var category = ReadBoundedString(componentElement, "Category", 64);
                    if (string.IsNullOrWhiteSpace(category)) continue;
                    var componentVersion = ReadBoundedString(componentElement, "Version", 128);
                    var detectionValues = ReadBoundedStringArray(componentElement, "DetectionValues",
                        MaximumDetectionValuesPerComponent, 512);
                    components.Add(new(category, componentVersion, detectionValues, ReadOperatingSystems(componentElement)));
                }
            }

            if (id.Length > 0 && name is not null && components.Count > 0)
                packages.Add(new(id, name, version, releaseDate, packageUrl, isBeta, operatingSystems, components));
        }

        DateTimeOffset? created = null;
        var metaEntry = FindUniqueEntry(archive, "meta.json");
        if (metaEntry is not null)
        {
            try
            {
                using var meta = JsonDocument.Parse(ReadEntry(metaEntry), new JsonDocumentOptions { MaxDepth = 8 });
                var createdText = ReadBoundedString(meta.RootElement, "Created", 64);
                if (DateTimeOffset.TryParse(createdText, System.Globalization.CultureInfo.InvariantCulture,
                        System.Globalization.DateTimeStyles.AssumeUniversal, out var parsed))
                    created = parsed.ToUniversalTime();
            }
            catch (JsonException)
            {
                // meta.json is optional and does not gate a valid software catalog.
            }
        }

        return new(true, packages, CatalogCreatedUtc: created);
    }

    private static ZipArchiveEntry? FindUniqueEntry(ZipArchive archive, string expectedName)
    {
        var matches = archive.Entries.Where(entry => string.Equals(entry.FullName, expectedName, StringComparison.Ordinal)).ToArray();
        if (matches.Length > 1) throw new InvalidDataException("Duplicate catalog JSON entries.");
        return matches.SingleOrDefault();
    }

    private static byte[] ReadEntry(ZipArchiveEntry entry)
    {
        if (entry.Length < 0 || entry.Length > MaximumJsonEntryBytes)
            throw new InvalidDataException("Catalog JSON entry exceeds its bound.");
        using var source = entry.Open();
        return ReadBoundedAsync(source, MaximumJsonEntryBytes, CancellationToken.None).GetAwaiter().GetResult();
    }

    private static async Task<byte[]> ReadBoundedAsync(Stream stream, int maximumBytes, CancellationToken cancellationToken)
    {
        using var output = new MemoryStream(Math.Min(maximumBytes, 64 * 1024));
        var buffer = new byte[16 * 1024];
        while (true)
        {
            var read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read == 0) break;
            if (output.Length + read > maximumBytes) throw new InvalidDataException("Response exceeded its byte limit.");
            output.Write(buffer, 0, read);
        }
        return output.ToArray();
    }

    private static string? ReadBoundedString(JsonElement element, string name, int maximumLength)
    {
        if (!TryGetProperty(element, name, out var value) || value.ValueKind != JsonValueKind.String) return null;
        var text = value.GetString();
        if (text is null || text.Length > maximumLength) return null;
        var sanitized = new string(text.Where(static character => !char.IsControl(character)).ToArray()).Trim();
        return sanitized.Length == 0 ? null : sanitized;
    }

    private static string? ReadBoundedScalar(JsonElement element, string name, int maximumLength)
    {
        if (!TryGetProperty(element, name, out var value)) return null;
        var text = value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.GetRawText(),
            _ => null
        };
        if (text is null || text.Length > maximumLength || text.Any(char.IsControl)) return null;
        var trimmed = text.Trim();
        return trimmed.Length == 0 ? null : trimmed;
    }

    private static bool? ReadOptionalBoolean(JsonElement element, string name)
    {
        if (!TryGetProperty(element, name, out var value)) return null;
        if (value.ValueKind == JsonValueKind.True) return true;
        if (value.ValueKind == JsonValueKind.False) return false;
        return value.ValueKind == JsonValueKind.String && bool.TryParse(value.GetString(), out var parsed) ? parsed : null;
    }

    private static IReadOnlyList<string> ReadBoundedStringArray(JsonElement element, string name, int maximumCount, int maximumLength)
    {
        if (!TryGetProperty(element, name, out var value) || value.ValueKind != JsonValueKind.Array) return [];
        if (value.GetArrayLength() > maximumCount) return [];
        var items = new List<string>(value.GetArrayLength());
        foreach (var item in value.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String) continue;
            var text = item.GetString()?.Trim();
            if (!string.IsNullOrEmpty(text) && text.Length <= maximumLength && !text.Any(char.IsControl)) items.Add(text);
        }
        return items;
    }

    private static IReadOnlyList<string> ReadOperatingSystems(JsonElement element)
    {
        var values = new List<string>();
        foreach (var propertyName in new[] { "SupportedOperatingSystems", "OperatingSystems", "OperatingSystem" })
        {
            if (TryGetProperty(element, propertyName, out var value)) AddOperatingSystemValues(value, values);
        }

        if (TryGetProperty(element, "OperatingSystemOverrides", out var overrides))
        {
            if (overrides.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in overrides.EnumerateArray().Take(64))
                {
                    if (item.ValueKind != JsonValueKind.Object) continue;
                    foreach (var propertyName in new[] { "SupportedOperatingSystems", "OperatingSystems", "OperatingSystem", "OS" })
                        if (TryGetProperty(item, propertyName, out var value)) AddOperatingSystemValues(value, values);
                }
            }
            else if (overrides.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in overrides.EnumerateObject().Take(64))
                {
                    if (IsExplicitWindowsVersionLabel(property.Name))
                        AddOperatingSystemValues(property.Name, values);
                    else if (property.Value.ValueKind == JsonValueKind.Object)
                        foreach (var propertyName in new[] { "SupportedOperatingSystems", "OperatingSystems", "OperatingSystem", "OS" })
                            if (TryGetProperty(property.Value, propertyName, out var value)) AddOperatingSystemValues(value, values);
                }
            }
        }
        return values.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static void AddOperatingSystemValues(JsonElement value, List<string> values)
    {
        if (value.ValueKind == JsonValueKind.String)
        {
            AddOperatingSystemValue(value.GetString(), values);
            return;
        }
        if (value.ValueKind != JsonValueKind.Array) return;
        foreach (var item in value.EnumerateArray().Take(64))
            if (item.ValueKind == JsonValueKind.String) AddOperatingSystemValue(item.GetString(), values);
    }

    private static void AddOperatingSystemValues(string value, List<string> values) =>
        AddOperatingSystemValue(value, values);

    private static void AddOperatingSystemValue(string? value, List<string> values)
    {
        if (values.Count >= 64 || string.IsNullOrWhiteSpace(value) || value.Length > 128 || value.Any(char.IsControl)) return;
        values.Add(value.Trim());
    }

    private static bool IsExplicitWindowsVersionLabel(string value)
    {
        var normalized = value.Replace(" ", string.Empty, StringComparison.Ordinal).ToLowerInvariant();
        return normalized.Contains("windows10", StringComparison.Ordinal)
            || normalized.Contains("win10", StringComparison.Ordinal)
            || normalized.Contains("windows11", StringComparison.Ordinal)
            || normalized.Contains("win11", StringComparison.Ordinal);
    }

    private static bool TryGetProperty(JsonElement element, string name, out JsonElement value)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    value = property.Value;
                    return true;
                }
            }
        }
        value = default;
        return false;
    }

    private static string SafeError(string value)
    {
        var safe = new string(value.Where(static character => char.IsLetterOrDigit(character) || character is '_' or '-').Take(64).ToArray());
        return safe.Length == 0 ? "CatalogUnavailable" : safe;
    }
}
