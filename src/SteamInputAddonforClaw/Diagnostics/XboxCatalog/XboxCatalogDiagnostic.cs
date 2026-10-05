using System.Text;
using SteamInputAddonforClaw.Contracts.Frontend;
using Windows.Management.Deployment;

namespace SteamInputAddonforClaw.Diagnostics.XboxCatalog;

internal sealed record XboxCatalogPackageLocation(string Kind, string RootPath);

internal sealed record XboxCatalogPackage(
    string PackageName,
    string PackageFullName,
    string PackageFamilyName,
    string DisplayName,
    IReadOnlyList<XboxCatalogPackageLocation> Locations,
    string? MetadataFailure = null,
    string? LocationFailure = null);

internal sealed record XboxCatalogPackageEnumeration(
    IReadOnlyList<XboxCatalogPackage> Packages,
    bool Succeeded,
    bool Unavailable,
    string? FailureReason);

internal interface IXboxCatalogPackageSource
{
    XboxCatalogPackageEnumeration Enumerate(CancellationToken cancellationToken);
}

internal sealed class WindowsXboxCatalogPackageSource : IXboxCatalogPackageSource
{
    public XboxCatalogPackageEnumeration Enumerate(CancellationToken cancellationToken)
    {
        var packages = new List<XboxCatalogPackage>();
        try
        {
            var installedPackages = new PackageManager().FindPackagesForUser(string.Empty);
            foreach (var package in installedPackages)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    var id = package.Id;
                    var locations = new List<XboxCatalogPackageLocation>();
                    var locationFailures = new List<string>();
                    try { AddLocation(locations, "Effective", package.EffectiveLocation?.Path); }
                    catch (Exception exception) { locationFailures.Add($"Effective location: {MicrosoftGameConfigReader.Describe(exception)}"); }
                    try { AddLocation(locations, "Installed", package.InstalledLocation?.Path); }
                    catch (Exception exception) { locationFailures.Add($"Installed location: {MicrosoftGameConfigReader.Describe(exception)}"); }
                    packages.Add(new XboxCatalogPackage(
                        id.Name,
                        id.FullName,
                        id.FamilyName,
                        package.DisplayName,
                        locations,
                        LocationFailure: locationFailures.Count == 0 ? null : string.Join("; ", locationFailures)));
                }
                catch (Exception exception)
                {
                    packages.Add(new XboxCatalogPackage(
                        "<Package metadata unavailable>", string.Empty, string.Empty, string.Empty, [], MicrosoftGameConfigReader.Describe(exception)));
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
            var unavailable = exception is PlatformNotSupportedException or NotSupportedException or TypeLoadException or EntryPointNotFoundException;
            return new(packages, false, unavailable, MicrosoftGameConfigReader.Describe(exception));
        }
    }

    private static void AddLocation(List<XboxCatalogPackageLocation> locations, string kind, string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || locations.Any(location => string.Equals(location.RootPath, path, StringComparison.OrdinalIgnoreCase)))
            return;
        locations.Add(new XboxCatalogPackageLocation(kind, path));
    }
}

