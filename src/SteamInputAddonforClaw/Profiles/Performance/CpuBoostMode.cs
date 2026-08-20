namespace SteamInputAddonforClaw.Profiles.Performance;

/// <summary>
/// The complete Windows 11 CPU Boost mode set exposed by this product, mirroring the
/// PROCESSOR_PERFORMANCE_BOOST_POLICY / PERFBOOSTMODE values Windows itself defines for the
/// <c>SUB_PROCESSOR</c> power subgroup. Deliberately not a Boolean -- every value is a distinct,
/// user-selectable mode; <see cref="Disabled"/> is mode <c>0</c>, not "unmanaged" (that is
/// represented by a <see langword="null"/> <c>CpuBoostMode?</c>, not by this enum).
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
