using SteamInputAddonforClaw.Contracts.Frontend;
using SteamInputAddonforClaw.Frontend;
using SteamInputAddonforClaw.Install;
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
        var control = CreateControl(_ =>
        {
            calls++;
            return Task.FromResult(new XboxInstalledGameCatalogResult(
                XboxInstalledGameCatalogOutcome.Completed,
                [CreateGame("store:game-one", "Aniimo Legend"), CreateGame("pfn:game-two", "星のカービィ")],
                null));
        });

        var snapshot = await control.ScanXboxGamesAsync();

        Assert.Equal(1, calls);
        Assert.Equal(FrontendXboxGameCatalogOutcome.Ready, snapshot.Outcome);
        Assert.Equal(
            new[] { new FrontendXboxGameCatalogEntry("store:game-one", "Aniimo Legend"), new("pfn:game-two", "星のカービィ") },
            snapshot.Games.ToArray());
        Assert.Equal(["DisplayName", "Key"], typeof(FrontendXboxGameCatalogEntry)
            .GetProperties()
            .Select(property => property.Name)
            .Order(StringComparer.Ordinal)
            .ToArray());
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
        Func<CancellationToken, Task<XboxInstalledGameCatalogResult>> scanXboxGames)
    {
        var settings = new StartupSettingsCoordinator(
            new AppSettings(),
            new SettingsStore(Path.Combine(Path.GetTempPath(), $"XboxFrontend-{Guid.NewGuid():N}.json")),
            new NoOpStartupManager());
        return new InProcessAddonFrontendControl(settings, new ThrowingStatusProvider(), null, scanXboxGames: scanXboxGames);
    }

    private static XboxInstalledGameCatalogEntry CreateGame(string key, string displayName)
    {
        var identity = new XboxGameIdentity(
            key,
            displayName,
            "store-internal",
            "title-internal",
            "family-internal",
            "IdentityName",
            "Publisher",
            "Resource",
            []);
        return new(identity, "PackageName", "PackageFullName", @"C:\\Package\\MicrosoftGame.config");
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
