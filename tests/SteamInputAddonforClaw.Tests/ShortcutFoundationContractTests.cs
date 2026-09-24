using System.Text.Json;
using SteamInputAddonforClaw.Contracts.Frontend;
using SteamInputAddonforClaw.Contracts.Shortcuts;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class ShortcutFoundationContractTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(4)]
    [InlineData(5)]
    public void Dashboard_accepts_any_ordered_tile_count(int count)
    {
        var tiles = Enumerable.Range(0, count)
            .Select(index => Tile(Guid.NewGuid(), $"Tile {index}", new($"future.action.{index}", 1, ObjectParameters())))
            .ToArray();

        Assert.Null(ShortcutDefinitionValidation.Validate(new ShortcutDashboardDefinition(tiles)));
    }

    [Fact]
    public void Empty_dashboard_is_valid_and_has_no_fixed_slot_count()
    {
        Assert.Empty(ShortcutDashboardDefinition.Empty.Tiles);
        Assert.Null(ShortcutDefinitionValidation.Validate(ShortcutDashboardDefinition.Empty));
    }

    [Fact]
    public void Tile_identity_survives_reorder_without_a_second_layout_field()
    {
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();
        var first = Tile(firstId, "First", new("future.first", 1, ObjectParameters()));
        var second = Tile(secondId, "Second", new("future.second", 1, ObjectParameters()));
        var reordered = new ShortcutDashboardDefinition([second, first]);

        Assert.Equal([secondId, firstId], reordered.Tiles.Select(tile => tile.TileId));
        Assert.DoesNotContain(typeof(ShortcutTileDefinition).GetProperties(), property =>
            property.Name is "Row" or "Column" or "X" or "Y" or "Order" or "Index");
    }

    [Fact]
    public void Future_action_examples_and_unknown_types_are_structurally_valid()
    {
        var actions = new[]
        {
            new ShortcutActionSpec("addon.tdp-preset", 1, JsonSerializer.SerializeToElement(new { watts = 30 })),
            new ShortcutActionSpec("system.executable", 1, JsonSerializer.SerializeToElement(new { path = @"C:\Tools\Tool.exe", arguments = "--example" })),
            new ShortcutActionSpec("system.powershell", 1, JsonSerializer.SerializeToElement(new { script = "Write-Output 'hello'" })),
            new ShortcutActionSpec("future.vendor.new-action", 7, JsonSerializer.SerializeToElement(new { anything = "value" })),
        };

        foreach (var (action, index) in actions.Select((action, index) => (action, index)))
            Assert.Null(ShortcutDefinitionValidation.Validate(new ShortcutDashboardDefinition(
                [Tile(Guid.NewGuid(), $"Action {index}", action)])));
    }

    [Fact]
    public void Validation_rejects_null_empty_duplicate_and_malformed_structure()
    {
        var validAction = new ShortcutActionSpec("future.action", 1, ObjectParameters());
        var validTile = Tile(Guid.NewGuid(), "Valid", validAction);

        Assert.NotNull(ShortcutDefinitionValidation.Validate(null));
        Assert.NotNull(ShortcutDefinitionValidation.Validate(new ShortcutDashboardDefinition(null!)));
        Assert.NotNull(ShortcutDefinitionValidation.Validate(new ShortcutDashboardDefinition([null!])));
        Assert.NotNull(ShortcutDefinitionValidation.Validate(new ShortcutDashboardDefinition([
            Tile(Guid.Empty, "Empty ID", validAction)])));
        Assert.NotNull(ShortcutDefinitionValidation.Validate(new ShortcutDashboardDefinition([
            validTile, validTile])));
        Assert.NotNull(ShortcutDefinitionValidation.Validate(new ShortcutDashboardDefinition([
            Tile(validTile.TileId, " ", validAction)])));
        Assert.NotNull(ShortcutDefinitionValidation.Validate(new ShortcutDashboardDefinition([
            Tile(validTile.TileId, "Missing action", null!)])));
    }

    [Fact]
    public void Validation_rejects_invalid_action_identity_and_schema_version()
    {
        var id = Guid.NewGuid();

        Assert.NotNull(ShortcutDefinitionValidation.Validate(new ShortcutDashboardDefinition([
            Tile(id, "Blank type", new(" ", 1, ObjectParameters()))])));
        Assert.NotNull(ShortcutDefinitionValidation.Validate(new ShortcutDashboardDefinition([
            Tile(id, "Trimmed type", new(" future.action", 1, ObjectParameters()))])));
        Assert.NotNull(ShortcutDefinitionValidation.Validate(new ShortcutDashboardDefinition([
            Tile(id, "Invalid version", new("future.action", 0, ObjectParameters()))])));
    }

    [Fact]
    public void Validation_requires_object_parameters_but_does_not_reject_unknown_type_ids()
    {
        var invalidParameters = new[]
        {
            default(JsonElement),
            JsonSerializer.SerializeToElement("text"),
            JsonSerializer.SerializeToElement(42),
            JsonSerializer.SerializeToElement(new[] { 1, 2 }),
            JsonNullParameters(),
        };

        foreach (var parameters in invalidParameters)
        {
            var result = ShortcutDefinitionValidation.Validate(new ShortcutDashboardDefinition([
                Tile(Guid.NewGuid(), "Invalid parameters", new("future.unknown", 1, parameters))]));
            Assert.NotNull(result);
        }

        var validUnknown = new ShortcutActionSpec("Future.Action", 7, ObjectParameters());
        Assert.Null(ShortcutDefinitionValidation.Validate(new ShortcutDashboardDefinition([
            Tile(Guid.NewGuid(), "Unknown action", validUnknown)])));
        Assert.Equal("Future.Action", validUnknown.TypeId);
    }

    [Fact]
    public void Frontend_projection_is_sanitized_and_available_empty_is_distinct_from_unavailable()
    {
        var tile = new FrontendShortcutTile(
            Guid.NewGuid(),
            "TDP",
            "30 W",
            FrontendShortcutTileState.Active,
            true);
        var availableEmpty = new FrontendShortcutDashboardSnapshot(true, []);
        var unavailable = FrontendShortcutDashboardSnapshot.Unavailable("Runtime unavailable");

        Assert.True(availableEmpty.Available);
        Assert.Empty(availableEmpty.Tiles);
        Assert.False(unavailable.Available);
        Assert.Empty(unavailable.Tiles);
        Assert.Equal("Runtime unavailable", unavailable.FailureMessage);
        Assert.Equal(tile, new FrontendShortcutDashboardSnapshot(true, [tile]).Tiles.Single());

        var propertyNames = typeof(FrontendShortcutTile).GetProperties().Select(property => property.Name);
        Assert.DoesNotContain("Action", propertyNames);
        Assert.DoesNotContain("Parameters", propertyNames);
        Assert.DoesNotContain("ParametersJson", propertyNames);
        Assert.DoesNotContain("Script", propertyNames);
        Assert.DoesNotContain("ExecutablePath", propertyNames);
        Assert.DoesNotContain("Arguments", propertyNames);
    }

    [Fact]
    public void Production_source_contains_no_legacy_fixed_slot_contract()
    {
        var root = RepositoryRoot();
        var source = string.Join(
            Environment.NewLine,
            Directory.EnumerateFiles(Path.Combine(root, "src"), "*.cs", SearchOption.AllDirectories)
                .Select(File.ReadAllText));

        Assert.DoesNotContain("AddonQuickSettingsShortcutSlotId", source);
        Assert.DoesNotContain("AddonQuickSettingsShortcutContract", source);
    }

    private static ShortcutTileDefinition Tile(Guid id, string title, ShortcutActionSpec action) => new(id, title, action);

    private static JsonElement ObjectParameters() => JsonSerializer.SerializeToElement(new { enabled = true });

    private static JsonElement JsonNullParameters()
    {
        using var document = JsonDocument.Parse("null");
        return document.RootElement.Clone();
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "README.md")))
            directory = directory.Parent;

        Assert.NotNull(directory);
        return directory!.FullName;
    }
}
