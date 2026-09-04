using System.Text.Json;
using SteamInputAddonforClaw.Contracts.Frontend;
using SteamInputAddonforClaw.Developer;
using SteamInputAddonforClaw.Frontend;
using SteamInputAddonforClaw.Install;
using SteamInputAddonforClaw.Settings;
using SteamInputAddonforClaw.Status;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

/// <summary>PR2.5: the frontend projects the mandatory launch-at-startup fact and the backend rejects
/// an OFF request while MSI Center M is Disabled.</summary>
[Collection("AppLog")]
public sealed class MandatoryLaunchAtStartupFrontendTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"SteamInputAddonforClaw.Tests.{Guid.NewGuid():N}");

    [Fact]
    public async Task Bootstrap_reports_the_mandatory_flag_from_the_center_m_state_predicate()
    {
        var mandatory = false;
        var control = CreateControl(() => mandatory);

        Assert.False((await control.GetBootstrapAsync()).Settings.LaunchAtWindowsStartupRequired);

        mandatory = true;
        Assert.True((await control.GetBootstrapAsync()).Settings.LaunchAtWindowsStartupRequired);
    }

    [Fact]
    public async Task Set_off_while_mandatory_comes_back_on_with_the_required_message()
    {
        var control = CreateControl(() => true);

        var result = await control.SetLaunchAtWindowsStartupAsync(false);

        Assert.True(result.Settings.LaunchAtWindowsStartup);
        Assert.True(result.Settings.LaunchAtWindowsStartupRequired);
        Assert.Equal("Required while MSI Center M is disabled.", result.RegistrationMessage);
    }

    [Fact]
    public async Task Set_off_while_not_mandatory_is_honored()
    {
        var control = CreateControl(() => false);
        var result = await control.SetLaunchAtWindowsStartupAsync(false);
        Assert.False(result.Settings.LaunchAtWindowsStartup);
        Assert.False(result.Settings.LaunchAtWindowsStartupRequired);
    }

    [Fact]
    public void Settings_snapshot_round_trips_the_required_flag_over_the_wire_codec()
    {
        var value = new FrontendSettingsSnapshot(true, FrontendLogLevel.Info, false, Contracts.Oem1.Oem1MappingSettings.Default)
        { LaunchAtWindowsStartupRequired = true };

        var restored = JsonSerializer.Deserialize<FrontendSettingsSnapshot>(JsonSerializer.Serialize(value));

        Assert.True(restored!.LaunchAtWindowsStartupRequired);
    }

    private InProcessAddonFrontendControl CreateControl(Func<bool> mandatory)
    {
        SteamInputAddonforClaw.Diagnostics.AppLog.DirectoryOverride = _dir;
        var store = new SettingsStore(Path.Combine(_dir, "settings.json"));
        var coordinator = new StartupSettingsCoordinator(new AppSettings(), store, new FakeStartupManager(), mandatory);
        return new InProcessAddonFrontendControl(coordinator, new ThrowingStatusProvider(), null, new DeveloperTestModeState(), "");
    }

    public void Dispose()
    {
        SteamInputAddonforClaw.Diagnostics.AppLog.DirectoryOverride = null;
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    private sealed class FakeStartupManager : IWindowsStartupManager
    {
        public StartupRegistrationResult Synchronize(bool enabled) =>
            enabled ? StartupRegistrationResult.Enabled() : StartupRegistrationResult.Disabled();
    }

    private sealed class ThrowingStatusProvider : ISystemStatusProvider
    {
        public Task<SystemStatusSnapshot> CaptureAsync(CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }
}
