using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using SteamInputAddonforClaw.HidHide;
using SteamInputAddonforClaw.Prerequisites;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class PrerequisiteInstallerAcquisitionTests
{
    [Fact]
    public async Task MatchingDownloadWritesVerifiedStagingFile()
    {
        var payload = "verified installer"u8.ToArray();
        var root = CreateDirectory();
        var descriptor = Descriptor(payload);
        try
        {
            using var acquisition = CreateAcquisition(payload);
            var result = await acquisition.AcquireAsync(descriptor, root, CancellationToken.None);

            Assert.True(result.Succeeded);
            Assert.Equal(Path.Combine(root, descriptor.InstallerFileName), result.InstallerPath);
            Assert.Equal(payload, await File.ReadAllBytesAsync(result.InstallerPath!));
            Assert.Equal(payload.Length, result.BytesWritten);
        }
        finally
        {
            PrerequisiteInstallerAcquisition.TryDeleteStagedInstaller(Path.Combine(root, descriptor.InstallerFileName));
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task HashMismatchNeverReturnsLaunchPath()
    {
        var root = CreateDirectory();
        var descriptor = Descriptor("expected"u8.ToArray());
        try
        {
            using var acquisition = CreateAcquisition("tampered"u8.ToArray());
            var result = await acquisition.AcquireAsync(descriptor, root, CancellationToken.None);

            Assert.False(result.Succeeded);
            Assert.Null(result.InstallerPath);
            Assert.Equal("InstallerHashMismatch", result.Reason);
            Assert.False(File.Exists(Path.Combine(root, descriptor.InstallerFileName)));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task TruncatedDownloadFailsByHash()
    {
        var root = CreateDirectory();
        var descriptor = Descriptor("complete payload"u8.ToArray());
        try
        {
            using var acquisition = CreateAcquisition("complete"u8.ToArray());
            var result = await acquisition.AcquireAsync(descriptor, root, CancellationToken.None);

            Assert.False(result.Succeeded);
            Assert.Equal("InstallerHashMismatch", result.Reason);
            Assert.Null(result.InstallerPath);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task HttpFailureDoesNotCreateStagingFile()
    {
        var root = CreateDirectory();
        var descriptor = Descriptor("payload"u8.ToArray());
        try
        {
            using var acquisition = new PrerequisiteInstallerAcquisition(new HttpClient(new FakeHandler(HttpStatusCode.NotFound, [])));
            var result = await acquisition.AcquireAsync(descriptor, root, CancellationToken.None);

            Assert.False(result.Succeeded);
            Assert.Equal("InstallerDownloadHttpFailure", result.Reason);
            Assert.Null(result.InstallerPath);
            Assert.False(File.Exists(Path.Combine(root, descriptor.InstallerFileName)));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task StagingWriteFailureDoesNotReturnExecutablePath()
    {
        var root = Path.Combine(Path.GetTempPath(), "prerequisite-staging-file-" + Guid.NewGuid().ToString("N"));
        File.WriteAllText(root, "not a directory");
        try
        {
            using var acquisition = CreateAcquisition("payload"u8.ToArray());
            var result = await acquisition.AcquireAsync(Descriptor("payload"u8.ToArray()), root, CancellationToken.None);

            Assert.False(result.Succeeded);
            Assert.Equal("InstallerStagingFailed", result.Reason);
            Assert.Null(result.InstallerPath);
        }
        finally { File.Delete(root); }
    }

    [Fact]
    public async Task ExistingStagingFileIsNeverTrustedAndIsReplaced()
    {
        var payload = "new verified payload"u8.ToArray();
        var root = CreateDirectory();
        var descriptor = Descriptor(payload);
        var stagingPath = Path.Combine(root, descriptor.InstallerFileName);
        await File.WriteAllBytesAsync(stagingPath, "stale payload"u8.ToArray());
        try
        {
            using var acquisition = CreateAcquisition(payload);
            var result = await acquisition.AcquireAsync(descriptor, root, CancellationToken.None);

            Assert.True(result.Succeeded);
            Assert.Equal(payload, await File.ReadAllBytesAsync(stagingPath));
        }
        finally
        {
            PrerequisiteInstallerAcquisition.TryDeleteStagedInstaller(stagingPath);
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void LaunchValidationUsesThePinnedHash()
    {
        var root = CreateDirectory();
        var path = Path.Combine(root, "installer.exe");
        var payload = "launch payload"u8.ToArray();
        var hash = Convert.ToHexString(SHA256.HashData(payload));
        try
        {
            File.WriteAllBytes(path, payload);
            Assert.True(PrerequisiteInstallerAcquisition.HasExpectedSha256(path, hash));
            File.WriteAllText(path, "tampered");
            Assert.False(PrerequisiteInstallerAcquisition.HasExpectedSha256(path, hash));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void MetadataDescriptorMatchesHidHideMetadata()
    {
        var descriptor = HidHidePackageMetadata.InstallerDescriptor;

        Assert.Equal("HidHide", descriptor.Component);
        Assert.Equal(HidHidePackageMetadata.BundledVersion, descriptor.PinnedVersion);
        Assert.Equal(HidHidePackageMetadata.InstallerFileName, descriptor.InstallerFileName);
        Assert.Equal(HidHidePackageMetadata.InstallerDownloadUri, descriptor.DownloadUri);
        Assert.Equal(HidHidePackageMetadata.InstallerSha256, descriptor.InstallerSha256);
    }

    private static PrerequisiteInstallerAcquisition CreateAcquisition(byte[] payload) =>
        new(new HttpClient(new FakeHandler(HttpStatusCode.OK, payload)));

    private static PrerequisiteInstallerDescriptor Descriptor(byte[] expectedPayload) => new(
        "test",
        new Version(1, 0, 0, 0),
        "test-installer.exe",
        new Uri("https://example.test/test-installer.exe"),
        Convert.ToHexString(SHA256.HashData(expectedPayload)));

    private static string CreateDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "prerequisite-staging-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private sealed class FakeHandler(HttpStatusCode statusCode, byte[] payload) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(statusCode) { Content = new ByteArrayContent(payload) });
    }
}
