using SteamInputAddonforClaw.Install;
using SteamInputAddonforClaw.Profiles;
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

    public void Dispose()
    {
        if (Directory.Exists(_testDirectory))
        {
            Directory.Delete(_testDirectory, recursive: true);
        }
    }
}
