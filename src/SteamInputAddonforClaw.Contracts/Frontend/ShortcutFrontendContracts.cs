namespace SteamInputAddonforClaw.Contracts.Frontend;

/// <summary>Presentation-only state for a projected Shortcut tile.</summary>
public enum FrontendShortcutTileState
{
    Neutral,
    Active,
    Inactive,
    Unavailable,
}

/// <summary>Sanitized read-only Shortcut data needed by a frontend renderer.</summary>
public sealed record FrontendShortcutTile(
    Guid TileId,
    string Title,
    string? StatusText,
    FrontendShortcutTileState State,
    bool Enabled);

/// <summary>Ordered Shortcut presentation published by the Runtime.</summary>
public sealed record FrontendShortcutDashboardSnapshot(
    bool Available,
    IReadOnlyList<FrontendShortcutTile> Tiles,
    string? FailureMessage = null)
{
    public static FrontendShortcutDashboardSnapshot Unavailable(string? message = null) =>
        new(false, [], message);
}
