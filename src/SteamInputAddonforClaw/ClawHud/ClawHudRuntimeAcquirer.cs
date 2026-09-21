using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using SteamInputAddonforClaw.Diagnostics;
using SteamInputAddonforClaw.Install;

namespace SteamInputAddonforClaw.ClawHud;

internal enum ClawHudRuntimeAcquisitionFailure
{
    None,
    LockInvalid,
    Network,
    Cancelled,
    HashMismatch,
    ArchiveInvalid,
    ManifestInvalid,
    PayloadInvalid,
    Storage,
}

internal sealed record ClawHudRuntimeAcquisitionResult(
    bool IsReady,
    string RuntimeVersion,
    string? RuntimeDirectory,
    string? ExecutablePath,
    ClawHudRuntimeAcquisitionFailure Failure,
    string? FailureMessage)
{
    internal static ClawHudRuntimeAcquisitionResult Ready(ClawHudRuntimeLock runtimeLock, string directory) =>
        new(true, runtimeLock.RuntimeVersion, directory, Path.Combine(directory, "ClawHUD.exe"), ClawHudRuntimeAcquisitionFailure.None, null);

    internal static ClawHudRuntimeAcquisitionResult Failed(string version, ClawHudRuntimeAcquisitionFailure failure, string message) =>
        new(false, version, null, null, failure, message);
}

