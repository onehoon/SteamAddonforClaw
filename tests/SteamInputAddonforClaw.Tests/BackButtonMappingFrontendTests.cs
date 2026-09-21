using SteamInputAddonforClaw.Contracts.BackButtons;
using SteamInputAddonforClaw.Diagnostics;
using SteamInputAddonforClaw.Contracts.Frontend;
using SteamInputAddonforClaw.Developer;
using SteamInputAddonforClaw.Frontend;
using SteamInputAddonforClaw.Install;
using SteamInputAddonforClaw.Settings;
using SteamInputAddonforClaw.Status;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

[Collection("AppLog")]
public sealed class BackButtonMappingFrontendTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"SteamInputAddonforClaw.BackButtonFrontend.{Guid.NewGuid():N}");

    [Fact]
    public async Task In_process_rpc_persists_and_returns_the_shared_mapping()
    {
        AppLog.DirectoryOverride = _directory;
        var store = new SettingsStore(Path.Combine(_directory, "settings.json"));
        var coordinator = new StartupSettingsCoordinator(new AppSettings(), store, new NoOpStartupManager());
        var control = new InProcessAddonFrontendControl(
            coordinator, new ThrowingSystemStatusProvider(), null, new DeveloperTestModeState(),
            frontButtonMappingAvailable: true);
        var mapping = new BackButtonMappingSettings(Xbox360BackButtonTarget.A, Xbox360BackButtonTarget.RightBumper);

        var snapshot = await control.SetBackButtonMappingAsync(mapping);
        var bootstrap = await control.GetBootstrapAsync();

        Assert.Equal(mapping, snapshot.BackButtonMapping);
        Assert.Equal(mapping, bootstrap.Settings.BackButtonMapping);
        Assert.True(bootstrap.BackButtonMappingAvailable);
    }

    [Fact]
    public async Task Invalid_in_process_rpc_returns_unchanged_mapping()
    {
        AppLog.DirectoryOverride = _directory;
        var store = new SettingsStore(Path.Combine(_directory, "settings.json"));
        var current = new BackButtonMappingSettings(Xbox360BackButtonTarget.B, Xbox360BackButtonTarget.B);
        var coordinator = new StartupSettingsCoordinator(new AppSettings { BackButtonMapping = current }, store, new NoOpStartupManager());
        var control = new InProcessAddonFrontendControl(
            coordinator, new ThrowingSystemStatusProvider(), null, new DeveloperTestModeState(),
            frontButtonMappingAvailable: false);

        var snapshot = await control.SetBackButtonMappingAsync(
            new BackButtonMappingSettings((Xbox360BackButtonTarget)999, Xbox360BackButtonTarget.Disabled));

        Assert.Equal(current, snapshot.BackButtonMapping);
        Assert.Equal(current, coordinator.BackButtonMapping);
        Assert.False((await control.GetBootstrapAsync()).BackButtonMappingAvailable);
    }

    public void Dispose()
    {
        AppLog.DrainForTests();
        AppLog.DirectoryOverride = null;
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }

    private sealed class NoOpStartupManager : IWindowsStartupManager
    {
        public StartupRegistrationResult Synchronize(bool enabled) => StartupRegistrationResult.Enabled();
    }

    private sealed class ThrowingSystemStatusProvider : ISystemStatusProvider
    {
        public Task<SystemStatusSnapshot> CaptureAsync(CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }
}
