using System.Text.Json;

namespace SteamInputAddonforClaw.Contracts.Shortcuts;

/// <summary>Opaque, versioned parameters for one Shortcut action family.</summary>
/// <remarks>
/// Retained values that originate from a <see cref="JsonDocument"/> must use
/// <see cref="JsonElement.Clone"/> before that document is disposed.
/// </remarks>
public sealed record ShortcutActionSpec(
    string TypeId,
    int SchemaVersion,
    JsonElement Parameters);

/// <summary>One user-visible Shortcut definition. Collection order supplies layout order.</summary>
public sealed record ShortcutTileDefinition(
    Guid TileId,
    string Title,
    ShortcutActionSpec Action);

/// <summary>The ordered Shortcut dashboard definition.</summary>
public sealed record ShortcutDashboardDefinition(
    IReadOnlyList<ShortcutTileDefinition> Tiles)
{
    public static ShortcutDashboardDefinition Empty { get; } = new([]);
}

/// <summary>Pure structural validation for Shortcut definitions.</summary>
public static class ShortcutDefinitionValidation
{
    /// <summary>Returns null when the definition is structurally valid; otherwise returns a short reason.</summary>
    public static string? Validate(ShortcutDashboardDefinition? dashboard)
    {
        if (dashboard is null)
            return "dashboard is null";
        if (dashboard.Tiles is null)
            return "tile collection is null";

        var tileIds = new HashSet<Guid>();
        for (var index = 0; index < dashboard.Tiles.Count; index++)
        {
            var tile = dashboard.Tiles[index];
            if (tile is null)
                return $"tile at index {index} is null";
            if (tile.TileId == Guid.Empty)
                return $"tile at index {index} has an empty TileId";
            if (!tileIds.Add(tile.TileId))
                return $"tile ID {tile.TileId} is duplicated";
            if (string.IsNullOrWhiteSpace(tile.Title))
                return $"tile {tile.TileId} has a blank title";

            var action = tile.Action;
            if (action is null)
                return $"tile {tile.TileId} has a null action";
            if (string.IsNullOrWhiteSpace(action.TypeId))
                return $"tile {tile.TileId} has a blank action TypeId";
            if (action.TypeId.Trim() != action.TypeId)
                return $"tile {tile.TileId} action TypeId has leading or trailing whitespace";
            if (action.SchemaVersion < 1)
                return $"tile {tile.TileId} action schema version is invalid";
            if (action.Parameters.ValueKind != JsonValueKind.Object)
                return $"tile {tile.TileId} action parameters must be a JSON object";
        }

        return null;
    }
}
