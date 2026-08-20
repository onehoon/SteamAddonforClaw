using SteamInputAddonforClaw.Diagnostics;

namespace SteamInputAddonforClaw.Profiles.Performance;

/// <summary>Result of an explicit AC/DC mutation request (<see cref="CpuBoostRuntime.SetDeviceCpuBoostAc"/>/
/// <see cref="CpuBoostRuntime.SetDeviceCpuBoostDc"/>).</summary>
public enum CpuBoostMutationOutcome
{
    Succeeded,

    /// <summary>Persistence failed before any Windows mutation was attempted -- zero Windows
    /// writes occurred, and the previous in-memory desired state remains authoritative (work order
    /// section 15).</summary>
    PersistenceFailed,

    /// <summary>Persistence succeeded and the new desired state is committed/durable, but the
    /// Windows PowrProf write failed. The new value remains persisted; no rollback to another mode
    /// (work order section 15).</summary>
    ApplyFailed
}

public readonly record struct CpuBoostMutationResult(CpuBoostMutationOutcome Outcome, string? FailureMessage)
{
    public bool Succeeded => Outcome == CpuBoostMutationOutcome.Succeeded;
}

/// <summary>Minimal, narrowly CPU-Boost-specific runtime snapshot (work order section 19) -- not a
/// general Device/Profile status framework. Distinguishes the actual current Windows AC/DC values
/// from the Addon's persisted/managed desired values, which is required so a future UI can show the
/// real Windows value even on an unmanaged side.</summary>
public sealed record CpuBoostRuntimeSnapshot(
    CpuBoostSideReading AcCurrent,
    CpuBoostSideReading DcCurrent,
    CpuBoostMode? AcDesired,
    CpuBoostMode? DcDesired,
    bool PersistenceWritable,
    string? LastFailure)
{
    public static readonly CpuBoostRuntimeSnapshot Empty = new(
        CpuBoostSideReading.Unavailable, CpuBoostSideReading.Unavailable, null, null, PersistenceWritable: false, LastFailure: null);
}

/// <summary>
/// The CPU Boost Device/Profile Runtime owner: desired/current state, startup reconcile, and
/// field-level AC/DC mutation with persist-then-apply ordering (work order sections 10/12/15).
///
/// Deliberately a sibling capability of Routing/OEM1, not a member of either -- it depends only on
/// <see cref="ProfileStore"/> (persistence) and <see cref="ICpuBoostPowerPolicy"/> (the Windows
/// mutation boundary), and is fully constructible/usable with Routing, OEM1, Steam, and the
/// frontend entirely absent (work order sections 0/2/31).
/// </summary>
internal sealed class CpuBoostRuntime
{
    private readonly ProfileStore _profileStore;
    private readonly ICpuBoostPowerPolicy _powerPolicy;
    private readonly Lock _sync = new();

    private ProfileDocument _document = new();
    private bool _persistenceWritable;
    private CpuBoostRuntimeSnapshot _snapshot = CpuBoostRuntimeSnapshot.Empty;

    internal CpuBoostRuntime(ProfileStore profileStore, ICpuBoostPowerPolicy? powerPolicy = null)
    {
        _profileStore = profileStore ?? throw new ArgumentNullException(nameof(profileStore));
        _powerPolicy = powerPolicy ?? new WindowsCpuBoostPowerPolicy();
    }

    internal CpuBoostRuntimeSnapshot Snapshot { get { lock (_sync) return _snapshot; } }

    /// <summary>
    /// Runs once during headless Runtime startup (work order section 12): loads the profile
    /// document, reads the current Windows AC/DC values, and -- only when the load was
    /// <see cref="ProfileLoadStatus.Loaded"/> -- applies the non-null managed side(s). A
    /// <see cref="ProfileLoadStatus.NotFound"/>/<see cref="ProfileLoadStatus.Malformed"/>/
    /// <see cref="ProfileLoadStatus.UnsupportedSchemaVersion"/>/<see cref="ProfileLoadStatus.ReadFailure"/>
    /// result never writes Windows: the freshly-constructed default document those statuses carry
    /// has no desired CPU Boost state to apply, and this method never treats "unreliable" the same
    /// as "no preference" by writing anything on their behalf (work order section 13). One
    /// reconciliation attempt only -- no retry loop (section 14).
    /// </summary>
    internal void StartupReconcile()
    {
        var loadResult = _profileStore.Load();
        lock (_sync)
        {
            _document = loadResult.Document;
            _persistenceWritable = loadResult.CanSafelyReplace;
        }

        AppLog.Debug("Profiles.CpuBoost", "CPU Boost startup reconcile started.", ("ProfileStatus", loadResult.Status));

        var desired = loadResult.Status == ProfileLoadStatus.Loaded
            ? loadResult.Document.Device.Performance.CpuBoost
            : null;

        ReconcileWindows(desired?.Ac, desired?.Dc, contextLabel: "startup reconcile");
    }

