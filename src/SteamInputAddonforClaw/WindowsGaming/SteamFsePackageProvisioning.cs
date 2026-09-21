using System.IO.Compression;
using System.Xml.Linq;
using SteamInputAddonforClaw.Diagnostics;
using Windows.Management.Deployment;

namespace SteamInputAddonforClaw.WindowsGaming;

internal enum SteamFsePackageProvisioningOutcome
{
    Unsupported,
    AlreadyProvisioned,
    Provisioned,
    MissingBundle,
    Failed,
}

internal sealed record SteamFsePackageProvisioningResult(
    SteamFsePackageProvisioningOutcome Outcome,
    Version? PackageVersion,
    string? Aumid,
    string? FailureReason)
{
    internal bool Succeeded => Outcome is SteamFsePackageProvisioningOutcome.AlreadyProvisioned or SteamFsePackageProvisioningOutcome.Provisioned;
}

internal interface ISteamFsePackageProvisioner
{
    SteamFsePackageProvisioningResult EnsureProvisioned(CancellationToken cancellationToken = default);
}

internal interface ISteamFsePackageDeployment
{
    Task AddOrUpdateAsync(string packagePath, CancellationToken cancellationToken);
}

internal sealed class SteamFsePackageProvisioner : ISteamFsePackageProvisioner
{
    private const string PackageRelativePath = "fse\\SteamInputAddonforClaw.FseHome.msix";
    private static readonly XNamespace ManifestNamespace = "http://schemas.microsoft.com/appx/manifest/foundation/windows10";
    private readonly ISteamFseOsProbe _osProbe;
    private readonly ISteamFsePackageEnumeration _packageEnumeration;
    private readonly ISteamFsePackageDeployment _deployment;
    private readonly Func<string> _packagePathProvider;

    internal SteamFsePackageProvisioner(
        ISteamFseOsProbe? osProbe = null,
        ISteamFsePackageEnumeration? packageEnumeration = null,
        ISteamFsePackageDeployment? deployment = null,
        Func<string>? packagePathProvider = null)
    {
        _osProbe = osProbe ?? new WindowsSteamFseOsProbe();
        _packageEnumeration = packageEnumeration ?? new WindowsSteamFsePackageEnumeration();
        _deployment = deployment ?? new WindowsSteamFsePackageDeployment();
        _packagePathProvider = packagePathProvider ?? (() => Path.Combine(AppContext.BaseDirectory, PackageRelativePath));
    }

