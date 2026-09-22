using Microsoft.Win32;
using SteamInputAddonforClaw.Contracts.Frontend;
using SteamInputAddonforClaw.Diagnostics;
using Windows.Management.Deployment;

namespace SteamInputAddonforClaw.WindowsGaming;

internal static class SteamFsePackageContract
{
    internal const string PackageIdentityName = "SteamInputAddonforClaw.FseHome";
    internal const string ApplicationId = "App";
    internal const string PackageRelativePath = "fse\\SteamInputAddonforClaw.FseHome.msix";
    internal const string CertificateRelativePath = "fse\\SteamInputAddonforClaw.FseHome.cer";
    internal const string CertificateSubject = "CN=SteamInputAddonforClaw";
    internal const string FixedPackageVersionText = "1.0.0.0";
    internal static Version FixedPackageVersion { get; } = Version.Parse(FixedPackageVersionText);
}

internal sealed record SteamFseOsSupport(bool Supported, string? FailureReason);

internal interface ISteamFseOsProbe
{
    SteamFseOsSupport Capture();
}

internal sealed record SteamFsePackageInfo(
    string IdentityName,
    string FamilyName,
    string FullName,
    Version Version);

internal sealed record SteamFsePackageInspection(
    bool Succeeded,
    SteamFsePackageInfo? Package,
    string? FailureReason);

internal interface ISteamFsePackageProbe
{
    SteamFsePackageInspection Inspect();
    bool TryRemoveOwnedPackage();
}

internal interface ISteamFsePackageEnumeration
{
    IReadOnlyList<SteamFsePackageInfo> FindCurrentUserPackages();
    void RemovePackage(string fullName);
}

internal interface IGamingConfigurationStore
{
    string? ReadGamingHomeApp();
    bool ReadStartupToGamingHome();
    void WriteGamingHomeApp(string aumid);
    void DeleteGamingHomeApp();
    void WriteStartupToGamingHome(bool enabled);
}

internal sealed class WindowsSteamFseOsProbe : ISteamFseOsProbe
{
    private const int MinimumBuild = 26100;
    private const int MinimumUbr = 8039;

    public SteamFseOsSupport Capture()
    {
        if (!OperatingSystem.IsWindows())
            return new(false, "Windows Gaming Full Screen Experience is supported only on Windows.");

        try
        {
            var version = Environment.OSVersion.Version;
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
            var ubr = key?.GetValue("UBR") is { } raw && int.TryParse(raw.ToString(), out var parsed) ? parsed : -1;
            var supported = version.Build > MinimumBuild || version.Build == MinimumBuild && ubr >= MinimumUbr;
            return supported
                ? new(true, null)
                : new(false, $"Windows Gaming Full Screen Experience requires Windows 11 build {MinimumBuild}.{MinimumUbr} or newer.");
        }
        catch (Exception exception)
        {
            AppLog.Warn("SteamFSE", "Windows Gaming Full Screen Experience support probe failed.", exception);
            return new(false, "Windows Gaming Full Screen Experience support could not be verified.");
        }
    }
}

internal sealed class WindowsSteamFsePackageProbe : ISteamFsePackageProbe
{
    internal const string PackageIdentityName = SteamFsePackageContract.PackageIdentityName;
    internal const string ApplicationId = SteamFsePackageContract.ApplicationId;
    private readonly ISteamFsePackageEnumeration _packageEnumeration;

    internal WindowsSteamFsePackageProbe(ISteamFsePackageEnumeration? packageEnumeration = null) =>
        _packageEnumeration = packageEnumeration ?? new WindowsSteamFsePackageEnumeration();

    public SteamFsePackageInspection Inspect()
    {
        if (!OperatingSystem.IsWindows())
            return new(false, null, "Windows Gaming Full Screen Experience is supported only on Windows.");

        try
        {
            var package = FindOwnedPackages().OrderByDescending(item => item.Version).FirstOrDefault();
            return new(true, package, null);
        }
        catch (Exception exception)
        {
            AppLog.Warn("SteamFSE", "Owned Gaming Home package probe failed.", exception,
                ("PackageIdentity", PackageIdentityName));
            return new(false, null, "The registered Gaming Home package could not be verified.");
        }
    }

    public bool TryRemoveOwnedPackage()
    {
        if (!OperatingSystem.IsWindows()) return true;

        try
        {
            foreach (var package in FindOwnedPackages())
                _packageEnumeration.RemovePackage(package.FullName);
            return true;
        }
        catch (Exception exception)
        {
            AppLog.Warn("Uninstall", "Owned Gaming Home package removal failed.", exception,
                ("PackageIdentity", PackageIdentityName));
            return false;
        }
    }

