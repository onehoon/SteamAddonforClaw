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
    internal const string FixedPackageVersionText = "1.0.1.0";
    internal static Version FixedPackageVersion { get; } = Version.Parse(FixedPackageVersionText);
}

internal sealed record GamingHomeOsSupport(bool Supported, string? FailureReason);

internal interface IGamingHomeOsProbe
{
    GamingHomeOsSupport Capture();
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

internal sealed class WindowsGamingHomeOsProbe : IGamingHomeOsProbe
{
    private const int MinimumBuild = 26100;
    private const int MinimumUbr = 8039;

    public GamingHomeOsSupport Capture()
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
            AppLog.Warn("GamingHome", "Windows Gaming Full Screen Experience support probe failed.", exception);
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

internal static class XboxGamingHomeAppIdentity
{
    internal const string PackageIdentityName = "Microsoft.GamingApp";
    internal const string PackageFamilyName = "Microsoft.GamingApp_8wekyb3d8bbwe";
    internal const string ApplicationId = "Microsoft.Xbox.App";
    internal const string Aumid = PackageFamilyName + "!" + ApplicationId;
}

internal interface IXboxGamingHomeAppProbe
{
    Task<bool> IsResolvableAsync();
}

internal sealed class WindowsXboxGamingHomeAppProbe : IXboxGamingHomeAppProbe
{
    public async Task<bool> IsResolvableAsync()
    {
        if (!OperatingSystem.IsWindows()) return false;

        var packages = new PackageManager()
            .FindPackagesForUser(string.Empty, XboxGamingHomeAppIdentity.PackageFamilyName)
            .Where(package => string.Equals(package.Id.Name, XboxGamingHomeAppIdentity.PackageIdentityName, StringComparison.Ordinal)
                && string.Equals(package.Id.FamilyName, XboxGamingHomeAppIdentity.PackageFamilyName, StringComparison.Ordinal));
        foreach (var package in packages)
        {
            var entries = await package.GetAppListEntriesAsync().AsTask().ConfigureAwait(false);
            if (entries.Any(entry => string.Equals(entry.AppUserModelId, XboxGamingHomeAppIdentity.Aumid, StringComparison.Ordinal)))
                return true;
        }

        return false;
    }
}

internal sealed class WindowsGamingHomeConfiguration
{
    internal const string DefaultUnavailableReason = "Windows Gaming Full Screen Experience is unavailable.";

    private readonly IGamingHomeOsProbe _osProbe;
    private readonly ISteamFsePackageProbe _packageProbe;
    private readonly ISteamFseRegistrationClient _registrationClient;
    private readonly IXboxGamingHomeAppProbe _xboxAppProbe;
    private readonly IGamingConfigurationStore _configuration;

    internal WindowsGamingHomeConfiguration(
        IGamingHomeOsProbe? osProbe = null,
        ISteamFsePackageProbe? packageProbe = null,
        IGamingConfigurationStore? configuration = null,
        ISteamFseRegistrationClient? registrationClient = null,
        IXboxGamingHomeAppProbe? xboxAppProbe = null)
    {
        _osProbe = osProbe ?? new WindowsGamingHomeOsProbe();
        _packageProbe = packageProbe ?? new WindowsSteamFsePackageProbe();
        _registrationClient = registrationClient ?? new SteamFseRegistrationClient();
        _xboxAppProbe = xboxAppProbe ?? new WindowsXboxGamingHomeAppProbe();
        _configuration = configuration ?? new WindowsGamingConfigurationStore();
    }

    internal FrontendGamingHomeSnapshot Capture()
    {
        try
        {
            var support = _osProbe.Capture();
            if (!support.Supported)
                return FrontendGamingHomeSnapshot.Unavailable(support.FailureReason ?? DefaultUnavailableReason);

            var state = CaptureRawState();
            var classification = Classify(state.GamingHomeApp);
            return classification.Succeeded
                ? new(true, classification.Selection, state.StartupToGamingHome, null)
                : FrontendGamingHomeSnapshot.Unavailable(classification.FailureReason ?? "The current Gaming Home app could not be identified.");
        }
        catch (Exception exception)
        {
            AppLog.Warn("GamingHome", "Windows GamingConfiguration capture failed.", exception);
            return FrontendGamingHomeSnapshot.Unavailable("Windows Gaming Full Screen Experience could not be read.");
        }
    }

