namespace SteamInputAddonforClaw.Contracts.Frontend;

public enum FrontendIntelGpuFrequencyProbeOperation
{
    SetMaxMax,
    RestoreOriginalFrequency,
    SetTestPl1,
    RestoreOriginalPower
}

public sealed record FrontendIntelGpuFrequencyProbeSnapshot(
    bool Available,
    bool CanControl,
    string AdapterName,
    uint VendorId,
    uint DeviceId,
    double? HardwareMinMhz,
    double? HardwareMaxMhz,
    double? CurrentMinMhz,
    double? CurrentMaxMhz,
    bool CurrentMinHasExternalLimit,
    bool CurrentMaxHasExternalLimit,
    double? RequestMhz,
    double? ActualMhz,
    double? TdpMhz,
    double? EfficientMhz,
    double? Voltage,
    uint? ThrottleReasons,
    bool OriginalRangeCaptured,
    bool ModifiedByProbe,
    string? LastOperation,
    bool? LastOperationVerified,
    uint? LastNativeResult,
    string? FailureMessage,
    bool PowerAvailable,
    bool PowerCanControl,
    int? PowerDefaultLimitMw,
    int? PowerMinLimitMw,
    int? PowerMaxLimitMw,
    bool? Pl1Enabled,
    int? Pl1PowerMw,
    int? Pl1IntervalMs,
    bool? Pl2Enabled,
    int? Pl2PowerMw,
    int? Pl4AcPowerMw,
    int? Pl4DcPowerMw,
    bool OriginalPowerLimitsCaptured,
    bool PowerModifiedByProbe,
    string? LastPowerOperation,
    bool? LastPowerOperationVerified,
    uint? LastPowerNativeResult,
    string? PowerFailureMessage)
{
    public static FrontendIntelGpuFrequencyProbeSnapshot Unavailable(string reason = "Intel IGCL GPU diagnostics are unavailable.") =>
        new(false, false, "Unavailable", 0, 0, null, null, null, null, false, false,
            null, null, null, null, null, 0, false, false, null, null, null, reason,
            false, false, null, null, null, null, null, null, null, null, null, null,
            false, false, null, null, null, null);
}
