using SteamInputAddonforClaw.Contracts.Frontend;
using SteamInputAddonforClaw.Developer;
using SteamInputAddonforClaw.Frontend;
using SteamInputAddonforClaw.HidHide;
using SteamInputAddonforClaw.Install;
using SteamInputAddonforClaw.Routing;
using SteamInputAddonforClaw.Settings;
using SteamInputAddonforClaw.Status;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

/// <summary>
/// Covers the frontend boundary for the single Steam Input routing master preference: the bootstrap
/// must report it, the setter must persist it and return the authoritative stored value, and the
/// obsolete Big-Picture-only contract must be gone from the production interface entirely.
/// </summary>
/// <remarks>
/// CI fix: this test touches the shared static <see cref="SteamInputAddonforClaw.Diagnostics.AppLog.DirectoryOverride"/> (via
/// <see cref="CreateControl"/>), so it must join the "AppLog" collection like every other test class
/// that touches that global -- otherwise xUnit can run it in parallel against, e.g.,
/// <c>AppLogTests</c>, letting a stray background-writer log write land in the wrong directory at the
/// wrong moment and intermittently fail an unrelated test's directory cleanup.
/// </remarks>
[Collection("AppLog")]
public sealed class FrontendSteamInputRoutingSettingTests : IDisposable
{
    private const string OfficialHidHideClient = "C:\\Program Files\\Nefarius Software Solutions\\HidHide\\x64\\HidHideClient.exe";
    private readonly string _testDirectory = Path.Combine(Path.GetTempPath(), $"SteamInputAddonforClaw.Tests.{Guid.NewGuid():N}");

    [Fact]
    public async Task Bootstrap_reports_the_stored_steam_input_routing_setting()
    {
        var control = CreateControl(new AppSettings(SteamInputRoutingEnabled: false), out _);

        var bootstrap = await control.GetBootstrapAsync();

        Assert.False(bootstrap.Settings.SteamInputRoutingEnabled);
    }

    [Fact]
    public async Task Setter_persists_the_setting_and_returns_the_authoritative_value()
    {
        var control = CreateControl(new AppSettings(SteamInputRoutingEnabled: true), out var store);

        var result = await control.SetSteamInputRoutingEnabledAsync(false);

        Assert.True(result.Succeeded);
        Assert.False(result.Settings.SteamInputRoutingEnabled);
        Assert.False(store.Load().SteamInputRoutingEnabled);
        Assert.False((await control.GetBootstrapAsync()).Settings.SteamInputRoutingEnabled);
    }

    [Fact]
    public async Task Enabling_with_foreign_hidhide_configuration_does_not_mutate_settings()
    {
        var control = CreateControl(new AppSettings(SteamInputRoutingEnabled: false), out var store,
            new FakeHidHide(new(HidHideInspectionStatus.Available, new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "C:\\other.exe" })));

        var result = await control.SetSteamInputRoutingEnabledAsync(true);

        Assert.Equal(FrontendSteamInputRoutingMutationOutcome.HidHideConflict, result.Outcome);
        Assert.False(result.Settings.SteamInputRoutingEnabled);
        Assert.False(store.Load().SteamInputRoutingEnabled);
    }

    [Fact]
    public async Task Enabling_with_official_hidhide_client_configuration_is_allowed()
    {
        var control = CreateControl(new AppSettings(SteamInputRoutingEnabled: false), out var store,
            new FakeHidHide(new(HidHideInspectionStatus.Available, new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "C:\\addon.exe", OfficialHidHideClient })),
            [OfficialHidHideClient], () => "C:\\addon.exe");

        var result = await control.SetSteamInputRoutingEnabledAsync(true);

        Assert.True(result.Succeeded, result.Outcome.ToString());
        Assert.True(result.Settings.SteamInputRoutingEnabled);
        Assert.True(store.Load().SteamInputRoutingEnabled);
    }

    [Fact]
    public async Task Disabling_does_not_inspect_hidhide()
    {
        var hid = new FakeHidHide(null) { ThrowOnInspect = true };
        var control = CreateControl(new AppSettings(SteamInputRoutingEnabled: true), out _, hid);

        var result = await control.SetSteamInputRoutingEnabledAsync(false);

        Assert.True(result.Succeeded);
        Assert.False(result.Settings.SteamInputRoutingEnabled);
    }

    [Fact]
    public void Production_contract_has_no_big_picture_only_routing_member()
    {
        var names = typeof(IAddonFrontendControl).GetMembers().Select(member => member.Name)
            .Concat(typeof(FrontendSettingsSnapshot).GetMembers().Select(member => member.Name))
            .ToList();

        Assert.DoesNotContain(names, name => name.Contains("RouteInSteamBigPicture", StringComparison.Ordinal));
        Assert.Contains(names, name => name == nameof(IAddonFrontendControl.SetSteamInputRoutingEnabledAsync));
        Assert.Contains(names, name => name == nameof(FrontendSettingsSnapshot.SteamInputRoutingEnabled));
    }

    private InProcessAddonFrontendControl CreateControl(AppSettings settings, out SettingsStore store, IHidHideClient? hidHide = null, IReadOnlyCollection<string>? trustedHidHideApplicationPaths = null, Func<string?>? processPath = null)
    {
        // The bootstrap snapshot reports the log directory, which otherwise resolves through the
        // Velopack locator that only exists in an installed app.
        SteamInputAddonforClaw.Diagnostics.AppLog.DirectoryOverride = _testDirectory;
        store = new SettingsStore(Path.Combine(_testDirectory, "settings.json"));
        var coordinator = new StartupSettingsCoordinator(settings, store, new FakeStartupManager());
        return new InProcessAddonFrontendControl(
            coordinator,
            new ThrowingSystemStatusProvider(),
            null,
            new DeveloperTestModeState(),
            "",
            captureRoutingStatus: () => new(true, RoutingOperationalState.Passive, false, false),
            processPath: processPath, hidHide: hidHide, trustedHidHideApplicationPaths: trustedHidHideApplicationPaths);
    }

    public void Dispose()
    {
        SteamInputAddonforClaw.Diagnostics.AppLog.DirectoryOverride = null;
        if (Directory.Exists(_testDirectory)) Directory.Delete(_testDirectory, recursive: true);
    }

    private sealed class FakeStartupManager : IWindowsStartupManager
    {
        public StartupRegistrationResult Synchronize(bool enabled) => StartupRegistrationResult.Enabled();
    }

    private sealed class ThrowingSystemStatusProvider : ISystemStatusProvider
    {
        public Task<SystemStatusSnapshot> CaptureAsync(CancellationToken cancellationToken = default)
            => throw new NotSupportedException("Status capture is not part of these tests.");
    }

    private sealed class FakeHidHide(HidHideInspection? inspection) : IHidHideClient
    {
        public bool ThrowOnInspect { get; init; }
        public HidHideInspection Inspect() => ThrowOnInspect ? throw new InvalidOperationException() : inspection ?? new(HidHideInspectionStatus.NotInstalled, new HashSet<string>());
        public bool AddApplication(string executablePath) => true;
        public bool RemoveApplication(string executablePath) => true;
        public bool AddHiddenDevice(string deviceEntry) => true;
        public bool RemoveHiddenDevice(string deviceEntry) => true;
    }
}