    internal static string? TryGetAumid(SteamFsePackageInfo? package) =>
        package is { FamilyName.Length: > 0 }
            ? $"{package.FamilyName}!{ApplicationId}"
            : null;

    private IEnumerable<SteamFsePackageInfo> FindOwnedPackages() =>
        _packageEnumeration.FindCurrentUserPackages()
            .Where(package => string.Equals(package.IdentityName, PackageIdentityName, StringComparison.Ordinal));
}

internal sealed class WindowsSteamFsePackageEnumeration : ISteamFsePackageEnumeration
{
    public IReadOnlyList<SteamFsePackageInfo> FindCurrentUserPackages() =>
        new PackageManager()
            .FindPackagesForUser(string.Empty)
            .Select(package => new SteamFsePackageInfo(
                package.Id.Name,
                package.Id.FamilyName,
                package.Id.FullName,
                new Version(package.Id.Version.Major, package.Id.Version.Minor, package.Id.Version.Build, package.Id.Version.Revision)))
            .ToArray();

    public void RemovePackage(string fullName) =>
        new PackageManager().RemovePackageAsync(fullName).AsTask().GetAwaiter().GetResult();
}

internal sealed class WindowsGamingConfigurationStore : IGamingConfigurationStore
{
    private const string KeyPath = @"Software\Microsoft\Windows\CurrentVersion\GamingConfiguration";

    public string? ReadGamingHomeApp()
    {
        using var key = Registry.CurrentUser.OpenSubKey(KeyPath);
        return key?.GetValue("GamingHomeApp") as string;
    }

    public bool ReadStartupToGamingHome()
    {
        using var key = Registry.CurrentUser.OpenSubKey(KeyPath);
        return key?.GetValue("StartupToGamingHome") switch
        {
            int value => value != 0,
            long value => value != 0,
            byte value => value != 0,
            string value when int.TryParse(value, out var parsed) => parsed != 0,
            _ => false,
        };
    }

    public void WriteGamingHomeApp(string aumid) => WriteValue("GamingHomeApp", aumid, RegistryValueKind.String);

    public void DeleteGamingHomeApp()
    {
        using var key = Registry.CurrentUser.OpenSubKey(KeyPath, writable: true);
        key?.DeleteValue("GamingHomeApp", throwOnMissingValue: false);
    }

    public void WriteStartupToGamingHome(bool enabled) => WriteValue("StartupToGamingHome", enabled ? 1 : 0, RegistryValueKind.DWord);

    private static void WriteValue(string name, object value, RegistryValueKind kind)
    {
        using var key = Registry.CurrentUser.CreateSubKey(KeyPath, writable: true)
            ?? throw new InvalidOperationException("Windows GamingConfiguration is unavailable.");
        key.SetValue(name, value, kind);
    }
}

internal sealed class WindowsGamingHomeConfiguration
{
    internal const string DefaultUnavailableReason = "Steam Big Picture Full Screen Experience is unavailable.";

    private readonly ISteamFseOsProbe _osProbe;
    private readonly ISteamFsePackageProbe _packageProbe;
    private readonly ISteamFseRegistrationClient _registrationClient;
    private readonly IGamingConfigurationStore _configuration;

    internal WindowsGamingHomeConfiguration(
        ISteamFseOsProbe? osProbe = null,
        ISteamFsePackageProbe? packageProbe = null,
        IGamingConfigurationStore? configuration = null,
        ISteamFseRegistrationClient? registrationClient = null)
    {
        _osProbe = osProbe ?? new WindowsSteamFseOsProbe();
        _packageProbe = packageProbe ?? new WindowsSteamFsePackageProbe();
        _registrationClient = registrationClient ?? new SteamFseRegistrationClient();
        _configuration = configuration ?? new WindowsGamingConfigurationStore();
    }

    internal FrontendSteamFseSnapshot Capture()
    {
        var support = _osProbe.Capture();
        if (!support.Supported)
            return FrontendSteamFseSnapshot.Unavailable(support.FailureReason ?? DefaultUnavailableReason);

        var inspection = _packageProbe.Inspect();
        if (!inspection.Succeeded)
            return FrontendSteamFseSnapshot.Unavailable(inspection.FailureReason ?? "The registered Gaming Home package could not be verified.");

        if (inspection.Package is null)
            return new(true, false, null);

        var aumid = WindowsSteamFsePackageProbe.TryGetAumid(inspection.Package);
        if (aumid is null)
            return FrontendSteamFseSnapshot.Unavailable("The registered Gaming Home package identity could not be verified.");
        if (inspection.Package.Version < SteamFsePackageContract.FixedPackageVersion)
            return new(true, false, null);

        var selectedHome = _configuration.ReadGamingHomeApp();
        var startup = _configuration.ReadStartupToGamingHome();
        return new(true, startup && string.Equals(selectedHome, aumid, StringComparison.Ordinal), null);
    }

