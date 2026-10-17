using SteamInputAddonforClaw.Contracts.Frontend;
using SteamInputAddonforClaw.Frontend;
using SteamInputAddonforClaw.Install;
using SteamInputAddonforClaw.Profiles;
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
                new GameProfileMutations(store, gate),
                new XboxGameProfileMutations(store, gate));

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
            var steamMutations = new GameProfileMutations(store, gate);
            var xboxMutations = new XboxGameProfileMutations(store, gate);
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
            var steamMutations = new GameProfileMutations(store, gate);
            var xboxMutations = new XboxGameProfileMutations(store, gate);
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
            var steamMutations = new GameProfileMutations(store, gate);
            var xboxMutations = new XboxGameProfileMutations(store, gate);
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
        XboxGameProfileMutations? xboxMutations = null)
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
            scanProfileGames: _ => Task.FromResult<IReadOnlyList<ProfileGameCatalogEntry>>(
            [
                new(101, "Steam favorite", ProfileGameSource.Steam),
                new(102, "Steam other", ProfileGameSource.NonSteam),
            ]),
            scanXboxGames: _ => Task.FromResult(new XboxInstalledGameCatalogResult(
                XboxInstalledGameCatalogOutcome.Completed,
                [CreateXboxGame("store:xbox-one", "XBOX favorite"), CreateXboxGame("pfn:xbox-two", "XBOX other")],
                null)),
            xboxGameProfileMutations: xboxMutations);
    }

    private static XboxInstalledGameCatalogEntry CreateXboxGame(string key, string displayName) =>
        new(new XboxGameIdentity(key, displayName, "store-internal", "family-internal"));

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
