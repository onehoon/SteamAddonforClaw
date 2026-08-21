namespace SteamInputAddonforClaw.Devices.MSI.Claw;

/// <summary>
/// The routing-safety invariant for the owned PID_1902 physical-input session: if routing
/// currently owns that session, it must remain alive for the lifetime of active routing. Pure
/// decision logic, independent of DirectInput/hardware, so it can be exercised deterministically.
/// </summary>
internal static class MsiClawPhysicalInputFaultPolicy
{
    internal const string PhysicalInputSessionLostReason = "PhysicalInputSessionLost";
    internal const string ExternalNativeTakeoverReason = "ExternalNativeTakeover";

    /// <summary>
    /// An owned physical-input session that stops for any reason other than an expected
    /// <see cref="MsiClawInputStopReason.Stopped"/> (normal cancellation via routing rollback) is a
    /// fatal routing fault. A stop before routing ownership was ever committed is an ordinary
    /// pipeline-entry failure, not a runtime fault.
    /// </summary>
    internal static bool IsFatal(MsiClawInputStopReason stopReason, bool ownedSessionIdentityPresent) =>
        ownedSessionIdentityPresent && stopReason != MsiClawInputStopReason.Stopped;
}