    public SteamFsePackageProvisioningResult EnsureProvisioned(CancellationToken cancellationToken = default)
    {
        var support = _osProbe.Capture();
        if (!support.Supported)
        {
            AppLog.Debug("SteamFSE", "FSE package provisioning skipped on an unsupported OS.", ("Reason", support.FailureReason));
            return new(SteamFsePackageProvisioningOutcome.Unsupported, null, null, support.FailureReason);
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var packagePath = _packagePathProvider();
            if (!TryReadBundle(packagePath, out var bundle, out var bundleFailure))
            {
                AppLog.Warn("SteamFSE", "Bundled FSE package is unavailable; continuing without provisioning.", null,
                    ("Path", packagePath), ("Reason", bundleFailure));
                return new(SteamFsePackageProvisioningOutcome.MissingBundle, null, null, bundleFailure);
            }

            var installed = FindOwnedPackages().OrderByDescending(package => package.Version).FirstOrDefault();
            if (IsUsable(installed, bundle!.Version, out var installedAumid))
            {
                AppLog.Debug("SteamFSE", "FSE package is already provisioned.",
                    ("Version", installed!.Version), ("Aumid", installedAumid));
                return new(SteamFsePackageProvisioningOutcome.AlreadyProvisioned, installed.Version, installedAumid, null);
            }

            AppLog.Info("SteamFSE", "FSE package provisioning started.",
                ("Path", packagePath), ("ExpectedVersion", bundle.Version),
                ("InstalledVersion", installed?.Version));
            _deployment.AddOrUpdateAsync(packagePath, cancellationToken)
                .WaitAsync(TimeSpan.FromSeconds(30), cancellationToken)
                .GetAwaiter()
                .GetResult();

            var verified = FindOwnedPackages().OrderByDescending(package => package.Version).FirstOrDefault();
            if (!IsUsable(verified, bundle.Version, out var aumid))
            {
                var reason = "FSE package registration completed without a current exact-identity package readback.";
                AppLog.Warn("SteamFSE", reason, null,
                    ("ExpectedVersion", bundle.Version), ("ObservedVersion", verified?.Version),
                    ("PackageIdentity", WindowsSteamFsePackageProbe.PackageIdentityName));
                return new(SteamFsePackageProvisioningOutcome.Failed, verified?.Version, null, reason);
            }

            AppLog.Info("SteamFSE", "FSE package provisioning completed.",
                ("Version", verified!.Version), ("Aumid", aumid));
            return new(SteamFsePackageProvisioningOutcome.Provisioned, verified.Version, aumid, null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            const string reason = "FSE package provisioning was cancelled.";
            AppLog.Warn("SteamFSE", reason, null);
            return new(SteamFsePackageProvisioningOutcome.Failed, null, null, reason);
        }
        catch (Exception exception)
        {
            AppLog.Warn("SteamFSE", "FSE package provisioning failed; continuing Runtime startup.", exception,
                ("PackageIdentity", WindowsSteamFsePackageProbe.PackageIdentityName));
            return new(SteamFsePackageProvisioningOutcome.Failed, null, null, exception.Message);
        }
    }

    private IEnumerable<SteamFsePackageInfo> FindOwnedPackages() =>
        _packageEnumeration.FindCurrentUserPackages()
            .Where(package => string.Equals(package.IdentityName, WindowsSteamFsePackageProbe.PackageIdentityName, StringComparison.Ordinal));

    private static bool IsUsable(SteamFsePackageInfo? package, Version expectedVersion, out string? aumid)
    {
        aumid = package is { FamilyName.Length: > 0 } current && current.Version >= expectedVersion
            ? $"{current.FamilyName}!{WindowsSteamFsePackageProbe.ApplicationId}"
            : null;
        return aumid is not null;
    }

    private static bool TryReadBundle(string packagePath, out SteamFsePackageBundle? bundle, out string? failure)
    {
        bundle = null;
        failure = null;
        if (!File.Exists(packagePath))
        {
            failure = "Bundled FSE MSIX was not found.";
            return false;
        }

        try
        {
            using var archive = ZipFile.OpenRead(packagePath);
            var manifestEntry = archive.GetEntry("AppxManifest.xml");
            if (manifestEntry is null)
            {
                failure = "Bundled FSE MSIX has no AppxManifest.xml.";
                return false;
            }

            using var manifestStream = manifestEntry.Open();
            var document = XDocument.Load(manifestStream);
            var identity = document.Root?.Element(ManifestNamespace + "Identity");
            if (identity is null)
            {
                failure = "Bundled FSE MSIX manifest has no Identity element.";
                return false;
            }

            if (!string.Equals(identity.Attribute("Name")?.Value, WindowsSteamFsePackageProbe.PackageIdentityName, StringComparison.Ordinal))
            {
                failure = "Bundled FSE MSIX identity does not match the Addon-owned identity.";
                return false;
            }

            if (!Version.TryParse(identity.Attribute("Version")?.Value, out var version)
                || version.Build < 0 || version.Revision < 0)
            {
                failure = "Bundled FSE MSIX manifest version is invalid.";
                return false;
            }

            bundle = new SteamFsePackageBundle(packagePath, version);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or System.Xml.XmlException)
        {
            failure = $"Bundled FSE MSIX manifest could not be read: {exception.Message}";
            return false;
        }
    }

    private sealed record SteamFsePackageBundle(string Path, Version Version);
}

internal sealed class WindowsSteamFsePackageDeployment : ISteamFsePackageDeployment
{
    public async Task AddOrUpdateAsync(string packagePath, CancellationToken cancellationToken)
    {
        await new PackageManager()
            .AddPackageAsync(new Uri(packagePath), dependencyPackageUris: null, DeploymentOptions.None)
            .AsTask(cancellationToken)
            .ConfigureAwait(false);
    }
}