internal sealed class XboxCatalogDiagnostic(
    IXboxCatalogPackageSource? packageSource = null,
    string? logDirectory = null,
    Func<DateTimeOffset>? clock = null)
{
    internal const int MaximumUiGames = 12;
    internal const int MaximumUiFailures = 24;
    internal const int MaximumUiExecutablesPerGame = 12;
    private const int MaximumReportFailures = 256;
    private const int MaximumUiTextLength = 512;
    private const long MaximumConfigBytes = 2 * 1024 * 1024;
    private readonly IXboxCatalogPackageSource _packageSource = packageSource ?? new WindowsXboxCatalogPackageSource();
    private readonly string _logDirectory = logDirectory ?? AppLog.DirectoryPath;
    private readonly Func<DateTimeOffset> _clock = clock ?? (() => DateTimeOffset.Now);

    internal async Task<FrontendXboxCatalogDiagnosticResult> RunAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var capturedAt = _clock();
        AppLog.Info("XboxCatalogDiagnostic", "XBOX catalog diagnostic scan requested.",
            ("Api", "PackageManager.FindPackagesForUser(current user)"));

        var enumeration = await Task.Run(() => _packageSource.Enumerate(cancellationToken), cancellationToken).ConfigureAwait(false);
        var games = new List<DiagnosticGame>();
        var failures = new List<FrontendXboxCatalogDiagnosticFailure>();
        var seenPackages = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seenGameObservations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var accessiblePackageCount = 0;
        var configCandidateCount = 0;
        var parsedConfigCount = 0;
        var skippedOrFailedCount = 0;

        if (enumeration.FailureReason is { } enumerationFailure)
        {
            AddFailure(failures, "PackageEnumeration", "Current user", enumerationFailure);
            skippedOrFailedCount++;
            AppLog.Warn("XboxCatalogDiagnostic", "Current-user package enumeration failed.", null,
                ("Reason", enumerationFailure), ("PackagesReturned", enumeration.Packages.Count));
        }

        foreach (var package in enumeration.Packages)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var packageIdentity = PackageIdentity(package);
            var observationKey = string.IsNullOrWhiteSpace(package.PackageFullName)
                ? $"{package.PackageName}|{package.PackageFamilyName}|{string.Join('|', package.Locations.Select(location => location.RootPath))}"
                : package.PackageFullName;
            if (!seenPackages.Add(observationKey))
                continue;

            if (package.MetadataFailure is { } metadataFailure)
            {
                skippedOrFailedCount++;
                AddFailure(failures, "PackageMetadata", packageIdentity, metadataFailure);
                continue;
            }

            var hasAccessibleLocation = false;
            var configWasFound = false;
            var locationFailures = package.LocationFailure is { } locationFailure
                ? new List<string> { locationFailure }
                : new List<string>();
            foreach (var location in package.Locations)
            {
                cancellationToken.ThrowIfCancellationRequested();
                FileAttributes attributes;
                try
                {
                    attributes = File.GetAttributes(location.RootPath);
                }
                catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
                {
                    continue;
                }
                catch (Exception exception) when (exception is UnauthorizedAccessException or IOException or ArgumentException or NotSupportedException)
                {
                    locationFailures.Add($"{location.Kind} location '{location.RootPath}': {MicrosoftGameConfigReader.Describe(exception)}");
                    continue;
                }

                if ((attributes & FileAttributes.Directory) == 0)
                    continue;
                hasAccessibleLocation = true;
                var configPath = Path.Combine(location.RootPath, "MicrosoftGame.config");
                FileStream configStream;
                try
                {
                    configStream = new FileStream(configPath, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete, 16 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
                }
                catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
                {
                    continue;
                }
                catch (Exception exception) when (exception is UnauthorizedAccessException or IOException or ArgumentException or NotSupportedException)
                {
                    locationFailures.Add($"MicrosoftGame.config at '{configPath}': {MicrosoftGameConfigReader.Describe(exception)}");
                    continue;
                }

                configWasFound = true;
                configCandidateCount++;
                await using (configStream)
                {
                    if (configStream.Length > MaximumConfigBytes)
                    {
                        skippedOrFailedCount++;
                        AddFailure(failures, "ConfigRead", packageIdentity, $"MicrosoftGame.config exceeds the {MaximumConfigBytes:N0}-byte diagnostic limit.");
                        break;
                    }

                    var parsed = await MicrosoftGameConfigReader.ReadAsync(configStream, cancellationToken).ConfigureAwait(false);
                    if (parsed.XmlParsed && parsed.RecognizedRoot)
                        parsedConfigCount++;
                    if (parsed.Config is not { } config)
                    {
                        skippedOrFailedCount++;
                        AddFailure(failures, parsed.XmlParsed ? "ConfigValidation" : "ConfigParse", packageIdentity, parsed.FailureReason ?? "MicrosoftGame.config is not a valid game config.");
                        AppLog.Debug("XboxCatalogDiagnostic", "MicrosoftGame.config candidate rejected.",
                            ("Package", packageIdentity), ("Reason", parsed.FailureReason));
                        break;
                    }

                    var candidateKey = CreateCandidateKey(config.StoreId, package.PackageFamilyName, config);
                    var gameObservationKey = $"{packageIdentity}|{candidateKey}";
                    if (seenGameObservations.Add(gameObservationKey))
                    {
                        var game = new DiagnosticGame(
                            candidateKey,
                            FirstNonBlank(config.DefaultDisplayName, package.DisplayName, package.PackageName, packageIdentity),
                            package.PackageFullName,
                            package.PackageFamilyName,
                            config.StoreId,
                            config.TitleId,
                            config.IdentityName,
                            config.IdentityPublisher,
                            config.IdentityResourceId,
                            config.Executables,
                            location.Kind,
                            location.RootPath,
                            configPath)
                        {
                            PackageName = package.PackageName,
                        };
                        games.Add(game);
                        AppLog.Info("XboxCatalogDiagnostic", "XBOX/GDK game candidate accepted.",
                            ("CandidateKey", game.CandidateKey), ("PackageFullName", game.PackageFullName),
                            ("PackageFamilyName", game.PackageFamilyName), ("StoreId", game.StoreId), ("TitleId", game.TitleId));
                    }
                }

                break;
            }

            if (hasAccessibleLocation)
                accessiblePackageCount++;
            else
            {
                skippedOrFailedCount++;
                AddFailure(failures, "PackageLocation", packageIdentity,
                    locationFailures.Count > 0 ? string.Join("; ", locationFailures) : "No installed or effective package location is available.");
            }

            if (hasAccessibleLocation && !configWasFound && locationFailures.Count > 0)
            {
                skippedOrFailedCount++;
                AddFailure(failures, "ConfigAccess", packageIdentity, string.Join("; ", locationFailures));
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        var status = enumeration.Succeeded
            ? skippedOrFailedCount == 0 ? "Completed." : $"Completed with {skippedOrFailedCount} skipped or failed package/config item(s)."
            : $"Package enumeration {(enumeration.Unavailable ? "is unavailable" : "failed")}; results may be partial.";
        var reportBody = BuildReport(capturedAt, enumeration, accessiblePackageCount, configCandidateCount, parsedConfigCount, games, failures, skippedOrFailedCount);
        string? reportPath = null;
        string? reportFailure = null;
        try
        {
            reportPath = await WriteReportAsync(capturedAt, reportBody, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            reportFailure = MicrosoftGameConfigReader.Describe(exception);
            AppLog.Warn("XboxCatalogDiagnostic", "Diagnostic report could not be written.", exception,
                ("Reason", exception.GetType().Name));
            status += " Report could not be saved.";
        }

        var outcome = !enumeration.Succeeded
            ? enumeration.Unavailable ? FrontendXboxCatalogDiagnosticOutcome.Unavailable : FrontendXboxCatalogDiagnosticOutcome.Failed
            : FrontendXboxCatalogDiagnosticOutcome.Completed;
        var uiGames = games.Take(MaximumUiGames).Select(ToFrontendGame).ToList();
        var uiFailures = failures.Take(MaximumUiFailures).ToList();
        if (reportFailure is not null && uiFailures.Count < MaximumUiFailures)
            uiFailures.Add(new FrontendXboxCatalogDiagnosticFailure("ReportWrite", "Diagnostic report", Truncate(reportFailure, MaximumUiTextLength)));

        AppLog.Info("XboxCatalogDiagnostic", "XBOX catalog diagnostic scan completed.",
            ("Outcome", outcome), ("EnumeratedPackages", enumeration.Packages.Count),
            ("AccessiblePackages", accessiblePackageCount), ("ConfigCandidates", configCandidateCount),
            ("ParsedConfigs", parsedConfigCount), ("ValidGames", games.Count),
            ("SkippedOrFailed", skippedOrFailedCount), ("ReportPath", reportPath));

        var result = new FrontendXboxCatalogDiagnosticResult(
            outcome,
            status,
            enumeration.Packages.Count,
            accessiblePackageCount,
            configCandidateCount,
            parsedConfigCount,
            games.Count,
            skippedOrFailedCount,
            uiGames,
            uiFailures,
            reportPath,
            Math.Max(0, games.Count - uiGames.Count),
            Math.Max(0, failures.Count - Math.Min(failures.Count, MaximumUiFailures)));

        while (System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(result).Length > 512 * 1024 && (uiFailures.Count > 0 || uiGames.Count > 0))
        {
            if (uiFailures.Count > 0)
            {
                uiFailures.RemoveAt(uiFailures.Count - 1);
                result = result with { Failures = uiFailures, OmittedFailureCount = result.OmittedFailureCount + 1 };
            }
            else
            {
                uiGames.RemoveAt(uiGames.Count - 1);
                result = result with { Games = uiGames, OmittedGameCount = result.OmittedGameCount + 1 };
            }
        }
        return result;
    }

    internal static string CreateCandidateKey(string? storeId, string? packageFamilyName, MicrosoftGameConfig config)
    {
        if (!string.IsNullOrWhiteSpace(storeId))
            return $"store:{storeId.Trim()}";
        if (!string.IsNullOrWhiteSpace(packageFamilyName))
            return $"pfn:{packageFamilyName.Trim()}";

        static string Normalize(string value) => Uri.EscapeDataString(value.Trim().ToUpperInvariant());
        return $"identity:{Normalize(config.IdentityName)}|{Normalize(config.IdentityPublisher)}|{Normalize(config.IdentityResourceId ?? string.Empty)}";
    }

    private async Task<string> WriteReportAsync(DateTimeOffset capturedAt, string contents, CancellationToken cancellationToken)
    {
        var directory = Path.Combine(_logDirectory, "Discovery");
        Directory.CreateDirectory(directory);
        var stem = $"xbox-catalog-diagnostic-{capturedAt:yyyyMMdd-HHmmss}";
        for (var suffix = 1; ; suffix++)
        {
            var fileName = suffix == 1 ? $"{stem}.txt" : $"{stem}-{suffix}.txt";
            var path = Path.Combine(directory, fileName);
            try
            {
                var bytes = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(contents);
                await using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read, 16 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
                try
                {
                    await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
                    await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                    return path;
                }
                catch
                {
                    await stream.DisposeAsync().ConfigureAwait(false);
                    File.Delete(path);
                    throw;
                }
            }
            catch (IOException) when (File.Exists(path))
            {
                cancellationToken.ThrowIfCancellationRequested();
            }
        }
    }

    private static string BuildReport(
        DateTimeOffset capturedAt,
        XboxCatalogPackageEnumeration enumeration,
        int accessiblePackageCount,
        int configCandidateCount,
        int parsedConfigCount,
        IReadOnlyList<DiagnosticGame> games,
        IReadOnlyList<FrontendXboxCatalogDiagnosticFailure> failures,
        int skippedOrFailedCount)
    {
        var report = new StringBuilder();
        report.AppendLine("XBOX Catalog Diagnostic");
        report.AppendLine($"Timestamp: {capturedAt:O}");
        report.AppendLine($"OS: {Environment.OSVersion.VersionString} (build {Environment.OSVersion.Version.Build})");
        report.AppendLine($"Addon version: {typeof(XboxCatalogDiagnostic).Assembly.GetName().Version?.ToString() ?? "<Unavailable>"}");
        report.AppendLine();
        report.AppendLine("Enumeration");
        report.AppendLine("API: Windows.Management.Deployment.PackageManager.FindPackagesForUser(\"\")");
        report.AppendLine($"Succeeded: {enumeration.Succeeded}");
        if (enumeration.FailureReason is { } enumerationFailure)
            report.AppendLine($"Failure: {enumerationFailure}");
        report.AppendLine($"Total packages returned: {enumeration.Packages.Count}");
        report.AppendLine($"Packages with accessible installed/effective location: {accessiblePackageCount}");
        report.AppendLine($"MicrosoftGame.config candidates: {configCandidateCount}");
        report.AppendLine($"Recognized/parseable game config roots: {parsedConfigCount}");
        report.AppendLine($"Valid XBOX/GDK candidates: {games.Count}");
        report.AppendLine($"Skipped or failed package/config items: {skippedOrFailedCount}");

        foreach (var game in games)
        {
            report.AppendLine();
            report.AppendLine($"Game: {game.DisplayName}");
            report.AppendLine($"  Candidate key: {game.CandidateKey}");
            report.AppendLine($"  Package name: {game.PackageName}");
            report.AppendLine($"  Package full name: {game.PackageFullName}");
            report.AppendLine($"  Package family name: {game.PackageFamilyName}");
            report.AppendLine($"  Package location kind: {game.LocationKind}");
            report.AppendLine($"  Package root: {game.PackageRoot}");
            report.AppendLine($"  MicrosoftGame.config: {game.ConfigPath}");
            report.AppendLine($"  Identity Name: {game.IdentityName}");
            report.AppendLine($"  Identity Publisher: {game.IdentityPublisher}");
            report.AppendLine($"  Identity ResourceId: {game.IdentityResourceId ?? "<Absent>"}");
            report.AppendLine($"  StoreId: {game.StoreId ?? "<Absent>"}");
            report.AppendLine($"  TitleId: {game.TitleId ?? "<Absent>"}");
            report.AppendLine("  Executables:");
            foreach (var executable in game.Executables)
                report.AppendLine($"    Name={executable.Name}; Id={executable.Id ?? "<Absent>"}; TargetDeviceFamily={executable.TargetDeviceFamily ?? "<Absent>"}; Architecture={executable.Architecture ?? "<Absent>"}");
        }

        if (failures.Count > 0)
        {
            report.AppendLine();
            report.AppendLine("Actionable failures");
            foreach (var failure in failures)
            {
                report.AppendLine($"  Stage: {failure.Stage}");
                report.AppendLine($"  Package: {failure.PackageIdentity}");
                report.AppendLine($"  Reason: {failure.Reason}");
            }
        }
        return report.ToString();
    }

    private static FrontendXboxCatalogDiagnosticGame ToFrontendGame(DiagnosticGame game)
    {
        var executables = game.Executables.Take(MaximumUiExecutablesPerGame)
            .Select(executable => new FrontendXboxCatalogDiagnosticExecutable(
                Truncate(executable.Name, 256), TruncateNullable(executable.Id, 128),
                TruncateNullable(executable.TargetDeviceFamily, 128), TruncateNullable(executable.Architecture, 128)))
            .ToList();
        return new FrontendXboxCatalogDiagnosticGame(
            Truncate(game.CandidateKey, MaximumUiTextLength),
            Truncate(game.DisplayName, MaximumUiTextLength),
            Truncate(game.PackageFullName, MaximumUiTextLength),
            Truncate(game.PackageFamilyName, MaximumUiTextLength),
            TruncateNullable(game.StoreId, MaximumUiTextLength),
            TruncateNullable(game.TitleId, MaximumUiTextLength),
            Truncate(game.IdentityName, MaximumUiTextLength),
            Truncate(game.IdentityPublisher, MaximumUiTextLength),
            TruncateNullable(game.IdentityResourceId, MaximumUiTextLength),
            executables,
            Truncate(game.LocationKind, 64),
            Truncate(game.PackageRoot, MaximumUiTextLength),
            Truncate(game.ConfigPath, MaximumUiTextLength),
            Math.Max(0, game.Executables.Count - executables.Count));
    }

    private static void AddFailure(List<FrontendXboxCatalogDiagnosticFailure> failures, string stage, string packageIdentity, string reason)
    {
        if (failures.Count < MaximumReportFailures)
            failures.Add(new FrontendXboxCatalogDiagnosticFailure(
                Truncate(stage, 128), Truncate(packageIdentity, MaximumUiTextLength), Truncate(reason, MaximumUiTextLength)));
    }

    private static string PackageIdentity(XboxCatalogPackage package) =>
        FirstNonBlank(package.PackageFullName, package.PackageFamilyName, package.PackageName, "<Unknown package>");

    private static string FirstNonBlank(params string?[] values) => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? "<Unavailable>";
    private static string Truncate(string value, int maximumLength) => value.Length <= maximumLength ? value : value[..maximumLength];
    private static string? TruncateNullable(string? value, int maximumLength) => value is null ? null : Truncate(value, maximumLength);

    private sealed record DiagnosticGame(
        string CandidateKey,
        string DisplayName,
        string PackageFullName,
        string PackageFamilyName,
        string? StoreId,
        string? TitleId,
        string IdentityName,
        string IdentityPublisher,
        string? IdentityResourceId,
        IReadOnlyList<XboxCatalogExecutable> Executables,
        string LocationKind,
        string PackageRoot,
        string ConfigPath)
    {
        public string PackageName { get; init; } = string.Empty;
    }
}
