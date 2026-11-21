using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace SteamInputAddonforClaw.FrontendTransport.XboxArtwork;

public enum XboxArtworkKind
{
    CatalogCard,
    ProfileHero,
}

public sealed record XboxArtworkResult(
    string Path,
    string Market,
    string Purpose,
    int Width,
    int Height,
    bool CacheHit);

public sealed record XboxArtworkDiagnostic(
    string StoreId,
    string Market,
    string Purpose,
    string Outcome,
    double DurationMs,
    string? ErrorType = null);

/// <summary>Best-effort Microsoft Store artwork lookup and stable local cache shared by UI processes.</summary>
public sealed class XboxStoreArtworkResolver : IDisposable
{
    private const string CatalogHost = "displaycatalog.mp.microsoft.com";
    private const string ImageHost = "store-images.s-microsoft.com";
    private const int MaximumCatalogBytes = 4 * 1024 * 1024;
    private const int MaximumImageBytes = 24 * 1024 * 1024;
    private const int MaximumImageDimension = 8192;
    private const long MaximumImagePixels = 36_000_000;
    private static readonly TimeSpan CatalogCacheLifetime = TimeSpan.FromHours(24);
    private static readonly TimeSpan FailedAttemptCooldown = TimeSpan.FromMinutes(15);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly string[] CatalogCardPurposes = ["Poster", "BoxArt"];
    private static readonly string[] ProfileHeroPurposes = ["TitledHeroArt", "SuperHeroArt"];

    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;
    private readonly string _cacheDirectory;
    private readonly Action<XboxArtworkDiagnostic>? _diagnostic;
    private readonly ConcurrentDictionary<string, DateTimeOffset> _failedAttempts = new(StringComparer.OrdinalIgnoreCase);

    public XboxStoreArtworkResolver(
        string cacheDirectory,
        Action<XboxArtworkDiagnostic>? diagnostic = null)
        : this(CreateHttpClient(), cacheDirectory, diagnostic, ownsHttpClient: true)
    {
    }

    public XboxStoreArtworkResolver(
        HttpClient httpClient,
        string cacheDirectory,
        Action<XboxArtworkDiagnostic>? diagnostic = null)
        : this(httpClient, cacheDirectory, diagnostic, ownsHttpClient: false)
    {
    }

