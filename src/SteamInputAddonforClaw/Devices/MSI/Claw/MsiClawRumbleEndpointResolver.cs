using SteamInputAddonforClaw.Diagnostics;

namespace SteamInputAddonforClaw.Devices.MSI.Claw;

internal sealed record MsiClawRumbleEndpointCandidate(
    string DevicePath,
    string PnpInstanceId,
    string PhysicalIdentity,
    ushort VendorId,
    ushort ProductId,
    int InputReportLength,
    int OutputReportLength,
    bool Writable);

internal readonly record struct MsiClawRumbleEndpointResolution(string? DevicePath, string Reason)
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
    // The underlying DeviceInformation.FindAllAsync query can transiently fail with a
    // COMException when it races the PnP device-tree churn produced by VIIPER's own Steam
    // Deck virtual device attach (observed in production telemetry immediately after PnP
    // identity resolution completes). One short retry absorbs that race without meaningfully
    // extending the routing-activation critical path on the (common) success path.
    private const int MaxCatalogAttempts = 2;
    private static readonly TimeSpan CatalogRetryDelay = TimeSpan.FromMilliseconds(150);

    private readonly Func<MsiClawPhysicalInputIdentity, IReadOnlyList<MsiClawRumbleEndpointCandidate>> _catalog;
    private readonly Action<TimeSpan> _delay;

    internal MsiClawRumbleEndpointResolver(
        Func<MsiClawPhysicalInputIdentity, IReadOnlyList<MsiClawRumbleEndpointCandidate>>? catalog = null,
        Action<TimeSpan>? delay = null)
    {
        _catalog = catalog ?? (identity => new WindowsMsiClawRumbleEndpointCatalog().Find(identity));
        _delay = delay ?? Thread.Sleep;
    }

    public MsiClawRumbleEndpointResolution Resolve(MsiClawPhysicalInputIdentity identity)
    {
        var candidates = FindCatalogCandidatesWithRetry(identity).Where(candidate =>
            candidate.VendorId == MsiClawHardware.VendorId &&
            candidate.ProductId == MsiClawHardware.DirectInputProductId &&
            candidate.InputReportLength == 64 && candidate.OutputReportLength == 64 && candidate.Writable &&
            string.Equals(candidate.PhysicalIdentity, identity.PhysicalIdentity, StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(candidate.DevicePath)).ToArray();
        return candidates.Length switch
        {
            1 => new(candidates[0].DevicePath, "VerifiedExactPid1902Endpoint"),
            0 => new(null, "NoVerifiedEndpoint"),
            _ => new(null, "AmbiguousEndpoints")
        };
    }

    private IReadOnlyList<MsiClawRumbleEndpointCandidate> FindCatalogCandidatesWithRetry(MsiClawPhysicalInputIdentity identity)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return _catalog(identity);
            }
            catch (Exception exception) when (attempt < MaxCatalogAttempts)
            {
                AppLog.Debug("Rumble", "MSI rumble endpoint catalog query failed; retrying.",
                    ("Attempt", attempt), ("Exception", exception.GetType().Name), ("HResult", exception.HResult), ("Message", exception.Message));
                _delay(CatalogRetryDelay);
            }
        }
    }
}