internal sealed class ClawHudRuntimeAcquirer
{
    private const string LockRelativePath = "Dependencies/ClawHUD/clawhud.lock.json";
    private const string PayloadRootName = "clawhud";
    private const string Category = "ClawHUD.Runtime";
    private static readonly SemaphoreSlim AcquisitionGate = new(1, 1);
    private static readonly string[] RequiredPayloadFiles =
    [
        "ClawHUD.exe",
        "ClawHUD.EcHelper.exe",
        "PresentMonAPI2Loader.dll",
        "velopack_libc.dll",
        "LICENSE",
        "THIRD-PARTY-NOTICES.md",
        "fonts/Unispace.otf",
        "fonts/Unispace-LICENSE.txt",
        "runtime/ClawHUD.PresentMonRuntime.msi",
        "runtime-manifest.json",
    ];
    private static readonly string[] ForbiddenPayloadFiles =
    [
        "ClawHUD.Settings.exe",
        "ClawHUD.Settings.dll",
        "ClawHUD.Settings.deps.json",
        "ClawHUD.Settings.runtimeconfig.json",
        "ClawHUD.Diag.exe",
        "Setup.exe",
        "releases.stable.json",
    ];
    private static readonly HashSet<string> ForbiddenRuntimeNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "coreclr.dll", "clrjit.dll", "hostfxr.dll", "hostpolicy.dll", "dotnet.exe",
    };

    private readonly HttpClient _httpClient;
    private readonly string _lockPath;
    private readonly string _rootAppDirectory;

    internal ClawHudRuntimeAcquirer(
        HttpClient httpClient,
        string? lockPath = null,
        string? rootAppDirectory = null)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _lockPath = lockPath ?? Path.Combine(AppContext.BaseDirectory, LockRelativePath.Replace('/', Path.DirectorySeparatorChar));
        _rootAppDirectory = rootAppDirectory ?? VelopackAppPaths.RootAppDirectory;
    }

    internal async Task<ClawHudRuntimeAcquisitionResult> AcquireAsync(CancellationToken cancellationToken)
    {
        ClawHudRuntimeLock runtimeLock;
        try
        {
            runtimeLock = ClawHudRuntimeLock.Load(_lockPath);
            AppLog.Info(Category, "Runtime lock loaded.", ("RuntimeVersion", runtimeLock.RuntimeVersion), ("Tag", runtimeLock.Tag), ("SourceCommit", runtimeLock.SourceCommit));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
        {
            AppLog.Warn(Category, "Runtime lock validation failed; acquisition skipped.", exception, ("Path", _lockPath));
            return ClawHudRuntimeAcquisitionResult.Failed("unknown", ClawHudRuntimeAcquisitionFailure.LockInvalid, exception.Message);
        }

        var runtimeDirectory = AddonDataPaths.ResolveClawHudRuntimeVersionDirectory(_rootAppDirectory, runtimeLock.RuntimeVersion);
        if (TryValidateInstalledRuntime(runtimeLock, runtimeDirectory, out _))
        {
            AppLog.Info(Category, "Runtime fast path validated.", ("RuntimeVersion", runtimeLock.RuntimeVersion), ("Path", runtimeDirectory));
            return ClawHudRuntimeAcquisitionResult.Ready(runtimeLock, runtimeDirectory);
        }

        try
        {
            await AcquisitionGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return ClawHudRuntimeAcquisitionResult.Failed(runtimeLock.RuntimeVersion, ClawHudRuntimeAcquisitionFailure.Cancelled, "Runtime acquisition was cancelled.");
        }
        try
        {
            if (TryValidateInstalledRuntime(runtimeLock, runtimeDirectory, out _))
            {
                AppLog.Info(Category, "Runtime fast path validated after acquisition serialization.", ("RuntimeVersion", runtimeLock.RuntimeVersion), ("Path", runtimeDirectory));
                return ClawHudRuntimeAcquisitionResult.Ready(runtimeLock, runtimeDirectory);
            }

            return await AcquireUnderGateAsync(runtimeLock, runtimeDirectory, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            AcquisitionGate.Release();
        }
    }

    private async Task<ClawHudRuntimeAcquisitionResult> AcquireUnderGateAsync(
        ClawHudRuntimeLock runtimeLock,
        string runtimeDirectory,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var runtimeRoot = AddonDataPaths.ResolveClawHudRuntimeRoot(_rootAppDirectory);
        var stagingRoot = Path.Combine(runtimeRoot, $"{runtimeLock.RuntimeVersion}.staging");
        var archivePath = Path.Combine(stagingRoot, runtimeLock.Asset);
        var extractionRoot = Path.Combine(stagingRoot, "extracted");

        try
        {
            Directory.CreateDirectory(runtimeRoot);
            DeleteOwnedStaging(stagingRoot);
            Directory.CreateDirectory(extractionRoot);

            AppLog.Info(Category, "Runtime download started.", ("RuntimeVersion", runtimeLock.RuntimeVersion), ("Tag", runtimeLock.Tag), ("Url", runtimeLock.DownloadUri));
            using (var response = await _httpClient.GetAsync(runtimeLock.DownloadUri, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false))
            {
                response.EnsureSuccessStatusCode();
                await using var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                await using var destination = new FileStream(archivePath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 128 * 1024, useAsync: true);
                await source.CopyToAsync(destination, cancellationToken).ConfigureAwait(false);
            }
            AppLog.Info(Category, "Runtime download completed.", ("RuntimeVersion", runtimeLock.RuntimeVersion), ("Path", archivePath));

            var actualHash = await ComputeSha256Async(archivePath, cancellationToken).ConfigureAwait(false);
            if (!string.Equals(actualHash, runtimeLock.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                return Fail(runtimeLock, ClawHudRuntimeAcquisitionFailure.HashMismatch, "Downloaded Runtime ZIP SHA-256 does not match the shipped lock.", ("Expected", runtimeLock.Sha256), ("Actual", actualHash));
            }
            AppLog.Info(Category, "Runtime hash verified.", ("RuntimeVersion", runtimeLock.RuntimeVersion), ("Sha256", actualHash));

            ExtractArchiveSafely(archivePath, extractionRoot);
            var payloadRoot = Path.Combine(extractionRoot, PayloadRootName);
            ValidatePayload(runtimeLock, payloadRoot);
            AppLog.Info(Category, "Runtime staging validated.", ("RuntimeVersion", runtimeLock.RuntimeVersion), ("Path", payloadRoot));

            if (Directory.Exists(runtimeDirectory)) Directory.Delete(runtimeDirectory, recursive: true);
            Directory.Move(payloadRoot, runtimeDirectory);
            AppLog.Info(Category, "Runtime adopted.", ("RuntimeVersion", runtimeLock.RuntimeVersion), ("Path", runtimeDirectory), ("ElapsedMs", stopwatch.Elapsed.TotalMilliseconds));
            return ClawHudRuntimeAcquisitionResult.Ready(runtimeLock, runtimeDirectory);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return Fail(runtimeLock, ClawHudRuntimeAcquisitionFailure.Cancelled, "Runtime acquisition was cancelled.");
        }
        catch (OperationCanceledException exception)
        {
            return Fail(runtimeLock, ClawHudRuntimeAcquisitionFailure.Network, "Runtime download timed out.", ("Reason", exception.Message));
        }
        catch (HttpRequestException exception)
        {
            return Fail(runtimeLock, ClawHudRuntimeAcquisitionFailure.Network, "Runtime download failed.", ("Reason", exception.Message));
        }
        catch (InvalidDataException exception)
        {
            var failure = exception.Message.Contains("manifest", StringComparison.OrdinalIgnoreCase)
                ? ClawHudRuntimeAcquisitionFailure.ManifestInvalid
                : exception.Message.Contains("required", StringComparison.OrdinalIgnoreCase) || exception.Message.Contains("forbidden", StringComparison.OrdinalIgnoreCase) || exception.Message.Contains("private .NET", StringComparison.OrdinalIgnoreCase)
                    ? ClawHudRuntimeAcquisitionFailure.PayloadInvalid
                    : ClawHudRuntimeAcquisitionFailure.ArchiveInvalid;
            return Fail(runtimeLock, failure, exception.Message);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return Fail(runtimeLock, ClawHudRuntimeAcquisitionFailure.Storage, "Runtime storage operation failed.", ("Reason", exception.Message));
        }
        finally
        {
            DeleteOwnedStaging(stagingRoot);
        }
    }

    private ClawHudRuntimeAcquisitionResult Fail(
        ClawHudRuntimeLock runtimeLock,
        ClawHudRuntimeAcquisitionFailure failure,
        string message,
        params (string Key, object? Value)[] fields)
    {
        var logFields = new (string Key, object? Value)[fields.Length + 3];
        logFields[0] = ("RuntimeVersion", runtimeLock.RuntimeVersion);
        logFields[1] = ("Failure", failure);
        logFields[2] = ("Reason", message);
        fields.CopyTo(logFields, 3);
        AppLog.Warn(Category, "Runtime acquisition failed.", null, logFields);
        return ClawHudRuntimeAcquisitionResult.Failed(runtimeLock.RuntimeVersion, failure, message);
    }

    private static bool TryValidateInstalledRuntime(ClawHudRuntimeLock runtimeLock, string runtimeDirectory, out string? failure)
    {
        failure = null;
        if (!Directory.Exists(runtimeDirectory))
        {
            failure = "Runtime directory does not exist.";
            return false;
        }

        try
        {
            ValidatePayload(runtimeLock, runtimeDirectory);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            failure = exception.Message;
            return false;
        }
    }

    private static void ValidatePayload(ClawHudRuntimeLock runtimeLock, string payloadRoot)
    {
        if (!Directory.Exists(payloadRoot)) throw new InvalidDataException("Runtime payload root is missing.");
        var manifestPath = Path.Combine(payloadRoot, "runtime-manifest.json");
        ValidateManifest(runtimeLock, manifestPath);

        foreach (var relativePath in RequiredPayloadFiles)
        {
            var path = CombinePayloadPath(payloadRoot, relativePath);
            if (!File.Exists(path)) throw new InvalidDataException($"Required Runtime payload file is missing: {relativePath}.");
        }

        foreach (var relativePath in ForbiddenPayloadFiles)
        {
            if (File.Exists(CombinePayloadPath(payloadRoot, relativePath))) throw new InvalidDataException($"Forbidden Standalone payload file is present: {relativePath}.");
        }

        foreach (var file in Directory.EnumerateFiles(payloadRoot, "*", SearchOption.AllDirectories))
        {
            if (ForbiddenRuntimeNames.Contains(Path.GetFileName(file))) throw new InvalidDataException($"Private .NET runtime file is present: {Path.GetRelativePath(payloadRoot, file)}.");
        }
    }

    private static void ValidateManifest(ClawHudRuntimeLock runtimeLock, string manifestPath)
    {
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(manifestPath));
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Embedded Runtime manifest is not an object.");
            var schemaVersion = ReadManifestInt32(root, "schema_version");
            var runtimeVersion = ReadManifestString(root, "runtime_version");
            var tag = ReadManifestString(root, "tag");
            var asset = ReadManifestString(root, "asset");
            var sourceCommit = ReadManifestString(root, "source_commit");
            if (!root.TryGetProperty("sha256", out var sha256) || sha256.ValueKind != JsonValueKind.Null) throw new InvalidDataException("Embedded Runtime manifest sha256 must be null for schema v1.");
            if (schemaVersion != runtimeLock.SchemaVersion || !string.Equals(runtimeVersion, runtimeLock.RuntimeVersion, StringComparison.Ordinal) ||
                !string.Equals(tag, runtimeLock.Tag, StringComparison.Ordinal) || !string.Equals(asset, runtimeLock.Asset, StringComparison.Ordinal) ||
                !string.Equals(sourceCommit, runtimeLock.SourceCommit, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Embedded Runtime manifest identity does not match the shipped lock.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("Embedded Runtime manifest is malformed.", exception);
        }
    }

    private static int ReadManifestInt32(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.Number || !property.TryGetInt32(out var value)) throw new InvalidDataException($"Embedded Runtime manifest field '{propertyName}' is invalid.");
        return value;
    }

    private static string ReadManifestString(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(property.GetString())) throw new InvalidDataException($"Embedded Runtime manifest field '{propertyName}' is invalid.");
        return property.GetString()!;
    }

    private static string CombinePayloadPath(string payloadRoot, string relativePath) => Path.Combine(payloadRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));

    private static void ExtractArchiveSafely(string archivePath, string extractionRoot)
    {
        using var archive = ZipFile.OpenRead(archivePath);
        var extractionRootFullPath = EnsureTrailingSeparator(Path.GetFullPath(extractionRoot));
        var payloadRootSeen = false;
        foreach (var entry in archive.Entries)
        {
            var normalized = entry.FullName.Replace('\\', '/');
            if (string.IsNullOrWhiteSpace(normalized) || normalized.StartsWith('/') || normalized.StartsWith("//", StringComparison.Ordinal) || normalized.Contains(':', StringComparison.Ordinal) || Path.IsPathRooted(normalized))
                throw new InvalidDataException($"Unsafe Runtime archive entry: {entry.FullName}");
            var segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length == 0 || segments.Any(segment => segment is "." or "..") || !string.Equals(segments[0], PayloadRootName, StringComparison.Ordinal))
                throw new InvalidDataException($"Runtime archive must contain only the top-level '{PayloadRootName}/' payload root.");
            payloadRootSeen = true;

            var destination = Path.GetFullPath(Path.Combine(extractionRoot, normalized.Replace('/', Path.DirectorySeparatorChar)));
            if (!destination.StartsWith(extractionRootFullPath, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException($"Runtime archive entry escapes extraction staging: {entry.FullName}");
            if (entry.FullName.EndsWith('/') || string.IsNullOrEmpty(entry.Name))
            {
                Directory.CreateDirectory(destination);
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            using var source = entry.Open();
            using var target = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            source.CopyTo(target);
        }

        if (!payloadRootSeen || !Directory.Exists(Path.Combine(extractionRoot, PayloadRootName))) throw new InvalidDataException("Runtime archive is missing the clawhud/ payload root.");
    }

    private static async Task<string> ComputeSha256Async(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 128 * 1024, useAsync: true);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static void DeleteOwnedStaging(string stagingRoot)
    {
        try { if (Directory.Exists(stagingRoot)) Directory.Delete(stagingRoot, recursive: true); }
        catch (Exception exception) { AppLog.Warn(Category, "Runtime staging cleanup failed.", exception, ("Path", stagingRoot)); }
    }

    private static string EnsureTrailingSeparator(string path) => path.EndsWith(Path.DirectorySeparatorChar) ? path : path + Path.DirectorySeparatorChar;
}