    internal async Task<FrontendGamingHomeMutationResult> SetSelectionAsync(
        FrontendGamingHomeSelection selection, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var support = _osProbe.Capture();
        if (!support.Supported)
            return UnavailableResult(support.FailureReason ?? DefaultUnavailableReason);

        try
        {
            if (!Enum.IsDefined(selection) || selection == FrontendGamingHomeSelection.Other)
                return Failed(Capture(), "Other Windows Gaming Home apps cannot be selected by the Addon.");

            var previous = CaptureRawState();
            AppLog.Info("GamingHome", "Gaming Home selection requested.",
                ("PreviousGamingHomeApp", previous.GamingHomeApp),
                ("PreviousStartupToGamingHome", previous.StartupToGamingHome),
                ("RequestedSelection", selection));

            string? expectedHome = null;
            var expectedStartup = previous.StartupToGamingHome;
            if (selection == FrontendGamingHomeSelection.None)
            {
                _configuration.DeleteGamingHomeApp();
                _configuration.WriteStartupToGamingHome(false);
                expectedStartup = false;
            }
            else if (selection == FrontendGamingHomeSelection.Xbox)
            {
                if (!await _xboxAppProbe.IsResolvableAsync().ConfigureAwait(false))
                    return Failed(Capture(), "The Xbox app is not installed or its application identity could not be verified.");

                expectedHome = XboxGamingHomeAppIdentity.Aumid;
                _configuration.WriteGamingHomeApp(expectedHome);
            }
            else
            {
                var inspection = _packageProbe.Inspect();
                if (!inspection.Succeeded)
                    return Failed(Capture(), inspection.FailureReason ?? "The registered Gaming Home package could not be verified.");

                expectedHome = WindowsSteamFsePackageProbe.TryGetAumid(inspection.Package);
                if (inspection.Package is null || inspection.Package.Version < SteamFsePackageContract.FixedPackageVersion || expectedHome is null)
                {
                    var registration = await _registrationClient.EnsureRegisteredAsync(cancellationToken).ConfigureAwait(false);
                    if (!registration.Succeeded)
                        return Failed(Capture(), registration.FailureReason ?? "The Steam Gaming Home package could not be registered.");

                    inspection = _packageProbe.Inspect();
                    if (!inspection.Succeeded)
                        return Failed(Capture(), inspection.FailureReason ?? "The registered Gaming Home package could not be verified.");

                    expectedHome = WindowsSteamFsePackageProbe.TryGetAumid(inspection.Package);
                    if (inspection.Package is null || inspection.Package.Version < SteamFsePackageContract.FixedPackageVersion || expectedHome is null)
                        return Failed(Capture(), "The registered Steam Gaming Home package could not be read back.");
                }

                _configuration.WriteGamingHomeApp(expectedHome);
            }

            var readback = CaptureRawState();
            AppLog.Info("GamingHome", "Gaming Home selection readback.",
                ("WrittenGamingHomeApp", expectedHome),
                ("ReadbackGamingHomeApp", readback.GamingHomeApp),
                ("ReadbackStartupToGamingHome", readback.StartupToGamingHome));
            var verified = string.Equals(readback.GamingHomeApp, expectedHome, StringComparison.Ordinal)
                && readback.StartupToGamingHome == expectedStartup;
            var snapshot = Capture();
            if (verified && snapshot.Available && snapshot.Selection == selection && snapshot.StartupEnabled == expectedStartup)
            {
                AppLog.Info("GamingHome", "Gaming Home selection verified.", ("Selection", selection));
                return new(FrontendGamingHomeMutationOutcome.Succeeded, snapshot, null);
            }

            AppLog.Warn("GamingHome", "Gaming Home selection readback verification failed.", null,
                ("RequestedSelection", selection), ("ReadbackGamingHomeApp", readback.GamingHomeApp),
                ("ReadbackStartupToGamingHome", readback.StartupToGamingHome));
            return Failed(snapshot, "Windows did not confirm the requested Gaming Home state.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return Failed(Capture(), "The Gaming Home selection was cancelled.");
        }
        catch (Exception exception)
        {
            AppLog.Warn("GamingHome", "Gaming Home selection mutation failed.", exception,
                ("RequestedSelection", selection));
            return Failed(Capture(), "The Gaming Home selection could not be changed.");
        }
    }

    internal Task<FrontendGamingHomeMutationResult> SetStartupEnabledAsync(
        bool enabled, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var support = _osProbe.Capture();
        if (!support.Supported)
            return Task.FromResult(UnavailableResult(support.FailureReason ?? DefaultUnavailableReason));

        try
        {
            var previous = CaptureRawState();
            var classification = Classify(previous.GamingHomeApp);
            if (!classification.Succeeded)
                return Task.FromResult(Failed(Capture(), classification.FailureReason ?? "The current Gaming Home app could not be identified."));

            AppLog.Info("GamingHome", "Gaming Home startup requested.",
                ("PreviousStartupToGamingHome", previous.StartupToGamingHome),
                ("RequestedStartupToGamingHome", enabled),
                ("CurrentGamingHomeApp", previous.GamingHomeApp));

            if (classification.Selection == FrontendGamingHomeSelection.Other)
                return Task.FromResult(Failed(Capture(), "Startup behavior for another Windows Gaming Home app is not managed by the Addon."));
            if (classification.Selection == FrontendGamingHomeSelection.None && enabled)
                return Task.FromResult(Failed(Capture(), "Select Xbox or Steam Big Picture before enabling startup."));
            if (classification.Selection == FrontendGamingHomeSelection.None && !previous.StartupToGamingHome)
                return Task.FromResult(new FrontendGamingHomeMutationResult(
                    FrontendGamingHomeMutationOutcome.Succeeded, Capture(), null));

            _configuration.WriteStartupToGamingHome(enabled);
            var readback = CaptureRawState();
            AppLog.Info("GamingHome", "Gaming Home startup readback.",
                ("ReadbackStartupToGamingHome", readback.StartupToGamingHome),
                ("CurrentGamingHomeApp", readback.GamingHomeApp));
            var verified = string.Equals(readback.GamingHomeApp, previous.GamingHomeApp, StringComparison.Ordinal)
                && readback.StartupToGamingHome == enabled;
            var snapshot = Capture();
            if (verified && snapshot.Available && snapshot.Selection == classification.Selection && snapshot.StartupEnabled == enabled)
                return Task.FromResult(new FrontendGamingHomeMutationResult(FrontendGamingHomeMutationOutcome.Succeeded, snapshot, null));

            AppLog.Warn("GamingHome", "Gaming Home startup readback verification failed.", null,
                ("RequestedStartupToGamingHome", enabled),
                ("ReadbackStartupToGamingHome", readback.StartupToGamingHome),
                ("PreviousGamingHomeApp", previous.GamingHomeApp),
                ("ReadbackGamingHomeApp", readback.GamingHomeApp));
            return Task.FromResult(Failed(snapshot, "Windows did not confirm the requested Gaming Home startup state."));
        }
        catch (Exception exception)
        {
            AppLog.Warn("GamingHome", "Gaming Home startup mutation failed.", exception,
                ("RequestedStartupToGamingHome", enabled));
            return Task.FromResult(Failed(Capture(), "The Gaming Home startup setting could not be changed."));
        }
    }

    internal bool TryCleanupForUninstall()
    {
        var configurationCleaned = true;
        try
        {
            var current = CaptureRawState();
            var inspection = _packageProbe.Inspect();
            var package = inspection.Succeeded
                && string.Equals(inspection.Package?.IdentityName, SteamFsePackageContract.PackageIdentityName, StringComparison.Ordinal)
                    ? inspection.Package
                    : null;
            var ownedAumid = WindowsSteamFsePackageProbe.TryGetAumid(package);
            if (ownedAumid is not null && string.Equals(current.GamingHomeApp, ownedAumid, StringComparison.Ordinal))
            {
                _configuration.DeleteGamingHomeApp();
                _configuration.WriteStartupToGamingHome(false);
            }
        }
        catch (Exception exception)
        {
            configurationCleaned = false;
            AppLog.Warn("Uninstall", "Gaming Home GamingConfiguration cleanup failed.", exception);
        }

        var packageRemoved = _packageProbe.TryRemoveOwnedPackage();
        return configurationCleaned && packageRemoved;
    }

    private (string? GamingHomeApp, bool StartupToGamingHome) CaptureRawState() =>
        (_configuration.ReadGamingHomeApp(), _configuration.ReadStartupToGamingHome());

    private (bool Succeeded, FrontendGamingHomeSelection Selection, string? FailureReason) Classify(string? aumid)
    {
        if (string.IsNullOrWhiteSpace(aumid))
            return (true, FrontendGamingHomeSelection.None, null);
        if (string.Equals(aumid, XboxGamingHomeAppIdentity.Aumid, StringComparison.Ordinal))
            return (true, FrontendGamingHomeSelection.Xbox, null);

        var inspection = _packageProbe.Inspect();
        if (!inspection.Succeeded)
            return (true, FrontendGamingHomeSelection.Other, null);

        var steamAumid = WindowsSteamFsePackageProbe.TryGetAumid(inspection.Package);
        return string.Equals(aumid, steamAumid, StringComparison.Ordinal)
            ? (true, FrontendGamingHomeSelection.SteamBigPicture, null)
            : (true, FrontendGamingHomeSelection.Other, null);
    }

    private static FrontendGamingHomeMutationResult Failed(FrontendGamingHomeSnapshot snapshot, string reason) =>
        new(FrontendGamingHomeMutationOutcome.Failed, snapshot, reason);

    private static FrontendGamingHomeMutationResult UnavailableResult(string reason) =>
        new(FrontendGamingHomeMutationOutcome.Unavailable, FrontendGamingHomeSnapshot.Unavailable(reason), reason);
}
