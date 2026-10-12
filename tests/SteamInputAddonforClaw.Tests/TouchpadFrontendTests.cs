using SteamInputAddonforClaw.Contracts.Frontend;
using SteamInputAddonforClaw.Frontend;
using SteamInputAddonforClaw.Install;
using SteamInputAddonforClaw.Settings;
using SteamInputAddonforClaw.Status;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

[Collection("AppLog")]
public sealed class TouchpadFrontendTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"SteamInputAddonforClaw.TouchpadFrontend.{Guid.NewGuid():N}");

    [Fact]
    public async Task TogglePersistsBeforeTouchCaptureReconcileWithoutRequestingPresentationMutation()
    {
        SteamInputAddonforClaw.Diagnostics.AppLog.DirectoryOverride = _directory;
        var path = Path.Combine(_directory, "settings.json");
        var coordinator = new StartupSettingsCoordinator(new AppSettings(), new SettingsStore(path), new NoOpStartupManager());
        var captureReconciles = 0;
        var presentationReconciles = 0;
        var control = new InProcessAddonFrontendControl(coordinator, new ThrowingSystemStatusProvider(), null,
            requestControllerPresentationReconcile: _ => presentationReconciles++,
            reconcileExperimentalVirtualTouchpad: _ =>
            {
                captureReconciles++;
                Assert.Equal(captureReconciles == 1,
                    new SettingsStore(path).Load().ExperimentalVirtualTouchpadEnabled);
                return Task.FromResult(true);
            });

        var enabled = await control.SetExperimentalVirtualTouchpadEnabledAsync(true);
        var disabled = await control.SetExperimentalVirtualTouchpadEnabledAsync(false);

        Assert.True(enabled.ExperimentalVirtualTouchpadEnabled);
        Assert.False(disabled.ExperimentalVirtualTouchpadEnabled);
        Assert.Equal(2, captureReconciles);
        Assert.Equal(0, presentationReconciles);
        Assert.False(new SettingsStore(path).Load().ExperimentalVirtualTouchpadEnabled);
    }

    [Fact]
    public async Task FailedPreferencePersistenceDoesNotPublishOrReconcile()
    {
        SteamInputAddonforClaw.Diagnostics.AppLog.DirectoryOverride = _directory;
        Directory.CreateDirectory(_directory);
        var blocker = Path.Combine(_directory, "not-a-directory");
        File.WriteAllText(blocker, "blocked");
        var coordinator = new StartupSettingsCoordinator(new AppSettings(),
            new SettingsStore(Path.Combine(blocker, "settings.json")), new NoOpStartupManager());
        var reconcileCount = 0;
        var control = new InProcessAddonFrontendControl(coordinator, new ThrowingSystemStatusProvider(), null,
            reconcileExperimentalVirtualTouchpad: _ =>
            {
                reconcileCount++;
                return Task.FromResult(true);
            });

        await Assert.ThrowsAnyAsync<IOException>(() => control.SetExperimentalVirtualTouchpadEnabledAsync(true));

        Assert.False(coordinator.ExperimentalVirtualTouchpadEnabled);
        Assert.Equal(0, reconcileCount);
    }

    [Fact]
    public async Task CaptureUnavailableDoesNotFalsifyThePersistedPreference()
    {
        SteamInputAddonforClaw.Diagnostics.AppLog.DirectoryOverride = _directory;
        var coordinator = new StartupSettingsCoordinator(new AppSettings(),
            new SettingsStore(Path.Combine(_directory, "settings.json")), new NoOpStartupManager());
        var control = new InProcessAddonFrontendControl(coordinator, new ThrowingSystemStatusProvider(), null,
            reconcileExperimentalVirtualTouchpad: _ => Task.FromResult(false));

        var result = await control.SetExperimentalVirtualTouchpadEnabledAsync(true);

        Assert.True(result.ExperimentalVirtualTouchpadEnabled);
        Assert.True(coordinator.ExperimentalVirtualTouchpadEnabled);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
        SteamInputAddonforClaw.Diagnostics.AppLog.DirectoryOverride = null;
    }

    private sealed class NoOpStartupManager : IWindowsStartupManager
    {
        public StartupRegistrationResult Synchronize(bool enabled) => StartupRegistrationResult.Enabled();
    }

    private sealed class ThrowingSystemStatusProvider : ISystemStatusProvider
    {
        public Task<SystemStatusSnapshot> CaptureAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("System status is not used by this preference test.");
    }
}
