namespace SteamInputAddonforClaw.Devices.Abstractions;

/// <summary>
/// Immutable model-level eligibility for composing or exposing existing features. This does not
/// assert that hardware is present, healthy, writable, or authorized at runtime.
/// </summary>
internal sealed record HandheldModelFeatures(
    bool SupportsTdp = false,
    bool SupportsBatteryChargeLimit = false,
    bool SupportsControllerVibrationStrength = false,
    bool SupportsControllerLed = false,
    bool SupportsMotion = false,
    bool SupportsIntelFpsLimit = false,
    bool SupportsIntelGpuMinimumClock = false)
{
    internal static readonly HandheldModelFeatures None = new();
}
