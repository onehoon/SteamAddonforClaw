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
}

/// <summary>Device-wide performance category. Empty placeholder in PR1 -- a later PR adds real
/// fields here (e.g. CPU Boost, TDP watts) as an additive change.</summary>
public sealed record DevicePerformanceSettings;

/// <summary>Device-wide display category. Empty placeholder in PR1, additive later.</summary>
public sealed record DeviceDisplaySettings;
