using Windows.Management.Deployment;
using SteamInputAddonforClaw.Diagnostics;

namespace SteamInputAddonforClaw.Xbox;

internal sealed record XboxInstalledPackageLocation(string Kind, string RootPath);

internal sealed record XboxInstalledPackage(
    string PackageName,
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
    XboxInstalledPackageEnumeration Enumerate(CancellationToken cancellationToken);
}

internal sealed class WindowsXboxInstalledPackageSource : IXboxInstalledPackageSource
{
    public XboxInstalledPackageEnumeration Enumerate(CancellationToken cancellationToken)
    {
        var packages = new List<XboxInstalledPackage>();
        try
        {
            var installedPackages = new PackageManager().FindPackagesForUser(string.Empty);
            foreach (var package in installedPackages)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    var id = package.Id;
                    var locations = new List<XboxInstalledPackageLocation>();
                    var locationFailures = new List<string>();
                    try { AddLocation(locations, "Effective", package.EffectiveLocation?.Path); }
                    catch (Exception exception) { locationFailures.Add($"Effective location: {MicrosoftGameConfigReader.Describe(exception)}"); }
                    try { AddLocation(locations, "Installed", package.InstalledLocation?.Path); }
                    catch (Exception exception) { locationFailures.Add($"Installed location: {MicrosoftGameConfigReader.Describe(exception)}"); }

                    packages.Add(new XboxInstalledPackage(
                        id.Name,
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
                        "<Package metadata unavailable>", string.Empty, string.Empty, string.Empty, [],
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
            return new(packages, false, unavailable, MicrosoftGameConfigReader.Describe(exception));
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
    int SkippedPackageCount = 0);

internal sealed class XboxInstalledGameCatalog(IXboxInstalledPackageSource? packageSource = null)
{
    private const long MaximumConfigBytes = 2 * 1024 * 1024;
    private readonly IXboxInstalledPackageSource _packageSource = packageSource ?? new WindowsXboxInstalledPackageSource();

    internal async Task<XboxInstalledGameCatalogResult> ScanAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        AppLog.Info("XboxCatalog", "Installed XBOX game catalog scan requested.",
            ("Api", "PackageManager.FindPackagesForUser(current user)"));

        var enumeration = await Task.Run(() => _packageSource.Enumerate(cancellationToken), cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        if (!enumeration.Succeeded)
        {
            var outcome = enumeration.Unavailable ? XboxInstalledGameCatalogOutcome.Unavailable : XboxInstalledGameCatalogOutcome.Failed;
            AppLog.Warn("XboxCatalog", "Current-user package enumeration failed.", null,
                ("Outcome", outcome), ("Reason", enumeration.FailureReason));
            return new(outcome, [], enumeration.FailureReason);
        }

        var games = new List<XboxInstalledGameCatalogEntry>();
        var seenPackages = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seenGameKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var skippedPackageCount = 0;

        foreach (var package in enumeration.Packages)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var packageObservation = string.IsNullOrWhiteSpace(package.PackageFullName)
                ? $"{package.PackageName}|{package.PackageFamilyName}|{string.Join('|', package.Locations.Select(location => location.RootPath))}"
                : package.PackageFullName;
            if (!seenPackages.Add(packageObservation))
                continue;

            if (package.MetadataFailure is { } metadataFailure)
            {
                skippedPackageCount++;
                AppLog.Debug("XboxCatalog", "Package metadata could not be read; package skipped.",
                    ("Package", PackageIdentity(package)), ("Reason", metadataFailure));
                continue;
            }

            var sawFailure = !string.IsNullOrWhiteSpace(package.LocationFailure);
            var foundGame = false;
            foreach (var location in DistinctLocations(package.Locations))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var configPath = Path.Combine(location.RootPath, "MicrosoftGame.config");
                var read = await ReadConfigAsync(configPath, cancellationToken).ConfigureAwait(false);
                if (!read.Found)
                {
                    if (read.FailureReason is { } locationFailure)
                    {
                        sawFailure = true;
                        AppLog.Debug("XboxCatalog", "Package location could not be scanned.",
                            ("Package", PackageIdentity(package)), ("LocationKind", location.Kind),
                            ("ConfigPath", configPath), ("Reason", locationFailure));
                    }
                    continue;
                }

                if (read.Config is not { } config)
                {
                    sawFailure = true;
                    AppLog.Debug("XboxCatalog", "MicrosoftGame.config candidate rejected; scanning the next package location.",
                        ("Package", PackageIdentity(package)), ("ConfigPath", configPath),
                        ("Reason", read.FailureReason));
                    continue;
                }

                var identity = new XboxGameIdentity(
                    XboxGameIdentity.CreateKey(config.StoreId, package.PackageFamilyName, config),
                    FirstNonBlank(config.DefaultDisplayName, package.DisplayName, package.PackageName, PackageIdentity(package)),
                    config.StoreId,
                    BlankToNull(package.PackageFamilyName));

                if (seenGameKeys.Add(identity.Key))
                {
                    games.Add(new(identity));
                    AppLog.Info("XboxCatalog", "Installed XBOX game identity accepted.",
                        ("Key", identity.Key), ("PackageFullName", package.PackageFullName),
                        ("PackageFamilyName", identity.PackageFamilyName), ("StoreId", identity.StoreId));
                }
                foundGame = true;
                break;
            }

            if (!foundGame && (sawFailure || package.Locations.Count == 0))
            {
                skippedPackageCount++;
                if (package.Locations.Count == 0 && string.IsNullOrWhiteSpace(package.LocationFailure))
                    AppLog.Debug("XboxCatalog", "Package had no usable Effective or Installed location.",
                        ("Package", PackageIdentity(package)));
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        AppLog.Info("XboxCatalog", "Installed XBOX game catalog scan completed.",
            ("Outcome", XboxInstalledGameCatalogOutcome.Completed),
            ("InstalledGameCount", games.Count), ("SkippedPackageCount", skippedPackageCount));
        return new(XboxInstalledGameCatalogOutcome.Completed, games, null, skippedPackageCount);
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
            return new(false, null, null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            return new(false, null, MicrosoftGameConfigReader.Describe(exception));
        }

        await using (stream.ConfigureAwait(false))
        {
            try
            {
                if (stream.Length > MaximumConfigBytes)
                    return new(true, null, $"MicrosoftGame.config exceeds the {MaximumConfigBytes:N0}-byte limit.");

                var result = await MicrosoftGameConfigReader.ReadAsync(stream, cancellationToken).ConfigureAwait(false);
                return new(true, result.Config, result.FailureReason);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                return new(true, null, MicrosoftGameConfigReader.Describe(exception));
            }
        }
    }

    private static string PackageIdentity(XboxInstalledPackage package) =>
        FirstNonBlank(package.PackageFullName, package.PackageFamilyName, package.PackageName, "<Unknown package>");

    private static string FirstNonBlank(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? "<Unavailable>";

    private static string? BlankToNull(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private sealed record ConfigRead(bool Found, MicrosoftGameConfig? Config, string? FailureReason);
}
