using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using SteamInputAddonforClaw.ClawHud;
using SteamInputAddonforClaw.Install;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class ClawHudRuntimeAcquirerTests
{
    private const string RuntimeVersion = "1.0.1";
    private const string SourceCommit = "717c57ea1812a874cf474f360faa01ed50cf39ea";

    [Fact]
    public async Task ValidInstalledRuntime_UsesFastPathWithoutHttp()
    {
        using var fixture = new Fixture();
        var runtimeDirectory = AddonDataPaths.ResolveClawHudRuntimeVersionDirectory(fixture.InstallRoot, RuntimeVersion);
        CreatePayload(runtimeDirectory, includeForbidden: false);
        var handler = new RecordingHandler(_ => throw new InvalidOperationException("HTTP must not be called."));
        var result = await fixture.CreateAcquirer(handler).AcquireAsync(CancellationToken.None);

        Assert.True(result.IsReady);
        Assert.Equal(Path.Combine(runtimeDirectory, "ClawHUD.exe"), result.ExecutablePath);
        Assert.Equal(0, handler.RequestCount);
    }

    [Fact]
    public async Task ExactUrlIsRequested_AndVerifiedPayloadIsAdopted()
    {
        using var fixture = new Fixture();
        var zip = CreateZip(includeForbidden: false);
        var handler = new RecordingHandler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(zip),
        }));
        handler.ResponseContentType = "application/zip";
        var result = await fixture.CreateAcquirer(handler, zip).AcquireAsync(CancellationToken.None);

        Assert.True(result.IsReady);
        Assert.Equal("https://github.com/onehoon/ClawHUD/releases/download/steamaddon-runtime-v1.0.1/ClawHUDRuntime.zip", handler.RequestedUri!.ToString());
        Assert.True(File.Exists(Path.Combine(result.RuntimeDirectory!, "ClawHUD.exe")));
        Assert.False(Directory.Exists(Path.Combine(fixture.DataRoot, "Runtime", "ClawHUD", "1.0.1.staging")));
    }

    [Fact]
    public async Task SuccessfulAcquisitionRetainsCurrentAndPreviousRuntimeVersions()
    {
        using var fixture = new Fixture();
        var runtimeRoot = AddonDataPaths.ResolveClawHudRuntimeRoot(fixture.InstallRoot);
        var previousVersion = Path.Combine(runtimeRoot, "1.0.0");
        var olderVersion = Path.Combine(runtimeRoot, "0.9.9");
        var newerVersion = Path.Combine(runtimeRoot, "1.0.2");
        var unknownDirectory = Path.Combine(runtimeRoot, "notes");
        var stagingDirectory = Path.Combine(runtimeRoot, "1.0.1.staging");
        foreach (var path in new[] { previousVersion, olderVersion, newerVersion, unknownDirectory, stagingDirectory })
        {
            Directory.CreateDirectory(path);
            File.WriteAllText(Path.Combine(path, "sentinel.txt"), "keep or remove");
        }

        var zip = CreateZip(includeForbidden: false);
        var handler = new RecordingHandler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(zip),
        }));
        var result = await fixture.CreateAcquirer(handler, zip).AcquireAsync(CancellationToken.None);

        Assert.True(result.IsReady);
        Assert.True(File.Exists(Path.Combine(runtimeRoot, "1.0.1", "ClawHUD.exe")));
        Assert.True(File.Exists(Path.Combine(previousVersion, "sentinel.txt")));
        Assert.False(Directory.Exists(olderVersion));
        Assert.True(Directory.Exists(newerVersion));
        Assert.True(Directory.Exists(unknownDirectory));
        Assert.False(Directory.Exists(stagingDirectory));
    }

    [Fact]
    public async Task FastPathAlsoCleansVersionsOlderThanCurrentAndPrevious()
    {
        using var fixture = new Fixture();
        var runtimeRoot = AddonDataPaths.ResolveClawHudRuntimeRoot(fixture.InstallRoot);
        var currentVersion = AddonDataPaths.ResolveClawHudRuntimeVersionDirectory(fixture.InstallRoot, RuntimeVersion);
        var previousVersion = Path.Combine(runtimeRoot, "1.0.0");
        var olderVersion = Path.Combine(runtimeRoot, "0.9.9");
        CreatePayload(currentVersion, includeForbidden: false);
        Directory.CreateDirectory(previousVersion);
        Directory.CreateDirectory(olderVersion);

        var handler = new RecordingHandler(_ => throw new InvalidOperationException("HTTP must not be called."));
        var result = await fixture.CreateAcquirer(handler).AcquireAsync(CancellationToken.None);

        Assert.True(result.IsReady);
        Assert.True(Directory.Exists(previousVersion));
        Assert.False(Directory.Exists(olderVersion));
        Assert.Equal(0, handler.RequestCount);
    }

    [Fact]
    public async Task HashMismatch_IsFeatureLocal_AndLeavesOtherVersionUntouched()
    {
        using var fixture = new Fixture();
        var otherVersion = AddonDataPaths.ResolveClawHudRuntimeVersionDirectory(fixture.InstallRoot, "1.0.0");
        Directory.CreateDirectory(otherVersion);
        File.WriteAllText(Path.Combine(otherVersion, "sentinel.txt"), "keep");
        var handler = new RecordingHandler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Encoding.UTF8.GetBytes("not-a-zip")) }));
        var result = await fixture.CreateAcquirer(handler).AcquireAsync(CancellationToken.None);

        Assert.False(result.IsReady);
        Assert.Equal(ClawHudRuntimeAcquisitionFailure.HashMismatch, result.Failure);
        Assert.Equal("keep", File.ReadAllText(Path.Combine(otherVersion, "sentinel.txt")));
        Assert.False(Directory.Exists(Path.Combine(fixture.DataRoot, "Runtime", "ClawHUD", "1.0.1.staging")));
    }

    [Fact]
    public async Task HttpFailure_IsFeatureLocal()
    {
        using var fixture = new Fixture();
        var handler = new RecordingHandler(_ => throw new HttpRequestException("offline"));
        var result = await fixture.CreateAcquirer(handler).AcquireAsync(CancellationToken.None);

        Assert.False(result.IsReady);
        Assert.Equal(ClawHudRuntimeAcquisitionFailure.Network, result.Failure);
        Assert.Equal(1, handler.RequestCount);
    }

    [Fact]
    public async Task HttpTimeout_IsFeatureLocal()
    {
        using var fixture = new Fixture();
        var handler = new RecordingHandler(_ => throw new TaskCanceledException("simulated HttpClient timeout"));
        var result = await fixture.CreateAcquirer(handler).AcquireAsync(CancellationToken.None);

        Assert.False(result.IsReady);
        Assert.Equal(ClawHudRuntimeAcquisitionFailure.Network, result.Failure);
        Assert.Equal(1, handler.RequestCount);
    }

    [Fact]
    public async Task Cancellation_IsFeatureLocal()
    {
        using var fixture = new Fixture();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var handler = new RecordingHandler(_ => throw new InvalidOperationException("HTTP must not be called."));
        var result = await fixture.CreateAcquirer(handler).AcquireAsync(cancellation.Token);

        Assert.False(result.IsReady);
        Assert.Equal(ClawHudRuntimeAcquisitionFailure.Cancelled, result.Failure);
        Assert.Equal(0, handler.RequestCount);
    }

    [Fact]
    public async Task MalformedLock_IsRejectedBeforeHttp()
    {
        using var fixture = new Fixture();
        fixture.SetLock("not-json");
        var handler = new RecordingHandler(_ => throw new InvalidOperationException("HTTP must not be called."));
        var result = await fixture.CreateAcquirer(handler).AcquireAsync(CancellationToken.None);

        Assert.False(result.IsReady);
        Assert.Equal(ClawHudRuntimeAcquisitionFailure.LockInvalid, result.Failure);
        Assert.Equal(0, handler.RequestCount);
    }

    [Fact]
    public async Task UnsafeTraversalArchive_IsRejectedWithoutWritingOutsideStaging()
    {
        using var fixture = new Fixture();
        var zip = CreateUnsafeZip();
        var handler = new RecordingHandler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(zip) }));
        var result = await fixture.CreateAcquirer(handler, zip).AcquireAsync(CancellationToken.None);

        Assert.False(result.IsReady);
        Assert.Equal(ClawHudRuntimeAcquisitionFailure.ArchiveInvalid, result.Failure);
        Assert.False(File.Exists(Path.Combine(fixture.DataRoot, "escape.txt")));
    }

    [Fact]
    public async Task AbsoluteArchiveEntry_IsRejected()
    {
        using var fixture = new Fixture();
        var zip = CreateAbsoluteZip();
        var handler = new RecordingHandler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(zip) }));
        var result = await fixture.CreateAcquirer(handler, zip).AcquireAsync(CancellationToken.None);

        Assert.False(result.IsReady);
        Assert.Equal(ClawHudRuntimeAcquisitionFailure.ArchiveInvalid, result.Failure);
    }

    [Fact]
    public async Task EmbeddedManifestWithNonNullHash_IsRejected()
    {
        using var fixture = new Fixture();
        var zip = CreateZip(includeForbidden: false, embeddedSha256: "not-null");
        var handler = new RecordingHandler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(zip) }));
        var result = await fixture.CreateAcquirer(handler, zip).AcquireAsync(CancellationToken.None);

        Assert.False(result.IsReady);
        Assert.Equal(ClawHudRuntimeAcquisitionFailure.ManifestInvalid, result.Failure);
    }

    [Theory]
    [InlineData("wrong-version", "steamaddon-runtime-v1.0.1", "ClawHUDRuntime.zip", "717c57ea1812a874cf474f360faa01ed50cf39ea")]
    [InlineData("1.0.1", "wrong-tag", "ClawHUDRuntime.zip", "717c57ea1812a874cf474f360faa01ed50cf39ea")]
    [InlineData("1.0.1", "steamaddon-runtime-v1.0.1", "wrong.zip", "717c57ea1812a874cf474f360faa01ed50cf39ea")]
    [InlineData("1.0.1", "steamaddon-runtime-v1.0.1", "ClawHUDRuntime.zip", "0000000000000000000000000000000000000000")]
    public async Task EmbeddedManifestIdentityMismatch_IsRejected(string runtimeVersion, string tag, string asset, string sourceCommit)
    {
        using var fixture = new Fixture();
        var zip = CreateZip(includeForbidden: false, manifestRuntimeVersion: runtimeVersion, manifestTag: tag, manifestAsset: asset, manifestSourceCommit: sourceCommit);
        var handler = new RecordingHandler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(zip) }));
        var result = await fixture.CreateAcquirer(handler, zip).AcquireAsync(CancellationToken.None);

        Assert.False(result.IsReady);
        Assert.Equal(ClawHudRuntimeAcquisitionFailure.ManifestInvalid, result.Failure);
    }

    [Fact]
    public async Task MissingRequiredPayload_IsRejected()
    {
        using var fixture = new Fixture();
        var zip = CreateZip(includeForbidden: false, omitRequired: "ClawHUD.EcHelper.exe");
        var handler = new RecordingHandler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(zip) }));
        var result = await fixture.CreateAcquirer(handler, zip).AcquireAsync(CancellationToken.None);

        Assert.False(result.IsReady);
        Assert.Equal(ClawHudRuntimeAcquisitionFailure.PayloadInvalid, result.Failure);
    }

    [Fact]
    public async Task ForbiddenPayload_IsRejected()
    {
        using var fixture = new Fixture();
        var zip = CreateZip(includeForbidden: true);
        var handler = new RecordingHandler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(zip) }));
        var result = await fixture.CreateAcquirer(handler, zip).AcquireAsync(CancellationToken.None);

        Assert.False(result.IsReady);
        Assert.Equal(ClawHudRuntimeAcquisitionFailure.PayloadInvalid, result.Failure);
    }

    [Fact]
    public async Task MissingPayloadRoot_IsRejected()
    {
        using var fixture = new Fixture();
        var zip = CreateZipWithoutPayloadRoot();
        var handler = new RecordingHandler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(zip) }));
        var result = await fixture.CreateAcquirer(handler, zip).AcquireAsync(CancellationToken.None);

        Assert.False(result.IsReady);
        Assert.Equal(ClawHudRuntimeAcquisitionFailure.ArchiveInvalid, result.Failure);
    }

    [Fact]
    public async Task InvalidExactVersionDirectory_IsKeptUntilReplacementValidates()
    {
        using var fixture = new Fixture();
        var runtimeDirectory = AddonDataPaths.ResolveClawHudRuntimeVersionDirectory(fixture.InstallRoot, RuntimeVersion);
        Directory.CreateDirectory(runtimeDirectory);
        var sentinel = Path.Combine(runtimeDirectory, "sentinel.txt");
        File.WriteAllText(sentinel, "keep until valid replacement");
        var badZip = Encoding.UTF8.GetBytes("not-a-zip");
        var handler = new RecordingHandler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(badZip) }));
        var result = await fixture.CreateAcquirer(handler).AcquireAsync(CancellationToken.None);

        Assert.False(result.IsReady);
        Assert.Equal(ClawHudRuntimeAcquisitionFailure.HashMismatch, result.Failure);
        Assert.Equal("keep until valid replacement", File.ReadAllText(sentinel));
    }

    private static void CreatePayload(string root, bool includeForbidden)
    {
        Directory.CreateDirectory(root);
        foreach (var path in RequiredFiles())
        {
            var fullPath = Path.Combine(root, path.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            File.WriteAllText(fullPath, path == "runtime-manifest.json" ? Manifest(null) : path);
        }
        if (includeForbidden) File.WriteAllText(Path.Combine(root, "Setup.exe"), "forbidden");
    }

    private static byte[] CreateZip(
        bool includeForbidden,
        string? embeddedSha256 = null,
        string? omitRequired = null,
        string? manifestSourceCommit = null,
        string manifestRuntimeVersion = RuntimeVersion,
        string manifestTag = "steamaddon-runtime-v1.0.1",
        string manifestAsset = "ClawHUDRuntime.zip")
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var path in RequiredFiles().Where(path => !string.Equals(path, omitRequired, StringComparison.Ordinal)))
            {
                var entry = archive.CreateEntry($"clawhud/{path}");
                entry.LastWriteTime = new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);
                using var writer = new StreamWriter(entry.Open());
                writer.Write(path == "runtime-manifest.json" ? Manifest(embeddedSha256, manifestSourceCommit, manifestRuntimeVersion, manifestTag, manifestAsset) : path);
            }
            if (includeForbidden)
            {
                var entry = archive.CreateEntry("clawhud/Setup.exe");
                entry.LastWriteTime = new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);
                using var writer = new StreamWriter(entry.Open());
                writer.Write("forbidden");
            }
        }
        return stream.ToArray();
    }

    private static byte[] CreateUnsafeZip()
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = archive.CreateEntry("clawhud/../escape.txt");
            entry.LastWriteTime = new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);
            using var writer = new StreamWriter(entry.Open());
            writer.Write("escape");
        }
        return stream.ToArray();
    }

    private static byte[] CreateAbsoluteZip()
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = archive.CreateEntry("/absolute.txt");
            entry.LastWriteTime = new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);
            using var writer = new StreamWriter(entry.Open());
            writer.Write("absolute");
        }
        return stream.ToArray();
    }

    private static byte[] CreateZipWithoutPayloadRoot()
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = archive.CreateEntry("ClawHUD.exe");
            entry.LastWriteTime = new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);
            using var writer = new StreamWriter(entry.Open());
            writer.Write("wrong-root");
        }
        return stream.ToArray();
    }

    private static string Manifest(
        string? sha256,
        string? sourceCommit = null,
        string runtimeVersion = RuntimeVersion,
        string tag = "steamaddon-runtime-v1.0.1",
        string asset = "ClawHUDRuntime.zip") => $$"""
        {
          "schema_version": 1,
          "runtime_version": "{{runtimeVersion}}",
          "tag": "{{tag}}",
          "source_commit": "{{sourceCommit ?? SourceCommit}}",
          "asset": "{{asset}}",
          "sha256": {{(sha256 is null ? "null" : $"\"{sha256}\"")}}
        }
        """;

    private static string[] RequiredFiles() =>
    [
        "ClawHUD.exe", "ClawHUD.EcHelper.exe", "PresentMonAPI2Loader.dll", "velopack_libc.dll", "LICENSE",
        "THIRD-PARTY-NOTICES.md", "fonts/Unispace.otf", "fonts/Unispace-LICENSE.txt", "runtime/ClawHUD.PresentMonRuntime.msi", "runtime-manifest.json",
    ];

    private sealed class Fixture : IDisposable
    {
        internal string Root { get; } = Path.Combine(Path.GetTempPath(), $"clawhud-acquirer-{Guid.NewGuid():N}");
        internal string InstallRoot { get; }
        internal string DataRoot => AddonDataPaths.ResolveDataRoot(InstallRoot);
        private string LockPath { get; }

        internal Fixture()
        {
            InstallRoot = Path.Combine(Root, "install");
            Directory.CreateDirectory(InstallRoot);
            LockPath = Path.Combine(Root, "clawhud.lock.json");
            File.WriteAllText(LockPath, $$"""
                {
                  "schema_version": 1,
                  "runtime_version": "1.0.1",
                  "tag": "steamaddon-runtime-v1.0.1",
                  "asset": "ClawHUDRuntime.zip",
                  "source_commit": "717c57ea1812a874cf474f360faa01ed50cf39ea",
                  "sha256": "{{Convert.ToHexString(SHA256.HashData(CreateZip(false))).ToLowerInvariant()}}"
                }
                """);
        }

        internal ClawHudRuntimeAcquirer CreateAcquirer(HttpMessageHandler handler, byte[]? responseZip = null)
        {
            if (responseZip is not null)
            {
                File.WriteAllText(LockPath, $$"""
                    {
                      "schema_version": 1,
                      "runtime_version": "1.0.1",
                      "tag": "steamaddon-runtime-v1.0.1",
                      "asset": "ClawHUDRuntime.zip",
                      "source_commit": "717c57ea1812a874cf474f360faa01ed50cf39ea",
                      "sha256": "{{Convert.ToHexString(SHA256.HashData(responseZip)).ToLowerInvariant()}}"
                    }
                    """);
            }
            return new ClawHudRuntimeAcquirer(new HttpClient(handler), LockPath, InstallRoot);
        }

        internal void SetLock(string contents) => File.WriteAllText(LockPath, contents);

        public void Dispose()
        {
            try { if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true); }
            catch { }
        }
    }

    private sealed class RecordingHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> responder) : HttpMessageHandler
    {
        internal int RequestCount { get; private set; }
        internal Uri? RequestedUri { get; private set; }
        internal string? ResponseContentType { get; set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            RequestedUri = request.RequestUri;
            var response = await responder(request);
            if (ResponseContentType is not null) response.Content.Headers.ContentType = new MediaTypeHeaderValue(ResponseContentType);
            return response;
        }
    }
}
