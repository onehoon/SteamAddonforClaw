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

public sealed class XboxGameCatalogFrontendTests
{
    [Fact]
    public async Task Completed_catalog_projects_only_the_canonical_key_and_display_name_once()
    {
        var calls = 0;
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "profiles.json");
        var store = new ProfileStore(path);
        var gate = new ProfileMutationGate();
        var mutations = new XboxGameProfileMutations(store, gate, TdpLimits());
        var steamMutations = new GameProfileMutations(store, gate, TdpLimits());
        var control = CreateControl(_ =>
        {
            calls++;
            return Task.FromResult(new XboxInstalledGameCatalogResult(
                XboxInstalledGameCatalogOutcome.Completed,
                [CreateGame("store:game-one", "Aniimo Legend"), CreateGame("pfn:game-two", "星のカービィ")],
                null));
        }, mutations, steamMutations);

        var snapshot = await control.ScanXboxGamesAsync();

        Assert.Equal(1, calls);
        Assert.Equal(FrontendXboxGameCatalogOutcome.Ready, snapshot.Outcome);
        Assert.Equal(
            new[]
            {
                new FrontendXboxGameCatalogEntry("store:game-one", "Aniimo Legend", FavoriteStateKnown: true),
                new("pfn:game-two", "星のカービィ", FavoriteStateKnown: true),
            },
            snapshot.Games.ToArray());
        Assert.Equal(["DisplayName", "Favorite", "FavoriteStateKnown", "Key"], typeof(FrontendXboxGameCatalogEntry)
            .GetProperties()
            .Select(property => property.Name)
            .Order(StringComparer.Ordinal)
            .ToArray());
        var saved = store.Load().Document.XboxGames;
        Assert.All(saved.Values, profile => Assert.True(profile.Enabled));
        Assert.Equal(CpuBoostMode.EfficientEnabled, saved["store:game-one"].Performance.CpuBoost!.Ac);
        Assert.Equal(new TdpPowerPair { Pl1Watts = 20, Pl2Watts = 22 }, saved["store:game-one"].Performance.Tdp!.Ac);
        Assert.Equal(WindowsPowerMode.BestPerformance, saved["store:game-one"].Performance.PowerMode!.Dc);
        Assert.Null(saved["store:game-one"].Performance.FpsLimit);
        Assert.Null(saved["store:game-one"].Performance.GpuMinimumClock);
        Assert.Null(saved["store:game-one"].Display.Resolution);
        Assert.Null(saved["store:game-one"].Controller.BackButtonMapping);
    }

    [Fact]
    public async Task Catalog_favorites_are_merged_from_the_XBOX_profile_store()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "profiles.json");
        var store = new ProfileStore(path);
        var gate = new ProfileMutationGate();
        var mutations = new XboxGameProfileMutations(store, gate, TdpLimits());
        var steamMutations = new GameProfileMutations(store, gate, TdpLimits());
        Assert.Equal(SteamInputAddonforClaw.Profiles.XboxGameProfileMutations.MutationOutcome.Succeeded,
            mutations.SetFavorite("store:game-one", true, "Aniimo Legend"));
        var control = CreateControl(_ => Task.FromResult(new XboxInstalledGameCatalogResult(
            XboxInstalledGameCatalogOutcome.Completed,
            [CreateGame("store:game-one", "Aniimo Legend"), CreateGame("pfn:game-two", "Other")], null)), mutations, steamMutations);

        var snapshot = await control.ScanXboxGamesAsync();

        Assert.Equal(new[] { true, false }, snapshot.Games.Select(game => game.Favorite));
        Assert.All(snapshot.Games, game => Assert.True(game.FavoriteStateKnown));
    }

    [Theory]
    [InlineData((int)XboxInstalledGameCatalogOutcome.Unavailable, FrontendXboxGameCatalogOutcome.Unavailable, "XBOX game catalog is unavailable.")]
    [InlineData((int)XboxInstalledGameCatalogOutcome.Failed, FrontendXboxGameCatalogOutcome.Failed, "XBOX game catalog could not be loaded.")]
    public async Task Catalog_failures_map_to_user_safe_frontend_messages(
        int runtimeOutcomeValue,
        FrontendXboxGameCatalogOutcome frontendOutcome,
        string expectedMessage)
    {
        const string rawFailure = "HRESULT 0x80070005 at C:\\Program Files\\WindowsApps\\PrivatePackage";
        var runtimeOutcome = (XboxInstalledGameCatalogOutcome)runtimeOutcomeValue;
        var control = CreateControl(_ => Task.FromResult(new XboxInstalledGameCatalogResult(runtimeOutcome, [], rawFailure)));

        var snapshot = await control.ScanXboxGamesAsync();

        Assert.Equal(frontendOutcome, snapshot.Outcome);
        Assert.Equal(expectedMessage, snapshot.FailureMessage);
        Assert.Empty(snapshot.Games);
        Assert.DoesNotContain(rawFailure, snapshot.FailureMessage!, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData((int)XboxInstalledGameCatalogOutcome.Failed)]
    [InlineData((int)XboxInstalledGameCatalogOutcome.Unavailable)]
    public async Task Failed_or_unavailable_catalog_does_not_enroll_profiles(int outcomeValue)
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "profiles.json");
        var store = new ProfileStore(path);
        var gate = new ProfileMutationGate();
        var mutations = new XboxGameProfileMutations(store, gate, TdpLimits());
        var steamMutations = new GameProfileMutations(store, gate, TdpLimits());
        var outcome = (XboxInstalledGameCatalogOutcome)outcomeValue;
        var control = CreateControl(_ => Task.FromResult(new XboxInstalledGameCatalogResult(
            outcome, [CreateGame("store:game-one", "Aniimo Legend")], "catalog unavailable")), mutations, steamMutations);

        var snapshot = await control.ScanXboxGamesAsync();

        Assert.NotEqual(FrontendXboxGameCatalogOutcome.Ready, snapshot.Outcome);
        Assert.False(File.Exists(path));
    }

    [Fact]
    public async Task Cancellation_token_is_forwarded_to_the_production_scan_seam()
    {
        using var cancellation = new CancellationTokenSource();
        var observed = CancellationToken.None;
        var control = CreateControl(async token =>
        {
            observed = token;
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return new XboxInstalledGameCatalogResult(XboxInstalledGameCatalogOutcome.Completed, [], null);
        });

        var scan = control.ScanXboxGamesAsync(cancellation.Token);
        Assert.Equal(cancellation.Token, observed);
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => scan);
    }

    private static InProcessAddonFrontendControl CreateControl(
        Func<CancellationToken, Task<XboxInstalledGameCatalogResult>> scanXboxGames,
        XboxGameProfileMutations? mutations = null,
        GameProfileMutations? steamMutations = null)
    {
        var settings = new StartupSettingsCoordinator(
            new AppSettings(),
            new SettingsStore(Path.Combine(Path.GetTempPath(), $"XboxFrontend-{Guid.NewGuid():N}.json")),
            new NoOpStartupManager());
        return new InProcessAddonFrontendControl(settings, new ThrowingStatusProvider(), null,
            scanXboxGames: scanXboxGames,
            gameProfileMutations: steamMutations,
            xboxGameProfileMutations: mutations);
    }

    private static XboxInstalledGameCatalogEntry CreateGame(string key, string displayName)
    {
        var identity = new XboxGameIdentity(
            key,
            displayName,
            "store-internal",
            "family-internal");
        return new(identity);
    }

    private static TdpPowerLimits TdpLimits() => new(8, 30, 8, 37);

    private sealed class TemporaryDirectory : IDisposable
    {
        internal string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"XboxCatalog-{Guid.NewGuid():N}");

        internal TemporaryDirectory() => Directory.CreateDirectory(Path);

        public void Dispose()
        {
            if (Directory.Exists(Path)) Directory.Delete(Path, recursive: true);
        }
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
