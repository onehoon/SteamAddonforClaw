using System.Text.Json;
using SteamInputAddonforClaw.Contracts.Shortcuts;
using SteamInputAddonforClaw.Shortcuts;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class ShortcutStoreTests : IDisposable
{
    private readonly string _testDirectory = Path.Combine(Path.GetTempPath(), $"SteamInputAddonforClaw.Shortcuts.Tests.{Guid.NewGuid():N}");

    private string ShortcutsPath => Path.Combine(_testDirectory, "shortcuts.json");

    [Fact]
    public void Load_WhenFileAbsent_ReturnsNotFoundWithoutCreatingTheFile()
    {
        var result = new ShortcutStore(ShortcutsPath).Load();

        Assert.Equal(ShortcutLoadStatus.NotFound, result.Status);
        Assert.True(result.CanSafelyReplace);
        Assert.Equal(ShortcutDocument.CurrentSchemaVersion, result.Document.SchemaVersion);
        Assert.Empty(result.Document.Dashboard.Tiles);
        Assert.False(File.Exists(ShortcutsPath));
    }

    [Fact]
    public void SaveAndLoad_RoundTripsAnEmptyDocument()
    {
        var store = new ShortcutStore(ShortcutsPath);

        store.Save(new ShortcutDocument());
        var result = store.Load();

        Assert.Equal(ShortcutLoadStatus.Loaded, result.Status);
        Assert.Equal(ShortcutDocument.CurrentSchemaVersion, result.Document.SchemaVersion);
        Assert.Empty(result.Document.Dashboard.Tiles);
        Assert.False(File.Exists($"{ShortcutsPath}.tmp"));
    }

    [Fact]
    public void SaveAndLoad_RoundTripsMoreThanFourTilesInExactOrder()
    {
        var tiles = Enumerable.Range(0, 6)
            .Select(index => Tile($"Tile {index}", $"future.action.{index}"))
            .ToArray();
        var document = new ShortcutDocument { Dashboard = new ShortcutDashboardDefinition(tiles) };

        var store = new ShortcutStore(ShortcutsPath);
        store.Save(document);
        var loaded = store.Load();

        Assert.Equal(ShortcutLoadStatus.Loaded, loaded.Status);
        Assert.Equal(tiles.Select(tile => tile.TileId), loaded.Document.Dashboard.Tiles.Select(tile => tile.TileId));
        Assert.Equal(tiles.Select(tile => tile.Title), loaded.Document.Dashboard.Tiles.Select(tile => tile.Title));
        Assert.Equal(tiles.Select(tile => tile.Action.TypeId), loaded.Document.Dashboard.Tiles.Select(tile => tile.Action.TypeId));
    }

    [Theory]
    [InlineData("addon.tdp-preset", 1, "{\"watts\":30}")]
    [InlineData("system.executable", 1, "{\"path\":\"C:\\\\Tools\\\\Tool.exe\",\"arguments\":\"--example\"}")]
    [InlineData("system.powershell", 1, "{\"script\":\"Write-Output 'hello'\"}")]
    [InlineData("Future.Vendor.Unknown-Action", 99, "{\"anything\":{\"nested\":true}}")]
    public void SaveAndLoad_PreservesRepresentativeActionEnvelope(string typeId, int schemaVersion, string parametersJson)
    {
        var tile = new ShortcutTileDefinition(
            Guid.NewGuid(),
            "Action",
            new ShortcutActionSpec(typeId, schemaVersion, JsonDocument.Parse(parametersJson).RootElement.Clone()));
        var store = new ShortcutStore(ShortcutsPath);

        store.Save(new ShortcutDocument { Dashboard = new ShortcutDashboardDefinition([tile]) });
        var loaded = store.Load();
        var action = Assert.Single(loaded.Document.Dashboard.Tiles).Action;

        Assert.Equal(ShortcutLoadStatus.Loaded, loaded.Status);
        Assert.Equal(typeId, action.TypeId);
        Assert.Equal(schemaVersion, action.SchemaVersion);
        using var expectedParameters = JsonDocument.Parse(parametersJson);
        Assert.True(JsonElement.DeepEquals(expectedParameters.RootElement, action.Parameters));
    }

    [Fact]
    public void Load_ParametersRemainReadableAfterLoadReturns()
    {
        Directory.CreateDirectory(_testDirectory);
        File.WriteAllText(ShortcutsPath, """
            {
              "schemaVersion": 1,
              "dashboard": {
                "tiles": [
                  {
                    "tileId": "11111111-2222-3333-4444-555555555555",
                    "title": "Nested",
                    "action": {
                      "typeId": "future.nested",
                      "schemaVersion": 3,
                      "parameters": { "outer": { "value": 42 } }
                    }
                  }
                ]
              }
            }
            """);

        var result = new ShortcutStore(ShortcutsPath).Load();
        var parameters = Assert.Single(result.Document.Dashboard.Tiles).Action.Parameters;

        Assert.Equal(ShortcutLoadStatus.Loaded, result.Status);
        Assert.Equal(42, parameters.GetProperty("outer").GetProperty("value").GetInt32());
    }

    [Theory]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("\"text\"")]
    public void Load_NonObjectRoot_ReturnsMalformedAndPreservesTheOriginal(string json)
    {
        WriteExisting(json);

        var result = new ShortcutStore(ShortcutsPath).Load();

        Assert.Equal(ShortcutLoadStatus.Malformed, result.Status);
        Assert.False(result.CanSafelyReplace);
        Assert.Equal(json, File.ReadAllText(ShortcutsPath));
    }

    [Fact]
    public void Load_MalformedJson_ReturnsMalformedAndPreservesTheOriginal()
    {
        WriteExisting("{ not valid json");

        var result = new ShortcutStore(ShortcutsPath).Load();

        Assert.Equal(ShortcutLoadStatus.Malformed, result.Status);
        Assert.False(result.CanSafelyReplace);
        Assert.Equal("{ not valid json", File.ReadAllText(ShortcutsPath));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"schemaVersion\":1}")]
    [InlineData("{\"schemaVersion\":1,\"dashboard\":null}")]
    [InlineData("{\"schemaVersion\":1,\"dashboard\":[]}")]
    [InlineData("{\"schemaVersion\":\"1\",\"dashboard\":{\"tiles\":[]}}")]
    [InlineData("{\"schemaVersion\":0,\"dashboard\":{\"tiles\":[]}}")]
    [InlineData("{\"schemaVersion\":-1,\"dashboard\":{\"tiles\":[]}}")]
    public void Load_InvalidRootStructure_ReturnsMalformedAndPreservesTheOriginal(string json)
    {
        WriteExisting(json);

        var result = new ShortcutStore(ShortcutsPath).Load();

        Assert.Equal(ShortcutLoadStatus.Malformed, result.Status);
        Assert.False(result.CanSafelyReplace);
        Assert.Equal(json, File.ReadAllText(ShortcutsPath));
    }

    [Fact]
    public void Load_NewerSchemaVersion_ReturnsUnsupportedAndPreservesTheOriginal()
    {
        var json = $"{{\"schemaVersion\":{ShortcutDocument.CurrentSchemaVersion + 1},\"dashboard\":{{\"tiles\":[]}}}}";
        WriteExisting(json);

        var result = new ShortcutStore(ShortcutsPath).Load();

        Assert.Equal(ShortcutLoadStatus.UnsupportedSchemaVersion, result.Status);
        Assert.False(result.CanSafelyReplace);
        Assert.Equal(json, File.ReadAllText(ShortcutsPath));
    }

    [Theory]
    [InlineData("{\"schemaVersion\":1,\"dashboard\":{\"tiles\":null}}")]
    [InlineData("{\"schemaVersion\":1,\"dashboard\":{\"tiles\":[null]}}")]
    [InlineData("{\"schemaVersion\":1,\"dashboard\":{\"tiles\":[{\"tileId\":\"11111111-2222-3333-4444-555555555555\",\"title\":\"Tile\",\"action\":null}]}}")]
    public void Load_ExplicitNullStructure_ReturnsMalformedAndPreservesTheOriginal(string json)
    {
        WriteExisting(json);

        var result = new ShortcutStore(ShortcutsPath).Load();

        Assert.Equal(ShortcutLoadStatus.Malformed, result.Status);
        Assert.False(result.CanSafelyReplace);
        Assert.Equal(json, File.ReadAllText(ShortcutsPath));
    }

    [Theory]
    [InlineData("{\"tileId\":\"00000000-0000-0000-0000-000000000000\",\"title\":\"Tile\",\"action\":{\"typeId\":\"future.action\",\"schemaVersion\":1,\"parameters\":{}}}")]
    [InlineData("{\"tileId\":\"11111111-2222-3333-4444-555555555555\",\"title\":\"\",\"action\":{\"typeId\":\"future.action\",\"schemaVersion\":1,\"parameters\":{}}}")]
    [InlineData("{\"tileId\":\"11111111-2222-3333-4444-555555555555\",\"title\":\"Tile\",\"action\":{\"typeId\":\"\",\"schemaVersion\":1,\"parameters\":{}}}")]
    [InlineData("{\"tileId\":\"11111111-2222-3333-4444-555555555555\",\"title\":\"Tile\",\"action\":{\"typeId\":\" future.action\",\"schemaVersion\":1,\"parameters\":{}}}")]
    [InlineData("{\"tileId\":\"11111111-2222-3333-4444-555555555555\",\"title\":\"Tile\",\"action\":{\"typeId\":\"future.action \",\"schemaVersion\":1,\"parameters\":{}}}")]
    [InlineData("{\"tileId\":\"11111111-2222-3333-4444-555555555555\",\"title\":\"Tile\",\"action\":{\"typeId\":\"future.action\",\"schemaVersion\":0,\"parameters\":{}}}")]
    [InlineData("{\"tileId\":\"11111111-2222-3333-4444-555555555555\",\"title\":\"Tile\",\"action\":{\"typeId\":\"future.action\",\"schemaVersion\":1,\"parameters\":null}}")]
    [InlineData("{\"tileId\":\"11111111-2222-3333-4444-555555555555\",\"title\":\"Tile\",\"action\":{\"typeId\":\"future.action\",\"schemaVersion\":1,\"parameters\":\"text\"}}")]
    [InlineData("{\"tileId\":\"11111111-2222-3333-4444-555555555555\",\"title\":\"Tile\",\"action\":{\"typeId\":\"future.action\",\"schemaVersion\":1,\"parameters\":[]}}")]
    [InlineData("{\"tileId\":\"11111111-2222-3333-4444-555555555555\",\"title\":\"Tile\",\"action\":{\"typeId\":\"future.action\",\"schemaVersion\":1,\"parameters\":42}}")]
    public void Load_StructurallyInvalidTile_ReturnsMalformedAndPreservesTheOriginal(string tileJson)
    {
        var json = $"{{\"schemaVersion\":1,\"dashboard\":{{\"tiles\":[{tileJson}]}}}}";
        WriteExisting(json);

        var result = new ShortcutStore(ShortcutsPath).Load();

        Assert.Equal(ShortcutLoadStatus.Malformed, result.Status);
        Assert.False(result.CanSafelyReplace);
        Assert.Equal(json, File.ReadAllText(ShortcutsPath));
    }

    [Fact]
    public void Load_DuplicateTileId_ReturnsMalformedAndPreservesTheOriginal()
    {
        var duplicate = Guid.NewGuid();
        var json = $"{{\"schemaVersion\":1,\"dashboard\":{{\"tiles\":[{{\"tileId\":\"{duplicate}\",\"title\":\"First\",\"action\":{{\"typeId\":\"future.first\",\"schemaVersion\":1,\"parameters\":{{}}}}}},{{\"tileId\":\"{duplicate}\",\"title\":\"Second\",\"action\":{{\"typeId\":\"future.second\",\"schemaVersion\":1,\"parameters\":{{}}}}}}]}}}}";
        WriteExisting(json);

        var result = new ShortcutStore(ShortcutsPath).Load();

        Assert.Equal(ShortcutLoadStatus.Malformed, result.Status);
        Assert.False(result.CanSafelyReplace);
        Assert.Equal(json, File.ReadAllText(ShortcutsPath));
    }

    [Fact]
    public void Load_ReadFailure_IsNotTreatedAsFirstRun()
    {
        WriteExisting("{\"schemaVersion\":1,\"dashboard\":{\"tiles\":[]}}");
        using var handle = new FileStream(ShortcutsPath, FileMode.Open, FileAccess.Read, FileShare.None);

        var result = new ShortcutStore(ShortcutsPath).Load();

        Assert.Equal(ShortcutLoadStatus.ReadFailure, result.Status);
        Assert.False(result.CanSafelyReplace);
    }

    [Fact]
    public void Save_InvalidDocumentCannotCreateOrReplaceAnything()
    {
        var invalidDocument = new ShortcutDocument
        {
            Dashboard = new ShortcutDashboardDefinition([
                new ShortcutTileDefinition(Guid.Empty, "Invalid", new ShortcutActionSpec("future.action", 1, JsonSerializer.SerializeToElement(new { value = 1 })))
            ])
        };

        Assert.Throws<ArgumentException>(() => new ShortcutStore(ShortcutsPath).Save(invalidDocument));

        Assert.False(Directory.Exists(_testDirectory));
        Assert.False(File.Exists(ShortcutsPath));
        Assert.False(File.Exists($"{ShortcutsPath}.tmp"));
    }

    [Fact]
    public void Save_InvalidDocumentLeavesExistingValidFileUnchanged()
    {
        var store = new ShortcutStore(ShortcutsPath);
        store.Save(new ShortcutDocument { Dashboard = new ShortcutDashboardDefinition([Tile("Valid", "future.valid")]) });
        var original = File.ReadAllText(ShortcutsPath);
        var invalid = new ShortcutDocument { SchemaVersion = ShortcutDocument.CurrentSchemaVersion + 1 };

        Assert.Throws<ArgumentException>(() => store.Save(invalid));

        Assert.Equal(original, File.ReadAllText(ShortcutsPath));
    }

    [Fact]
    public void Save_AbandonedTemporaryFileDoesNotAlterCanonicalFile()
    {
        var store = new ShortcutStore(ShortcutsPath);
        store.Save(new ShortcutDocument { Dashboard = new ShortcutDashboardDefinition([Tile("Original", "future.original")]) });
        var original = File.ReadAllText(ShortcutsPath);
        File.WriteAllText($"{ShortcutsPath}.tmp", "{ incomplete");

        Assert.Equal(original, File.ReadAllText(ShortcutsPath));
    }

    [Fact]
    public void Save_RejectsNullPath()
    {
        Assert.Throws<ArgumentNullException>(() => new ShortcutStore(null!));
    }

    private void WriteExisting(string json)
    {
        Directory.CreateDirectory(_testDirectory);
        File.WriteAllText(ShortcutsPath, json);
    }

    private static ShortcutTileDefinition Tile(string title, string typeId, Guid? tileId = null) =>
        new(tileId ?? Guid.NewGuid(), title, new ShortcutActionSpec(typeId, 1, JsonSerializer.SerializeToElement(new { value = 30 })));

    public void Dispose()
    {
        if (Directory.Exists(_testDirectory))
            Directory.Delete(_testDirectory, recursive: true);
    }
}
