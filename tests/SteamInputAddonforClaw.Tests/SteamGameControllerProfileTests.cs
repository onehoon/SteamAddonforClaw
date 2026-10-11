using SteamInputAddonforClaw.Contracts.Frontend;
using SteamInputAddonforClaw.Profiles;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class SteamGameControllerProfileTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"SteamGameControllerProfile-{Guid.NewGuid():N}");
    private string StorePath => Path.Combine(_directory, "profiles.json");

    [Fact]
    public void Nullable_controller_override_round_trips_without_losing_legacy_profile_fields()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(StorePath,
            """{"schemaVersion":1,"device":{},"games":{"12345":{"enabled":true,"favorite":true,"displayName":"Legacy Game","performance":{"cpuBoost":{"enabled":true,"ac":"enabled","dc":"enabled"},"tdp":{"enabled":true,"ac":{"pl1Watts":20,"pl2Watts":22},"dc":{"pl1Watts":20,"pl2Watts":22}}},"display":{},"futureSetting":{"retained":true}}},"xboxGames":{}}""");
        var store = new ProfileStore(StorePath);
        var mutations = new GameProfileMutations(store, new ProfileMutationGate());

        Assert.Equal((true, (SteamVirtualControllerPreference?)null), mutations.CaptureSteamVirtualController(12345));
        Assert.Equal(GameProfileMutations.MutationOutcome.Succeeded,
            mutations.SetSteamVirtualController(12345, SteamVirtualControllerPreference.DualSenseEdge));
        Assert.Equal(SteamVirtualControllerPreference.DualSenseEdge,
            new ProfileStore(StorePath).Load().Document.Games["12345"].SteamVirtualController);
        Assert.Equal(GameProfileMutations.MutationOutcome.Succeeded,
            mutations.SetSteamVirtualController(12345, SteamVirtualControllerPreference.SteamDeck));
        Assert.Equal(SteamVirtualControllerPreference.SteamDeck,
            new ProfileStore(StorePath).Load().Document.Games["12345"].SteamVirtualController);
        Assert.Equal(GameProfileMutations.MutationOutcome.Succeeded, mutations.SetSteamVirtualController(12345, null));

        var reloaded = new ProfileStore(StorePath).Load().Document.Games["12345"];
        Assert.True(reloaded.Enabled);
        Assert.True(reloaded.Favorite);
        Assert.Equal("Legacy Game", reloaded.DisplayName);
        Assert.Null(reloaded.SteamVirtualController);
        Assert.True(reloaded.ExtensionData!["futureSetting"].GetProperty("retained").GetBoolean());
        Assert.Equal((true, (SteamVirtualControllerPreference?)null), mutations.CaptureSteamVirtualController(12345));
    }

    [Fact]
    public void Profile_off_is_reported_to_the_presentation_resolver_even_when_override_exists()
    {
        var store = new ProfileStore(StorePath);
        store.Save(new ProfileDocument
        {
            Games = new Dictionary<string, GameProfile>(StringComparer.Ordinal)
            {
                ["12345"] = new() { Enabled = false, SteamVirtualController = SteamVirtualControllerPreference.DualSenseEdge }
            }
        });
        var mutations = new GameProfileMutations(store, new ProfileMutationGate());

        Assert.Equal((false, (SteamVirtualControllerPreference?)SteamVirtualControllerPreference.DualSenseEdge),
            mutations.CaptureSteamVirtualController(12345));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }
}
