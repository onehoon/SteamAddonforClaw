using SteamInputAddonforClaw.Contracts.Frontend;
using SteamInputAddonforClaw.WindowsGaming;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class SteamFseConfigurationTests
{
    private const string Aumid = "SteamInputAddonforClaw.FseHome_123!App";

    [Fact]
    public void Capture_reports_enabled_only_for_our_aumid_and_startup_flag()
    {
        var enabled = CreateConfiguration(home: Aumid, startup: true).Capture();
        var startupDisabled = CreateConfiguration(home: Aumid, startup: false).Capture();
        var otherLauncher = CreateConfiguration(home: "OtherLauncher_123!App", startup: true).Capture();

        Assert.Equal(new FrontendSteamFseSnapshot(true, true, null), enabled);
        Assert.False(startupDisabled.Enabled);
        Assert.False(otherLauncher.Enabled);
    }

    [Fact]
    public void Capture_fails_closed_when_os_or_package_is_unavailable()
    {
        var unsupported = CreateConfiguration(osSupported: false).Capture();
        var packageMissing = CreateConfiguration(aumid: null).Capture();

        Assert.False(unsupported.Available);
        Assert.False(unsupported.Enabled);
        Assert.False(packageMissing.Available);
        Assert.False(packageMissing.Enabled);
        Assert.NotNull(packageMissing.UnavailableReason);
    }

    [Fact]
    public void Enable_writes_exact_aumid_and_verifies_readback()
    {
        var store = new FakeConfigurationStore();
        var result = CreateConfiguration(store: store).SetEnabled(true);

        Assert.True(result.Succeeded);
        Assert.Equal(Aumid, store.GamingHomeApp);
        Assert.True(store.StartupToGamingHome);
        Assert.True(result.Snapshot.Enabled);
    }

    [Fact]
    public void Enable_does_not_claim_success_when_readback_is_wrong()
    {
        var store = new FakeConfigurationStore { IgnoreWrites = true };
        var result = CreateConfiguration(store: store).SetEnabled(true);

        Assert.False(result.Succeeded);
        Assert.Equal(FrontendSteamFseMutationOutcome.Failed, result.Outcome);
        Assert.False(result.Snapshot.Enabled);
    }

    [Fact]
    public void Disable_deletes_home_and_disables_startup_without_restoring_other_launcher()
    {
        var store = new FakeConfigurationStore
        {
            GamingHomeApp = "OtherLauncher_123!App",
            StartupToGamingHome = true,
        };

        var result = CreateConfiguration(store: store).SetEnabled(false);

        Assert.True(result.Succeeded);
        Assert.Null(store.GamingHomeApp);
        Assert.False(store.StartupToGamingHome);
        Assert.False(result.Snapshot.Enabled);
    }

    [Fact]
    public void Uninstall_cleanup_clears_windows_state_and_removes_only_owned_package()
    {
        var store = new FakeConfigurationStore { GamingHomeApp = Aumid, StartupToGamingHome = true };
        var package = new FakePackageProbe(Aumid);

        var cleaned = CreateConfiguration(store: store, package: package).TryCleanupForUninstall();

        Assert.True(cleaned);
        Assert.Null(store.GamingHomeApp);
        Assert.False(store.StartupToGamingHome);
        Assert.Equal(1, package.RemoveCalls);
    }

    [Fact]
    public void Package_probe_matches_identity_name_but_derives_aumid_from_family_name()
    {
        var owned = new SteamFsePackageInfo(
            WindowsSteamFsePackageProbe.PackageIdentityName,
            "SteamInputAddonforClaw.FseHome_realPublisher_abc",
            "SteamInputAddonforClaw.FseHome_realPublisher_abc_1.0.0.0_x64__full",
            new Version(1, 0));
        var other = new SteamFsePackageInfo(
            "OtherPackage",
            owned.FamilyName,
            "other-full-name",
            new Version(9, 0));
        var enumeration = new FakePackageEnumeration([other, owned]);
        var probe = new WindowsSteamFsePackageProbe(enumeration);

        Assert.Equal($"{owned.FamilyName}!App", probe.TryGetOwnedAumid());
        Assert.True(probe.TryRemoveOwnedPackage());
        Assert.Equal([owned.FullName], enumeration.RemovedPackages);
    }

    private static WindowsGamingHomeConfiguration CreateConfiguration(
        string? home = null,
        bool startup = false,
        bool osSupported = true,
        string? aumid = Aumid,
        FakeConfigurationStore? store = null,
        FakePackageProbe? package = null)
    {
        return new(
            new FakeOsProbe(osSupported),
            package ?? new FakePackageProbe(aumid),
            store ?? new FakeConfigurationStore { GamingHomeApp = home, StartupToGamingHome = startup });
    }

    private sealed class FakeOsProbe(bool supported) : ISteamFseOsProbe
    {
        public SteamFseOsSupport Capture() => supported
            ? new(true, null)
            : new(false, "unsupported-test-os");
    }

    private sealed class FakePackageProbe(string? aumid) : ISteamFsePackageProbe
    {
        public int RemoveCalls { get; private set; }
        public string? TryGetOwnedAumid() => aumid;
        public bool TryRemoveOwnedPackage() { RemoveCalls++; return true; }
    }

    private sealed class FakePackageEnumeration(IReadOnlyList<SteamFsePackageInfo> packages) : ISteamFsePackageEnumeration
    {
        public List<string> RemovedPackages { get; } = [];
        public IReadOnlyList<SteamFsePackageInfo> FindCurrentUserPackages() => packages;
        public void RemovePackage(string fullName) => RemovedPackages.Add(fullName);
    }

    private sealed class FakeConfigurationStore : IGamingConfigurationStore
    {
        public string? GamingHomeApp { get; set; }
        public bool StartupToGamingHome { get; set; }
        public bool IgnoreWrites { get; init; }

        public string? ReadGamingHomeApp() => GamingHomeApp;
        public bool ReadStartupToGamingHome() => StartupToGamingHome;
        public void WriteGamingHomeApp(string aumid) { if (!IgnoreWrites) GamingHomeApp = aumid; }
        public void DeleteGamingHomeApp() { if (!IgnoreWrites) GamingHomeApp = null; }
        public void WriteStartupToGamingHome(bool enabled) { if (!IgnoreWrites) StartupToGamingHome = enabled; }
    }
}
