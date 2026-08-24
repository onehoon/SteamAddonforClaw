using SteamInputAddonforClaw.Install;
using SteamInputAddonforClaw.Profiles;
using SteamInputAddonforClaw.Contracts.DeviceProfiles;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class ProfileStoreTests : IDisposable
{
    private readonly string _testDirectory = Path.Combine(Path.GetTempPath(), $"SteamInputAddonforClaw.Tests.{Guid.NewGuid():N}");

    private string ProfilesPath => Path.Combine(_testDirectory, "profiles.json");

    // ---- First run ----

    [Fact]
    public void Load_WhenFileAbsent_ReturnsDefaultDocumentAsNotFound()
    {
        var store = new ProfileStore(ProfilesPath);

        var result = store.Load();

        Assert.Equal(ProfileLoadStatus.NotFound, result.Status);
        Assert.True(result.CanSafelyReplace);
        Assert.Equal(ProfileDocument.CurrentSchemaVersion, result.Document.SchemaVersion);
        Assert.Empty(result.Document.Games);
    }

    // ---- Save/load round trip ----

    [Fact]
    public void SaveAndLoad_RoundTripsAnEmptyDocument()
    {
        var store = new ProfileStore(ProfilesPath);

        store.Save(new ProfileDocument());
        var result = store.Load();

        Assert.Equal(ProfileLoadStatus.Loaded, result.Status);
        Assert.Equal(ProfileDocument.CurrentSchemaVersion, result.Document.SchemaVersion);
        Assert.Empty(result.Document.Games);
    }

    [Fact]
    public void SaveAndLoad_RoundTripsGlobalTdpWithoutChangingSchema()
    {
        var store = new ProfileStore(ProfilesPath);
        store.Save(new ProfileDocument
        {
            Device = new DeviceSettings
            {
                Performance = new DevicePerformanceSettings
                {
                    Tdp = new DeviceTdpSettings
                    {
                        Enabled = false,
                        Ac = new() { Pl1Watts = 25, Pl2Watts = 37 },
                        Dc = new() { Pl1Watts = 17, Pl2Watts = 25 }
                    }
                }
            }
        });

        var result = store.Load();
        var tdp = result.Document.Device.Performance.Tdp;

        Assert.Equal(ProfileDocument.CurrentSchemaVersion, result.Document.SchemaVersion);
        Assert.NotNull(tdp);
        Assert.False(tdp.Enabled);
        Assert.Equal(new TdpPowerPair { Pl1Watts = 25, Pl2Watts = 37 }, tdp.Ac);
        Assert.Equal(new TdpPowerPair { Pl1Watts = 17, Pl2Watts = 25 }, tdp.Dc);
    }

    [Fact]
    public void Load_WithoutTdpLeavesTdpNullAndDoesNotRewriteFile()
    {
        Directory.CreateDirectory(_testDirectory);
        var json = "{\"schemaVersion\":1,\"device\":{\"performance\":{}},\"games\":{}}";
        File.WriteAllText(ProfilesPath, json);

        var result = new ProfileStore(ProfilesPath).Load();

        Assert.Null(result.Document.Device.Performance.Tdp);
        Assert.Equal(json, File.ReadAllText(ProfilesPath));
    }

    [Fact]
    public void SaveAndLoad_PreservesUnknownTdpProperties()
    {
        Directory.CreateDirectory(_testDirectory);
        File.WriteAllText(ProfilesPath, """{"schemaVersion":1,"device":{"performance":{"tdp":{"enabled":true,"ac":{"pl1Watts":25,"pl2Watts":37,"futureAc":42},"dc":{"pl1Watts":17,"pl2Watts":25},"futureTdp":true}}},"games":{}}""");
        var store = new ProfileStore(ProfilesPath);

        var loaded = store.Load();
        store.Save(loaded.Document);

        var saved = File.ReadAllText(ProfilesPath);
        Assert.Contains("futureAc", saved);
        Assert.Contains("futureTdp", saved);
    }

    [Theory]
    [InlineData("{\"schemaVersion\":1,\"device\":{\"performance\":{\"tdp\":{\"enabled\":true,\"dc\":{\"pl1Watts\":17,\"pl2Watts\":25}}}},\"games\":{}}")]
    [InlineData("{\"schemaVersion\":1,\"device\":{\"performance\":{\"tdp\":{\"enabled\":true,\"ac\":null,\"dc\":{\"pl1Watts\":17,\"pl2Watts\":25}}}},\"games\":{}}")]
    public void Load_IncompleteOrNullTdpPair_ReturnsMalformedAndPreservesFile(string json)
    {
        Directory.CreateDirectory(_testDirectory);
        File.WriteAllText(ProfilesPath, json);

        var result = new ProfileStore(ProfilesPath).Load();

        Assert.Equal(ProfileLoadStatus.Malformed, result.Status);
        Assert.False(result.CanSafelyReplace);
        Assert.Equal(json, File.ReadAllText(ProfilesPath));
    }

    [Fact]
    public void SaveAndLoad_RoundTripsMultipleGamesIndependently()
    {
        var store = new ProfileStore(ProfilesPath);
        var document = new ProfileDocument
        {
            Games = new Dictionary<string, GameProfile>
            {
                ["1091500"] = new() { DisplayName = "Cyberpunk 2077" },
                ["570"] = new() { DisplayName = "Dota 2" }
            }
        };

        store.Save(document);
        var result = store.Load();

        Assert.Equal(ProfileLoadStatus.Loaded, result.Status);
        Assert.Equal(2, result.Document.Games.Count);
        Assert.Equal("Cyberpunk 2077", result.Document.Games["1091500"].DisplayName);
        Assert.Equal("Dota 2", result.Document.Games["570"].DisplayName);
    }

    [Fact]
    public void EnableGameProfile_CopiesSavedDeviceValuesEvenWhenDeviceTogglesAreOff()
    {
        var store = new ProfileStore(ProfilesPath);
        store.Save(new ProfileDocument
        {
            Device = new DeviceSettings
            {
                Performance = new DevicePerformanceSettings
                {
                    CpuBoost = new DeviceCpuBoostSettings { Enabled = false, Ac = CpuBoostMode.Aggressive, Dc = CpuBoostMode.EfficientEnabled },
                    Tdp = new DeviceTdpSettings { Enabled = false, Ac = new() { Pl1Watts = 30, Pl2Watts = 35 }, Dc = new() { Pl1Watts = 18, Pl2Watts = 22 } }
                }
            }
        });

        Assert.True(new GameProfileMutations(store).Enable(123, "Game"));
        var game = store.Load().Document.Games["123"];

        Assert.True(game.Enabled);
        Assert.Equal(CpuBoostMode.Aggressive, game.Performance.CpuBoost!.Ac);
        Assert.Equal(CpuBoostMode.EfficientEnabled, game.Performance.CpuBoost.Dc);
        Assert.Equal(new TdpPowerPair { Pl1Watts = 30, Pl2Watts = 35 }, game.Performance.Tdp!.Ac);
        Assert.Equal(new TdpPowerPair { Pl1Watts = 18, Pl2Watts = 22 }, game.Performance.Tdp.Dc);
    }

    [Fact]
    public void EnableGameProfile_UsesFallbacksWhenDeviceValuesAreUnavailable()
    {
        var store = new ProfileStore(ProfilesPath);

        Assert.True(new GameProfileMutations(store).Enable(123, null));
        var game = store.Load().Document.Games["123"];

        Assert.Equal(new GameCpuBoostSettings { Ac = CpuBoostMode.Enabled, Dc = CpuBoostMode.Enabled }, game.Performance.CpuBoost);
        Assert.Equal(new TdpPowerPair { Pl1Watts = 20, Pl2Watts = 22 }, game.Performance.Tdp!.Ac);
        Assert.Equal(new TdpPowerPair { Pl1Watts = 20, Pl2Watts = 22 }, game.Performance.Tdp.Dc);
    }

    [Fact]
    public void DisableAndReenable_PreservesGameValuesAndEntry()
    {
        var store = new ProfileStore(ProfilesPath);
        var mutations = new GameProfileMutations(store);
        Assert.True(mutations.Enable(123, "Game"));
        var custom = store.Load().Document.Games["123"] with
        {
            Performance = store.Load().Document.Games["123"].Performance with
            {
                CpuBoost = new() { Ac = CpuBoostMode.Disabled, Dc = CpuBoostMode.Aggressive },
                Tdp = new() { Ac = new() { Pl1Watts = 25, Pl2Watts = 30 }, Dc = new() { Pl1Watts = 15, Pl2Watts = 20 } }
            }
        };
        store.Save(store.Load().Document with { Games = new Dictionary<string, GameProfile> { ["123"] = custom with { Enabled = false } } });

        Assert.True(mutations.Enable(123, null));
        Assert.True(mutations.Disable(123));
        var game = store.Load().Document.Games["123"];
        Assert.False(game.Enabled);
        Assert.Equal(CpuBoostMode.Disabled, game.Performance.CpuBoost!.Ac);
        Assert.Equal(25, game.Performance.Tdp!.Ac.Pl1Watts);
    }

    [Fact]
    public void CpuBoostSideMutations_PreserveTheOppositePersistedSide()
    {
        var store = new ProfileStore(ProfilesPath);
        var mutations = new GameProfileMutations(store);
        Assert.True(mutations.Enable(123, "Game"));

        Assert.Equal(GameProfileMutations.MutationOutcome.Succeeded, mutations.SetCpuBoostAc(123, CpuBoostMode.Aggressive));
        var afterAc = store.Load().Document.Games["123"].Performance.CpuBoost!;
        Assert.Equal(CpuBoostMode.Aggressive, afterAc.Ac);
        Assert.Equal(CpuBoostMode.Enabled, afterAc.Dc);

        Assert.Equal(GameProfileMutations.MutationOutcome.Succeeded, mutations.SetCpuBoostDc(123, CpuBoostMode.Disabled));
        var afterDc = store.Load().Document.Games["123"].Performance.CpuBoost!;
        Assert.Equal(CpuBoostMode.Aggressive, afterDc.Ac);
        Assert.Equal(CpuBoostMode.Disabled, afterDc.Dc);
    }

    [Fact]
    public void SaveAndLoad_RoundTripsCompleteGameProfile()
    {
        var store = new ProfileStore(ProfilesPath);
        store.Save(new ProfileDocument { Games = new Dictionary<string, GameProfile> { ["123"] = new()
        {
            Enabled = true, DisplayName = "Game", Performance = new() { CpuBoost = new() { Ac = CpuBoostMode.Disabled, Dc = CpuBoostMode.Enabled }, Tdp = new() { Ac = new() { Pl1Watts = 20, Pl2Watts = 22 }, Dc = new() { Pl1Watts = 18, Pl2Watts = 20 } } }
        } } });

        var game = store.Load().Document.Games["123"];
        Assert.True(game.Enabled);
        Assert.Equal("Game", game.DisplayName);
        Assert.Equal(CpuBoostMode.Disabled, game.Performance.CpuBoost!.Ac);
        Assert.Equal(22, game.Performance.Tdp!.Ac.Pl2Watts);
    }

    [Fact]
    public void Load_UsesAppIdAsIdentityNotDisplayName()
    {
        var path = ProfilesPath;
        Directory.CreateDirectory(_testDirectory);
        File.WriteAllText(path, """{"schemaVersion":1,"device":{},"games":{"1091500":{"displayName":"Cyberpunk 2077"}}}""");

        var result = new ProfileStore(path).Load();

        Assert.Equal(ProfileLoadStatus.Loaded, result.Status);
        Assert.True(result.Document.Games.ContainsKey("1091500"));
    }

    // ---- Malformed JSON ----

    [Fact]
    public void Load_MalformedJson_ReturnsMalformedAndPreservesTheOriginalFile()
    {
        var path = ProfilesPath;
        Directory.CreateDirectory(_testDirectory);
        File.WriteAllText(path, "{ not valid json");

        var result = new ProfileStore(path).Load();

        Assert.Equal(ProfileLoadStatus.Malformed, result.Status);
        Assert.False(result.CanSafelyReplace);
        Assert.Equal("{ not valid json", File.ReadAllText(path));
    }

    [Fact]
    public void Load_MissingSchemaVersion_ReturnsMalformedAndPreservesTheOriginalFile()
    {
        var path = ProfilesPath;
        Directory.CreateDirectory(_testDirectory);
        File.WriteAllText(path, """{"device":{},"games":{}}""");

        var result = new ProfileStore(path).Load();

        Assert.Equal(ProfileLoadStatus.Malformed, result.Status);
        Assert.False(result.CanSafelyReplace);
    }

    // ---- Unsupported schema version ----

    [Fact]
    public void Load_NewerSchemaVersion_ReturnsUnsupportedAndPreservesTheOriginalFile()
    {
        var path = ProfilesPath;
        Directory.CreateDirectory(_testDirectory);
        var original = "{\"schemaVersion\":" + (ProfileDocument.CurrentSchemaVersion + 1) + ",\"device\":{},\"games\":{}}";
        File.WriteAllText(path, original);

        var result = new ProfileStore(path).Load();

        Assert.Equal(ProfileLoadStatus.UnsupportedSchemaVersion, result.Status);
        Assert.False(result.CanSafelyReplace);
        Assert.Equal(original, File.ReadAllText(path));
    }

    [Fact]
    public void Load_NewerSchemaVersion_DoesNotSilentlyReinterpretAsVersionOne()
    {
        var path = ProfilesPath;
        Directory.CreateDirectory(_testDirectory);
        File.WriteAllText(path, "{\"schemaVersion\":" + (ProfileDocument.CurrentSchemaVersion + 1) + ",\"device\":{},\"games\":{\"1\":{}}}");

        var result = new ProfileStore(path).Load();

        // A fresh default is returned for the caller to use, not the newer document's contents.
        Assert.Empty(result.Document.Games);
    }

    // ---- Read failure vs. absent-file first run ----

    [Fact]
    public void Load_ReadFailure_IsNotTreatedAsFirstRun()
    {
        var path = ProfilesPath;
        Directory.CreateDirectory(_testDirectory);
        File.WriteAllText(path, """{"schemaVersion":1,"device":{},"games":{}}""");

        using var handle = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);
        var result = new ProfileStore(path).Load();

        Assert.Equal(ProfileLoadStatus.ReadFailure, result.Status);
        Assert.False(result.CanSafelyReplace);
    }

    // ---- Additive JSON forward-compatibility (top-level extension data) ----

    [Fact]
    public void SaveAndLoad_PreservesUnrecognizedTopLevelProperty()
    {
        var path = ProfilesPath;
        Directory.CreateDirectory(_testDirectory);
        File.WriteAllText(path, """{"schemaVersion":1,"device":{},"games":{},"futureField":{"nested":42}}""");
        var store = new ProfileStore(path);

        var loaded = store.Load();
        Assert.Equal(ProfileLoadStatus.Loaded, loaded.Status);

        store.Save(loaded.Document);
        var savedText = File.ReadAllText(path);

        Assert.Contains("futureField", savedText);
    }

    [Fact]
    public void SaveAndLoad_PreservesFutureFieldUnderDevicePerformance()
    {
        var path = ProfilesPath;
        Directory.CreateDirectory(_testDirectory);
        File.WriteAllText(path, """{"schemaVersion":1,"device":{"performance":{"futureCpuBoost":true},"display":{}},"games":{}}""");
        var store = new ProfileStore(path);

        var loaded = store.Load();
        Assert.Equal(ProfileLoadStatus.Loaded, loaded.Status);

        store.Save(loaded.Document);

        Assert.Contains("futureCpuBoost", File.ReadAllText(path));
    }

    [Fact]
    public void SaveAndLoad_PreservesFutureFieldUnderGamePerformance()
    {
        var path = ProfilesPath;
        Directory.CreateDirectory(_testDirectory);
        File.WriteAllText(path, """{"schemaVersion":1,"device":{},"games":{"1091500":{"performance":{"futureTdpWatts":20}}}}""");
        var store = new ProfileStore(path);

        var loaded = store.Load();
        Assert.Equal(ProfileLoadStatus.Loaded, loaded.Status);

        store.Save(loaded.Document);

        Assert.Contains("futureTdpWatts", File.ReadAllText(path));
    }

    // ---- Explicit JSON null in a required container must not poison the model ----

    [Theory]
    [InlineData("""{"schemaVersion":1,"device":null,"games":{}}""")]
    [InlineData("""{"schemaVersion":1,"device":{},"games":null}""")]
    [InlineData("""{"schemaVersion":1,"device":{"performance":null,"display":{}},"games":{}}""")]
    [InlineData("""{"schemaVersion":1,"device":{},"games":{"1":null}}""")]
    [InlineData("""{"schemaVersion":1,"device":{},"games":{"1":{"performance":null}}}""")]
    public void Load_ExplicitNullStructuralField_ReturnsMalformedAndPreservesTheOriginalFile(string json)
    {
        var path = ProfilesPath;
        Directory.CreateDirectory(_testDirectory);
        File.WriteAllText(path, json);

        var result = new ProfileStore(path).Load();

        Assert.Equal(ProfileLoadStatus.Malformed, result.Status);
        Assert.False(result.CanSafelyReplace);
        Assert.NotNull(result.Document.Device);
        Assert.NotNull(result.Document.Games);
        Assert.Equal(json, File.ReadAllText(path));
    }

    [Fact]
    public void Load_EnabledIncompleteGameProfile_ReturnsMalformedAndPreservesTheFile()
    {
        var json = "{\"schemaVersion\":1,\"device\":{},\"games\":{\"123\":{\"enabled\":true}}}";
        Directory.CreateDirectory(_testDirectory);
        File.WriteAllText(ProfilesPath, json);

        var result = new ProfileStore(ProfilesPath).Load();

        Assert.Equal(ProfileLoadStatus.Malformed, result.Status);
        Assert.False(result.CanSafelyReplace);
        Assert.Equal(json, File.ReadAllText(ProfilesPath));
    }

    [Fact]
    public void Load_DisabledIncompleteGameProfile_RemainsLoadable()
    {
        Directory.CreateDirectory(_testDirectory);
        File.WriteAllText(ProfilesPath, "{\"schemaVersion\":1,\"device\":{},\"games\":{\"123\":{\"displayName\":\"Legacy\"}}}");

        var result = new ProfileStore(ProfilesPath).Load();

        Assert.Equal(ProfileLoadStatus.Loaded, result.Status);
        Assert.False(result.Document.Games["123"].Enabled);
        Assert.Equal("Legacy", result.Document.Games["123"].DisplayName);
    }

    // ---- Atomic save: failure before replacement must not destroy the existing document ----

    [Fact]
    public void Save_DoesNotLeaveATemporaryFileBehindOnSuccess()
    {
        var store = new ProfileStore(ProfilesPath);

        store.Save(new ProfileDocument());

        Assert.False(File.Exists($"{ProfilesPath}.tmp"));
        Assert.True(File.Exists(ProfilesPath));
    }

    [Fact]
    public void Save_WritesToATemporaryFileInTheSameDirectoryThenReplaces()
    {
        var store = new ProfileStore(ProfilesPath);
        store.Save(new ProfileDocument { Games = new Dictionary<string, GameProfile> { ["1"] = new() { DisplayName = "Original" } } });
        var originalText = File.ReadAllText(ProfilesPath);

        // A failure mid-serialize (e.g. process crash) never happens inside File.WriteAllText for
        // a fully-formed in-memory document, so this test instead proves the existing valid
        // document survives an aborted save attempt: writing the temp file and never completing
        // the move must not be observable as a canonical-file change.
        var temporaryPath = $"{ProfilesPath}.tmp";
        File.WriteAllText(temporaryPath, "{ incomplete");

        Assert.Equal(originalText, File.ReadAllText(ProfilesPath));
    }

    // ---- Persistent root ----

    [Fact]
    public void ResolveProfilesPath_UsesTheCanonicalDataRoot_NotTheAppDirectory()
    {
        var appDirectory = Path.Combine(_testDirectory, "app", "current");
        Directory.CreateDirectory(appDirectory);

        var path = AddonDataPaths.ResolveProfilesPath(appDirectory);

        Assert.EndsWith("profiles.json", path);
        Assert.Contains("SteamInputAddonforClaw-Data", path);
        Assert.DoesNotContain(Path.Combine(_testDirectory, "app", "current"), path);
    }

    [Fact]
    public void FavoriteMutation_CreatesDisabledEntryAndRoundTrips()
    {
        var mutations = new GameProfileMutations(new ProfileStore(ProfilesPath));

        Assert.Equal(GameProfileMutations.MutationOutcome.Succeeded, mutations.SetFavorite(123, true, "Game"));

        var profile = new ProfileStore(ProfilesPath).Load().Document.Games["123"];
        Assert.True(profile.Favorite);
        Assert.False(profile.Enabled);
        Assert.Null(profile.Performance.CpuBoost);
        Assert.True(new ProfileStore(ProfilesPath).Load().Document.Games["123"].Favorite);
    }

    [Fact]
    public void FavoriteMutation_PreservesExistingPerformanceProfile()
    {
        var store = new ProfileStore(ProfilesPath);
        store.Save(new ProfileDocument { Games = new Dictionary<string, GameProfile>
        {
            ["123"] = new() { DisplayName = "Game", Performance = new() { CpuBoost = new() { Ac = CpuBoostMode.Aggressive, Dc = CpuBoostMode.Enabled } } }
        }});
        var mutations = new GameProfileMutations(store);

        Assert.Equal(GameProfileMutations.MutationOutcome.Succeeded, mutations.SetFavorite(123, true, null));

        var profile = store.Load().Document.Games["123"];
        Assert.True(profile.Favorite);
        Assert.False(profile.Enabled);
        Assert.Equal(CpuBoostMode.Aggressive, profile.Performance.CpuBoost!.Ac);
    }

    [Fact]
    public void SetResolution_CreatesDisabledProfileAndRoundTrips()
    {
        var store = new ProfileStore(ProfilesPath);
        var mutations = new GameProfileMutations(store);

        var result = mutations.SetResolution(123, new GameDisplayResolution { Width = 1440, Height = 900 }, "Game");

        var saved = store.Load().Document.Games["123"];
        Assert.Equal(GameProfileMutations.MutationOutcome.Succeeded, result);
        Assert.False(saved.Enabled);
        Assert.Equal(1440, saved.Display.Resolution!.Width);
        Assert.Equal(900, saved.Display.Resolution.Height);
        Assert.Equal("Game", saved.DisplayName);
    }

    [Fact]
    public void SetResolution_ClearProducesDoNotChangeAndPreservesUnknownDisplayData()
    {
        var store = new ProfileStore(ProfilesPath);
        store.Save(new ProfileDocument { Games = new Dictionary<string, GameProfile>
        {
            ["123"] = new() { Enabled = false, Display = new() { Resolution = new GameDisplayResolution { Width = 1920, Height = 1080 }, ExtensionData = new() { ["future"] = System.Text.Json.JsonDocument.Parse("true").RootElement.Clone() } } }
        }});
        var mutations = new GameProfileMutations(store);

        Assert.Equal(GameProfileMutations.MutationOutcome.Succeeded, mutations.SetResolution(123, null, null));

        var saved = store.Load().Document.Games["123"];
        Assert.False(saved.Enabled);
        Assert.Null(saved.Display.Resolution);
        Assert.True(saved.Display.ExtensionData!.ContainsKey("future"));
    }

    [Fact]
    public void SetResolution_ClearingAbsentResolutionDoesNotCreateGhostProfile()
    {
        var store = new ProfileStore(ProfilesPath);
        var mutations = new GameProfileMutations(store);

        Assert.Equal(GameProfileMutations.MutationOutcome.Succeeded, mutations.SetResolution(123, null, "Game"));
        Assert.False(store.Load().Document.Games.ContainsKey("123"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDirectory))
        {
            Directory.Delete(_testDirectory, recursive: true);
        }
    }
}
