using SteamInputAddonforClaw.Contracts.Frontend;
using SteamInputAddonforClaw.WindowsGaming;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class SteamFsePackageProvisionerTests
{
    [Fact]
    public void Missing_exact_package_requests_install_and_verifies_family_derived_aumid()
    {
        var packages = new FakePackageEnumeration([
            Package("Other.Package", "other_family", "9.0.0.0")
        ]);
        var deployment = new FakeDeployment(packages, Package(WindowsSteamFsePackageProbe.PackageIdentityName, "SteamInputAddonforClaw.FseHome_publisher", "1.2.3.4"));

        var result = CreateProvisioner(packages, deployment, "1.2.3.4").EnsureProvisioned();

        Assert.Equal(SteamFsePackageProvisioningOutcome.Provisioned, result.Outcome);
        Assert.Equal("SteamInputAddonforClaw.FseHome_publisher!App", result.Aumid);
        Assert.Equal(1, deployment.InstallCalls);
    }

    [Fact]
    public void Current_exact_package_is_a_cheap_noop()
    {
        var packages = new FakePackageEnumeration([
            Package(WindowsSteamFsePackageProbe.PackageIdentityName, "stable_family", "1.2.3.4")
        ]);
        var deployment = new FakeDeployment(packages, packages.Packages[0]);

        var result = CreateProvisioner(packages, deployment, "1.2.3.4").EnsureProvisioned();

        Assert.Equal(SteamFsePackageProvisioningOutcome.AlreadyProvisioned, result.Outcome);
        Assert.Equal(0, deployment.InstallCalls);
    }

    [Fact]
    public void Older_exact_package_requests_an_in_place_update()
    {
        var packages = new FakePackageEnumeration([
            Package(WindowsSteamFsePackageProbe.PackageIdentityName, "stable_family", "1.2.2.0")
        ]);
        var deployment = new FakeDeployment(packages, Package(WindowsSteamFsePackageProbe.PackageIdentityName, "stable_family", "1.2.3.0"));

        var result = CreateProvisioner(packages, deployment, "1.2.3.0").EnsureProvisioned();

        Assert.Equal(SteamFsePackageProvisioningOutcome.Provisioned, result.Outcome);
        Assert.Equal(new Version(1, 2, 3, 0), result.PackageVersion);
        Assert.Equal(1, deployment.InstallCalls);
    }

    [Fact]
    public void Different_package_identity_does_not_satisfy_provisioning()
    {
        var packages = new FakePackageEnumeration([
            Package("SteamInputAddonforClaw.FseHome.Legacy", "stable_family", "9.0.0.0")
        ]);
        var deployment = new FakeDeployment(packages, Package(WindowsSteamFsePackageProbe.PackageIdentityName, "stable_family", "1.0.0.0"));

        var result = CreateProvisioner(packages, deployment, "1.0.0.0").EnsureProvisioned();

        Assert.Equal(SteamFsePackageProvisioningOutcome.Provisioned, result.Outcome);
        Assert.Equal(1, deployment.InstallCalls);
    }

    [Fact]
    public void Registration_failure_is_reported_without_throwing()
    {
        var packages = new FakePackageEnumeration([]);
        var deployment = new FakeDeployment(packages, null) { Failure = new InvalidOperationException("registration failed") };

        var result = CreateProvisioner(packages, deployment, "1.0.0.0").EnsureProvisioned();

        Assert.Equal(SteamFsePackageProvisioningOutcome.Failed, result.Outcome);
        Assert.Contains("registration failed", result.FailureReason, StringComparison.Ordinal);
    }

    [Fact]
    public void Unsupported_os_skips_package_manager_and_registration()
    {
        var packages = new FakePackageEnumeration([]);
        var deployment = new FakeDeployment(packages, null);

        var result = CreateProvisioner(packages, deployment, "1.0.0.0", supported: false).EnsureProvisioned();

        Assert.Equal(SteamFsePackageProvisioningOutcome.Unsupported, result.Outcome);
        Assert.Equal(0, deployment.InstallCalls);
    }

    [Fact]
    public void Successful_provisioning_makes_settings_available_without_enabling_preference()
    {
        var packages = new FakePackageEnumeration([
            Package(WindowsSteamFsePackageProbe.PackageIdentityName, "stable_family", "1.0.0.0")
        ]);
        var deployment = new FakeDeployment(packages, packages.Packages[0]);
        var result = CreateProvisioner(packages, deployment, "1.0.0.0").EnsureProvisioned();
        var store = new FakeConfigurationStore();
        var configuration = new WindowsGamingHomeConfiguration(
            new FakeOsProbe(true),
            new WindowsSteamFsePackageProbe(packages),
            store);

        Assert.True(result.Succeeded);
        var snapshot = configuration.Capture();
        Assert.Equal(new FrontendSteamFseSnapshot(true, false, null), snapshot);
        Assert.Null(store.GamingHomeApp);
        Assert.False(store.StartupToGamingHome);
    }

    [Fact]
    public void Existing_on_preference_survives_package_version_update()
    {
        var family = "SteamInputAddonforClaw.FseHome_stable";
        var packages = new FakePackageEnumeration([
            Package(WindowsSteamFsePackageProbe.PackageIdentityName, family, "1.0.0.0")
        ]);
        var deployment = new FakeDeployment(packages, Package(WindowsSteamFsePackageProbe.PackageIdentityName, family, "1.1.0.0"));
        var store = new FakeConfigurationStore { GamingHomeApp = $"{family}!App", StartupToGamingHome = true };

        var result = CreateProvisioner(packages, deployment, "1.1.0.0").EnsureProvisioned();
        var configuration = new WindowsGamingHomeConfiguration(
            new FakeOsProbe(true),
            new WindowsSteamFsePackageProbe(packages),
            store);

        Assert.True(result.Succeeded);
        Assert.True(configuration.Capture().Enabled);
        Assert.Equal($"{family}!App", store.GamingHomeApp);
        Assert.True(store.StartupToGamingHome);
    }

    private static SteamFsePackageProvisioner CreateProvisioner(
        FakePackageEnumeration packages,
        FakeDeployment deployment,
        string version,
        bool supported = true)
    {
        var path = Path.Combine(Path.GetTempPath(), "SteamInputAddonforClaw-FseTests", Guid.NewGuid().ToString("N"), "SteamInputAddonforClaw.FseHome.msix");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, $"""
            <Package xmlns="http://schemas.microsoft.com/appx/manifest/foundation/windows10">
              <Identity Name="{WindowsSteamFsePackageProbe.PackageIdentityName}" Publisher="CN=SteamInputAddonforClaw" Version="{version}" ProcessorArchitecture="x64" />
              <Applications><Application Id="App" /></Applications>
            </Package>
            """);

        return new SteamFsePackageProvisioner(
            new FakeOsProbe(supported), packages, deployment, () => path);
    }

    private static SteamFsePackageInfo Package(string identity, string family, string version) =>
        new(identity, family, $"{family}_{version}_x64__full", Version.Parse(version));

    private sealed class FakeOsProbe(bool supported) : ISteamFseOsProbe
    {
        public SteamFseOsSupport Capture() => supported
            ? new(true, null)
            : new(false, "unsupported-test-os");
    }

    private sealed class FakePackageEnumeration(IReadOnlyList<SteamFsePackageInfo> packages) : ISteamFsePackageEnumeration
    {
        public List<SteamFsePackageInfo> Packages { get; } = [.. packages];
        public IReadOnlyList<SteamFsePackageInfo> FindCurrentUserPackages() => Packages;
        public void RemovePackage(string fullName) { }
    }

    private sealed class FakeDeployment(FakePackageEnumeration packages, SteamFsePackageInfo? installedPackage) : ISteamFsePackageDeployment
    {
        public int InstallCalls { get; private set; }
        public Exception? Failure { get; init; }

        public Task AddOrUpdateAsync(string packagePath, CancellationToken cancellationToken)
        {
            InstallCalls++;
            if (Failure is not null) return Task.FromException(Failure);
            if (installedPackage is not null)
                packages.Packages.Add(installedPackage);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeConfigurationStore : IGamingConfigurationStore
    {
        public string? GamingHomeApp { get; set; }
        public bool StartupToGamingHome { get; set; }
        public string? ReadGamingHomeApp() => GamingHomeApp;
        public bool ReadStartupToGamingHome() => StartupToGamingHome;
        public void WriteGamingHomeApp(string aumid) => GamingHomeApp = aumid;
        public void DeleteGamingHomeApp() => GamingHomeApp = null;
        public void WriteStartupToGamingHome(bool enabled) => StartupToGamingHome = enabled;
    }
}
