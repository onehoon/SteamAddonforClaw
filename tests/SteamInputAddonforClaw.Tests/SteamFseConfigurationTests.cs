using SteamInputAddonforClaw.Contracts.Frontend;
using SteamInputAddonforClaw.WindowsGaming;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class SteamFseConfigurationTests
{
    private const string SteamAumid = "SteamInputAddonforClaw.FseHome_123!App";
    private const string XboxAumid = "Microsoft.GamingApp_8wekyb3d8bbwe!Microsoft.Xbox.App";

    [Fact]
    public void Capture_reports_none_and_startup_independently()
    {
        var snapshot = CreateConfiguration().Capture();

        Assert.Equal(new FrontendGamingHomeSnapshot(true, FrontendGamingHomeSelection.None, false, null), snapshot);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Capture_reports_xbox_selection_and_actual_startup_value(bool startup)
    {
        var snapshot = CreateConfiguration(home: XboxAumid, startup: startup).Capture();

        Assert.Equal(FrontendGamingHomeSelection.Xbox, snapshot.Selection);
        Assert.Equal(startup, snapshot.StartupEnabled);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Capture_reports_steam_selection_and_actual_startup_value(bool startup)
    {
        var snapshot = CreateConfiguration(home: SteamAumid, startup: startup).Capture();

        Assert.Equal(FrontendGamingHomeSelection.SteamBigPicture, snapshot.Selection);
        Assert.Equal(startup, snapshot.StartupEnabled);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Capture_reports_an_external_home_as_other_and_preserves_startup(bool startup)
    {
        var snapshot = CreateConfiguration(home: "ExternalLauncher_family!App", startup: startup).Capture();

        Assert.Equal(FrontendGamingHomeSelection.Other, snapshot.Selection);
        Assert.Equal(startup, snapshot.StartupEnabled);
    }

    [Fact]
    public void Capture_does_not_require_steam_package_for_off_or_xbox()
    {
        var off = CreateConfiguration(probeSucceeded: false).Capture();
        var xbox = CreateConfiguration(home: XboxAumid, probeSucceeded: false).Capture();

        Assert.True(off.Available);
        Assert.Equal(FrontendGamingHomeSelection.None, off.Selection);
        Assert.True(xbox.Available);
        Assert.Equal(FrontendGamingHomeSelection.Xbox, xbox.Selection);
    }

    [Fact]
    public void Capture_with_missing_steam_package_keeps_unknown_windows_home_available_as_other()
    {
        var snapshot = CreateConfiguration(home: "OldSteamFamily!App", startup: true, aumid: null).Capture();

        Assert.True(snapshot.Available);
        Assert.Equal(FrontendGamingHomeSelection.Other, snapshot.Selection);
        Assert.True(snapshot.StartupEnabled);
    }

    [Fact]
    public void Capture_fails_closed_when_os_is_unsupported()
    {
        var unsupported = CreateConfiguration(osSupported: false).Capture();

        Assert.False(unsupported.Available);
    }

    [Fact]
    public void Capture_keeps_unknown_home_available_as_other_when_steam_package_probe_fails()
    {
        var snapshot = CreateConfiguration(
            home: "Unclassified_family!App",
            startup: true,
            probeSucceeded: false).Capture();

        Assert.True(snapshot.Available);
        Assert.Equal(FrontendGamingHomeSelection.Other, snapshot.Selection);
        Assert.True(snapshot.StartupEnabled);
        Assert.Null(snapshot.UnavailableReason);
    }

    [Fact]
    public void Capture_recognizes_the_current_steam_package_even_when_its_version_is_older()
    {
        var store = new FakeConfigurationStore { GamingHomeApp = "old_family!App", StartupToGamingHome = true };
        var package = new FakePackageProbe(Package("old_family", "0.9.0.0"));

        var snapshot = CreateConfiguration(store: store, package: package).Capture();

        Assert.Equal(FrontendGamingHomeSelection.SteamBigPicture, snapshot.Selection);
        Assert.True(snapshot.StartupEnabled);
    }

    [Fact]
    public async Task Select_none_clears_home_and_forces_startup_off()
    {
        var store = new FakeConfigurationStore { GamingHomeApp = "External_family!App", StartupToGamingHome = true };
        var result = await CreateConfiguration(store: store, probeSucceeded: false)
            .SetSelectionAsync(FrontendGamingHomeSelection.None);

        Assert.True(result.Succeeded);
        Assert.Null(store.GamingHomeApp);
        Assert.False(store.StartupToGamingHome);
        Assert.Equal(FrontendGamingHomeSelection.None, result.Snapshot.Selection);
        Assert.False(result.Snapshot.StartupEnabled);
        Assert.Equal(["DeleteGamingHomeApp", "WriteStartup:False"], store.Writes);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Select_xbox_writes_exact_aumid_and_preserves_startup(bool startup)
    {
        var store = new FakeConfigurationStore { StartupToGamingHome = startup };
        var result = await CreateConfiguration(store: store, xboxResolvable: true)
            .SetSelectionAsync(FrontendGamingHomeSelection.Xbox);

        Assert.True(result.Succeeded);
        Assert.Equal(XboxAumid, store.GamingHomeApp);
        Assert.Equal(startup, store.StartupToGamingHome);
        Assert.Equal(["WriteHome:" + XboxAumid], store.Writes);
        Assert.Equal(FrontendGamingHomeSelection.Xbox, result.Snapshot.Selection);
        Assert.Equal(startup, result.Snapshot.StartupEnabled);
    }

    [Fact]
    public async Task Select_xbox_recovers_from_unknown_home_when_steam_package_probe_fails()
    {
        var store = new FakeConfigurationStore { GamingHomeApp = "Unclassified_family!App", StartupToGamingHome = true };
        var result = await CreateConfiguration(store: store, probeSucceeded: false, xboxResolvable: true)
            .SetSelectionAsync(FrontendGamingHomeSelection.Xbox);

        Assert.True(result.Succeeded);
        Assert.Equal(XboxAumid, store.GamingHomeApp);
        Assert.True(store.StartupToGamingHome);
        Assert.Equal(FrontendGamingHomeSelection.Xbox, result.Snapshot.Selection);
        Assert.Equal(["WriteHome:" + XboxAumid], store.Writes);
    }

    [Fact]
    public async Task Select_xbox_does_not_probe_or_register_the_addon_fse_package()
    {
        var package = new FakePackageProbe(null);
        var registration = new FakeRegistration();

        var result = await CreateConfiguration(package: package, registration: registration, xboxResolvable: true)
            .SetSelectionAsync(FrontendGamingHomeSelection.Xbox);

        Assert.True(result.Succeeded);
        Assert.Equal(0, package.InspectCalls);
        Assert.Equal(0, registration.Calls);
    }

    [Fact]
    public async Task Select_xbox_when_app_is_missing_does_not_mutate_windows_state()
    {
        var store = new FakeConfigurationStore { GamingHomeApp = "External_family!App", StartupToGamingHome = true };
        var result = await CreateConfiguration(store: store, xboxResolvable: false)
            .SetSelectionAsync(FrontendGamingHomeSelection.Xbox);

        Assert.False(result.Succeeded);
        Assert.Equal("External_family!App", store.GamingHomeApp);
        Assert.True(store.StartupToGamingHome);
        Assert.Empty(store.Writes);
        Assert.Equal(FrontendGamingHomeSelection.Other, result.Snapshot.Selection);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Select_steam_with_current_package_preserves_startup(bool startup)
    {
        var store = new FakeConfigurationStore { StartupToGamingHome = startup };
        var registration = new FakeRegistration();
        var result = await CreateConfiguration(store: store, startup: startup, registration: registration)
            .SetSelectionAsync(FrontendGamingHomeSelection.SteamBigPicture);

        Assert.True(result.Succeeded);
        Assert.Equal(SteamAumid, store.GamingHomeApp);
        Assert.Equal(startup, store.StartupToGamingHome);
        Assert.Equal(["WriteHome:" + SteamAumid], store.Writes);
        Assert.Equal(0, registration.Calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Select_steam_registers_missing_or_old_package_once_and_preserves_startup(bool startup)
    {
        var package = new FakePackageProbe(null);
        var registration = new FakeRegistration
        {
            OnEnsure = () => package.Package = Package("SteamInputAddonforClaw.FseHome_registered", "1.0.1.0"),
        };
        var store = new FakeConfigurationStore { StartupToGamingHome = startup };

        var result = await CreateConfiguration(store: store, package: package, registration: registration)
            .SetSelectionAsync(FrontendGamingHomeSelection.SteamBigPicture);

        Assert.True(result.Succeeded);
        Assert.Equal("SteamInputAddonforClaw.FseHome_registered!App", store.GamingHomeApp);
        Assert.Equal(startup, store.StartupToGamingHome);
        Assert.Equal(["WriteHome:SteamInputAddonforClaw.FseHome_registered!App"], store.Writes);
        Assert.Equal(1, registration.Calls);
    }

    [Fact]
    public async Task Select_steam_upgrades_an_old_package_only_when_steam_is_selected()
    {
        var package = new FakePackageProbe(Package("old_family", "0.9.0.0"));
        var registration = new FakeRegistration
        {
            OnEnsure = () => package.Package = Package("updated_family", "1.0.1.0"),
        };
        var store = new FakeConfigurationStore { StartupToGamingHome = true };

        var result = await CreateConfiguration(store: store, package: package, registration: registration)
            .SetSelectionAsync(FrontendGamingHomeSelection.SteamBigPicture);

        Assert.True(result.Succeeded);
        Assert.Equal("updated_family!App", store.GamingHomeApp);
        Assert.True(store.StartupToGamingHome);
        Assert.Equal(1, registration.Calls);
    }

    [Fact]
    public async Task Select_other_is_rejected_without_mutation()
    {
        var store = new FakeConfigurationStore { GamingHomeApp = "External_family!App", StartupToGamingHome = true };
        var result = await CreateConfiguration(store: store).SetSelectionAsync(FrontendGamingHomeSelection.Other);

        Assert.False(result.Succeeded);
        Assert.Equal("External_family!App", store.GamingHomeApp);
        Assert.True(store.StartupToGamingHome);
        Assert.Empty(store.Writes);
    }

    [Fact]
    public async Task Selection_readback_mismatch_returns_the_authoritative_snapshot_as_failure()
    {
        var store = new FakeConfigurationStore { IgnoreWrites = true };
        var result = await CreateConfiguration(store: store, xboxResolvable: true)
            .SetSelectionAsync(FrontendGamingHomeSelection.Xbox);

        Assert.False(result.Succeeded);
        Assert.Equal(FrontendGamingHomeSelection.None, result.Snapshot.Selection);
        Assert.False(result.Snapshot.StartupEnabled);
    }

    [Theory]
    [InlineData(FrontendGamingHomeSelection.Xbox, false, true)]
    [InlineData(FrontendGamingHomeSelection.Xbox, true, false)]
    [InlineData(FrontendGamingHomeSelection.SteamBigPicture, false, true)]
    [InlineData(FrontendGamingHomeSelection.SteamBigPicture, true, false)]
    public async Task Startup_mutation_changes_only_startup_for_selected_xbox_or_steam(
        FrontendGamingHomeSelection selection, bool previousStartup, bool requestedStartup)
    {
        var home = selection == FrontendGamingHomeSelection.Xbox ? XboxAumid : SteamAumid;
        var store = new FakeConfigurationStore { GamingHomeApp = home, StartupToGamingHome = previousStartup };

        var result = await CreateConfiguration(store: store).SetStartupEnabledAsync(requestedStartup);

        Assert.True(result.Succeeded);
        Assert.Equal(home, store.GamingHomeApp);
        Assert.Equal(requestedStartup, store.StartupToGamingHome);
        Assert.Equal(["WriteStartup:" + requestedStartup], store.Writes);
        Assert.Equal(selection, result.Snapshot.Selection);
        Assert.Equal(requestedStartup, result.Snapshot.StartupEnabled);
    }

    [Fact]
    public async Task Startup_on_is_rejected_when_no_home_is_selected()
    {
        var store = new FakeConfigurationStore();

        var result = await CreateConfiguration(store: store).SetStartupEnabledAsync(true);

        Assert.False(result.Succeeded);
        Assert.Equal(FrontendGamingHomeSelection.None, result.Snapshot.Selection);
        Assert.Empty(store.Writes);
    }

    [Fact]
    public async Task Startup_off_is_idempotent_when_no_home_is_selected()
    {
        var store = new FakeConfigurationStore();

        var result = await CreateConfiguration(store: store).SetStartupEnabledAsync(false);

        Assert.True(result.Succeeded);
        Assert.Equal(FrontendGamingHomeSelection.None, result.Snapshot.Selection);
        Assert.False(result.Snapshot.StartupEnabled);
        Assert.Empty(store.Writes);
    }

    [Fact]
    public async Task Startup_mutation_is_rejected_for_an_external_home()
    {
        var store = new FakeConfigurationStore { GamingHomeApp = "External_family!App", StartupToGamingHome = true };

        var result = await CreateConfiguration(store: store).SetStartupEnabledAsync(false);

        Assert.False(result.Succeeded);
        Assert.Equal(FrontendGamingHomeSelection.Other, result.Snapshot.Selection);
        Assert.True(store.StartupToGamingHome);
        Assert.Empty(store.Writes);
    }

    [Fact]
    public async Task Startup_mutation_is_rejected_for_unknown_home_when_steam_package_probe_fails()
    {
        var store = new FakeConfigurationStore { GamingHomeApp = "Unclassified_family!App", StartupToGamingHome = true };

        var result = await CreateConfiguration(store: store, probeSucceeded: false).SetStartupEnabledAsync(false);

        Assert.False(result.Succeeded);
        Assert.True(result.Snapshot.Available);
        Assert.Equal(FrontendGamingHomeSelection.Other, result.Snapshot.Selection);
        Assert.True(result.Snapshot.StartupEnabled);
        Assert.True(store.StartupToGamingHome);
        Assert.Empty(store.Writes);
    }

    [Fact]
    public async Task Startup_readback_mismatch_does_not_claim_success()
    {
        var store = new FakeConfigurationStore { GamingHomeApp = XboxAumid, IgnoreStartupWrites = true };

        var result = await CreateConfiguration(store: store).SetStartupEnabledAsync(true);

        Assert.False(result.Succeeded);
        Assert.Equal(FrontendGamingHomeSelection.Xbox, result.Snapshot.Selection);
        Assert.False(result.Snapshot.StartupEnabled);
    }

    [Fact]
    public async Task Startup_mutation_fails_if_the_home_changes_during_write()
    {
        var store = new FakeConfigurationStore { GamingHomeApp = XboxAumid };
        store.AfterStartupWrite = () => store.GamingHomeApp = "External_family!App";
        var result = await CreateConfiguration(store: store).SetStartupEnabledAsync(true);

        Assert.False(result.Succeeded);
        Assert.Equal("External_family!App", store.GamingHomeApp);
        Assert.Equal(FrontendGamingHomeSelection.Other, result.Snapshot.Selection);
        Assert.True(result.Snapshot.StartupEnabled);
    }

    [Fact]
    public void Uninstall_cleanup_clears_windows_state_and_removes_only_owned_package()
    {
        var store = new FakeConfigurationStore { GamingHomeApp = SteamAumid, StartupToGamingHome = true };
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
        var other = new SteamFsePackageInfo("OtherPackage", owned.FamilyName, "other-full-name", new Version(9, 0));
        var enumeration = new FakePackageEnumeration([other, owned]);
        var probe = new WindowsSteamFsePackageProbe(enumeration);

        Assert.Equal($"{owned.FamilyName}!App", WindowsSteamFsePackageProbe.TryGetAumid(probe.Inspect().Package));
        Assert.True(probe.TryRemoveOwnedPackage());
        Assert.Equal([owned.FullName], enumeration.RemovedPackages);
    }

    [Fact]
    public void Xbox_identity_is_shared_with_the_front_button_launcher()
    {
        Assert.Equal(XboxAumid, XboxGamingHomeAppIdentity.Aumid);
    }

    private static WindowsGamingHomeConfiguration CreateConfiguration(
        string? home = null,
        bool startup = false,
        bool osSupported = true,
        bool probeSucceeded = true,
        string? aumid = SteamAumid,
        FakeConfigurationStore? store = null,
        FakePackageProbe? package = null,
        FakeRegistration? registration = null,
        bool xboxResolvable = false)
    {
        return new(
            new FakeOsProbe(osSupported),
            package ?? new FakePackageProbe(aumid is null ? null : Package(aumid[..^4], SteamFsePackageContract.FixedPackageVersionText), probeSucceeded),
            store ?? new FakeConfigurationStore { GamingHomeApp = home, StartupToGamingHome = startup },
            registration ?? new FakeRegistration(),
            new FakeXboxAppProbe(xboxResolvable));
    }

    private static SteamFsePackageInfo Package(string family, string version) =>
        new(WindowsSteamFsePackageProbe.PackageIdentityName, family, $"{family}_{version}_x64__full", Version.Parse(version));

    private sealed class FakeOsProbe(bool supported) : IGamingHomeOsProbe
    {
        public GamingHomeOsSupport Capture() => supported ? new(true, null) : new(false, "unsupported-test-os");
    }

    private sealed class FakePackageProbe(SteamFsePackageInfo? package, bool succeeded = true) : ISteamFsePackageProbe
    {
        public SteamFsePackageInfo? Package { get; set; } = package;
        public int RemoveCalls { get; private set; }
        public int InspectCalls { get; private set; }
        public SteamFsePackageInspection Inspect()
        {
            InspectCalls++;
            return succeeded ? new(true, Package, null) : new(false, null, "probe-failed");
        }
        public bool TryRemoveOwnedPackage() { RemoveCalls++; return true; }
    }

    private sealed class FakeXboxAppProbe(bool resolvable) : IXboxGamingHomeAppProbe
    {
        public int Calls { get; private set; }
        public Task<bool> IsResolvableAsync() { Calls++; return Task.FromResult(resolvable); }
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
        public bool IgnoreStartupWrites { get; init; }
        public Action? AfterStartupWrite { get; set; }
        public List<string> Writes { get; } = [];

        public string? ReadGamingHomeApp() => GamingHomeApp;
        public bool ReadStartupToGamingHome() => StartupToGamingHome;
        public void WriteGamingHomeApp(string aumid)
        {
            Writes.Add("WriteHome:" + aumid);
            if (!IgnoreWrites) GamingHomeApp = aumid;
        }
        public void DeleteGamingHomeApp()
        {
            Writes.Add("DeleteGamingHomeApp");
            if (!IgnoreWrites) GamingHomeApp = null;
        }
        public void WriteStartupToGamingHome(bool enabled)
        {
            Writes.Add("WriteStartup:" + enabled);
            if (!IgnoreWrites && !IgnoreStartupWrites) StartupToGamingHome = enabled;
            AfterStartupWrite?.Invoke();
        }
    }
}
