using Windows.Management.Deployment;
using SteamInputAddonforClaw.Diagnostics;

namespace SteamInputAddonforClaw.Xbox;

internal sealed record XboxInstalledPackageLocation(string Kind, string RootPath);

internal sealed record XboxInstalledPackage(
    string PackageName,
    string PackagePublisher,
    string PackageFullName,
    string PackageFamilyName,
    string DisplayName,
    IReadOnlyList<XboxInstalledPackageLocation> Locations,
    string? MetadataFailure = null,
    string? LocationFailure = null);

internal sealed record XboxInstalledPackageEnumeration(
    IReadOnlyList<XboxInstalledPackage> Packages,
    bool Succeeded,
    bool Unavailable,
    string? FailureReason);

internal interface IXboxInstalledPackageSource
{
    XboxInstalledPackageEnumeration FindForGame(MicrosoftGameConfig config, CancellationToken cancellationToken);
}

internal sealed class WindowsXboxInstalledPackageSource : IXboxInstalledPackageSource
{
    public XboxInstalledPackageEnumeration FindForGame(MicrosoftGameConfig config, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var packages = new List<XboxInstalledPackage>();
            var installedPackages = new PackageManager().FindPackagesForUser(
                string.Empty, config.IdentityName, config.IdentityPublisher);
            foreach (var package in installedPackages)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    var id = package.Id;
                    if (!string.Equals(id.Name, config.IdentityName, StringComparison.OrdinalIgnoreCase)
                        || !string.Equals(id.Publisher, config.IdentityPublisher, StringComparison.OrdinalIgnoreCase))
                        continue;

                    var locations = new List<XboxInstalledPackageLocation>();
                    var locationFailures = new List<string>();
                    try { AddLocation(locations, "Effective", package.EffectiveLocation?.Path); }
                    catch (Exception exception) { locationFailures.Add($"Effective location: {MicrosoftGameConfigReader.Describe(exception)}"); }
                    try { AddLocation(locations, "Installed", package.InstalledLocation?.Path); }
                    catch (Exception exception) { locationFailures.Add($"Installed location: {MicrosoftGameConfigReader.Describe(exception)}"); }

                    packages.Add(new XboxInstalledPackage(
                        id.Name,
                        id.Publisher,
                        id.FullName,
                        id.FamilyName,
                        package.DisplayName,
                        locations,
                        LocationFailure: locationFailures.Count == 0 ? null : string.Join("; ", locationFailures)));
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    packages.Add(new XboxInstalledPackage(
                        config.IdentityName, config.IdentityPublisher, string.Empty, string.Empty, string.Empty, [],
                        MetadataFailure: MicrosoftGameConfigReader.Describe(exception)));
                }
            }

            return new(packages, true, false, null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            var unavailable = exception is NotSupportedException or TypeLoadException or EntryPointNotFoundException or DllNotFoundException;
            return new([], false, unavailable, MicrosoftGameConfigReader.Describe(exception));
        }
    }

    private static void AddLocation(List<XboxInstalledPackageLocation> locations, string kind, string? path)
    {
        if (string.IsNullOrWhiteSpace(path)
            || locations.Any(location => string.Equals(location.RootPath, path, StringComparison.OrdinalIgnoreCase)))
            return;
        locations.Add(new(kind, path));
    }
}

internal enum XboxInstalledGameCatalogOutcome
{
    Completed,
    Unavailable,
    Failed,
}

internal sealed record XboxInstalledGameCatalogEntry(
    XboxGameIdentity Identity);

internal sealed record XboxInstalledGameCatalogResult(
    XboxInstalledGameCatalogOutcome Outcome,
    IReadOnlyList<XboxInstalledGameCatalogEntry> Games,
    string? FailureReason,
    int SkippedGameCount = 0);

