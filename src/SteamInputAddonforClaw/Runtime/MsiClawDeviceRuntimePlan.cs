namespace SteamInputAddonforClaw.Runtime;

/// <summary>Pure composition decision for MSI's shared ACPI helper and independent device features.</summary>
internal sealed record MsiClawDeviceRuntimePlan(
    bool CreateSharedTransport,
    bool CreateTdpRuntime,
    bool CreateBatteryChargeLimitRuntime,
    bool CreateTdpWatchers)
{
    internal static MsiClawDeviceRuntimePlan Create(
        bool verifiedMsiClaw,
        bool supportsTdp,
        bool hasTdpPolicy,
        bool supportsBatteryChargeLimit,
        bool headless)
    {
        var createTdp = !headless && verifiedMsiClaw && supportsTdp && hasTdpPolicy;
        var createBattery = !headless && verifiedMsiClaw && supportsBatteryChargeLimit;
        return new(createTdp || createBattery, createTdp, createBattery, createTdp);
    }
}
