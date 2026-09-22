using System.Net.Http;
using System.Security.Cryptography;
using SteamInputAddonforClaw.Diagnostics;

namespace SteamInputAddonforClaw.Prerequisites;

internal sealed record PrerequisiteInstallerDescriptor(
    string Component,
    Version PinnedVersion,
    string InstallerFileName,
    Uri DownloadUri,
    string InstallerSha256);

internal sealed record InstallerAcquisitionResult(
    bool Succeeded,
    string? InstallerPath,
    string Reason,
    long BytesWritten)
{
    public static InstallerAcquisitionResult Failure(string reason) => new(false, null, reason, 0);
}

internal sealed class PrerequisiteInstallerAcquisition : IDisposable
{
    internal static readonly TimeSpan DefaultTimeout = TimeSpan.FromMinutes(2);
    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;

    public PrerequisiteInstallerAcquisition(HttpClient? httpClient = null)
    {
        _ownsHttpClient = httpClient is null;
        _httpClient = httpClient ?? new HttpClient { Timeout = DefaultTimeout };
        if (_ownsHttpClient)
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("SteamInputAddonforClaw-prerequisite-setup");
    }

    public async Task<InstallerAcquisitionResult> AcquireAsync(
        PrerequisiteInstallerDescriptor descriptor,
        string stagingDirectory,
        CancellationToken cancellationToken)
    {
        var stagingPath = Path.Combine(stagingDirectory, descriptor.InstallerFileName);
        var temporaryPath = stagingPath + ".download-" + Guid.NewGuid().ToString("N");

        try
        {
            if (!IsValidDescriptor(descriptor) || !Directory.Exists(stagingDirectory))
                return InstallerAcquisitionResult.Failure("InstallerStagingFailed");

            File.Delete(stagingPath);
            AppLog.Info("PrerequisiteSetup", "Pinned prerequisite installer download started.",
                ("Component", descriptor.Component),
                ("PinnedVersion", descriptor.PinnedVersion),
                ("PinnedAssetUri", descriptor.DownloadUri),
                ("DownloadStarted", true));

            using var response = await _httpClient.GetAsync(
                descriptor.DownloadUri,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                AppLog.Warn("PrerequisiteSetup", "Pinned prerequisite installer download returned an unsuccessful HTTP status.", null,
                    ("Component", descriptor.Component),
                    ("PinnedVersion", descriptor.PinnedVersion),
                    ("StatusCode", (int)response.StatusCode),
                    ("Reason", "InstallerDownloadHttpFailure"));
                return InstallerAcquisitionResult.Failure("InstallerDownloadHttpFailure");
            }

            await using (var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
            await using (var destination = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, FileOptions.SequentialScan))
            {
                await source.CopyToAsync(destination, cancellationToken).ConfigureAwait(false);
                await destination.FlushAsync(cancellationToken).ConfigureAwait(false);
                destination.Flush(true);
            }

            var bytesWritten = new FileInfo(temporaryPath).Length;
            AppLog.Info("PrerequisiteSetup", "Pinned prerequisite installer download completed.",
                ("Component", descriptor.Component),
                ("PinnedVersion", descriptor.PinnedVersion),
                ("BytesWritten", bytesWritten),
                ("DownloadCompleted", true));

            if (!HasExpectedSha256(temporaryPath, descriptor.InstallerSha256))
            {
                AppLog.Warn("PrerequisiteSetup", "Pinned prerequisite installer hash validation failed.", null,
                    ("Component", descriptor.Component),
                    ("PinnedVersion", descriptor.PinnedVersion),
                    ("Reason", "InstallerHashMismatch"));
                return InstallerAcquisitionResult.Failure("InstallerHashMismatch");
            }

            File.Move(temporaryPath, stagingPath, true);
            AppLog.Info("PrerequisiteSetup", "Pinned prerequisite installer hash verified.",
                ("Component", descriptor.Component),
                ("PinnedVersion", descriptor.PinnedVersion),
                ("BytesWritten", bytesWritten),
                ("HashVerified", true));
            return new(true, stagingPath, "Acquired", bytesWritten);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            AppLog.Warn("PrerequisiteSetup", "Pinned prerequisite installer download timed out.", null,
                ("Component", descriptor.Component),
                ("PinnedVersion", descriptor.PinnedVersion),
                ("Reason", "InstallerDownloadTimedOut"));
            return InstallerAcquisitionResult.Failure("InstallerDownloadTimedOut");
        }
        catch (OperationCanceledException)
        {
            return InstallerAcquisitionResult.Failure("InstallerDownloadFailed");
        }
        catch (HttpRequestException exception)
        {
            AppLog.Warn("PrerequisiteSetup", "Pinned prerequisite installer download failed.", exception,
                ("Component", descriptor.Component),
                ("PinnedVersion", descriptor.PinnedVersion),
                ("Reason", "InstallerDownloadFailed"));
            return InstallerAcquisitionResult.Failure("InstallerDownloadFailed");
        }
        catch (IOException exception)
        {
            AppLog.Warn("PrerequisiteSetup", "Pinned prerequisite installer staging failed.", exception,
                ("Component", descriptor.Component),
                ("PinnedVersion", descriptor.PinnedVersion),
                ("Reason", "InstallerStagingFailed"));
            return InstallerAcquisitionResult.Failure("InstallerStagingFailed");
        }
        catch (UnauthorizedAccessException exception)
        {
            AppLog.Warn("PrerequisiteSetup", "Pinned prerequisite installer staging was denied.", exception,
                ("Component", descriptor.Component),
                ("PinnedVersion", descriptor.PinnedVersion),
                ("Reason", "InstallerStagingFailed"));
            return InstallerAcquisitionResult.Failure("InstallerStagingFailed");
        }
        finally
        {
            TryDeleteStagedInstaller(temporaryPath);
        }
    }

    internal static bool HasExpectedSha256(string path, string expectedHash)
    {
        if (!File.Exists(path)) return false;
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.SequentialScan);
            return string.Equals(Convert.ToHexString(SHA256.HashData(stream)), expectedHash, StringComparison.OrdinalIgnoreCase);
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }

    internal static void TryDeleteStagedInstaller(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        try { File.Delete(path); }
        catch (Exception exception)
        {
            AppLog.Warn("PrerequisiteSetup", "Prerequisite installer staging cleanup failed.", exception,
                ("InstallerPath", path),
                ("Reason", "InstallerStagingCleanupFailed"));
        }
    }

    public void Dispose()
    {
        if (_ownsHttpClient) _httpClient.Dispose();
    }

    private static bool IsValidDescriptor(PrerequisiteInstallerDescriptor descriptor) =>
        descriptor.DownloadUri.IsAbsoluteUri
        && descriptor.DownloadUri.Scheme == Uri.UriSchemeHttps
        && string.Equals(Path.GetFileName(descriptor.InstallerFileName), descriptor.InstallerFileName, StringComparison.Ordinal)
        && descriptor.InstallerSha256.Length == 64
        && descriptor.InstallerSha256.All(Uri.IsHexDigit);
}
