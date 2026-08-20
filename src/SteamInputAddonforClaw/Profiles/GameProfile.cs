using System.Text.Json;
using System.Text.Json.Serialization;

namespace SteamInputAddonforClaw.Profiles;

/// <summary>
/// A single per-game profile, keyed by Steam AppID in <see cref="ProfileDocument.Games"/>.
/// <see cref="DisplayName"/> is descriptive metadata only -- it may be absent, stale, or updated
/// later, and is never the identity of the entry (the dictionary key/AppID is).
/// </summary>
public sealed record GameProfile
{
    public string? DisplayName { get; init; }

    public GamePerformanceOverrides Performance { get; init; } = new();
    public GameDisplayOverrides Display { get; init; } = new();

    /// <summary>Preserves unrecognized properties directly under this game entry (as opposed to
    /// inside <see cref="Performance"/>/<see cref="Display"/>) across a load/save round trip.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; init; }
}

/// <summary>
/// Per-game field-level overrides of <see cref="DevicePerformanceSettings"/>. Every field is
/// nullable: <see langword="null"/> means "inherit the Device/global value", an explicit value
/// (including <see langword="false"/> or <c>0</c>) means "override it for this game". Empty
/// placeholder in PR1 -- fields are added here as the corresponding Device setting is introduced,
/// which is an additive change. <see cref="ExtensionData"/> preserves a future override field an
/// older/compatible build does not yet understand across a load-then-save round trip.
/// </summary>
public sealed record GamePerformanceOverrides
{
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; init; }
}

/// <summary>Per-game field-level overrides of <see cref="DeviceDisplaySettings"/>. Same
/// null-means-inherit semantic as <see cref="GamePerformanceOverrides"/>.</summary>
public sealed record GameDisplayOverrides
{
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; init; }
}
