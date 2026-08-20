using SteamInputAddonforClaw.Contracts.DeviceProfiles;

namespace SteamInputAddonforClaw.Profiles.Performance;

/// <summary>One side (AC or DC) of a Windows CPU Boost read: either a known/mapped
/// <see cref="CpuBoostMode"/>, or <see cref="Unavailable"/> when Windows could not be queried, or
/// <see cref="Unknown"/> when Windows returned a raw value this build does not map to a supported
/// mode (work order section 17 -- never silently normalized to a known mode).</summary>
public enum CpuBoostReadStatus
{
    Known,
    Unknown,
    Unavailable
}

/// <summary>A single AC or DC current-Windows-value read.</summary>
public readonly record struct CpuBoostSideReading(CpuBoostReadStatus Status, CpuBoostMode? Mode)
{
    public static readonly CpuBoostSideReading Unavailable = new(CpuBoostReadStatus.Unavailable, null);

    public static CpuBoostSideReading Known(CpuBoostMode mode) => new(CpuBoostReadStatus.Known, mode);
    public static CpuBoostSideReading UnknownValue() => new(CpuBoostReadStatus.Unknown, null);
}

/// <summary>Current Windows AC/DC CPU Boost state for the active power scheme, as actually
/// observed -- independent of whatever the Addon's persisted desired state says.</summary>
public readonly record struct CpuBoostSystemState(bool Succeeded, CpuBoostSideReading Ac, CpuBoostSideReading Dc, string? FailureMessage)
{
    public static CpuBoostSystemState Failure(string message) => new(false, CpuBoostSideReading.Unavailable, CpuBoostSideReading.Unavailable, message);
}

/// <summary>Outcome of applying (writing) one or both sides to Windows.</summary>
public readonly record struct CpuBoostApplyResult(bool AcSucceeded, bool DcSucceeded, string? FailureMessage)
{
    public static readonly CpuBoostApplyResult NoOp = new(true, true, null);
    public bool Succeeded => AcSucceeded && DcSucceeded;
}

/// <summary>
/// Narrow seam around the actual Windows PowrProf mutation boundary (work order section 11). This
/// exists only so unit tests can substitute a fake backend and never touch the real machine's
/// active power scheme -- it is not the start of a generic settings/power framework, and no other
/// future Device/Profile setting should be routed through this interface.
/// </summary>
internal interface ICpuBoostPowerPolicy
{
    /// <summary>Reads the current AC/DC CPU Boost values for the active power scheme. Never
    /// mutates anything.</summary>
    CpuBoostSystemState Read();

    /// <summary>Writes only the non-null side(s) supplied, then (if anything was written)
    /// re-activates the active scheme so Windows applies the change. A <see langword="null"/> side
    /// is left completely untouched -- never read-then-written-back for symmetry (work order
    /// section 16).</summary>
    CpuBoostApplyResult Apply(CpuBoostMode? ac, CpuBoostMode? dc);
}