    private XboxStoreArtworkResolver(
        HttpClient httpClient,
        string cacheDirectory,
        Action<XboxArtworkDiagnostic>? diagnostic,
        bool ownsHttpClient)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentException.ThrowIfNullOrWhiteSpace(cacheDirectory);
        _httpClient = httpClient;
        _cacheDirectory = Path.GetFullPath(cacheDirectory);
        _diagnostic = diagnostic;
        _ownsHttpClient = ownsHttpClient;
    }

    public static string ResolveCacheDirectory(string[] args) =>
        Path.Combine(FrontendLaunchArguments.ResolveDataDirectory(args), "Artwork", "Xbox");

    public Task<XboxArtworkResult?> ResolveCatalogCardAsync(
        string? canonicalGameKey,
        string? windowsUiLanguage,
        CancellationToken cancellationToken = default,
        bool forceRetry = false) =>
        ResolveAsync(canonicalGameKey, windowsUiLanguage, XboxArtworkKind.CatalogCard, cancellationToken, forceRetry);

    public Task<XboxArtworkResult?> ResolveProfileHeroAsync(
        string? canonicalGameKey,
        string? windowsUiLanguage,
        CancellationToken cancellationToken = default,
        bool forceRetry = false) =>
        ResolveAsync(canonicalGameKey, windowsUiLanguage, XboxArtworkKind.ProfileHero, cancellationToken, forceRetry);

    public static bool TryGetStoreId(string? canonicalGameKey, out string storeId)
    {
        storeId = string.Empty;
        if (canonicalGameKey is null
            || !canonicalGameKey.StartsWith("store:", StringComparison.OrdinalIgnoreCase))
            return false;

        var value = canonicalGameKey["store:".Length..];
        if (value.Length != 12 || value.Any(character => !char.IsAsciiLetterOrDigit(character)))
            return false;

        storeId = value.ToUpperInvariant();
        return true;
    }

    public static (string Market, string Language) ResolveMarketAndLanguage(string? windowsUiLanguage)
    {
        if (string.IsNullOrWhiteSpace(windowsUiLanguage))
            return ("US", "en-US");

        try
        {
            var culture = CultureInfo.GetCultureInfo(windowsUiLanguage);
            if (culture.IsNeutralCulture)
                return ("US", "en-US");

            var market = new RegionInfo(culture.Name).TwoLetterISORegionName.ToUpperInvariant();
            return market.Length == 2 ? (market, culture.Name) : ("US", "en-US");
        }
        catch (ArgumentException)
        {
            return ("US", "en-US");
        }
    }

    private async Task<XboxArtworkResult?> ResolveAsync(
        string? canonicalGameKey,
        string? windowsUiLanguage,
        XboxArtworkKind kind,
        CancellationToken cancellationToken,
        bool forceRetry)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!TryGetStoreId(canonicalGameKey, out var storeId))
            return null;

        var primary = ResolveMarketAndLanguage(windowsUiLanguage);
        var markets = new List<MarketRequest> { new(primary.Market, primary.Language) };
        if (primary.Market != "US" || primary.Language != "en-US")
            markets.Add(new MarketRequest("US", "en-US"));

        var purposes = kind == XboxArtworkKind.CatalogCard ? CatalogCardPurposes : ProfileHeroPurposes;
        var failureKey = $"{storeId}|{kind}|{primary.Market}|{primary.Language}";
        var failurePath = GetFailurePath(failureKey);
        if (forceRetry)
        {
            _failedAttempts.TryRemove(failureKey, out _);
            TryDelete(failurePath);
        }
        else if (IsFailureSuppressed(failureKey, failurePath))
        {
            var cached = await FindBestCachedAsync(storeId, markets, purposes, cancellationToken).ConfigureAwait(false);
            return cached is null ? null : Complete(storeId, primary.Market, cached, "CacheHit", failureKey, failurePath, clearFailureMarker: false);
        }

        foreach (var market in markets)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var metadataPath = GetMetadataPath(storeId, market);
            var metadata = forceRetry ? null : await TryReadCatalogCacheAsync(metadataPath, storeId, market, cancellationToken).ConfigureAwait(false);
            var firstPurposeCache = await TryReadImageCacheAsync(storeId, market.Market, purposes[0], cancellationToken).ConfigureAwait(false);
            if (firstPurposeCache is not null)
                return Complete(storeId, primary.Market, firstPurposeCache, "CacheHit", failureKey, failurePath);

            if (metadata is null)
            {
                var fetch = await TryFetchCatalogAsync(storeId, market, cancellationToken).ConfigureAwait(false);
                if (fetch.Snapshot is null)
                {
                    continue;
                }

                metadata = fetch.Snapshot;
                await TryWriteCatalogCacheAsync(metadataPath, metadata, cancellationToken).ConfigureAwait(false);
            }

            foreach (var purpose in purposes)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var cached = purpose == purposes[0]
                    ? firstPurposeCache
                    : await TryReadImageCacheAsync(storeId, market.Market, purpose, cancellationToken).ConfigureAwait(false);
                if (cached is not null)
                    return Complete(storeId, primary.Market, cached, "CacheHit", failureKey, failurePath);

                if (!metadata.Images.TryGetValue(purpose, out var image))
                    continue;

                var downloaded = await TryDownloadImageAsync(storeId, market, purpose, image, cancellationToken).ConfigureAwait(false);
                if (downloaded is not null)
                    return Complete(storeId, primary.Market, downloaded, "Downloaded", failureKey, failurePath);
            }
        }

        await RecordFailedAttemptAsync(storeId, primary.Market, failureKey, failurePath, cancellationToken).ConfigureAwait(false);
        Emit(new XboxArtworkDiagnostic(storeId, primary.Market, string.Join(',', purposes), "NoArtwork", 0));
        return null;
    }

    private async Task<CatalogFetchResult> TryFetchCatalogAsync(
        string storeId,
        MarketRequest market,
        CancellationToken cancellationToken)
    {
        var uri = new Uri($"https://{CatalogHost}/v7.0/products/{Uri.EscapeDataString(storeId)}?fieldsTemplate=Details&market={Uri.EscapeDataString(market.Market)}&languages={Uri.EscapeDataString(market.Language)}");
        var timer = Stopwatch.StartNew();
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            if (response.StatusCode != HttpStatusCode.OK)
            {
                Emit(new XboxArtworkDiagnostic(storeId, market.Market, "Catalog", "HttpFailure", timer.Elapsed.TotalMilliseconds,
                    ((int)response.StatusCode).ToString(CultureInfo.InvariantCulture)));
                return new CatalogFetchResult(null);
            }

            var bytes = await ReadBoundedContentAsync(response, MaximumCatalogBytes, cancellationToken).ConfigureAwait(false);
            var catalog = ParseCatalog(bytes, storeId, market);
            Emit(new XboxArtworkDiagnostic(storeId, market.Market, "Catalog", catalog is null ? "InvalidMetadata" : "Resolved",
                timer.Elapsed.TotalMilliseconds));
            return new CatalogFetchResult(catalog);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or IOException)
        {
            Emit(new XboxArtworkDiagnostic(storeId, market.Market, "Catalog", "Unavailable", timer.Elapsed.TotalMilliseconds,
                exception.GetType().Name));
            return new CatalogFetchResult(null);
        }
        catch (Exception exception) when (exception is JsonException or InvalidDataException or UriFormatException or ArgumentException)
        {
            Emit(new XboxArtworkDiagnostic(storeId, market.Market, "Catalog", "InvalidMetadata", timer.Elapsed.TotalMilliseconds,
                exception.GetType().Name));
            return new CatalogFetchResult(null);
        }
    }

    private async Task<XboxArtworkResult?> TryDownloadImageAsync(
        string storeId,
        MarketRequest market,
        string purpose,
        CatalogImage image,
        CancellationToken cancellationToken)
    {
        var timer = Stopwatch.StartNew();
        if (image.FileSizeInBytes is > MaximumImageBytes
            || image.Width is <= 0 or > MaximumImageDimension
            || image.Height is <= 0 or > MaximumImageDimension
            || image.Width is { } width && image.Height is { } height && (long)width * height > MaximumImagePixels
            || !TryCreateAllowedImageUri(image.Uri, out var imageUri))
        {
            Emit(new XboxArtworkDiagnostic(storeId, market.Market, purpose, "RejectedMetadata", timer.Elapsed.TotalMilliseconds));
            return null;
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, imageUri);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("image/jpeg"));
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("image/png"));
            using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            if (response.StatusCode != HttpStatusCode.OK)
            {
                Emit(new XboxArtworkDiagnostic(storeId, market.Market, purpose, "HttpFailure", timer.Elapsed.TotalMilliseconds,
                    ((int)response.StatusCode).ToString(CultureInfo.InvariantCulture)));
                return null;
            }

            var bytes = await ReadBoundedContentAsync(response, MaximumImageBytes, cancellationToken).ConfigureAwait(false);
            if (!IsAllowedImageContentType(response.Content.Headers.ContentType, bytes, purpose, out var extension, out var dimensions))
            {
                Emit(new XboxArtworkDiagnostic(storeId, market.Market, purpose, "InvalidImage", timer.Elapsed.TotalMilliseconds));
                return null;
            }

            var path = await PublishImageCacheAsync(storeId, market.Market, purpose, extension, bytes, cancellationToken).ConfigureAwait(false);
            Emit(new XboxArtworkDiagnostic(storeId, market.Market, purpose, "Downloaded", timer.Elapsed.TotalMilliseconds));
            return new XboxArtworkResult(path, market.Market, purpose, dimensions.Width, dimensions.Height, CacheHit: false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (IsOptionalNetworkFailure(exception))
        {
            Emit(new XboxArtworkDiagnostic(storeId, market.Market, purpose, "Unavailable", timer.Elapsed.TotalMilliseconds,
                exception.GetType().Name));
            return null;
        }
    }

    private async Task<XboxArtworkResult?> FindBestCachedAsync(
        string storeId,
        IReadOnlyList<MarketRequest> markets,
        IReadOnlyList<string> purposes,
        CancellationToken cancellationToken)
    {
        foreach (var market in markets)
        {
            foreach (var purpose in purposes)
            {
                var cached = await TryReadImageCacheAsync(storeId, market.Market, purpose, cancellationToken).ConfigureAwait(false);
                if (cached is not null)
                    return cached;
            }
        }

        return null;
    }

    private XboxArtworkResult Complete(
        string storeId,
        string primaryMarket,
        XboxArtworkResult result,
        string outcome,
        string failureKey,
        string failurePath,
        bool clearFailureMarker = true)
    {
        if (clearFailureMarker)
        {
            _failedAttempts.TryRemove(failureKey, out _);
            TryDelete(failurePath);
        }
        Emit(new XboxArtworkDiagnostic(storeId, result.Market, result.Purpose,
            result.Market == primaryMarket ? outcome : $"Fallback{outcome}", 0));
        return result;
    }

    private async Task<CatalogSnapshot?> TryReadCatalogCacheAsync(
        string path,
        string storeId,
        MarketRequest market,
        CancellationToken cancellationToken)
    {
        try
        {
            if (!File.Exists(path) || new FileInfo(path).Length > MaximumCatalogBytes)
                return null;

            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 16 * 1024, useAsync: true);
            var cache = await JsonSerializer.DeserializeAsync<CatalogCache>(stream, JsonOptions, cancellationToken).ConfigureAwait(false);
            if (cache is null || cache.Images is null || cache.StoreId != storeId || cache.Market != market.Market || cache.Language != market.Language
                || DateTimeOffset.UtcNow - cache.CheckedAtUtc > CatalogCacheLifetime || cache.CheckedAtUtc > DateTimeOffset.UtcNow.AddMinutes(5))
                return null;

            var images = new Dictionary<string, CatalogImage>(StringComparer.Ordinal);
            foreach (var (purpose, image) in cache.Images)
            {
                if (!IsKnownPurpose(purpose) || !TryCreateAllowedImageUri(image.Uri, out _))
                    continue;
                images[purpose] = image;
            }

            return new CatalogSnapshot(storeId, market.Market, market.Language, cache.CheckedAtUtc, images);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
        {
            TryDelete(path);
            return null;
        }
    }

    private async Task TryWriteCatalogCacheAsync(string path, CatalogSnapshot catalog, CancellationToken cancellationToken)
    {
        var cache = new CatalogCache(catalog.StoreId, catalog.Market, catalog.Language, catalog.CheckedAtUtc, catalog.Images);
        try
        {
            await WriteAtomicAsync(path, JsonSerializer.SerializeToUtf8Bytes(cache, JsonOptions), cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            Emit(new XboxArtworkDiagnostic(catalog.StoreId, catalog.Market, "CatalogCache", "WriteFailure", 0, exception.GetType().Name));
        }
    }

    private async Task<XboxArtworkResult?> TryReadImageCacheAsync(
        string storeId,
        string market,
        string purpose,
        CancellationToken cancellationToken)
    {
        foreach (var extension in new[] { ".jpg", ".png" })
        {
            var path = GetImagePath(storeId, market, purpose, extension);
            try
            {
                if (!File.Exists(path))
                    continue;
                var info = new FileInfo(path);
                if (info.Length <= 0 || info.Length > MaximumImageBytes)
                {
                    TryDelete(path);
                    continue;
                }

                var bytes = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
                if (!ImageContentValidator.TryInspect(bytes, out var detectedExtension, out var dimensions)
                    || !string.Equals(detectedExtension, extension, StringComparison.Ordinal))
                {
                    TryDelete(path);
                    continue;
                }

                return new XboxArtworkResult(path, market, purpose, dimensions.Width, dimensions.Height, CacheHit: true);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
            {
                Emit(new XboxArtworkDiagnostic(storeId, market, purpose, "CacheReadFailure", 0, exception.GetType().Name));
            }
        }

        return null;
    }

    private async Task<string> PublishImageCacheAsync(
        string storeId,
        string market,
        string purpose,
        string extension,
        byte[] bytes,
        CancellationToken cancellationToken)
    {
        var path = GetImagePath(storeId, market, purpose, extension);
        await WriteAtomicAsync(path, bytes, cancellationToken).ConfigureAwait(false);
        TryDelete(GetImagePath(storeId, market, purpose, extension == ".jpg" ? ".png" : ".jpg"));
        return path;
    }

    private async Task WriteAtomicAsync(string path, byte[] bytes, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_cacheDirectory);
        var temporaryPath = path + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            await using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                64 * 1024, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            TryDelete(temporaryPath);
        }
    }

    private async Task RecordFailedAttemptAsync(string storeId, string market, string key, string path, CancellationToken cancellationToken)
    {
        var retryAfter = DateTimeOffset.UtcNow.Add(FailedAttemptCooldown);
        _failedAttempts[key] = retryAfter;
        try
        {
            await WriteAtomicAsync(path, JsonSerializer.SerializeToUtf8Bytes(retryAfter, JsonOptions), cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            Emit(new XboxArtworkDiagnostic(storeId, market, "RetryMarker", "WriteFailure", 0, exception.GetType().Name));
        }
    }

    private bool IsFailureSuppressed(string key, string path)
    {
        if (_failedAttempts.TryGetValue(key, out var retryAfter))
        {
            if (retryAfter > DateTimeOffset.UtcNow)
                return true;
            _failedAttempts.TryRemove(key, out _);
        }

        try
        {
            if (!File.Exists(path))
                return false;
            if (new FileInfo(path).Length > 128)
            {
                TryDelete(path);
                return false;
            }

            var cachedRetryAfter = JsonSerializer.Deserialize<DateTimeOffset>(File.ReadAllBytes(path), JsonOptions);
            if (cachedRetryAfter > DateTimeOffset.UtcNow)
            {
                _failedAttempts[key] = cachedRetryAfter;
                return true;
            }

            TryDelete(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
        {
            TryDelete(path);
        }

        return false;
    }

    private static CatalogSnapshot? ParseCatalog(byte[] bytes, string expectedStoreId, MarketRequest market)
    {
        using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 48 });
        var root = document.RootElement;
        var product = root.ValueKind == JsonValueKind.Object && root.TryGetProperty("Product", out var wrapped)
            ? wrapped
            : root;
        if (product.ValueKind != JsonValueKind.Object
            || !product.TryGetProperty("ProductId", out var productId)
            || !string.Equals(productId.GetString(), expectedStoreId, StringComparison.OrdinalIgnoreCase))
            return null;

        if (!product.TryGetProperty("LocalizedProperties", out var localizedProperties)
            || localizedProperties.ValueKind != JsonValueKind.Array)
            return new CatalogSnapshot(expectedStoreId, market.Market, market.Language, DateTimeOffset.UtcNow,
                new Dictionary<string, CatalogImage>(StringComparer.Ordinal));

        var requestedCulture = CultureInfo.GetCultureInfo(market.Language);
        var languageTags = string.Equals(requestedCulture.Name, requestedCulture.TwoLetterISOLanguageName, StringComparison.OrdinalIgnoreCase)
            ? new[] { requestedCulture.Name }
            : new[] { requestedCulture.Name, requestedCulture.TwoLetterISOLanguageName };
        var images = new Dictionary<string, CatalogImage>(StringComparer.Ordinal);
        foreach (var languageTag in languageTags)
        {
            foreach (var localized in localizedProperties.EnumerateArray())
            {
                if (localized.ValueKind != JsonValueKind.Object
                    || !TryGetString(localized, "Language", out var localizedLanguage)
                    || !string.Equals(localizedLanguage, languageTag, StringComparison.OrdinalIgnoreCase)
                    || !localized.TryGetProperty("Images", out var imageArray)
                    || imageArray.ValueKind != JsonValueKind.Array)
                    continue;

                foreach (var entry in imageArray.EnumerateArray())
                {
                    if (entry.ValueKind != JsonValueKind.Object
                        || !TryGetString(entry, "ImagePurpose", out var purpose)
                        || !IsKnownPurpose(purpose)
                        || images.ContainsKey(purpose)
                        || !TryGetString(entry, "Uri", out var uri)
                        || !TryCreateAllowedImageUri(uri, out _))
                        continue;

                    var width = TryGetInt32(entry, "Width");
                    var height = TryGetInt32(entry, "Height");
                    var fileSize = TryGetInt64(entry, "FileSizeInBytes");
                    if (fileSize is > MaximumImageBytes
                        || width is <= 0 or > MaximumImageDimension
                        || height is <= 0 or > MaximumImageDimension
                        || width is { } imageWidth && height is { } imageHeight && (long)imageWidth * imageHeight > MaximumImagePixels
                        || !IsExpectedAspectRatio(purpose, width, height))
                        continue;

                    images.Add(purpose, new CatalogImage(uri, width, height, fileSize));
                }
            }
        }

        return new CatalogSnapshot(expectedStoreId, market.Market, market.Language, DateTimeOffset.UtcNow, images);
    }

    private static bool IsExpectedAspectRatio(string purpose, int? width, int? height)
    {
        if (width is not { } w || height is not { } h || w <= 0 || h <= 0)
            return true;

        var ratio = (double)w / h;
        var expected = purpose switch
        {
            "Poster" => 2.0 / 3.0,
            "BoxArt" => 1.0,
            "TitledHeroArt" or "SuperHeroArt" => 16.0 / 9.0,
            _ => ratio,
        };
        return Math.Abs(ratio - expected) <= 0.08;
    }

    private static bool IsAllowedImageContentType(
        MediaTypeHeaderValue? contentType,
        byte[] bytes,
        string purpose,
        out string extension,
        out (int Width, int Height) dimensions)
    {
        extension = string.Empty;
        dimensions = default;
        if (contentType?.MediaType is not { } mediaType
            || !ImageContentValidator.TryInspect(bytes, out var detectedExtension, out dimensions))
            return false;

        if ((detectedExtension == ".jpg" && !mediaType.Equals("image/jpeg", StringComparison.OrdinalIgnoreCase))
            || (detectedExtension == ".png" && !mediaType.Equals("image/png", StringComparison.OrdinalIgnoreCase))
            || !IsExpectedAspectRatio(purpose, dimensions.Width, dimensions.Height))
            return false;

        extension = detectedExtension;
        return true;
    }

    private static bool TryCreateAllowedImageUri(string value, out Uri uri)
    {
        uri = null!;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var normalized = value.StartsWith("//", StringComparison.Ordinal) ? "https:" + value : value;
        if (!Uri.TryCreate(normalized, UriKind.Absolute, out var parsed)
            || parsed.Scheme != Uri.UriSchemeHttps
            || !string.Equals(parsed.Host, ImageHost, StringComparison.OrdinalIgnoreCase)
            || !string.IsNullOrEmpty(parsed.UserInfo)
            || parsed.Port != 443
            || string.IsNullOrEmpty(parsed.AbsolutePath))
            return false;

        uri = parsed;
        return true;
    }

    private static async Task<byte[]> ReadBoundedContentAsync(
        HttpResponseMessage response,
        int maximumBytes,
        CancellationToken cancellationToken)
    {
        if (response.Content.Headers.ContentLength is long contentLength && contentLength > maximumBytes)
            throw new InvalidDataException("The artwork response exceeded its size limit.");

        await using var input = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var output = new MemoryStream();
        var buffer = new byte[32 * 1024];
        while (true)
        {
            var read = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read == 0)
                break;
            if (output.Length + read > maximumBytes)
                throw new InvalidDataException("The artwork response exceeded its size limit.");
            output.Write(buffer, 0, read);
        }

        return output.ToArray();
    }

    private static bool TryGetString(JsonElement element, string name, out string value)
    {
        value = string.Empty;
        return element.TryGetProperty(name, out var property)
            && property.ValueKind == JsonValueKind.String
            && !string.IsNullOrWhiteSpace(value = property.GetString()!);
    }

    private static int? TryGetInt32(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.TryGetInt32(out var number) ? number : null;

    private static long? TryGetInt64(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.TryGetInt64(out var number) ? number : null;

    private static bool IsKnownPurpose(string purpose) =>
        CatalogCardPurposes.Contains(purpose, StringComparer.Ordinal)
        || ProfileHeroPurposes.Contains(purpose, StringComparer.Ordinal);

    private static bool IsOptionalNetworkFailure(Exception exception) =>
        exception is HttpRequestException or IOException or InvalidDataException or JsonException or UriFormatException
            or TaskCanceledException or UnauthorizedAccessException or ArgumentException;

    private string GetImagePath(string storeId, string market, string purpose, string extension) =>
        Path.Combine(_cacheDirectory, $"{storeId}-{market}-{purpose}{extension}");

    private string GetMetadataPath(string storeId, MarketRequest market) =>
        Path.Combine(_cacheDirectory, $"{storeId}-{market.Market}-{SanitizeLanguage(market.Language)}-catalog.json");

    private string GetFailurePath(string key) =>
        Path.Combine(_cacheDirectory, $"retry-{Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(key)))}.json");

    private static string SanitizeLanguage(string language) =>
        new(language.Where(character => char.IsAsciiLetterOrDigit(character) || character == '-').ToArray());

    private void Emit(XboxArtworkDiagnostic diagnostic)
    {
        try { _diagnostic?.Invoke(diagnostic); }
        catch { }
    }

    private static HttpClient CreateHttpClient() => new(new SocketsHttpHandler
    {
        AllowAutoRedirect = false,
        UseCookies = false,
        Credentials = null,
        ConnectTimeout = TimeSpan.FromSeconds(3),
    })
    {
        Timeout = TimeSpan.FromSeconds(3),
    };

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    public void Dispose()
    {
        if (_ownsHttpClient)
            _httpClient.Dispose();
    }

    private sealed record MarketRequest(string Market, string Language);
    private sealed record CatalogFetchResult(CatalogSnapshot? Snapshot);
    private sealed record CatalogImage(string Uri, int? Width, int? Height, long? FileSizeInBytes);
    private sealed record CatalogSnapshot(
        string StoreId,
        string Market,
        string Language,
        DateTimeOffset CheckedAtUtc,
        Dictionary<string, CatalogImage> Images);
    private sealed record CatalogCache(
        string StoreId,
        string Market,
        string Language,
        DateTimeOffset CheckedAtUtc,
        Dictionary<string, CatalogImage> Images);

    private static class ImageContentValidator
    {
        private static readonly byte[] PngSignature = [137, 80, 78, 71, 13, 10, 26, 10];

        internal static bool TryInspect(byte[] bytes, out string extension, out (int Width, int Height) dimensions)
        {
            extension = string.Empty;
            dimensions = default;
            if (bytes.Length is <= 16 or > MaximumImageBytes)
                return false;

            if (bytes.AsSpan().StartsWith(PngSignature))
            {
                if (!TryInspectPng(bytes, out dimensions))
                    return false;
                extension = ".png";
                return true;
            }

            if (!TryInspectJpeg(bytes, out dimensions))
                return false;
            extension = ".jpg";
            return true;
        }

        private static bool TryInspectPng(byte[] bytes, out (int Width, int Height) dimensions)
        {
            dimensions = default;
            if (bytes.Length < 45 || !bytes.AsSpan(12, 4).SequenceEqual("IHDR"u8))
                return false;

            var width = ReadBigEndianInt32(bytes.AsSpan(16, 4));
            var height = ReadBigEndianInt32(bytes.AsSpan(20, 4));
            if (!IsValidDimensions(width, height))
                return false;

            var offset = 8;
            var hasImageData = false;
            var hasEnd = false;
            while (offset <= bytes.Length - 12)
            {
                var length = ReadBigEndianInt32(bytes.AsSpan(offset, 4));
                if (length < 0 || length > bytes.Length - offset - 12)
                    return false;
                var type = bytes.AsSpan(offset + 4, 4);
                var data = bytes.AsSpan(offset + 8, length);
                var expectedCrc = ReadBigEndianUInt32(bytes.AsSpan(offset + 8 + length, 4));
                if (ComputePngCrc(type, data) != expectedCrc)
                    return false;
                hasImageData |= type.SequenceEqual("IDAT"u8) && length > 0;
                offset += 12 + length;
                if (type.SequenceEqual("IEND"u8))
                {
                    hasEnd = length == 0 && offset == bytes.Length;
                    break;
                }
            }

            if (!hasImageData || !hasEnd)
                return false;
            dimensions = (width, height);
            return true;
        }

        private static bool TryInspectJpeg(byte[] bytes, out (int Width, int Height) dimensions)
        {
            dimensions = default;
            if (bytes.Length < 16 || bytes[0] != 0xFF || bytes[1] != 0xD8
                || bytes[^2] != 0xFF || bytes[^1] != 0xD9)
                return false;

            var offset = 2;
            while (offset < bytes.Length - 2)
            {
                if (bytes[offset++] != 0xFF)
                    continue;
                while (offset < bytes.Length && bytes[offset] == 0xFF)
                    offset++;
                if (offset >= bytes.Length)
                    return false;

                var marker = bytes[offset++];
                if (marker is 0xD8 or 0xD9 or 0x01 or >= 0xD0 and <= 0xD7)
                    continue;
                if (offset > bytes.Length - 2)
                    return false;

                var segmentLength = (bytes[offset] << 8) | bytes[offset + 1];
                if (segmentLength < 2 || segmentLength > bytes.Length - offset)
                    return false;

                if (IsStartOfFrame(marker))
                {
                    if (segmentLength < 7)
                        return false;
                    var height = (bytes[offset + 3] << 8) | bytes[offset + 4];
                    var width = (bytes[offset + 5] << 8) | bytes[offset + 6];
                    if (!IsValidDimensions(width, height))
                        return false;
                    dimensions = (width, height);
                    return true;
                }

                offset += segmentLength;
            }

            return false;
        }

        private static bool IsStartOfFrame(byte marker) => marker is
            0xC0 or 0xC1 or 0xC2 or 0xC3 or 0xC5 or 0xC6 or 0xC7 or 0xC9 or 0xCA or 0xCB or 0xCD or 0xCE or 0xCF;

        private static bool IsValidDimensions(int width, int height) =>
            width > 0 && height > 0 && width <= MaximumImageDimension && height <= MaximumImageDimension
            && (long)width * height <= MaximumImagePixels;

        private static int ReadBigEndianInt32(ReadOnlySpan<byte> bytes) =>
            (int)ReadBigEndianUInt32(bytes);

        private static uint ReadBigEndianUInt32(ReadOnlySpan<byte> bytes) =>
            ((uint)bytes[0] << 24) | ((uint)bytes[1] << 16) | ((uint)bytes[2] << 8) | bytes[3];

        private static uint ComputePngCrc(ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
        {
            var crc = 0xFFFFFFFFu;
            foreach (var value in type)
                crc = UpdateCrc(crc, value);
            foreach (var value in data)
                crc = UpdateCrc(crc, value);
            return ~crc;
        }

        private static uint UpdateCrc(uint crc, byte value)
        {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++)
                crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
            return crc;
        }
    }
}
