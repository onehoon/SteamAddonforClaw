namespace SteamInputAddonforClaw.Contracts.DeviceProfiles;

/// <summary>
/// The complete Windows 11 CPU Boost mode set exposed by this product, mirroring the
/// PROCESSOR_PERFORMANCE_BOOST_POLICY / PERFBOOSTMODE values Windows itself defines for the
/// <c>SUB_PROCESSOR</c> power subgroup. Deliberately not a Boolean -- every value is a distinct,
/// user-selectable mode; <see cref="Disabled"/> is mode <c>0</c>, not "unmanaged" (that is
/// represented by a <see langword="null"/> <c>CpuBoostMode?</c>, not by this enum).
///
/// Lives in Contracts (not <c>SteamInputAddonforClaw.Profiles.Performance</c>, where it
/// originated) so the Runtime, persistence, and every frontend (in-process and Named Pipe) share
/// exactly one mode vocabulary -- moved, not duplicated. The numeric values, member names, and
/// persisted <c>profiles.json</c> mode names (System.Text.Json serializes enums by member name,
/// which is unaffected by namespace) are unchanged by this move.
/// </summary>
public enum CpuBoostMode
{
    Disabled = 0,
    Enabled = 1,
    Aggressive = 2,
    EfficientEnabled = 3,
    EfficientAggressive = 4,
    AggressiveAtGuaranteed = 5,
    EfficientAggressiveAtGuaranteed = 6
}
