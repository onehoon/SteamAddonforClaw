using SteamInputAddonforClaw.Contracts.Frontend;
using SteamInputAddonforClaw.Contracts.DeviceProfiles;
using SteamInputAddonforClaw.Frontend;
using SteamInputAddonforClaw.Install;
using SteamInputAddonforClaw.Profiles;
using SteamInputAddonforClaw.Profiles.Performance;
using SteamInputAddonforClaw.Settings;
using SteamInputAddonforClaw.Status;
using SteamInputAddonforClaw.Xbox;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class GameCatalogFavoriteStateTests
{
    [Fact]
    public async Task First_run_projects_a_known_empty_favorite_set_for_both_catalogs()
    {
        var root = CreateTempDirectory();
        try
        {
            var store = new ProfileStore(Path.Combine(root, "profiles.json"));
            var gate = new ProfileMutationGate();
            var control = CreateControl(
                store,
                new GameProfileMutations(store, gate, TdpLimits()),
                new XboxGameProfileMutations(store, gate, TdpLimits()));

            var steam = await control.ScanProfileGamesAsync();
            var xbox = await control.ScanXboxGamesAsync();

            Assert.All(steam, game =>
            {
                Assert.False(game.Favorite);
                Assert.True(game.FavoriteStateKnown);
            });
            Assert.Equal(2, steam.Count);
            Assert.Equal(new[] { false, false }, xbox.Games.Select(game => game.Favorite));
            Assert.All(xbox.Games, game => Assert.True(game.FavoriteStateKnown));
            var saved = store.Load().Document;
            Assert.All(saved.Games.Values, profile => Assert.True(profile.Enabled));
            Assert.All(saved.XboxGames.Values, profile => Assert.True(profile.Enabled));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Catalog_enrollment_copies_global_values_once_and_preserves_the_snapshot_on_refresh()
    {
        var root = CreateTempDirectory();
        try
        {
            var store = new ProfileStore(Path.Combine(root, "profiles.json"));
            store.Save(new ProfileDocument
            {
                Device = new DeviceSettings
                {
                    Performance = new DevicePerformanceSettings
                    {
                        CpuBoost = new DeviceCpuBoostSettings { Enabled = false, Ac = CpuBoostMode.Disabled, Dc = CpuBoostMode.Aggressive },
                        Tdp = new DeviceTdpSettings { Enabled = false, Ac = new() { Pl1Watts = 24, Pl2Watts = 30 }, Dc = new() { Pl1Watts = 15, Pl2Watts = 20 } },
                        PowerMode = new DevicePowerModeSettings { Ac = WindowsPowerMode.Balanced, Dc = WindowsPowerMode.BestPowerEfficiency }
                    }
                }
            });
            var gate = new ProfileMutationGate();
            var steam = new GameProfileMutations(store, gate, TdpLimits());
            var xbox = new XboxGameProfileMutations(store, gate, TdpLimits());
            var control = CreateControl(store, steam, xbox);

            await control.ScanProfileGamesAsync();
            await control.ScanXboxGamesAsync();

            var first = store.Load().Document;
            var firstSteam = first.Games["101"];
            var firstXbox = first.XboxGames["store:xbox-one"];
            Assert.Equal(CpuBoostMode.Disabled, firstSteam.Performance.CpuBoost!.Ac);
            Assert.Equal(CpuBoostMode.Aggressive, firstSteam.Performance.CpuBoost.Dc);
            Assert.Equal(new TdpPowerPair { Pl1Watts = 24, Pl2Watts = 30 }, firstSteam.Performance.Tdp!.Ac);
            Assert.Equal(new TdpPowerPair { Pl1Watts = 15, Pl2Watts = 20 }, firstXbox.Performance.Tdp!.Dc);
            Assert.Equal(WindowsPowerMode.Balanced, firstXbox.Performance.PowerMode!.Ac);
            Assert.Null(firstSteam.Performance.FpsLimit);
            Assert.Null(firstSteam.Performance.GpuMinimumClock);
            Assert.Null(firstSteam.Display.Resolution);
            Assert.Null(firstSteam.SteamVirtualController);
            Assert.Null(firstXbox.Controller.BackButtonMapping);

            store.Save(first with
            {
                Device = first.Device with
                {
                    Performance = first.Device.Performance with
                    {
                        CpuBoost = new() { Ac = CpuBoostMode.EfficientEnabled, Dc = CpuBoostMode.Enabled },
                        Tdp = new() { Enabled = false, Ac = new() { Pl1Watts = 20, Pl2Watts = 22 }, Dc = new() { Pl1Watts = 20, Pl2Watts = 22 } },
                        PowerMode = new() { Ac = WindowsPowerMode.BestPerformance, Dc = WindowsPowerMode.BestPerformance }
                    }
                }
            });

            await control.ScanProfileGamesAsync();
            await control.ScanXboxGamesAsync();

            var refreshed = store.Load().Document;
            Assert.Equal(firstSteam.Performance.CpuBoost, refreshed.Games["101"].Performance.CpuBoost);
            Assert.Equal(firstSteam.Performance.Tdp, refreshed.Games["101"].Performance.Tdp);
            Assert.Equal(firstSteam.Performance.PowerMode, refreshed.Games["101"].Performance.PowerMode);
            Assert.Equal(firstXbox.Performance.CpuBoost, refreshed.XboxGames["store:xbox-one"].Performance.CpuBoost);
            Assert.Equal(firstXbox.Performance.Tdp, refreshed.XboxGames["store:xbox-one"].Performance.Tdp);
            Assert.Equal(firstXbox.Performance.PowerMode, refreshed.XboxGames["store:xbox-one"].Performance.PowerMode);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Empty_steam_and_failed_xbox_catalogs_do_not_create_profiles()
    {
        var root = CreateTempDirectory();
        try
        {
            var path = Path.Combine(root, "profiles.json");
            var store = new ProfileStore(path);
            var gate = new ProfileMutationGate();
            var control = CreateControl(
                store,
                new GameProfileMutations(store, gate, TdpLimits()),
                new XboxGameProfileMutations(store, gate, TdpLimits()),
                steamGames: [],
                xboxResult: new XboxInstalledGameCatalogResult(XboxInstalledGameCatalogOutcome.Failed, [], "scan failed"));

            Assert.Empty(await control.ScanProfileGamesAsync());
            Assert.Equal(FrontendXboxGameCatalogOutcome.Failed, (await control.ScanXboxGamesAsync()).Outcome);
            Assert.False(File.Exists(path));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Hidden_native_steam_apps_are_not_enrolled_but_matching_non_steam_shortcuts_are()
    {
        var root = CreateTempDirectory();
        try
        {
            var store = new ProfileStore(Path.Combine(root, "profiles.json"));
            var gate = new ProfileMutationGate();
            var control = CreateControl(
                store,
                new GameProfileMutations(store, gate, TdpLimits()),
                new XboxGameProfileMutations(store, gate, TdpLimits()),
                steamGames:
                [
                    new(480, "Spacewar", ProfileGameSource.Steam),
                    new(228980, "Redistributables", ProfileGameSource.Steam),
                    new(993090, "Lossless Scaling", ProfileGameSource.Steam),
                    new(993090, "User shortcut", ProfileGameSource.NonSteam),
                    new(570, "Visible game", ProfileGameSource.Steam)
                ]);

            await control.ScanProfileGamesAsync();

            var profiles = store.Load().Document.Games;
            Assert.DoesNotContain("480", profiles.Keys);
            Assert.DoesNotContain("228980", profiles.Keys);
            Assert.True(profiles["993090"].Enabled);
            Assert.True(profiles["570"].Enabled);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Loaded_favorites_are_known_and_projected_for_both_catalogs()
    {
        var root = CreateTempDirectory();
        try
        {
            var store = new ProfileStore(Path.Combine(root, "profiles.json"));
            var gate = new ProfileMutationGate();
            var steamMutations = new GameProfileMutations(store, gate, TdpLimits());
            var xboxMutations = new XboxGameProfileMutations(store, gate, TdpLimits());
            Assert.Equal(GameProfileMutations.MutationOutcome.Succeeded, steamMutations.SetFavorite(101, true, "Steam favorite"));
            Assert.Equal(XboxGameProfileMutations.MutationOutcome.Succeeded, xboxMutations.SetFavorite("store:xbox-one", true, "XBOX favorite"));

            var control = CreateControl(store, steamMutations, xboxMutations);
            var steam = await control.ScanProfileGamesAsync();
            var xbox = await control.ScanXboxGamesAsync();

            Assert.Equal(new[] { true, false }, steam.Select(game => game.Favorite));
            Assert.All(steam, game => Assert.True(game.FavoriteStateKnown));
            Assert.Equal(new[] { true, false }, xbox.Games.Select(game => game.Favorite));
            Assert.All(xbox.Games, game => Assert.True(game.FavoriteStateKnown));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData("{not valid json")]
    [InlineData("{\"schemaVersion\":999}")]
    public async Task Unsafe_profile_load_projects_unknown_without_overwriting_the_document(string original)
    {
        var root = CreateTempDirectory();
        var path = Path.Combine(root, "profiles.json");
        File.WriteAllText(path, original);
        try
        {
            var store = new ProfileStore(path);
            var gate = new ProfileMutationGate();
            var steamMutations = new GameProfileMutations(store, gate, TdpLimits());
            var xboxMutations = new XboxGameProfileMutations(store, gate, TdpLimits());
            var control = CreateControl(store, steamMutations, xboxMutations);

            var steam = await control.ScanProfileGamesAsync();
            var xbox = await control.ScanXboxGamesAsync();

            AssertCatalogFavoritesUnknown(steam.Select(game => (game.Favorite, game.FavoriteStateKnown)));
            AssertCatalogFavoritesUnknown(xbox.Games.Select(game => (game.Favorite, game.FavoriteStateKnown)));
            Assert.Equal(GameProfileMutations.MutationOutcome.PersistenceFailed, steamMutations.SetFavorite(101, true, "Steam favorite"));
            Assert.Equal(XboxGameProfileMutations.MutationOutcome.PersistenceFailed, xboxMutations.SetFavorite("store:xbox-one", true, "XBOX favorite"));
            Assert.Equal(original, File.ReadAllText(path));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Profile_read_failure_projects_unknown_and_preserves_the_locked_file()
    {
        var root = CreateTempDirectory();
        var path = Path.Combine(root, "profiles.json");
        const string original = "profile contents kept unreadable";
        File.WriteAllText(path, original);
        try
        {
            var store = new ProfileStore(path);
            var gate = new ProfileMutationGate();
            var steamMutations = new GameProfileMutations(store, gate, TdpLimits());
            var xboxMutations = new XboxGameProfileMutations(store, gate, TdpLimits());
            var control = CreateControl(store, steamMutations, xboxMutations);
            IReadOnlyList<FrontendProfileGameCatalogEntry> steam;
            FrontendXboxGameCatalogSnapshot xbox;

            using (new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                steam = await control.ScanProfileGamesAsync();
                xbox = await control.ScanXboxGamesAsync();
                Assert.Equal(GameProfileMutations.MutationOutcome.PersistenceFailed, steamMutations.SetFavorite(101, true, "Steam favorite"));
                Assert.Equal(XboxGameProfileMutations.MutationOutcome.PersistenceFailed, xboxMutations.SetFavorite("store:xbox-one", true, "XBOX favorite"));
            }

            AssertCatalogFavoritesUnknown(steam.Select(game => (game.Favorite, game.FavoriteStateKnown)));
            AssertCatalogFavoritesUnknown(xbox.Games.Select(game => (game.Favorite, game.FavoriteStateKnown)));
            Assert.Equal(original, File.ReadAllText(path));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static InProcessAddonFrontendControl CreateControl(
        ProfileStore store,
        GameProfileMutations? steamMutations = null,
        XboxGameProfileMutations? xboxMutations = null,
        IReadOnlyList<ProfileGameCatalogEntry>? steamGames = null,
        XboxInstalledGameCatalogResult? xboxResult = null)
    {
        var settings = new StartupSettingsCoordinator(
            new AppSettings(),
            new SettingsStore(Path.Combine(Path.GetTempPath(), $"FavoriteCatalogSettings-{Guid.NewGuid():N}.json")),
            new NoOpStartupManager());
        return new InProcessAddonFrontendControl(
            settings,
            new ThrowingStatusProvider(),
            null,
            gameProfileMutations: steamMutations,
            scanProfileGames: _ => Task.FromResult<IReadOnlyList<ProfileGameCatalogEntry>>(steamGames ??
            [
                new(101, "Steam favorite", ProfileGameSource.Steam),
                new(102, "Steam other", ProfileGameSource.NonSteam),
            ]),
            scanXboxGames: _ => Task.FromResult(xboxResult ?? new XboxInstalledGameCatalogResult(
                XboxInstalledGameCatalogOutcome.Completed,
                [CreateXboxGame("store:xbox-one", "XBOX favorite"), CreateXboxGame("pfn:xbox-two", "XBOX other")],
                null)),
            xboxGameProfileMutations: xboxMutations);
    }

    private static XboxInstalledGameCatalogEntry CreateXboxGame(string key, string displayName) =>
        new(new XboxGameIdentity(key, displayName, "store-internal", "family-internal"));

    private static TdpPowerLimits TdpLimits() => new(8, 30, 8, 37);

    private static void AssertCatalogFavoritesUnknown(IEnumerable<(bool Favorite, bool Known)> states)
    {
        var materialized = states.ToArray();
        Assert.NotEmpty(materialized);
        Assert.All(materialized, state =>
        {
            Assert.False(state.Favorite);
            Assert.False(state.Known);
        });
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"FavoriteCatalogState-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private sealed class NoOpStartupManager : IWindowsStartupManager
    {
        public StartupRegistrationResult Synchronize(bool enabled) => StartupRegistrationResult.Enabled();
    }

    private sealed class ThrowingStatusProvider : ISystemStatusProvider
    {
        public Task<SystemStatusSnapshot> CaptureAsync(CancellationToken cancellationToken = default)
            => throw new NotSupportedException("System status is not used by these tests.");
    }
}