    /// <summary>Sets the persisted/desired AC CPU Boost mode and applies it to Windows. DC is left
    /// completely untouched.</summary>
    internal CpuBoostMutationResult SetDeviceCpuBoostAc(CpuBoostMode mode) => Mutate(mode, mutateAc: true);

    /// <summary>Sets the persisted/desired DC CPU Boost mode and applies it to Windows. AC is left
    /// completely untouched.</summary>
    internal CpuBoostMutationResult SetDeviceCpuBoostDc(CpuBoostMode mode) => Mutate(mode, mutateAc: false);

    private CpuBoostMutationResult Mutate(CpuBoostMode mode, bool mutateAc)
    {
        ProfileDocument updatedDocument;
        ProfileDocument previousDocument;
        lock (_sync)
        {
            previousDocument = _document;
            var previousCpuBoost = previousDocument.Device.Performance.CpuBoost ?? new DeviceCpuBoostSettings();
            var updatedCpuBoost = mutateAc ? previousCpuBoost with { Ac = mode } : previousCpuBoost with { Dc = mode };
            var updatedPerformance = previousDocument.Device.Performance with { CpuBoost = updatedCpuBoost };
            var updatedDevice = previousDocument.Device with { Performance = updatedPerformance };
            updatedDocument = previousDocument with { Device = updatedDevice };
        }

        try
        {
            _profileStore.Save(updatedDocument);
        }
        catch (Exception exception)
        {
            // Persistence failed before any Windows mutation was attempted: zero Windows writes,
            // and the previous in-memory desired state remains authoritative (work order section
            // 15) -- _document is intentionally left unchanged.
            AppLog.Error("Profiles.CpuBoost", "CPU Boost persistence failed; the Windows setting was not touched.", exception, ("Side", mutateAc ? "AC" : "DC"));
            return new CpuBoostMutationResult(CpuBoostMutationOutcome.PersistenceFailed, exception.Message);
        }

        lock (_sync)
        {
            _document = updatedDocument;
            _persistenceWritable = true;
        }

        var applyResult = _powerPolicy.Apply(mutateAc ? mode : null, mutateAc ? null : mode);
        RefreshSnapshotAfterApply(updatedDocument.Device.Performance.CpuBoost, applyResult);

        if (!applyResult.Succeeded)
        {
            // Persistence succeeded (the new desired value is durable); the Windows apply failed.
            // Keep the persisted desired state -- no rollback to another/default mode (section 15).
            AppLog.Warn("Profiles.CpuBoost", "CPU Boost desired value was persisted but the Windows apply failed.", null,
                ("Side", mutateAc ? "AC" : "DC"), ("Mode", mode));
            return new CpuBoostMutationResult(CpuBoostMutationOutcome.ApplyFailed, applyResult.FailureMessage);
        }

        AppLog.Info("Profiles.CpuBoost", "CPU Boost desired value applied.", ("Side", mutateAc ? "AC" : "DC"), ("Mode", mode));
        return new CpuBoostMutationResult(CpuBoostMutationOutcome.Succeeded, null);
    }

    /// <summary>Reads the current Windows state and -- only for the non-null side(s) supplied --
    /// applies the desired mode. Never writes an unmanaged (null) side, and never turns a read of
    /// an unmanaged side into a persisted value (work order sections 6/9/16).</summary>
    private void ReconcileWindows(CpuBoostMode? desiredAc, CpuBoostMode? desiredDc, string contextLabel)
    {
        if (desiredAc is null && desiredDc is null)
        {
            var currentOnly = _powerPolicy.Read();
            UpdateSnapshot(currentOnly, desiredAc, desiredDc);
            AppLog.Debug("Profiles.CpuBoost", "CPU Boost is fully unmanaged; Windows left untouched.", ("Context", contextLabel));
            return;
        }

        var applyResult = _powerPolicy.Apply(desiredAc, desiredDc);
        if (!applyResult.Succeeded)
            AppLog.Warn("Profiles.CpuBoost", "CPU Boost reconcile apply failed for at least one side.", null,
                ("Context", contextLabel), ("AcSucceeded", applyResult.AcSucceeded), ("DcSucceeded", applyResult.DcSucceeded));

        var current = _powerPolicy.Read();
        UpdateSnapshot(current, desiredAc, desiredDc, applyResult.Succeeded ? null : applyResult.FailureMessage);
    }

    private void RefreshSnapshotAfterApply(DeviceCpuBoostSettings? cpuBoost, CpuBoostApplyResult applyResult)
    {
        var current = _powerPolicy.Read();
        UpdateSnapshot(current, cpuBoost?.Ac, cpuBoost?.Dc, applyResult.Succeeded ? null : applyResult.FailureMessage);
    }

    private void UpdateSnapshot(CpuBoostSystemState current, CpuBoostMode? desiredAc, CpuBoostMode? desiredDc, string? failure = null)
    {
        lock (_sync)
        {
            _snapshot = new CpuBoostRuntimeSnapshot(
                current.Ac, current.Dc, desiredAc, desiredDc, _persistenceWritable,
                failure ?? current.FailureMessage);
        }
    }
}