    internal async Task<FrontendSteamFseMutationResult> SetEnabledAsync(bool enabled, CancellationToken cancellationToken = default)
    {
        var support = _osProbe.Capture();
        if (!support.Supported)
            return Unavailable(support.FailureReason ?? DefaultUnavailableReason);

        var inspection = _packageProbe.Inspect();
        if (!inspection.Succeeded)
            return Unavailable(inspection.FailureReason ?? "The registered Gaming Home package could not be verified.");

        try
        {
            AppLog.Info("SteamFSE", enabled ? "SteamFSE enable requested." : "SteamFSE disable requested.");
            if (enabled)
            {
                var aumid = WindowsSteamFsePackageProbe.TryGetAumid(inspection.Package);
                if (inspection.Package is null || inspection.Package.Version < SteamFsePackageContract.FixedPackageVersion || aumid is null)
                {
                    var registration = await _registrationClient.EnsureRegisteredAsync(cancellationToken).ConfigureAwait(false);
                    if (!registration.Succeeded)
                        return Failed(Capture(), registration.FailureReason ?? "The Gaming Home package could not be registered.");

                    inspection = _packageProbe.Inspect();
                    if (!inspection.Succeeded)
                        return Failed(Capture(), inspection.FailureReason ?? "The registered Gaming Home package could not be verified.");

                    aumid = WindowsSteamFsePackageProbe.TryGetAumid(inspection.Package);
                    if (inspection.Package is null || inspection.Package.Version < SteamFsePackageContract.FixedPackageVersion || aumid is null)
                        return Failed(Capture(), "The registered Gaming Home package could not be read back.");
                }

                _configuration.WriteGamingHomeApp(aumid!);
                _configuration.WriteStartupToGamingHome(true);
            }
            else
            {
                _configuration.DeleteGamingHomeApp();
                _configuration.WriteStartupToGamingHome(false);
            }

            var readback = CaptureRawState();
            var verified = enabled
                ? string.Equals(readback.GamingHomeApp, WindowsSteamFsePackageProbe.TryGetAumid(inspection.Package), StringComparison.Ordinal) && readback.StartupToGamingHome
                : readback.GamingHomeApp is null && !readback.StartupToGamingHome;
            var snapshot = Capture();
            if (verified)
            {
                AppLog.Info("SteamFSE", enabled ? "SteamFSE enable verified." : "SteamFSE disable verified.");
                return new(FrontendSteamFseMutationOutcome.Succeeded, snapshot, null);
            }

            AppLog.Warn("SteamFSE", "SteamFSE readback verification failed.", null,
                ("EnabledRequested", enabled), ("SelectedHome", readback.GamingHomeApp),
                ("StartupToGamingHome", readback.StartupToGamingHome));
            return Failed(snapshot, "Windows did not confirm the requested Gaming Home state.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return Failed(Capture(), "The Gaming Home change was cancelled.");
        }
        catch (Exception exception)
        {
            AppLog.Warn("SteamFSE", "SteamFSE registry mutation failed.", exception,
                ("EnabledRequested", enabled));
            return Failed(Capture(), "The Gaming Home setting could not be changed.");
        }
    }

    internal bool TryCleanupForUninstall()
    {
        var configurationCleaned = true;
        try
        {
            _configuration.DeleteGamingHomeApp();
            _configuration.WriteStartupToGamingHome(false);
        }
        catch (Exception exception)
        {
            configurationCleaned = false;
            AppLog.Warn("Uninstall", "SteamFSE GamingConfiguration cleanup failed.", exception);
        }

        return configurationCleaned && _packageProbe.TryRemoveOwnedPackage();
    }

    private (string? GamingHomeApp, bool StartupToGamingHome) CaptureRawState() =>
        (_configuration.ReadGamingHomeApp(), _configuration.ReadStartupToGamingHome());

    private static FrontendSteamFseMutationResult Failed(FrontendSteamFseSnapshot snapshot, string reason) =>
        new(FrontendSteamFseMutationOutcome.Failed, snapshot, reason);

    private static FrontendSteamFseMutationResult Unavailable(string reason) =>
        new(FrontendSteamFseMutationOutcome.Unavailable, FrontendSteamFseSnapshot.Unavailable(reason), reason);
}
