using SteamInputAddonforClaw.Diagnostics;
using SteamInputAddonforClaw.GameDetection.Windows;
using SteamInputAddonforClaw.Xbox;

namespace SteamInputAddonforClaw.Xbox.Session;

internal sealed record XboxGamePackageConfigLocation(string Kind, string RootPath);

internal sealed record XboxGamePackageConfigMetadata(
    Func<string?> EffectiveLocationPath,
    Func<string?> InstalledLocationPath)
{
    internal Func<string?>? DisplayName { get; init; }
    internal Func<string?>? Name { get; init; }
}

internal sealed record XboxGamePackageConfigLocationResolution(
    IReadOnlyList<XboxGamePackageConfigLocation> Locations,
    string? FailureReason)
{
    internal string? PackageDisplayName { get; init; }
    internal string? PackageName { get; init; }
}

internal static class XboxGamePackageConfigLocationResolver
{
    internal static XboxGamePackageConfigLocationResolution ResolveCurrentUserPackage(
        string packageFullName,
        Func<string, string, XboxGamePackageConfigMetadata?> findPackageForUser)
    {
        XboxGamePackageConfigMetadata? package;
        try
        {
            package = findPackageForUser(string.Empty, packageFullName);
        }
        catch (Exception exception)
        {
            return new([], $"PackageManager did not resolve the live PackageFullName: {MicrosoftGameConfigReader.Describe(exception)}");
        }

        if (package is null)
            return new([], "PackageManager did not resolve the live PackageFullName.");

        var locations = new List<XboxGamePackageConfigLocation>(2);
        var failures = new List<string>(2);
        AddLocation("Effective", package.EffectiveLocationPath, locations, failures);
        AddLocation("Installed", package.InstalledLocationPath, locations, failures);

        if (locations.Count == 0)
        {
            var reason = "Package metadata exposed no usable Effective/Installed location.";
            if (failures.Count > 0)
                reason += " " + string.Join("; ", failures);
            return new([], reason);
        }

        return new(locations, failures.Count == 0 ? null : string.Join("; ", failures))
        {
            PackageDisplayName = ReadOptionalMetadata(package.DisplayName),
            PackageName = ReadOptionalMetadata(package.Name),
        };
    }

    private static string? ReadOptionalMetadata(Func<string?>? getValue)
    {
        try
        {
            var value = getValue?.Invoke();
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }
        catch
        {
            return null;
        }
    }

    private static void AddLocation(
        string kind,
        Func<string?> getPath,
        List<XboxGamePackageConfigLocation> locations,
        List<string> failures)
    {
        try
        {
            var path = getPath();
            if (!string.IsNullOrWhiteSpace(path)
                && !locations.Any(location => string.Equals(location.RootPath, path, StringComparison.OrdinalIgnoreCase)))
                locations.Add(new(kind, path));
        }
        catch (Exception exception)
        {
            failures.Add($"{kind} location: {MicrosoftGameConfigReader.Describe(exception)}");
        }
    }
}

internal sealed record XboxGameProcessIdentityEvidence(
    int ImageResultCode,
    string? RunningProcessPath,
    int PackageFullNameResultCode,
    string? PackageFullName,
    int PackageFamilyNameResultCode,
    string? PackageFamilyName,
    string? PackageDisplayName,
    string? PackageName,
    IReadOnlyList<XboxGamePackageConfigLocation> ConfigLocations,
    string? ConfigLocationFailure);

internal enum XboxGameProcessInspectionDisposition
{
    ProcessImageFailure,
    NoPackage,
    PackageIdentityFailure,
    ConfigNegative,
    ExecutableMismatch,
    Matched,
}

internal sealed record XboxGameProcessInspection(
    XboxGameProcessInspectionDisposition Disposition,
    string? FailureReason,
    XboxGameProcessMatch? Match);

/// <summary>Applies the XBOX package/config/executable identity contract to live-process evidence.
/// Kept separate from Win32 acquisition so those decisions can be tested without a packaged game.</summary>
internal static class XboxGameProcessIdentityEvaluator
{
    private const int ErrorSuccess = 0;
    private const int AppModelErrorNoPackage = 15700;
    private const long MaximumConfigBytes = 2 * 1024 * 1024;

