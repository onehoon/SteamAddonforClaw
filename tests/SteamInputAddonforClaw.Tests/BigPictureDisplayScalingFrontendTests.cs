using SteamInputAddonforClaw.Contracts.Frontend;
using SteamInputAddonforClaw.Diagnostics;
using SteamInputAddonforClaw.Frontend;
using SteamInputAddonforClaw.Install;
using SteamInputAddonforClaw.Profiles;
using SteamInputAddonforClaw.Profiles.Display;
using SteamInputAddonforClaw.Settings;
using SteamInputAddonforClaw.Status;
using SteamInputAddonforClaw.Steam;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

[Collection("AppLog")]
public sealed class BigPictureDisplayScalingFrontendTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "SteamInputAddonforClaw.BigPictureDisplayScaling", Guid.NewGuid().ToString("N"));
    private static readonly DisplayModeSnapshot OriginalMode = new(1920, 1200, 120, 32);
    private static readonly DisplayScaleSnapshot OriginalScale = new("\\.\\DISPLAY1", "monitor-path", 2);

    public BigPictureDisplayScalingFrontendTests()
    {
        AppLog.DirectoryOverride = Path.Combine(_root, "logs");
    }

    [Fact]
    public async Task Toggle_during_BPM_persists_then_immediately_applies_and_restores_shared_display_override()
    {
        Directory.CreateDirectory(_root);
        var settingsPath = Path.Combine(_root, "settings.json");
        var settings = new StartupSettingsCoordinator(new AppSettings(), new SettingsStore(settingsPath), new NoOpStartupManager());
        var display = new FakeDisplay { Current = OriginalMode };
        var scaling = new FakeScaling { Current = OriginalScale };
        var bpmActive = true;
        var displayRuntime = new GameDisplayResolutionRuntime(new ProfileStore(Path.Combine(_root, "profiles.json")), new(), _root,
            display, scaling, () => bpmActive && settings.SteamBigPictureScalingTo100Percent);
        displayRuntime.SetActiveProfileResolver(static _ => null);
        var frontend = new InProcessAddonFrontendControl(settings, new ThrowingSystemStatusProvider(), null,
            displayResolutionRuntime: displayRuntime,
            capturePresentationSnapshot: () => new SteamPresentationSnapshot(0, bpmActive));
        Assert.False((await frontend.GetBootstrapAsync()).Settings.SteamBigPictureScalingTo100Percent);
        var invalidations = 0;
        frontend.StateInvalidated += (_, _) => invalidations++;

        var enabled = await frontend.SetSteamBigPictureScalingTo100PercentAsync(true);
        Assert.True(enabled.SteamBigPictureScalingTo100Percent);
        Assert.True(new SettingsStore(settingsPath).Load().SteamBigPictureScalingTo100Percent);
        Assert.True((await frontend.GetBootstrapAsync()).Settings.SteamBigPictureScalingTo100Percent);
        Assert.Equal(1, scaling.CaptureCalls);
        Assert.Equal(1, scaling.SetCalls);
        Assert.Equal(scaling.MinimumRelativeScale, scaling.Current.OriginalRelativeScale);

        var disabled = await frontend.SetSteamBigPictureScalingTo100PercentAsync(false);
        Assert.False(disabled.SteamBigPictureScalingTo100Percent);
        Assert.False(new SettingsStore(settingsPath).Load().SteamBigPictureScalingTo100Percent);
        Assert.False((await frontend.GetBootstrapAsync()).Settings.SteamBigPictureScalingTo100Percent);
        Assert.Equal(1, scaling.RestoreCalls);
        Assert.Equal(OriginalScale, scaling.Current);
        Assert.Equal(2, invalidations);
    }

    [Fact]
    public async Task Failed_settings_save_keeps_bootstrap_preference_off()
    {
        Directory.CreateDirectory(_root);
        var blockingFile = Path.Combine(_root, "settings-parent-file");
        File.WriteAllText(blockingFile, "not a directory");
        var settings = new StartupSettingsCoordinator(new AppSettings(),
            new SettingsStore(Path.Combine(blockingFile, "settings.json")), new NoOpStartupManager());
        var frontend = new InProcessAddonFrontendControl(settings, new ThrowingSystemStatusProvider(), null);

        await Assert.ThrowsAnyAsync<IOException>(() => frontend.SetSteamBigPictureScalingTo100PercentAsync(true));

        Assert.False(settings.SteamBigPictureScalingTo100Percent);
        Assert.False((await frontend.GetBootstrapAsync()).Settings.SteamBigPictureScalingTo100Percent);
    }

    [Fact]
    public async Task Display_write_failure_does_not_undo_saved_preference_or_block_other_settings()
    {
        Directory.CreateDirectory(_root);
        var settingsPath = Path.Combine(_root, "settings.json");
        var settings = new StartupSettingsCoordinator(new AppSettings(), new SettingsStore(settingsPath), new NoOpStartupManager());
        var displayRuntime = new GameDisplayResolutionRuntime(new ProfileStore(Path.Combine(_root, "profiles.json")), new(), _root,
            new FakeDisplay { Current = OriginalMode }, new FakeScaling { SetSucceeds = false }, static () => true);
        displayRuntime.SetActiveProfileResolver(static _ => null);
        var frontend = new InProcessAddonFrontendControl(settings, new ThrowingSystemStatusProvider(), null,
            displayResolutionRuntime: displayRuntime,
            capturePresentationSnapshot: static () => new SteamPresentationSnapshot(0, true));

        var result = await frontend.SetSteamBigPictureScalingTo100PercentAsync(true);
        await frontend.SetQuickSettingsCurrentPowerSourceOnlyAsync(true);

        Assert.True(result.SteamBigPictureScalingTo100Percent);
        Assert.True(settings.SteamBigPictureScalingTo100Percent);
        Assert.True(settings.QuickSettingsCurrentPowerSourceOnly);
    }

    public void Dispose()
    {
        AppLog.DrainForTests();
        AppLog.DirectoryOverride = null;
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
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

    private sealed class FakeDisplay : IDisplayResolutionService
    {
        public DisplayModeSnapshot Current;
        public bool TryCapture(out DisplayModeSnapshot snapshot) { snapshot = Current; return true; }
        public bool TryApply(DisplayModeSnapshot current, int width, int height) { Current = current with { Width = width, Height = height }; return true; }
        public bool TryRestore(DisplayModeSnapshot original) { Current = original; return true; }
    }

    private sealed class FakeScaling : IDisplayScalingService
    {
        public DisplayScaleSnapshot Current = OriginalScale;
        public bool SetSucceeds = true;
        public int MinimumRelativeScale = 0;
        public int CaptureCalls, SetCalls, RestoreCalls;
        public bool TryCapture(out DisplayScaleSnapshot snapshot) { CaptureCalls++; snapshot = Current; return true; }
        public bool TrySetTo100(DisplayScaleSnapshot original)
        {
            if (Current.OriginalRelativeScale == MinimumRelativeScale) return true;
            if (!SetSucceeds) return false;
            SetCalls++;
            Current = Current with { OriginalRelativeScale = MinimumRelativeScale };
            return true;
        }
        public bool TryRestore(DisplayScaleSnapshot original)
        {
            RestoreCalls++;
            Current = original;
            return true;
        }
    }
}
