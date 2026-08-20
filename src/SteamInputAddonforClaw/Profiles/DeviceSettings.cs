using System.Text.Json;
using System.Text.Json.Serialization;
using SteamInputAddonforClaw.Contracts.DeviceProfiles;

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

/// <summary>Device-wide performance category. <see cref="CpuBoost"/> is the first real field added
/// here (PR276); TDP and other performance fields may follow additively later.
/// <see cref="ExtensionData"/> preserves a future field an older/compatible build does not yet
/// understand across a load-then-save round trip, so that build does not silently erase it.</summary>
public sealed record DevicePerformanceSettings
{
    /// <summary><see langword="null"/> means Device CPU Boost is uninitialized -- no complete AC/DC
    /// baseline has ever been established yet. There is no hardcoded Addon default: a non-null
    /// <see cref="DeviceCpuBoostSettings"/> only ever appears here as a direct result of a startup
    /// bootstrap or an explicit user mutation, both of which establish AC and DC together. See
    /// <see cref="DeviceCpuBoostSettings"/> for the current initialized/uninitialized semantics.</summary>
    public DeviceCpuBoostSettings? CpuBoost { get; init; }

    /// <summary><see langword="null"/> means Device TDP has not been initialized or configured by
    /// the Addon. A non-null value always contains complete AC and DC power pairs.</summary>
    public DeviceTdpSettings? Tdp { get; init; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; init; }
}

/// <summary>Persisted Device-wide TDP configuration. Values are desired watts only; hardware
/// protocol details belong to the later MSI TDP backend.</summary>
public sealed record DeviceTdpSettings
{
    public bool Enabled { get; init; }
    public TdpPowerPair Ac { get; init; } = new();
    public TdpPowerPair Dc { get; init; } = new();

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; init; }
}

/// <summary>Independent PL1/PL2 values for one power rail.</summary>
public sealed record TdpPowerPair
{
    public int Pl1Watts { get; init; }
    public int Pl2Watts { get; init; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; init; }
}

/// <summary>Persisted desired CPU Boost state. <c>Ac</c>/<c>Dc</c> stay nullable in storage only for
/// backward compatibility with pre-initialization/legacy-partial documents; the meaning of
/// <see langword="null"/> is "initialization incomplete", not "intentionally unmanaged side". A
/// successfully initialized Device CPU Boost configuration always has both a concrete
/// <see cref="Ac"/> and a concrete <see cref="Dc"/> value (including <see cref="CpuBoostMode.Disabled"/>,
/// which is mode <c>0</c>, a real user-selectable mode). There is no per-side ownership state.
///
/// <see cref="Enabled"/> (Device CPU Boost Toggle addendum) is a SEPARATE concept from AC/DC
/// nullability: it controls only whether the Addon's Device/global apply path is currently allowed
/// to apply these AC/DC values -- it is not an application-wide CPU Boost master switch, and a
/// future Game Profile CPU Boost path is not gated by it. Turning it off never clears/erases Ac/Dc:
/// the saved selections remain so the feature can be re-enabled and immediately re-apply them.</summary>
public sealed record DeviceCpuBoostSettings
{
    /// <summary>Defaults to <see langword="true"/> so a pre-toggle PR276 schema-v1 document (which
    /// persisted <c>ac</c>/<c>dc</c> but has no <c>enabled</c> property at all) deserializes as ON,
    /// preserving its previously-active behavior -- the property being ABSENT means "not yet
    /// expressed, so ON" exactly like the toggle's own initial-ON policy on first-run bootstrap. Only
    /// an explicit persisted <c>"enabled": false</c> means OFF.</summary>
    public bool Enabled { get; init; } = true;
    public CpuBoostMode? Ac { get; init; }
    public CpuBoostMode? Dc { get; init; }

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
