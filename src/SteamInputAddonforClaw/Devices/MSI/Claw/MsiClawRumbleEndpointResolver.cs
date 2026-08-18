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
    private readonly Func<MsiClawPhysicalInputIdentity, IReadOnlyList<MsiClawRumbleEndpointCandidate>> _catalog;

    internal MsiClawRumbleEndpointResolver(Func<MsiClawPhysicalInputIdentity, IReadOnlyList<MsiClawRumbleEndpointCandidate>>? catalog = null)
        => _catalog = catalog ?? (identity => new WindowsMsiClawRumbleEndpointCatalog().Find(identity));

    public MsiClawRumbleEndpointResolution Resolve(MsiClawPhysicalInputIdentity identity)
    {
        var candidates = _catalog(identity).Where(candidate =>
            candidate.VendorId == MsiClawHardware.VendorId &&
            candidate.ProductId == MsiClawHardware.DirectInputProductId &&
            candidate.InputReportLength == 64 && candidate.OutputReportLength >= 64 && candidate.Writable &&
            string.Equals(candidate.PnpInstanceId, identity.PnpInstanceId, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(candidate.PhysicalIdentity, identity.PhysicalIdentity, StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(candidate.DevicePath)).ToArray();
        return candidates.Length switch
        {
            1 => new(candidates[0].DevicePath, "VerifiedExactPid1902Endpoint"),
            0 => new(null, "NoVerifiedEndpoint"),
            _ => new(null, "AmbiguousEndpoints")
        };
    }
}