    internal static async Task<XboxGameProcessInspection> InspectAsync(
        IGameProcessGeneration generation,
        XboxGameProcessIdentityEvidence evidence,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (evidence.ImageResultCode != ErrorSuccess || string.IsNullOrWhiteSpace(evidence.RunningProcessPath))
            return Negative(XboxGameProcessInspectionDisposition.ProcessImageFailure,
                $"QueryFullProcessImageNameW failed with Win32 error {evidence.ImageResultCode}.");

        var runningExecutableName = Path.GetFileName(evidence.RunningProcessPath);
        if (string.IsNullOrWhiteSpace(runningExecutableName))
            return Negative(XboxGameProcessInspectionDisposition.ProcessImageFailure, "The running process image has no executable basename.");

        if (evidence.PackageFullNameResultCode == AppModelErrorNoPackage)
            return Negative(XboxGameProcessInspectionDisposition.NoPackage, "The process has no package identity.");
        if (evidence.PackageFullNameResultCode != ErrorSuccess || string.IsNullOrWhiteSpace(evidence.PackageFullName))
            return Negative(XboxGameProcessInspectionDisposition.PackageIdentityFailure,
                $"GetPackageFullName failed with result {evidence.PackageFullNameResultCode}.");
        var packageFamilyName = evidence.PackageFamilyNameResultCode == ErrorSuccess
            ? evidence.PackageFamilyName
            : null;

        var configFailure = "MicrosoftGame.config was not found in the Package object's Effective/Installed locations.";
        if (!string.IsNullOrWhiteSpace(evidence.ConfigLocationFailure))
            configFailure += " " + evidence.ConfigLocationFailure;
        var sawExecutableMismatch = false;
        var checkedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var configLocation in evidence.ConfigLocations)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(configLocation.RootPath) || !checkedPaths.Add(configLocation.RootPath))
                continue;

            var configPath = Path.Combine(configLocation.RootPath, "MicrosoftGame.config");
            FileStream stream;
            try
            {
                stream = new FileStream(
                    configPath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read | FileShare.Delete,
                    16 * 1024,
                    FileOptions.Asynchronous | FileOptions.SequentialScan);
            }
            catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
            {
                continue;
            }
            catch (Exception exception) when (exception is UnauthorizedAccessException or IOException or ArgumentException or NotSupportedException)
            {
                configFailure = $"MicrosoftGame.config at '{configPath}' could not be read: {MicrosoftGameConfigReader.Describe(exception)}";
                continue;
            }

            await using (stream.ConfigureAwait(false))
            {
                if (stream.Length > MaximumConfigBytes)
                {
                    configFailure = $"MicrosoftGame.config at '{configPath}' exceeded the {MaximumConfigBytes} byte limit.";
                    continue;
                }

                var read = await MicrosoftGameConfigReader.ReadAsync(stream, cancellationToken).ConfigureAwait(false);
                if (!read.RecognizedRoot || read.Config is null)
                {
                    configFailure = $"MicrosoftGame.config at '{configPath}' was not valid: {read.FailureReason ?? "Required identity or executable evidence is missing."}";
                    continue;
                }

                var matchedExecutable = read.Config.Executables.FirstOrDefault(executable =>
                    string.Equals(Path.GetFileName(executable.Name), runningExecutableName, StringComparison.OrdinalIgnoreCase));
                if (matchedExecutable is null)
                {
                    sawExecutableMismatch = true;
                    continue;
                }

                if (generation.IsSignaled)
                    return Negative(XboxGameProcessInspectionDisposition.ProcessImageFailure, "The process exited before its identity evidence was complete.");

                var config = read.Config;
                var displayName = FirstNonBlank(config.DefaultDisplayName, evidence.PackageDisplayName, evidence.PackageName, runningExecutableName);
                var identity = new XboxGameIdentity(
                    XboxGameIdentity.CreateKey(config.StoreId, packageFamilyName, config),
                    displayName,
                    config.StoreId,
                    config.TitleId,
                    packageFamilyName,
                    config.IdentityName,
                    config.IdentityPublisher,
                    config.IdentityResourceId,
                    config.Executables);
                var match = new XboxGameProcessMatch(
                    identity,
                    generation.ProcessId,
                    evidence.RunningProcessPath,
                    runningExecutableName,
                    evidence.PackageFullName);
                LogConfigResolution(generation, evidence, configPath, null);
                return new(XboxGameProcessInspectionDisposition.Matched, null, match);
            }
        }

        if (sawExecutableMismatch)
        {
            var mismatchReason = $"Running executable '{runningExecutableName}' did not exactly match MicrosoftGame.config ExecutableList.";
            LogConfigResolution(generation, evidence, null, mismatchReason);
            return Negative(XboxGameProcessInspectionDisposition.ExecutableMismatch, mismatchReason);
        }
        LogConfigResolution(generation, evidence, null, configFailure);
        return Negative(XboxGameProcessInspectionDisposition.ConfigNegative, configFailure);
    }

    private static void LogConfigResolution(
        IGameProcessGeneration generation,
        XboxGameProcessIdentityEvidence evidence,
        string? selectedConfigPath,
        string? failureReason)
    {
        AppLog.Debug("XboxSession", "Resolved live process MicrosoftGame.config location.",
            ("PID", generation.ProcessId),
            ("PackageFullName", evidence.PackageFullName),
            ("RunningProcessPath", evidence.RunningProcessPath),
            ("ConfigLocation.Effective", LocationPath(evidence.ConfigLocations, "Effective")),
            ("ConfigLocation.Installed", LocationPath(evidence.ConfigLocations, "Installed")),
            ("SelectedConfigPath", selectedConfigPath),
            ("Failure", failureReason));
    }

    private static string? LocationPath(IReadOnlyList<XboxGamePackageConfigLocation> locations, string kind) =>
        locations.FirstOrDefault(location => string.Equals(location.Kind, kind, StringComparison.OrdinalIgnoreCase))?.RootPath;

    private static string FirstNonBlank(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))!.Trim();

    private static XboxGameProcessInspection Negative(XboxGameProcessInspectionDisposition disposition, string reason) =>
        new(disposition, reason, null);
}
