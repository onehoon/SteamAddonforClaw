using Microsoft.Win32;
using SteamInputAddonforClaw.Contracts.Frontend;
using SteamInputAddonforClaw.Diagnostics;
using Windows.Management.Deployment;

namespace SteamInputAddonforClaw.WindowsGaming;

internal sealed record SteamFseOsSupport(bool Supported, string? FailureReason);

internal interface ISteamFseOsProbe
{
    SteamFseOsSupport Capture();
}

internal interface ISteamFsePackageProbe
{
    string? TryGetOwnedAumid();
    bool TryRemoveOwnedPackage();
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
    internal const string PackageIdentityName = "SteamInputAddonforClaw.FseHome";
    internal const string ApplicationId = "App";

    public string? TryGetOwnedAumid()
    {
        if (!OperatingSystem.IsWindows()) return null;

        try
        {
            var package = FindOwnedPackages().OrderByDescending(item => item.Id.Version).FirstOrDefault();
            return package is null ? null : $"{package.Id.FamilyName}!{ApplicationId}";
        }
        catch (Exception exception)
        {
            AppLog.Warn("SteamFSE", "Owned Gaming Home package probe failed.", exception,
                ("PackageIdentity", PackageIdentityName));
            return null;
        }
    }

    public bool TryRemoveOwnedPackage()
    {
        if (!OperatingSystem.IsWindows()) return true;

        try
        {
            var manager = new PackageManager();
            foreach (var package in FindOwnedPackages(manager))
                manager.RemovePackageAsync(package.Id.FullName).AsTask().GetAwaiter().GetResult();
            return true;
        }
        catch (Exception exception)
        {
            AppLog.Warn("Uninstall", "Owned Gaming Home package removal failed.", exception,
                ("PackageIdentity", PackageIdentityName));
            return false;
        }
    }

    private static IEnumerable<Windows.ApplicationModel.Package> FindOwnedPackages()
        => FindOwnedPackages(new PackageManager());

    private static IEnumerable<Windows.ApplicationModel.Package> FindOwnedPackages(PackageManager manager)
        => manager.FindPackagesForUser(string.Empty, PackageIdentityName);
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
    private readonly IGamingConfigurationStore _configuration;

    internal WindowsGamingHomeConfiguration(
        ISteamFseOsProbe? osProbe = null,
        ISteamFsePackageProbe? packageProbe = null,
        IGamingConfigurationStore? configuration = null)
    {
        _osProbe = osProbe ?? new WindowsSteamFseOsProbe();
        _packageProbe = packageProbe ?? new WindowsSteamFsePackageProbe();
        _configuration = configuration ?? new WindowsGamingConfigurationStore();
    }

    internal FrontendSteamFseSnapshot Capture()
    {
        var support = _osProbe.Capture();
        if (!support.Supported)
            return FrontendSteamFseSnapshot.Unavailable(support.FailureReason ?? DefaultUnavailableReason);

        var aumid = _packageProbe.TryGetOwnedAumid();
        if (string.IsNullOrWhiteSpace(aumid))
            return FrontendSteamFseSnapshot.Unavailable("The Steam Big Picture Gaming Home package is not registered.");

        var selectedHome = _configuration.ReadGamingHomeApp();
        var startup = _configuration.ReadStartupToGamingHome();
        return new(true, startup && string.Equals(selectedHome, aumid, StringComparison.Ordinal), null);
    }

    internal FrontendSteamFseMutationResult SetEnabled(bool enabled)
    {
        var support = _osProbe.Capture();
        if (!support.Supported)
            return Unavailable(support.FailureReason ?? DefaultUnavailableReason);

        var aumid = _packageProbe.TryGetOwnedAumid();
        if (enabled && string.IsNullOrWhiteSpace(aumid))
            return Unavailable("The Steam Big Picture Gaming Home package is not registered.");

        try
        {
            AppLog.Info("SteamFSE", enabled ? "SteamFSE enable requested." : "SteamFSE disable requested.");
            if (enabled)
            {
                _configuration.WriteStartupToGamingHome(true);
                _configuration.WriteGamingHomeApp(aumid!);
            }
            else
            {
                _configuration.DeleteGamingHomeApp();
                _configuration.WriteStartupToGamingHome(false);
            }

            var readback = CaptureRawState();
            var verified = enabled
                ? string.Equals(readback.GamingHomeApp, aumid, StringComparison.Ordinal) && readback.StartupToGamingHome
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
            return new(FrontendSteamFseMutationOutcome.Failed, snapshot, "Windows did not confirm the requested Gaming Home state.");
        }
        catch (Exception exception)
        {
            AppLog.Warn("SteamFSE", "SteamFSE registry mutation failed.", exception,
                ("EnabledRequested", enabled));
            return new(FrontendSteamFseMutationOutcome.Failed, Capture(), "The Gaming Home setting could not be changed.");
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

    private FrontendSteamFseMutationResult Unavailable(string reason) =>
        new(FrontendSteamFseMutationOutcome.Unavailable, FrontendSteamFseSnapshot.Unavailable(reason), reason);
}
