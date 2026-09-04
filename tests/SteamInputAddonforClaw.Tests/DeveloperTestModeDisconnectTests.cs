using SteamInputAddonforClaw.Developer;
using SteamInputAddonforClaw.Frontend;
using SteamInputAddonforClaw.Install;
using SteamInputAddonforClaw.Settings;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

/// <summary>Full1902 Cleanup I: the Developer Test toggle is parked as disconnected UI-only state.
/// The RPC still works against a standalone <see cref="DeveloperTestModeState"/> (no live runtime),
/// and no production controller/presentation/Steam owner consumes it.</summary>
[Collection("AppLog")]
public sealed class DeveloperTestModeDisconnectTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ClawDevToggleTests", Guid.NewGuid().ToString("N"));

    private InProcessAddonFrontendControl CreateControl(DeveloperTestModeState developer)
    {
        Directory.CreateDirectory(_dir);
        SteamInputAddonforClaw.Diagnostics.AppLog.DirectoryOverride = _dir;
        var store = new SettingsStore(Path.Combine(_dir, "settings.json"));
        var coordinator = new StartupSettingsCoordinator(new AppSettings(), store, new FakeStartupManager());
        return new InProcessAddonFrontendControl(coordinator, new ThrowingSystemStatusProvider(), null, developer, "");
    }

    [Fact]
    public async Task SetDeveloperTestModeAsync_round_trips_against_a_standalone_state_without_a_runtime()
    {
        var developer = new DeveloperTestModeState();
        var control = CreateControl(developer);

        Assert.True((await control.SetDeveloperTestModeAsync(true)).TestModeEnabled);
        Assert.True(developer.IsEnabled);
        Assert.True((await control.GetBootstrapAsync()).Developer.TestModeEnabled);

        Assert.False((await control.SetDeveloperTestModeAsync(false)).TestModeEnabled);
        Assert.False(developer.IsEnabled);
    }

    [Fact]
    public void No_production_controller_or_presentation_owner_subscribes_to_developer_state()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "SteamInputAddonforClaw.slnx"))) dir = dir.Parent;
        var root = dir!.FullName;

        foreach (var relative in new[]
        {
            "src/SteamInputAddonforClaw/Steam/SteamSessionRuntime.cs",
            "src/SteamInputAddonforClaw/Runtime/AddonRuntimeHost.cs",
            "src/SteamInputAddonforClaw/Runtime/AddonRuntimeComposition.cs",
        })
        {
            var source = File.ReadAllText(Path.Combine(root, relative));
            Assert.DoesNotContain("DeveloperTestModeState", source, StringComparison.Ordinal);
            Assert.DoesNotContain("EffectiveSteamSessionSource", source, StringComparison.Ordinal);
        }
    }

    private sealed class FakeStartupManager : IWindowsStartupManager
    {
        public StartupRegistrationResult Synchronize(bool enabled) => StartupRegistrationResult.Enabled();
    }

    private sealed class ThrowingSystemStatusProvider : SteamInputAddonforClaw.Status.ISystemStatusProvider
    {
        public Task<SteamInputAddonforClaw.Status.SystemStatusSnapshot> CaptureAsync(CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("status not needed for this test");
    }

    public void Dispose()
    {
        SteamInputAddonforClaw.Diagnostics.AppLog.DirectoryOverride = null;
        try { if (Directory.Exists(_dir)) Directory.Delete(_dir, true); } catch (IOException) { }
    }
}
