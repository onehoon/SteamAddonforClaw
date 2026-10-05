using SteamInputAddonforClaw.Contracts.Frontend;
using SteamInputAddonforClaw.Diagnostics.XboxCatalog;

namespace SteamInputAddonforClaw.Diagnostics.XboxSession;

internal sealed record XboxGameProcessIdentityEvidence(
    int ImageResultCode,
    string? RunningProcessPath,
    int PackageFullNameResultCode,
    string? PackageFullName,
    int PackageFamilyNameResultCode,
    string? PackageFamilyName,
    string? ApplicationUserModelId,
    int ApplicationUserModelIdResult,
    string? PackageIdentityName,
    string? PackageIdentityPublisher,
    string? PackageIdentityPublisherId,
    string? PackageIdentityResourceId,
    string? PackageIdentityArchitecture,
    string? PackageIdentityVersion,
    IReadOnlyList<FrontendXboxSessionDiagnosticPackagePath> PackagePaths);

/// <summary>Applies the XBOX package/config/executable identity contract to live-process evidence.
/// Kept separate from Win32 acquisition so those decisions can be tested without a packaged game.</summary>
internal static class XboxGameProcessIdentityEvaluator
{
    private const int ErrorSuccess = 0;
    private const int AppModelErrorNoPackage = 15700;
    private const long MaximumConfigBytes = 2 * 1024 * 1024;

    internal static async Task<XboxGameProcessInspection> InspectAsync(
        IXboxGameProcessGeneration generation,
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
        if (evidence.PackageFamilyNameResultCode != ErrorSuccess || string.IsNullOrWhiteSpace(evidence.PackageFamilyName))
            return Negative(XboxGameProcessInspectionDisposition.PackageIdentityFailure,
                $"GetPackageFamilyName failed with result {evidence.PackageFamilyNameResultCode}.");
        var configFailure = "MicrosoftGame.config was not found in the package paths returned by Windows.";
        var sawExecutableMismatch = false;
        var checkedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var packagePath in evidence.PackagePaths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (packagePath.ResultCode != ErrorSuccess || string.IsNullOrWhiteSpace(packagePath.Path)
                || !checkedPaths.Add(packagePath.Path))
                continue;

            var configPath = Path.Combine(packagePath.Path, "MicrosoftGame.config");
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

                var game = new FrontendXboxSessionDiagnosticGame(
                    XboxCatalogDiagnostic.CreateCandidateKey(read.Config.StoreId, evidence.PackageFamilyName, read.Config),
                    generation.ProcessId,
                    evidence.RunningProcessPath,
                    runningExecutableName,
                    evidence.PackageFullName,
                    evidence.PackageFamilyName,
                    evidence.ApplicationUserModelId,
                    evidence.ApplicationUserModelIdResult,
                    evidence.PackageIdentityName,
                    evidence.PackageIdentityPublisher,
                    evidence.PackageIdentityPublisherId,
                    evidence.PackageIdentityResourceId,
                    evidence.PackageIdentityArchitecture,
                    evidence.PackageIdentityVersion,
                    read.Config.IdentityName,
                    read.Config.IdentityPublisher,
                    read.Config.IdentityResourceId,
                    read.Config.StoreId,
                    read.Config.TitleId,
                    matchedExecutable.Name,
                    configPath,
                    evidence.PackagePaths);
                return new(XboxGameProcessInspectionDisposition.Matched, null, game);
            }
        }

        if (sawExecutableMismatch)
            return Negative(XboxGameProcessInspectionDisposition.ExecutableMismatch,
                $"Running executable '{runningExecutableName}' did not exactly match MicrosoftGame.config ExecutableList.");
        return Negative(XboxGameProcessInspectionDisposition.ConfigNegative, configFailure);
    }

    private static XboxGameProcessInspection Negative(XboxGameProcessInspectionDisposition disposition, string reason) =>
        new(disposition, reason, null);
}
