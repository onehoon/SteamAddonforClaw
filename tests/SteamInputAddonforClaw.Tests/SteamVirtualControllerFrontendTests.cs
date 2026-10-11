using SteamInputAddonforClaw.Contracts.Frontend;
using SteamInputAddonforClaw.Frontend;
using SteamInputAddonforClaw.Install;
using SteamInputAddonforClaw.Settings;
using SteamInputAddonforClaw.Status;
using SteamInputAddonforClaw.Steam;
using SteamInputAddonforClaw.Profiles;
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

    [Theory]
    [InlineData(12345u, false)]
    [InlineData(0u, true)]
    public async Task Controller_preference_mutations_are_rejected_while_a_game_or_big_picture_is_active(uint appId, bool bigPictureActive)
    {
        SteamInputAddonforClaw.Diagnostics.AppLog.DirectoryOverride = _directory;
        Directory.CreateDirectory(_directory);
        var settingsPath = Path.Combine(_directory, "guard-settings.json");
        var profilePath = Path.Combine(_directory, "guard-profiles.json");
        var settings = new StartupSettingsCoordinator(new AppSettings(), new SettingsStore(settingsPath), new NoOpStartupManager());
        var profileStore = new ProfileStore(profilePath);
        profileStore.Save(new ProfileDocument
        {
            Games = new Dictionary<string, GameProfile>(StringComparer.Ordinal)
            {
                ["12345"] = new() { Enabled = false, Favorite = false, DisplayName = "Test Game" }
            }
        });
        var gameMutations = new GameProfileMutations(profileStore, new ProfileMutationGate());
        var profileBefore = File.ReadAllBytes(profilePath);
        var reconcileCount = 0;
        var invalidationCount = 0;
        var control = new InProcessAddonFrontendControl(settings, new ThrowingSystemStatusProvider(), null,
            gameProfileMutations: gameMutations,
            requestControllerPresentationReconcile: _ => reconcileCount++,
            capturePresentationSnapshot: () => new SteamPresentationSnapshot(appId, bigPictureActive));
        control.StateInvalidated += (_, _) => invalidationCount++;

        var globalResult = await control.SetSteamVirtualControllerAsync(SteamVirtualControllerPreference.DualSenseEdge);
        var gameResult = await control.SetGameProfileSteamVirtualControllerAsync(12345, SteamVirtualControllerPreference.DualSenseEdge);
        Assert.Equal(profileBefore, File.ReadAllBytes(profilePath));
        var unrelatedResult = await control.SetGameProfileFavoriteAsync(12345, true, "Test Game");

        Assert.Equal(SteamVirtualControllerPreference.SteamDeck, globalResult.SteamVirtualController);
        Assert.Equal(SteamVirtualControllerPreference.SteamDeck, settings.SteamVirtualController);
        Assert.Equal(FrontendGameProfileMutationOutcome.Unavailable, gameResult.Outcome);
        Assert.Null(gameResult.Snapshot.SteamVirtualController);
        Assert.True(unrelatedResult.Succeeded, $"{unrelatedResult.Outcome}: {unrelatedResult.FailureMessage}");
        Assert.Equal(0, reconcileCount);
        Assert.Equal(1, invalidationCount);
        Assert.True(profileStore.Load().Document.Games["12345"].Favorite);
        Assert.False(File.Exists(settingsPath) && new SettingsStore(settingsPath).Load().SteamVirtualController == SteamVirtualControllerPreference.DualSenseEdge);
    }

    [Fact]
    public async Task Per_game_controller_override_can_be_saved_while_steam_is_idle()
    {
        SteamInputAddonforClaw.Diagnostics.AppLog.DirectoryOverride = _directory;
        var profilePath = Path.Combine(_directory, "idle-profiles.json");
        var store = new ProfileStore(profilePath);
        var gameProfileMutations = new GameProfileMutations(store, new ProfileMutationGate());
        Assert.Equal(GameProfileMutations.MutationOutcome.Succeeded,
            gameProfileMutations.SetEnabled(12345, true, "Idle Game"));
        var control = new InProcessAddonFrontendControl(
            new StartupSettingsCoordinator(new AppSettings(), new SettingsStore(Path.Combine(_directory, "idle-settings.json")), new NoOpStartupManager()),
            new ThrowingSystemStatusProvider(), null,
            gameProfileMutations: gameProfileMutations,
            capturePresentationSnapshot: () => new SteamPresentationSnapshot(0, false));

        var result = await control.SetGameProfileSteamVirtualControllerAsync(12345, SteamVirtualControllerPreference.DualSenseEdge);

        Assert.True(result.Succeeded);
        Assert.Equal(SteamVirtualControllerPreference.DualSenseEdge, result.Snapshot.SteamVirtualController);
        Assert.True(result.Snapshot.Enabled);
        Assert.Equal(SteamVirtualControllerPreference.DualSenseEdge, store.Load().Document.Games["12345"].SteamVirtualController);
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
