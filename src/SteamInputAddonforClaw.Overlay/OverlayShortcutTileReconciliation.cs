using SteamInputAddonforClaw.Contracts.Frontend;

namespace SteamInputAddonforClaw.Overlay;

/// <summary>Describes the minimal changes between two ordered Shortcut tile snapshots.</summary>
internal sealed record OverlayShortcutTileReconciliation(
    IReadOnlyList<Guid> AddedTileIds,
    IReadOnlyList<Guid> RemovedTileIds,
    IReadOnlyList<Guid> UpdatedTileIds,
    IReadOnlyList<Guid> RepositionedTileIds)
{
    internal bool IsUnchanged => AddedTileIds.Count == 0
        && RemovedTileIds.Count == 0
        && UpdatedTileIds.Count == 0
        && RepositionedTileIds.Count == 0;

    internal static OverlayShortcutTileReconciliation Between(
        IReadOnlyList<FrontendShortcutTile> previous,
        IReadOnlyList<FrontendShortcutTile> current)
    {
        var previousById = previous.ToDictionary(tile => tile.TileId);
        var previousIndices = previous
            .Select((tile, index) => (tile.TileId, index))
            .ToDictionary(item => item.TileId, item => item.index);
        var currentIds = current.Select(tile => tile.TileId).ToHashSet();

        var added = current
            .Where(tile => !previousById.ContainsKey(tile.TileId))
            .Select(tile => tile.TileId)
            .ToArray();
        var removed = previous
            .Where(tile => !currentIds.Contains(tile.TileId))
            .Select(tile => tile.TileId)
            .ToArray();
        var updated = current
            .Where(tile => previousById.TryGetValue(tile.TileId, out var oldTile) && oldTile != tile)
            .Select(tile => tile.TileId)
            .ToArray();
        var repositioned = current
            .Select((tile, index) => (tile.TileId, index))
            .Where(item => previousIndices.TryGetValue(item.TileId, out var oldIndex) && oldIndex != item.index)
            .Select(item => item.TileId)
            .ToArray();

        return new(added, removed, updated, repositioned);
    }
}
