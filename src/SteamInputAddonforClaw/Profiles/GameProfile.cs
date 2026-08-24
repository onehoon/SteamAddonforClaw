using System.Text.Json;
using System.Text.Json.Serialization;
using SteamInputAddonforClaw.Contracts.DeviceProfiles;

namespace SteamInputAddonforClaw.Profiles;

/// <summary>
/// A single per-game profile, keyed by Steam AppID in <see cref="ProfileDocument.Games"/>.
/// <see cref="DisplayName"/> is descriptive metadata only -- it may be absent, stale, or updated
/// later, and is never the identity of the entry (the dictionary key/AppID is).
/// </summary>
public sealed record GameProfile
{
    /// <summary>Owns activation of the complete per-game performance profile.</summary>
    public bool Enabled { get; init; }
    /// <summary>Desktop catalog metadata only; never grants performance ownership.</summary>
    public bool Favorite { get; init; }

    public string? DisplayName { get; init; }

    public GamePerformanceOverrides Performance { get; init; } = new();
    public GameDisplayOverrides Display { get; init; } = new();

    /// <summary>Preserves unrecognized properties directly under this game entry (as opposed to
    /// inside <see cref="Performance"/>/<see cref="Display"/>) across a load/save round trip.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; init; }
}

/// <summary>
/// Complete per-game performance settings. When the owning <see cref="GameProfile.Enabled"/>
/// switch is true, both CPU Boost and TDP are populated; they are not independent enabled
/// switches and do not use null to inherit individual fields from Device. <see cref="ExtensionData"/>
/// preserves future fields across a load-then-save round trip.
/// </summary>
public sealed record GamePerformanceOverrides
{
    public GameCpuBoostSettings? CpuBoost { get; init; }
    public GameTdpSettings? Tdp { get; init; }
    public GamePowerModeSettings? PowerMode { get; init; }
    public GameFpsLimitSettings? FpsLimit { get; init; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; init; }
}

public sealed record GameFpsLimitSettings
{
    public bool Enabled { get; init; }
    public required int AcFps { get; init; }
    public required int DcFps { get; init; }
    [JsonExtensionData] public Dictionary<string, JsonElement>? ExtensionData { get; init; }
}

public sealed record GamePowerModeSettings
{
    public required WindowsPowerMode Ac { get; init; }
    public required WindowsPowerMode Dc { get; init; }
    [JsonExtensionData] public Dictionary<string, JsonElement>? ExtensionData { get; init; }
}

public sealed record GameCpuBoostSettings
{
    public required CpuBoostMode Ac { get; init; }
    public required CpuBoostMode Dc { get; init; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; init; }
}

public sealed record GameTdpSettings
{
    public required TdpPowerPair Ac { get; init; }
    public required TdpPowerPair Dc { get; init; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; init; }
}

/// <summary>Per-game display settings, retained as an independent additive section.</summary>
public sealed record GameDisplayOverrides
{
    public GameDisplayResolution? Resolution { get; init; }
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; init; }
}

public sealed record GameDisplayResolution
{
    public required int Width { get; init; }
    public required int Height { get; init; }
}
