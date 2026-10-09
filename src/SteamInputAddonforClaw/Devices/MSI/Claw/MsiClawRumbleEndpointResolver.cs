namespace SteamInputAddonforClaw.Devices.MSI.Claw;

internal sealed record MsiClawRumbleEndpointCandidate(
    string DevicePath,
    string PnpInstanceId,
    string PhysicalIdentity,
    ushort VendorId,
    ushort ProductId,
    int InputReportLength,
    int OutputReportLength,
    ushort UsagePage,
    ushort Usage,
    bool OpenSucceeded,
    bool CapabilitiesReadSucceeded = true);

internal readonly record struct MsiClawRumbleEndpointResolution(string? DevicePath, string Reason, int OutputReportLength = 0)
{
    internal bool IsAvailable => DevicePath is not null;
}

internal interface IMsiClawRumbleEndpointResolver
{
    MsiClawRumbleEndpointResolution Resolve(MsiClawPhysicalInputIdentity identity);
}

/// <summary>Resolves only explicitly catalogued, identity-correlated MSI HID endpoints.</summary>
internal sealed class MsiClawRumbleEndpointResolver : IMsiClawRumbleEndpointResolver
{
    private readonly Func<MsiClawPhysicalInputIdentity, IReadOnlyList<MsiClawRumbleEndpointCandidate>> _catalog;

    internal MsiClawRumbleEndpointResolver(
        Func<MsiClawPhysicalInputIdentity, IReadOnlyList<MsiClawRumbleEndpointCandidate>>? catalog = null)
    {
        _catalog = catalog ?? (identity => new WindowsMsiClawRumbleEndpointCatalog().Find(identity));
    }

    public MsiClawRumbleEndpointResolution Resolve(MsiClawPhysicalInputIdentity identity)
    {
        // Do not assume a fixed OutputReportLength -- the real PID1902 gamepad HID collection on
        // proven hardware (HHC/ClawTweaks) reports OutputReportLength=32, not 64. The gamepad
        // collection is instead identified by its HID Usage (Game Pad or Joystick) on the Generic
        // Desktop usage page, matching the exact contract HHC/ClawTweaks both rely on.
        var candidates = _catalog(identity).Where(candidate =>
            candidate.VendorId == MsiClawHardware.VendorId &&
            candidate.ProductId == MsiClawHardware.DirectInputProductId).ToArray();
        var matchingRoot = candidates.Where(candidate =>
            string.Equals(candidate.PhysicalIdentity, identity.PhysicalIdentity, StringComparison.OrdinalIgnoreCase)).ToArray();
        var gamepadUsage = matchingRoot.Where(IsGamepadUsage).ToArray();
        var verified = gamepadUsage.Where(candidate =>
            candidate.CapabilitiesReadSucceeded &&
            candidate.InputReportLength == 64 &&
            candidate.OutputReportLength > 0 &&
            candidate.OpenSucceeded &&
            !string.IsNullOrWhiteSpace(candidate.DevicePath)).ToArray();
        return verified.Length switch
        {
            1 => new(verified[0].DevicePath, "VerifiedExactPid1902Endpoint", verified[0].OutputReportLength),
            0 => new(null, GetUnavailableReason(candidates, matchingRoot, gamepadUsage)),
            // Ambiguous valid gamepad candidates must never be guessed at -- fail closed for
            // rumble rather than picking one arbitrarily.
            _ => new(null, "AmbiguousEndpoints")
        };
    }

    private static bool IsGamepadUsage(MsiClawRumbleEndpointCandidate candidate) =>
        candidate.UsagePage == MsiClawHardware.DirectInputUsagePage
        && candidate.Usage is MsiClawHardware.DirectInputUsage or MsiClawHardware.DirectInputJoystickUsage;

    private static string GetUnavailableReason(
        IReadOnlyList<MsiClawRumbleEndpointCandidate> candidates,
        IReadOnlyList<MsiClawRumbleEndpointCandidate> matchingRoot,
        IReadOnlyList<MsiClawRumbleEndpointCandidate> gamepadUsage)
    {
        if (matchingRoot.Count == 0)
            return candidates.Count > 0 ? "PhysicalIdentityMismatch" : "NoPhysicalGamepadEndpoint";
        if (gamepadUsage.Count > 0 || matchingRoot.Any(candidate => !candidate.CapabilitiesReadSucceeded))
            return "PhysicalRumbleEndpointUnusable";
        if (matchingRoot.Any(candidate => candidate.UsagePage == MsiClawHardware.A2vm230ObservedControlUsagePage
            && candidate.Usage == MsiClawHardware.A2vm230ObservedControlUsage))
            return "NonGamepadUsageOnly";
        return "NoVerifiedGamepadEndpoint";
    }

}
