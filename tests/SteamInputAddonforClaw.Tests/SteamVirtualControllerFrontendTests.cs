using SteamInputAddonforClaw.Contracts.Frontend;
using SteamInputAddonforClaw.Frontend;
using SteamInputAddonforClaw.Install;
using SteamInputAddonforClaw.Settings;
using SteamInputAddonforClaw.Status;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

[Collection("AppLog")]
public sealed class SteamVirtualControllerFrontendTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"SteamInputAddonforClaw.SteamController.{Guid.NewGuid():N}");

    [Fact]
    public async Task Preference_rpc_returns_persisted_choice_and_reconciles_only_after_a_changed_save()
    {
        SteamInputAddonforClaw.Diagnostics.AppLog.DirectoryOverride = _directory;
        var path = Path.Combine(_directory, "settings.json");
        var coordinator = new StartupSettingsCoordinator(new AppSettings(), new SettingsStore(path), new NoOpStartupManager());
        var reconciles = new List<string>();
        var control = new InProcessAddonFrontendControl(coordinator, new ThrowingSystemStatusProvider(), null,
            requestControllerPresentationReconcile: trigger => reconciles.Add(trigger));
        var invalidations = 0;
        control.StateInvalidated += (_, _) => invalidations++;

        var changed = await control.SetSteamVirtualControllerAsync(SteamVirtualControllerPreference.DualSenseEdge);
        var same = await control.SetSteamVirtualControllerAsync(SteamVirtualControllerPreference.DualSenseEdge);

        Assert.Equal(SteamVirtualControllerPreference.DualSenseEdge, changed.SteamVirtualController);
        Assert.Equal(SteamVirtualControllerPreference.DualSenseEdge, same.SteamVirtualController);
        Assert.Equal(SteamVirtualControllerPreference.DualSenseEdge, new SettingsStore(path).Load().SteamVirtualController);
        Assert.Equal(["SteamVirtualControllerPreferenceChanged"], reconciles);
        Assert.Equal(1, invalidations);
    }

    [Fact]
    public async Task Invalid_choice_returns_authoritative_saved_value_without_reconcile()
    {
        SteamInputAddonforClaw.Diagnostics.AppLog.DirectoryOverride = _directory;
        var coordinator = new StartupSettingsCoordinator(new AppSettings(),
            new SettingsStore(Path.Combine(_directory, "settings.json")), new NoOpStartupManager());
        var reconcileCount = 0;
        var control = new InProcessAddonFrontendControl(coordinator, new ThrowingSystemStatusProvider(), null,
            requestControllerPresentationReconcile: _ => reconcileCount++);

        var result = await control.SetSteamVirtualControllerAsync((SteamVirtualControllerPreference)99);

        Assert.Equal(SteamVirtualControllerPreference.SteamDeck, result.SteamVirtualController);
        Assert.Equal(SteamVirtualControllerPreference.SteamDeck, coordinator.SteamVirtualController);
        Assert.Equal(0, reconcileCount);
    }

    [Fact]
    public async Task Failed_persistence_does_not_publish_or_reconcile_preference()
    {
        SteamInputAddonforClaw.Diagnostics.AppLog.DirectoryOverride = _directory;
        Directory.CreateDirectory(_directory);
        var blocker = Path.Combine(_directory, "not-a-directory");
        File.WriteAllText(blocker, "blocked");
        var coordinator = new StartupSettingsCoordinator(new AppSettings(),
            new SettingsStore(Path.Combine(blocker, "settings.json")), new NoOpStartupManager());
        var reconcileCount = 0;
        var control = new InProcessAddonFrontendControl(coordinator, new ThrowingSystemStatusProvider(), null,
            requestControllerPresentationReconcile: _ => reconcileCount++);

        await Assert.ThrowsAnyAsync<IOException>(() =>
            control.SetSteamVirtualControllerAsync(SteamVirtualControllerPreference.DualSenseEdge));

        Assert.Equal(SteamVirtualControllerPreference.SteamDeck, coordinator.SteamVirtualController);
        Assert.Equal(0, reconcileCount);
    }

    public void Dispose()
    {
        SteamInputAddonforClaw.Diagnostics.AppLog.DrainForTests();
        SteamInputAddonforClaw.Diagnostics.AppLog.DirectoryOverride = null;
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }

    private sealed class NoOpStartupManager : IWindowsStartupManager
    {
        public StartupRegistrationResult Synchronize(bool enabled) => StartupRegistrationResult.Enabled();
    }

    private sealed class ThrowingSystemStatusProvider : ISystemStatusProvider
    {
        public Task<SystemStatusSnapshot> CaptureAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