internal sealed class XboxInstalledGameCatalog(
    IXboxInstalledGameFolderSource? folderSource = null,
    IXboxInstalledPackageSource? packageSource = null)
{
    private const long MaximumConfigBytes = 2 * 1024 * 1024;
    private readonly IXboxInstalledGameFolderSource _folderSource = folderSource ?? new WindowsXboxInstalledGameFolderSource();
    private readonly IXboxInstalledPackageSource _packageSource = packageSource ?? new WindowsXboxInstalledPackageSource();

    internal async Task<XboxInstalledGameCatalogResult> ScanAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        AppLog.Info("XboxCatalog", "Installed XBOX game catalog scan requested.",
            ("Discovery", "GDK flat-file game folders"), ("Marker", ".GamingRoot"));

        var enumeration = await Task.Run(() => _folderSource.Enumerate(cancellationToken), cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        if (!enumeration.Succeeded)
        {
            var outcome = enumeration.Unavailable ? XboxInstalledGameCatalogOutcome.Unavailable : XboxInstalledGameCatalogOutcome.Failed;
            AppLog.Warn("XboxCatalog", "Xbox game-folder discovery failed.", null,
                ("Outcome", outcome), ("Reason", enumeration.FailureReason));
            return new(outcome, [], enumeration.FailureReason);
        }

        var games = new List<XboxInstalledGameCatalogEntry>();
        var seenGameDirectories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seenGameKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var skippedGameCount = enumeration.SkippedFolderCount;
        var packageLookupFailureCount = 0;
        var unavailablePackageLookup = false;
        var failureReasons = new List<string>();

        foreach (var gameFolder in enumeration.Folders)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string normalizedGameDirectory;
            try
            {
                normalizedGameDirectory = Path.GetFullPath(gameFolder.GameDirectory);
            }
            catch (Exception exception) when (exception is ArgumentException or IOException or NotSupportedException)
            {
                skippedGameCount++;
                continue;
            }
            if (!seenGameDirectories.Add(normalizedGameDirectory)) continue;

            var read = await ReadConfigAsync(gameFolder.ConfigPath, cancellationToken).ConfigureAwait(false);
            if (read.Config is not { } config)
            {
                skippedGameCount++;
                AppLog.Debug("XboxCatalog", "GDK game folder has no usable MicrosoftGame.config; folder skipped.",
                    ("GameDirectory", normalizedGameDirectory), ("ConfigPath", gameFolder.ConfigPath),
                    ("Reason", read.FailureReason ?? "Missing config"));
                continue;
            }

            var registration = _packageSource.FindForGame(config, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (!registration.Succeeded)
            {
                packageLookupFailureCount++;
                unavailablePackageLookup |= registration.Unavailable;
                if (registration.FailureReason is { Length: > 0 } failureReason)
                    failureReasons.Add(failureReason);

                // Keep the already persisted Store ID key stable when Windows package lookup is
                // temporarily unavailable. A successful lookup returning no matching package is
                // handled below as a stale/uninstalled folder and is never surfaced.
                if (!string.IsNullOrWhiteSpace(config.StoreId))
                    AddGame(games, seenGameKeys, config, null, normalizedGameDirectory);
                else
                    skippedGameCount++;
                continue;
            }
            var matchingPackage = FindMatchingRegisteredPackage(config, gameFolder.GameDirectory, gameFolder.ConfigPath, registration.Packages);
            if (matchingPackage is null)
            {
                skippedGameCount++;
                AppLog.Debug("XboxCatalog", "Xbox game folder has no registered package at its discovered location; stale folder skipped.",
                    ("GameDirectory", normalizedGameDirectory), ("IdentityName", config.IdentityName));
                continue;
            }

            AddGame(games, seenGameKeys, config, matchingPackage, normalizedGameDirectory);
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (games.Count == 0 && packageLookupFailureCount > 0)
        {
            var outcome = unavailablePackageLookup
                ? XboxInstalledGameCatalogOutcome.Unavailable
                : XboxInstalledGameCatalogOutcome.Failed;
            var reason = string.Join("; ", failureReasons.Distinct(StringComparer.Ordinal));
            AppLog.Warn("XboxCatalog", "Targeted Xbox package lookup failed for every candidate game.", null,
                ("Outcome", outcome), ("FailedLookupCount", packageLookupFailureCount), ("Reason", reason));
            return new(outcome, [], string.IsNullOrEmpty(reason) ? "Targeted package lookup failed." : reason, skippedGameCount);
        }

        AppLog.Info("XboxCatalog", "Installed XBOX game catalog scan completed.",
            ("Outcome", XboxInstalledGameCatalogOutcome.Completed),
            ("InstalledGameCount", games.Count), ("SkippedGameCount", skippedGameCount),
            ("TargetedPackageLookupFailureCount", packageLookupFailureCount));
        return new(XboxInstalledGameCatalogOutcome.Completed, games, null, skippedGameCount);
    }

    private static void AddGame(
        List<XboxInstalledGameCatalogEntry> games,
        HashSet<string> seenGameKeys,
        MicrosoftGameConfig config,
        XboxInstalledPackage? package,
        string gameDirectory)
    {
        var packageFamilyName = BlankToNull(package?.PackageFamilyName);
        var storeId = BlankToNull(config.StoreId);
        var identity = new XboxGameIdentity(
            XboxGameIdentity.CreateKey(storeId, packageFamilyName, config),
            FirstNonBlank(config.DefaultDisplayName, package?.DisplayName, package?.PackageName, config.IdentityName),
            storeId,
            packageFamilyName);

        if (!seenGameKeys.Add(identity.Key)) return;
        games.Add(new(identity));
        AppLog.Info("XboxCatalog", "Installed XBOX game identity accepted.",
            ("Key", identity.Key), ("GameDirectory", gameDirectory),
            ("PackageFamilyName", identity.PackageFamilyName), ("StoreId", identity.StoreId));
    }

    private static XboxInstalledPackage? FindMatchingRegisteredPackage(
        MicrosoftGameConfig config,
        string gameDirectory,
        string configPath,
        IReadOnlyList<XboxInstalledPackage> packages)
    {
        foreach (var package in packages)
        {
            if (package.MetadataFailure is not null
                || !string.Equals(package.PackageName, config.IdentityName, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(package.PackagePublisher, config.IdentityPublisher, StringComparison.OrdinalIgnoreCase))
                continue;

            foreach (var location in DistinctLocations(package.Locations))
                if (IsRegisteredGameLocation(gameDirectory, configPath, location.RootPath))
                    return package;
        }
        return null;
    }

    private static bool IsRegisteredGameLocation(string gameDirectory, string configPath, string registeredLocation)
    {
        try
        {
            var fullGameDirectory = Path.GetFullPath(gameDirectory);
            var fullRegisteredLocation = Path.TrimEndingDirectorySeparator(Path.GetFullPath(registeredLocation));
            var configDirectory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetDirectoryName(configPath)!));
            return string.Equals(fullRegisteredLocation, fullGameDirectory, StringComparison.OrdinalIgnoreCase)
                || string.Equals(fullRegisteredLocation, configDirectory, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or NotSupportedException or System.Security.SecurityException)
        {
            return false;
        }
    }

    internal static IReadOnlyList<XboxInstalledPackageLocation> DistinctLocations(
        IReadOnlyList<XboxInstalledPackageLocation> locations)
    {
        var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var distinctLocations = new List<XboxInstalledPackageLocation>(locations.Count);
        foreach (var location in locations)
            if (!string.IsNullOrWhiteSpace(location.RootPath) && roots.Add(location.RootPath))
                distinctLocations.Add(location);
        return distinctLocations;
    }

    private static async Task<ConfigRead> ReadConfigAsync(string configPath, CancellationToken cancellationToken)
    {
        FileStream stream;
        try
        {
            stream = new FileStream(configPath, FileMode.Open, FileAccess.Read,
                FileShare.Read | FileShare.Delete, 16 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
        }
        catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
        {
            return new(null, null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            return new(null, MicrosoftGameConfigReader.Describe(exception));
        }

        await using (stream.ConfigureAwait(false))
        {
            try
            {
                if (stream.Length > MaximumConfigBytes)
                    return new(null, $"MicrosoftGame.config exceeds the {MaximumConfigBytes:N0}-byte limit.");

                var result = await MicrosoftGameConfigReader.ReadAsync(stream, cancellationToken).ConfigureAwait(false);
                return new(result.Config, result.FailureReason);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                return new(null, MicrosoftGameConfigReader.Describe(exception));
            }
        }
    }

    private static string FirstNonBlank(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? "<Unavailable>";

    private static string? BlankToNull(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private sealed record ConfigRead(MicrosoftGameConfig? Config, string? FailureReason);
}
