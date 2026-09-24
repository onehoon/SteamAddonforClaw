using SteamInputAddonforClaw.Contracts.Shortcuts;

namespace SteamInputAddonforClaw.Shortcuts;

/// <summary>Root of the persisted Shortcut document (<c>shortcuts.json</c>).</summary>
public sealed record ShortcutDocument
{
    /// <summary>The schema version this build writes and understands.</summary>
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    public ShortcutDashboardDefinition Dashboard { get; init; } = ShortcutDashboardDefinition.Empty;
}
