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
    public void Capture_reports_supported_os_and_missing_package_as_interactive_off()
    {
        var packageMissing = CreateConfiguration(aumid: null).Capture();

        Assert.Equal(new FrontendSteamFseSnapshot(true, false, null), packageMissing);
    }

    [Fact]
    public void Capture_fails_closed_when_os_or_package_probe_is_unavailable()
    {
        var unsupported = CreateConfiguration(osSupported: false).Capture();
        var probeFailure = CreateConfiguration(probeSucceeded: false).Capture();

        Assert.False(unsupported.Available);
        Assert.False(unsupported.Enabled);
        Assert.False(probeFailure.Available);
        Assert.False(probeFailure.Enabled);
        Assert.NotNull(probeFailure.UnavailableReason);
    }

    [Fact]
    public void Capture_treats_an_older_package_as_available_but_off()
    {
        var package = new FakePackageProbe(Package("old_family", "0.9.0.0"));
        var store = new FakeConfigurationStore { GamingHomeApp = "old_family!App", StartupToGamingHome = true };

        var snapshot = CreateConfiguration(store: store, package: package).Capture();

        Assert.Equal(new FrontendSteamFseSnapshot(true, false, null), snapshot);
    }

    [Fact]
    public async Task Enable_with_current_package_does_not_invoke_registration()
    {
        var registration = new FakeRegistration();
        var store = new FakeConfigurationStore();
        var result = await CreateConfiguration(store: store, registration: registration).SetEnabledAsync(true);

        Assert.True(result.Succeeded);
        Assert.Equal(0, registration.Calls);
        Assert.Equal(Aumid, store.GamingHomeApp);
        Assert.True(store.StartupToGamingHome);
    }

    [Fact]
    public async Task Enable_with_missing_package_registers_once_then_uses_actual_family_aumid()
    {
        var package = new FakePackageProbe(null);
        var registration = new FakeRegistration
        {
            OnEnsure = () => package.Package = Package("SteamInputAddonforClaw.FseHome_registered", "1.0.0.0"),
        };
        var store = new FakeConfigurationStore();

        var result = await CreateConfiguration(store: store, package: package, registration: registration).SetEnabledAsync(true);

        Assert.True(result.Succeeded);
        Assert.Equal(1, registration.Calls);
        Assert.Equal("SteamInputAddonforClaw.FseHome_registered!App", store.GamingHomeApp);
        Assert.True(store.StartupToGamingHome);
    }

    [Fact]
    public async Task Enable_with_older_package_registers_once()
    {
        var package = new FakePackageProbe(Package("old_family", "0.9.0.0"));
        var registration = new FakeRegistration
        {
            OnEnsure = () => package.Package = Package("new_family", "1.0.0.0"),
        };

        var result = await CreateConfiguration(package: package, registration: registration).SetEnabledAsync(true);

        Assert.True(result.Succeeded);
        Assert.Equal(1, registration.Calls);
    }

    [Fact]
    public async Task Registration_failure_writes_no_on_registry_state()
    {
        var store = new FakeConfigurationStore();
        var registration = new FakeRegistration { Result = new(false, "uac-cancelled") };
        var result = await CreateConfiguration(aumid: null, store: store, registration: registration).SetEnabledAsync(true);

        Assert.False(result.Succeeded);
        Assert.Equal(FrontendSteamFseMutationOutcome.Failed, result.Outcome);
        Assert.Null(store.GamingHomeApp);
        Assert.False(store.StartupToGamingHome);
        Assert.Contains("uac-cancelled", result.FailureMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Enable_does_not_claim_success_when_readback_is_wrong()
    {
        var store = new FakeConfigurationStore { IgnoreWrites = true };
        var result = await CreateConfiguration(store: store).SetEnabledAsync(true);

        Assert.False(result.Succeeded);
        Assert.Equal(FrontendSteamFseMutationOutcome.Failed, result.Outcome);
        Assert.False(result.Snapshot.Enabled);
    }

    [Fact]
    public async Task Disable_deletes_home_and_disables_startup_without_restoring_other_launcher()
    {
        var store = new FakeConfigurationStore
        {
            GamingHomeApp = "OtherLauncher_123!App",
            StartupToGamingHome = true,
        };

        var result = await CreateConfiguration(store: store).SetEnabledAsync(false);

        Assert.True(result.Succeeded);
        Assert.Null(store.GamingHomeApp);
        Assert.False(store.StartupToGamingHome);
        Assert.False(result.Snapshot.Enabled);
    }

    [Fact]
    public async Task Disable_then_enable_after_registration_does_not_register_again()
    {
        var registration = new FakeRegistration();
        var store = new FakeConfigurationStore { GamingHomeApp = Aumid, StartupToGamingHome = true };
        var configuration = CreateConfiguration(store: store, registration: registration);

        Assert.True((await configuration.SetEnabledAsync(false)).Succeeded);
        Assert.True((await configuration.SetEnabledAsync(true)).Succeeded);
        Assert.Equal(0, registration.Calls);
    }

    [Fact]
    public void Uninstall_cleanup_clears_windows_state_and_removes_only_owned_package()
    {
        var store = new FakeConfigurationStore { GamingHomeApp = Aumid, StartupToGamingHome = true };
        var package = new FakePackageProbe(Package("owned_family", "1.0.0.0"));

        var cleaned = CreateConfiguration(store: store, package: package).TryCleanupForUninstall();

        Assert.True(cleaned);
        Assert.Null(store.GamingHomeApp);
        Assert.False(store.StartupToGamingHome);
        Assert.Equal(1, package.RemoveCalls);
    }

    [Fact]
    public void Package_probe_matches_identity_name_but_derives_aumid_from_family_name()
    {
        var owned = Package("SteamInputAddonforClaw.FseHome_realPublisher_abc", "1.0.0.0");
        var other = new SteamFsePackageInfo(
            "OtherPackage",
            owned.FamilyName,
            "other-full-name",
            new Version(9, 0));
        var enumeration = new FakePackageEnumeration([other, owned]);
        var probe = new WindowsSteamFsePackageProbe(enumeration);

        Assert.Equal($"{owned.FamilyName}!App", WindowsSteamFsePackageProbe.TryGetAumid(probe.Inspect().Package));
        Assert.True(probe.TryRemoveOwnedPackage());
        Assert.Equal([owned.FullName], enumeration.RemovedPackages);
    }

    private static WindowsGamingHomeConfiguration CreateConfiguration(
        string? home = null,
        bool startup = false,
        bool osSupported = true,
        bool probeSucceeded = true,
        string? aumid = Aumid,
        FakeConfigurationStore? store = null,
        FakePackageProbe? package = null,
        FakeRegistration? registration = null)
    {
        return new(
            new FakeOsProbe(osSupported),
            package ?? new FakePackageProbe(aumid is null ? null : Package(aumid[..^4], SteamFsePackageContract.FixedPackageVersionText), probeSucceeded),
            store ?? new FakeConfigurationStore { GamingHomeApp = home, StartupToGamingHome = startup },
            registration ?? new FakeRegistration());
    }

    private static SteamFsePackageInfo Package(string family, string version) =>
        new(WindowsSteamFsePackageProbe.PackageIdentityName, family, $"{family}_{version}_x64__full", Version.Parse(version));

    private sealed class FakeOsProbe(bool supported) : ISteamFseOsProbe
    {
        public SteamFseOsSupport Capture() => supported
            ? new(true, null)
            : new(false, "unsupported-test-os");
    }

    private sealed class FakePackageProbe(SteamFsePackageInfo? package, bool succeeded = true) : ISteamFsePackageProbe
    {
        public SteamFsePackageInfo? Package { get; set; } = package;
        public int RemoveCalls { get; private set; }

        public SteamFsePackageInspection Inspect() => succeeded
            ? new(true, Package, null)
            : new(false, null, "probe-failed");

        public bool TryRemoveOwnedPackage() { RemoveCalls++; return true; }
    }

    private sealed class FakeRegistration : ISteamFseRegistrationClient
    {
        public int Calls { get; private set; }
        public SteamFseRegistrationResult Result { get; init; } = new(true, null);
        public Action? OnEnsure { get; init; }

        public Task<SteamFseRegistrationResult> EnsureRegisteredAsync(CancellationToken cancellationToken = default)
        {
            Calls++;
            OnEnsure?.Invoke();
            return Task.FromResult(Result);
        }
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
