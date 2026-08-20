using System.Text.Json;
using System.Text.Json.Serialization;

namespace SteamInputAddonforClaw.Profiles;

/// <summary>
/// Device-wide (global) Device/Profile settings, grouped into typed category sections. Deliberately
/// structural/near-empty in PR1: this PR establishes the persistence foundation only, not any
/// actual Performance/Display setting semantics -- see <see cref="DevicePerformanceSettings"/>/
/// <see cref="DeviceDisplaySettings"/>. Later PRs add typed fields (e.g. CPU Boost, TDP) to the
/// category records below; that is an additive change to this document, not a storage redesign.
/// </summary>
public sealed record DeviceSettings
{
    public DevicePerformanceSettings Performance { get; init; } = new();
    public DeviceDisplaySettings Display { get; init; } = new();

    /// <summary>Preserves unrecognized properties directly under <c>device</c> (as opposed to
    /// inside <see cref="Performance"/>/<see cref="Display"/>) across a load/save round trip --
    /// see the identical rationale on those two types and on <see cref="ProfileDocument.ExtensionData"/>.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; init; }
}

/// <summary>Device-wide performance category. Empty placeholder in PR1 -- a later PR adds real
/// fields here (e.g. CPU Boost, TDP watts) as an additive change. <see cref="ExtensionData"/>
/// preserves a future field an older/compatible build does not yet understand across a
/// load-then-save round trip, so that build does not silently erase it.</summary>
public sealed record DevicePerformanceSettings
{
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; init; }
}

/// <summary>Device-wide display category. Empty placeholder in PR1, additive later. See
/// <see cref="DevicePerformanceSettings.ExtensionData"/> for why this carries the same sidecar.</summary>
public sealed record DeviceDisplaySettings
{
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; init; }
}
