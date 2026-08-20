using System.Text.Json;
using System.Text.Json.Serialization;

namespace SteamInputAddonforClaw.Profiles;

/// <summary>
/// Root of the persisted Device/Profile document (<c>profiles.json</c>). This is a separate
/// storage domain from the existing operational <c>settings.json</c> (routing/OEM1/startup/log
/// level) -- see docs on <see cref="ProfileStore"/>. Runtime-owned: this type and its store carry
/// no dependency on WinUI, QamHost, or any frontend connection, so Device/Profile persistence
/// works whether or not a UI ever starts.
/// </summary>
public sealed record ProfileDocument
{
    /// <summary>The schema version this build writes and the highest version it understands how
    /// to read. Bump only when introducing an actual breaking change; purely additive fields do
    /// not require a bump.</summary>
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    public DeviceSettings Device { get; init; } = new();

    /// <summary>Per-game profiles keyed by Steam AppID (as a string, matching the persisted JSON
    /// object-map shape) -- the numeric AppID is the authoritative identity; <see cref="GameProfile.DisplayName"/>
    /// is descriptive metadata only.</summary>
    public Dictionary<string, GameProfile> Games { get; init; } = [];

    /// <summary>Preserves genuinely unrecognized top-level JSON properties across a load/save
    /// round trip (e.g. a field a newer build added that this build does not understand yet), so
    /// an older/compatible build editing an unrelated value does not silently destroy it. Uses
    /// System.Text.Json's built-in extension-data support rather than a custom JSON DOM
    /// synchronization engine. The same sidecar is applied to every intentionally extensible
    /// nested section (<see cref="DeviceSettings"/>, <see cref="DevicePerformanceSettings"/>,
    /// <see cref="DeviceDisplaySettings"/>, <see cref="GameProfile"/>,
    /// <see cref="GamePerformanceOverrides"/>, <see cref="GameDisplayOverrides"/>) so a future
    /// additive field anywhere in the document round-trips, not just at the root.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; init; }
}
