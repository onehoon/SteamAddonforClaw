using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using SteamInputAddonforClaw.FrontendTransport;
using SteamInputAddonforClaw.FrontendTransport.XboxArtwork;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class XboxStoreArtworkResolverTests
{
    private const string StoreId = "9PK8PHLCQDF6";
    private const string StoreKey = "store:9PK8PHLCQDF6";

    [Theory]
    [InlineData("ko-KR", "KR", "ko-KR")]
    [InlineData("en-GB", "GB", "en-GB")]
    [InlineData("en-US", "US", "en-US")]
    [InlineData("en", "US", "en-US")]
    [InlineData("invalid-culture-tag", "US", "en-US")]
    [InlineData(null, "US", "en-US")]
    public void Windows_ui_language_maps_to_market_and_full_language(string? language, string expectedMarket, string expectedLanguage)
    {
        Assert.Equal((expectedMarket, expectedLanguage), XboxStoreArtworkResolver.ResolveMarketAndLanguage(language));
    }

    [Theory]
    [InlineData("store:9PK8PHLCQDF6", true, "9PK8PHLCQDF6")]
    [InlineData("STORE:9pk8phlcqdf6", true, "9PK8PHLCQDF6")]
    [InlineData("pfn:9PK8PHLCQDF6", false, "")]
    [InlineData("store:9PK8PHLCQDF", false, "")]
    [InlineData("store:9PK8PHLCQDF6/extra", false, "")]
    [InlineData("store:!!!!!!!!!", false, "")]
    [InlineData(null, false, "")]
    public void Only_a_canonical_store_key_with_a_valid_store_id_is_eligible(string? key, bool expected, string expectedId)
    {
        Assert.Equal(expected, XboxStoreArtworkResolver.TryGetStoreId(key, out var id));
        Assert.Equal(expectedId, id);
    }

    [Fact]
    public async Task Primary_market_boxart_wins_before_us_poster()
    {
        using var temp = new TempDirectory();
        var requests = new List<string>();
        using var client = CreateClient((request, _) =>
        {
            requests.Add(request.RequestUri!.Query);
            if (request.RequestUri.AbsolutePath.Contains("/v7.0/products/", StringComparison.Ordinal))
                return Json(Product(StoreId, Image("BoxArt", 1, 1)));
            return PngImage(1, 1);
        });
        using var resolver = new XboxStoreArtworkResolver(client, temp.Path);

        var result = await resolver.ResolveCatalogCardAsync(StoreKey, "ko-KR");

        Assert.NotNull(result);
        Assert.Equal("KR", result.Market);
        Assert.Equal("BoxArt", result.Purpose);
        Assert.True(File.Exists(result.Path));
        Assert.Equal(2, requests.Count);
        Assert.Contains("market=KR", requests[0], StringComparison.Ordinal);
        Assert.Contains("languages=ko-KR", requests[0], StringComparison.Ordinal);
    }

    [Fact]
    public async Task Missing_primary_art_uses_us_poster_before_us_boxart()
    {
        using var temp = new TempDirectory();
        var requests = new List<string>();
        using var client = CreateClient((request, _) =>
        {
            var uri = request.RequestUri!;
            requests.Add(uri.AbsolutePath + uri.Query);
            if (uri.AbsolutePath.Contains("/v7.0/products/", StringComparison.Ordinal))
                return Json(uri.Query.Contains("market=KR", StringComparison.Ordinal)
                    ? Product(StoreId)
                    : ProductWithLanguage(StoreId, "en-us", Image("Poster", 2, 3), Image("BoxArt", 1, 1)));

            return PngImage(uri.AbsolutePath.EndsWith("/Poster", StringComparison.Ordinal) ? 2 : 1,
                uri.AbsolutePath.EndsWith("/Poster", StringComparison.Ordinal) ? 3 : 1);
        });
        using var resolver = new XboxStoreArtworkResolver(client, temp.Path);

        var result = await resolver.ResolveCatalogCardAsync(StoreKey, "ko-KR");

        Assert.NotNull(result);
        Assert.Equal(("US", "Poster"), (result.Market, result.Purpose));
        Assert.Contains(requests, value => value.Contains("market=KR", StringComparison.Ordinal));
        Assert.Contains(requests, value => value.Contains("market=US", StringComparison.Ordinal));
        Assert.True(requests.FindIndex(value => value.Contains("market=KR", StringComparison.Ordinal))
            < requests.FindIndex(value => value.Contains("market=US", StringComparison.Ordinal)));
        Assert.DoesNotContain(requests, value => value.EndsWith("/BoxArt", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Primary_market_http_failure_still_tries_the_us_artwork_fallback()
    {
        using var temp = new TempDirectory();
        var requests = new List<string>();
        using var client = CreateClient((request, _) =>
        {
            var uri = request.RequestUri!;
            requests.Add(uri.Query);
            if (uri.AbsolutePath.Contains("/v7.0/products/", StringComparison.Ordinal))
                return uri.Query.Contains("market=KR", StringComparison.Ordinal)
                    ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
                    : Json(ProductWithLanguage(StoreId, "en-us", Image("Poster", 2, 3)));
            return PngImage(2, 3);
        });
        using var resolver = new XboxStoreArtworkResolver(client, temp.Path);

        var result = await resolver.ResolveCatalogCardAsync(StoreKey, "ko-KR");

        Assert.NotNull(result);
        Assert.Equal(("US", "Poster"), (result.Market, result.Purpose));
        Assert.Equal(3, requests.Count);
        Assert.Contains("market=KR", requests[0], StringComparison.Ordinal);
        Assert.Contains("market=US", requests[1], StringComparison.Ordinal);
    }

    [Fact]
    public async Task Invalid_or_missing_primary_poster_falls_through_to_primary_boxart()
    {
        using var temp = new TempDirectory();
        var requests = new List<string>();
        using var client = CreateClient((request, _) =>
        {
            var uri = request.RequestUri!;
            requests.Add(uri.AbsolutePath);
            if (uri.AbsolutePath.Contains("/v7.0/products/", StringComparison.Ordinal))
                return Json(Product(StoreId, Image("Poster", 2, 3), Image("BoxArt", 1, 1), Image("Screenshot", 16, 9)));
            if (uri.AbsolutePath.EndsWith("/Poster", StringComparison.Ordinal))
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            if (uri.AbsolutePath.EndsWith("/BoxArt", StringComparison.Ordinal))
                return PngImage(1, 1);
            throw new InvalidOperationException("An unrelated image purpose must not be requested.");
        });
        using var resolver = new XboxStoreArtworkResolver(client, temp.Path);

        var result = await resolver.ResolveCatalogCardAsync(StoreKey, "ko-KR");

        Assert.NotNull(result);
        Assert.Equal(("KR", "BoxArt"), (result.Market, result.Purpose));
        Assert.Equal(3, requests.Count);
        Assert.Contains("/v7.0/products/", requests[0], StringComparison.Ordinal);
        Assert.EndsWith("/Poster", requests[1], StringComparison.Ordinal);
        Assert.EndsWith("/BoxArt", requests[2], StringComparison.Ordinal);
    }

    [Fact]
    public async Task Product_id_mismatch_is_rejected_and_us_fallback_is_used()
    {
        using var temp = new TempDirectory();
        using var client = CreateClient((request, _) =>
        {
            var uri = request.RequestUri!;
            if (uri.AbsolutePath.Contains("/v7.0/products/", StringComparison.Ordinal))
                return Json(uri.Query.Contains("market=KR", StringComparison.Ordinal)
                    ? Product("000000000000", Image("Poster", 2, 3))
                    : ProductWithLanguage(StoreId, "en-us", Image("Poster", 2, 3)));
            return PngImage(2, 3);
        });
        using var resolver = new XboxStoreArtworkResolver(client, temp.Path);

        var result = await resolver.ResolveCatalogCardAsync(StoreKey, "ko-KR");

        Assert.NotNull(result);
        Assert.Equal("US", result.Market);
        Assert.Equal("Poster", result.Purpose);
    }

    [Fact]
    public async Task Catalog_uses_the_requested_localized_property_instead_of_the_first_language()
    {
        using var temp = new TempDirectory();
        using var client = CreateClient((request, _) =>
        {
            if (request.RequestUri!.AbsolutePath.Contains("/v7.0/products/", StringComparison.Ordinal))
            {
                var body = JsonSerializer.Serialize(new
                {
                    Product = new
                    {
                        ProductId = StoreId,
                        LocalizedProperties = new object[]
                        {
                            new { Language = "en-us", Images = new[] { Image("Poster", 2, 3) } },
                            new { Language = "ko-kr", Images = new[] { Image("BoxArt", 1, 1) } },
                        },
                    },
                });
                return Json(body);
            }

            return PngImage(1, 1);
        });
        using var resolver = new XboxStoreArtworkResolver(client, temp.Path);

        var result = await resolver.ResolveCatalogCardAsync(StoreKey, "ko-KR");

        Assert.NotNull(result);
        Assert.Equal("BoxArt", result.Purpose);
        Assert.Equal("KR", result.Market);
    }

    [Fact]
    public async Task Hero_uses_titled_then_superhero_then_us_market()
    {
        using var temp = new TempDirectory();
        var requests = new List<string>();
        using var client = CreateClient((request, _) =>
        {
            var uri = request.RequestUri!;
            requests.Add(uri.AbsolutePath + uri.Query);
            if (uri.AbsolutePath.Contains("/v7.0/products/", StringComparison.Ordinal))
                return Json(uri.Query.Contains("market=KR", StringComparison.Ordinal)
                    ? Product(StoreId, Image("TitledHeroArt", 16, 9), Image("SuperHeroArt", 16, 9))
                    : Product(StoreId, Image("TitledHeroArt", 16, 9)));
            if (uri.AbsolutePath.EndsWith("/TitledHeroArt", StringComparison.Ordinal))
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            if (uri.AbsolutePath.EndsWith("/SuperHeroArt", StringComparison.Ordinal))
                return PngImage(16, 9);
            return PngImage(16, 9);
        });
        using var resolver = new XboxStoreArtworkResolver(client, temp.Path);

        var result = await resolver.ResolveProfileHeroAsync(StoreKey, "ko-KR");

        Assert.NotNull(result);
        Assert.Equal(("KR", "SuperHeroArt"), (result.Market, result.Purpose));
        Assert.DoesNotContain(requests, value => value.Contains("market=US", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Scheme_relative_microsoft_cdn_url_is_accepted_but_non_https_or_other_hosts_are_skipped()
    {
        using var temp = new TempDirectory();
        var requests = new List<Uri>();
        using var client = CreateClient((request, _) =>
        {
            var uri = request.RequestUri!;
            Assert.Null(request.Headers.Authorization);
            Assert.False(request.Headers.Contains("Cookie"));
            requests.Add(uri);
            if (uri.AbsolutePath.Contains("/v7.0/products/", StringComparison.Ordinal))
                return Json(Product(StoreId,
                    Image("Poster", 2, 3, "http://store-images.s-microsoft.com/image/Poster"),
                    Image("BoxArt", 1, 1, "//store-images.s-microsoft.com/image/BoxArt"),
                    Image("TitledHeroArt", 16, 9, "https://untrusted.example/image/TitledHeroArt")));
            return PngImage(1, 1);
        });
        using var resolver = new XboxStoreArtworkResolver(client, temp.Path);

        var result = await resolver.ResolveCatalogCardAsync(StoreKey, "ko-KR");

        Assert.NotNull(result);
        Assert.Equal("BoxArt", result.Purpose);
        Assert.Contains(requests, uri => uri.Scheme == "https" && uri.Host == "store-images.s-microsoft.com");
        Assert.DoesNotContain(requests, uri => uri.Host == "untrusted.example");
    }

    [Fact]
    public async Task Cached_image_is_market_and_purpose_scoped_and_hit_does_not_contact_network()
    {
        using var temp = new TempDirectory();
        var calls = 0;
        using (var firstClient = CreateClient((request, _) =>
        {
            calls++;
            var uri = request.RequestUri!;
            if (uri.AbsolutePath.Contains("/v7.0/products/", StringComparison.Ordinal))
                return Json(ProductWithLanguage(StoreId, uri.Query.Contains("languages=en-GB", StringComparison.Ordinal) ? "en-gb" : "ko-kr", Image("Poster", 2, 3)));
            return PngImage(2, 3);
        }))
        using (var firstResolver = new XboxStoreArtworkResolver(firstClient, temp.Path))
        {
            var downloaded = await firstResolver.ResolveCatalogCardAsync(StoreKey, "ko-KR");
            Assert.NotNull(downloaded);
            Assert.False(downloaded.CacheHit);
            Assert.EndsWith("-KR-Poster.png", downloaded.Path, StringComparison.Ordinal);
        }

        calls = 0;
        using (var offlineClient = CreateClient((_, _) => Task.FromException<HttpResponseMessage>(new HttpRequestException("Offline"))))
        using (var offlineResolver = new XboxStoreArtworkResolver(offlineClient, temp.Path))
        {
            var cached = await offlineResolver.ResolveCatalogCardAsync(StoreKey, "ko-KR");
            Assert.NotNull(cached);
            Assert.True(cached.CacheHit);
            Assert.Equal(0, calls);
        }

        using var otherMarketClient = CreateClient((request, _) =>
        {
            var uri = request.RequestUri!;
            if (uri.AbsolutePath.Contains("/v7.0/products/", StringComparison.Ordinal))
                return Json(ProductWithLanguage(StoreId, "en-gb", Image("Poster", 2, 3)));
            return PngImage(2, 3);
        });
        using var otherMarketResolver = new XboxStoreArtworkResolver(otherMarketClient, temp.Path);
        var otherMarket = await otherMarketResolver.ResolveCatalogCardAsync(StoreKey, "en-GB");
        Assert.NotNull(otherMarket);
        Assert.Equal("GB", otherMarket.Market);
        Assert.EndsWith("-GB-Poster.png", otherMarket.Path, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Failed_or_corrupt_image_does_not_replace_a_valid_cached_candidate()
    {
        using var temp = new TempDirectory();
        var first = Product(StoreId, Image("BoxArt", 1, 1));
        using (var firstClient = CreateClient((request, _) => request.RequestUri!.AbsolutePath.Contains("/v7.0/products/", StringComparison.Ordinal)
            ? Json(first)
            : PngImage(1, 1)))
        using (var firstResolver = new XboxStoreArtworkResolver(firstClient, temp.Path))
        {
            Assert.NotNull(await firstResolver.ResolveCatalogCardAsync(StoreKey, "ko-KR"));
        }

        var cachePath = Path.Combine(temp.Path, $"{StoreId}-KR-BoxArt.png");
        var knownGood = await File.ReadAllBytesAsync(cachePath);
        using var secondClient = CreateClient((request, _) =>
        {
            if (request.RequestUri!.AbsolutePath.Contains("/v7.0/products/", StringComparison.Ordinal))
                return Json(Product(StoreId, Image("Poster", 2, 3), Image("BoxArt", 1, 1)));
            if (request.RequestUri.AbsolutePath.EndsWith("/Poster", StringComparison.Ordinal))
            {
                var corrupt = new ByteArrayContent([1, 2, 3, 4]);
                corrupt.Headers.ContentType = new MediaTypeHeaderValue("image/png");
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = corrupt };
            }

            throw new InvalidOperationException("The valid BoxArt cache must be reused after the corrupt Poster.");
        });
        using var secondResolver = new XboxStoreArtworkResolver(secondClient, temp.Path);

        var cached = await secondResolver.ResolveCatalogCardAsync(StoreKey, "ko-KR", forceRetry: true);

        Assert.NotNull(cached);
        Assert.True(cached.CacheHit);
        Assert.Equal("BoxArt", cached.Purpose);
        Assert.Equal(knownGood, await File.ReadAllBytesAsync(cachePath));
    }

    [Fact]
    public async Task Cancellation_during_catalog_lookup_is_propagated()
    {
        using var temp = new TempDirectory();
        using var client = CreateClient(async (_, cancellationToken) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        using var resolver = new XboxStoreArtworkResolver(client, temp.Path);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => resolver.ResolveProfileHeroAsync(StoreKey, "ko-KR", cancellation.Token));
    }

    [Fact]
    public void Shared_cache_directory_is_derived_from_the_existing_stable_log_argument()
    {
        var directory = Path.Combine(Path.GetTempPath(), "AddonData", "logs");
        var args = new[] { FrontendLaunchArguments.LogDirectoryOption, directory };

        Assert.Equal(Path.Combine(Path.GetTempPath(), "AddonData", "Artwork", "Xbox"),
            XboxStoreArtworkResolver.ResolveCacheDirectory(args));
    }

    private static HttpClient CreateClient(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) =>
        new(new DelegateHandler(send), disposeHandler: true);

    private static HttpClient CreateClient(Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> send) =>
        CreateClient((request, token) => Task.FromResult(send(request, token)));

    private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json"),
    };

    private static HttpResponseMessage PngImage(int width, int height)
    {
        var content = new ByteArrayContent(BuildPng(width, height));
        content.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
    }

    private static string Product(string productId, params object[] images) => ProductWithLanguage(productId, "ko-kr", images);

    private static string ProductWithLanguage(string productId, string language, params object[] images)
    {
        var payload = new
        {
            Product = new
            {
                ProductId = productId,
                LocalizedProperties = new[] { new { Language = language, Images = images } },
            },
        };
        return JsonSerializer.Serialize(payload);
    }

    private static object Image(string purpose, int width, int height, string? uri = null) => new
    {
        ImagePurpose = purpose,
        Uri = uri ?? $"//store-images.s-microsoft.com/image/{purpose}",
        Width = width,
        Height = height,
        FileSizeInBytes = 128,
    };

    private static byte[] BuildPng(int width, int height)
    {
        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            var row = new byte[1 + width * 4];
            for (var x = 1; x < row.Length; x += 4)
            {
                row[x] = 32;
                row[x + 1] = 96;
                row[x + 2] = 160;
                row[x + 3] = 255;
            }
            for (var y = 0; y < height; y++)
                zlib.Write(row);
        }

        using var png = new MemoryStream();
        png.Write([137, 80, 78, 71, 13, 10, 26, 10]);
        var header = new byte[13];
        WriteBigEndian(header.AsSpan(0, 4), width);
        WriteBigEndian(header.AsSpan(4, 4), height);
        header[8] = 8;
        header[9] = 6;
        WriteChunk(png, "IHDR", header);
        WriteChunk(png, "IDAT", compressed.ToArray());
        WriteChunk(png, "IEND", []);
        return png.ToArray();
    }

    private static void WriteChunk(Stream output, string type, byte[] data)
    {
        Span<byte> length = stackalloc byte[4];
        WriteBigEndian(length, data.Length);
        output.Write(length);
        var typeBytes = Encoding.ASCII.GetBytes(type);
        output.Write(typeBytes);
        output.Write(data);
        Span<byte> crc = stackalloc byte[4];
        WriteBigEndian(crc, ComputeCrc(typeBytes, data));
        output.Write(crc);
    }

    private static uint ComputeCrc(ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
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

    private static void WriteBigEndian(Span<byte> destination, int value) =>
        WriteBigEndian(destination, unchecked((uint)value));

    private static void WriteBigEndian(Span<byte> destination, uint value)
    {
        destination[0] = (byte)(value >> 24);
        destination[1] = (byte)(value >> 16);
        destination[2] = (byte)(value >> 8);
        destination[3] = (byte)value;
    }

    private sealed class DelegateHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            send(request, cancellationToken);
    }

    private sealed class TempDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "XboxArtworkTests", Guid.NewGuid().ToString("N"));
        public void Dispose()
        {
            try { if (Directory.Exists(Path)) Directory.Delete(Path, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
