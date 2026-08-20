using SteamInputAddonforClaw.Contracts.DeviceProfiles;
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

/// <summary>Minimal, narrowly CPU-Boost-specific runtime snapshot -- not a general Device/Profile
/// status framework. Distinguishes the actual current Windows AC/DC values from the Addon's
/// persisted desired values. <see cref="AcDesired"/>/<see cref="DcDesired"/> are <see langword="null"/>
/// only while Device CPU Boost is uninitialized (no complete AC/DC baseline has ever been
/// established yet); once initialized, both are always concrete values -- there is no per-side
/// "unmanaged" state.
///
/// <see cref="Enabled"/> (Device CPU Boost Toggle addendum) reflects only whether the Device/global
/// apply path is currently allowed to apply <see cref="AcDesired"/>/<see cref="DcDesired"/> -- it is
/// not an application-wide CPU Boost switch. <see cref="AcDesired"/>/<see cref="DcDesired"/> remain
/// populated even while <see cref="Enabled"/> is <see langword="false"/>, so the UI can keep showing
/// the saved selections that will be re-applied the moment the feature is turned back on.</summary>
public sealed record CpuBoostRuntimeSnapshot(
    CpuBoostSideReading AcCurrent,
    CpuBoostSideReading DcCurrent,
    CpuBoostMode? AcDesired,
    CpuBoostMode? DcDesired,
    bool Enabled,
    bool PersistenceWritable,
    string? LastFailure)
{
    public static readonly CpuBoostRuntimeSnapshot Empty = new(
        CpuBoostSideReading.Unavailable, CpuBoostSideReading.Unavailable, null, null, Enabled: false, PersistenceWritable: false, LastFailure: null);
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
    private readonly ProfileMutationGate _mutationGate;
    private readonly Lock _sync = new();
    // Serializes the full mutation transaction (derive latest document -> persist -> commit ->
    // apply) end to end, distinct from _sync (which only guards fast snapshot/field reads).
    // Without this, two concurrent AC/DC mutations can both read the same starting _document,
    // and whichever Save() finishes last silently discards the other's change.

    private ProfileDocument _document = new();
    private bool _persistenceWritable;
    private CpuBoostRuntimeSnapshot _snapshot = CpuBoostRuntimeSnapshot.Empty;

    internal CpuBoostRuntime(ProfileStore profileStore, ICpuBoostPowerPolicy? powerPolicy = null, ProfileMutationGate? mutationGate = null)
    {
        _profileStore = profileStore ?? throw new ArgumentNullException(nameof(profileStore));
        _powerPolicy = powerPolicy ?? new WindowsCpuBoostPowerPolicy();
        _mutationGate = mutationGate ?? new ProfileMutationGate();
    }

    internal CpuBoostRuntimeSnapshot Snapshot { get { lock (_sync) return _snapshot; } }

    /// <summary>
    /// Runs once during headless Runtime startup (work order PR277 addendum "CPU Boost First-Run
    /// Baseline Policy"): loads the profile document, then either bootstraps or reconciles.
    ///
    /// If no CPU Boost value has ever been persisted (<see cref="DeviceCpuBoostSettings"/> is
    /// <see langword="null"/>) and persistence is safe to write to, this is first-run: the current
    /// Windows AC/DC values are read once and adopted as-is as the initial persisted Device values
    /// (see <see cref="TryBootstrapFromWindows"/>). Once a CPU Boost value exists, it is simply
    /// this app's saved value -- a later startup never re-adopts whatever Windows currently reads,
    /// even if another application changed it in the meantime; it only (re-)applies the persisted
    /// value, exactly as before.
    ///
    /// A <see cref="ProfileLoadStatus.Malformed"/>/<see cref="ProfileLoadStatus.UnsupportedSchemaVersion"/>/
    /// <see cref="ProfileLoadStatus.ReadFailure"/> result never bootstraps and never writes Windows:
    /// this method never treats "unreliable" the same as "no preference yet" by writing anything on
    /// that state's behalf. One reconciliation attempt only -- no retry loop.
    ///
    /// Holds the same <see cref="_mutationSync"/> gate as <see cref="Mutate"/> for its entire
    /// duration: without this, a mutation that starts (and finishes) while startup reconcile is
    /// still applying a stale captured value could have its newer value clobbered when the slower
    /// startup reconcile applies afterward. Serializing against the one concrete mutation gate
    /// guarantees a mutation can only ever run strictly before or strictly after startup reconcile,
    /// never interleaved with it.
    /// </summary>
    internal void StartupReconcile()
    {
        lock (_mutationGate.Sync)
        {
            var loadResult = _profileStore.Load();
            lock (_sync)
            {
                _document = loadResult.Document;
                _persistenceWritable = loadResult.CanSafelyReplace;
            }

            AppLog.Debug("Profiles.CpuBoost", "CPU Boost startup reconcile started.", ("ProfileStatus", loadResult.Status));

            if (!loadResult.CanSafelyReplace)
            {
                // Malformed/UnsupportedSchemaVersion/ReadFailure: never bootstrap, never write
                // Windows on this state's behalf -- just report the current unmanaged read.
                UpdateSnapshot(_powerPolicy.Read(), null, null, enabled: false);
                return;
            }

            var cpuBoost = loadResult.Document.Device.Performance.CpuBoost;
            if (cpuBoost is null || cpuBoost.Ac is null || cpuBoost.Dc is null)
            {
                // Missing entirely, or an incomplete legacy/partial baseline (one side never
                // persisted): never apply a partial policy -- complete the baseline from Windows
                // first (current product policy section 3.4), fail-safe if that is not possible.
                TryBootstrapFromWindows(loadResult.Document, cpuBoost);
                return;
            }

            if (!cpuBoost.Enabled)
            {
                // Device CPU Boost Toggle addendum: OFF means the Device/global apply path does not
                // apply CPU Boost -- at startup or anywhere else. The saved AC/DC selections remain
                // visible (a diagnostic Windows read only, never a write) so the UI can show what
                // will be applied the moment the feature is turned back on.
                UpdateSnapshot(_powerPolicy.Read(), cpuBoost.Ac, cpuBoost.Dc, enabled: false);
                AppLog.Debug("Profiles.CpuBoost", "Device CPU Boost is disabled; Windows left untouched at startup.");
                return;
            }

            ReconcileWindows(cpuBoost.Ac.Value, cpuBoost.Dc.Value, contextLabel: "startup reconcile");
        }
    }

    /// <summary>Establishes a complete (concrete AC and concrete DC) baseline from
    /// <paramref name="previous"/>, reading Windows only for whichever side(s) are not already
    /// known -- an already-persisted side is preserved, never silently re-adopted from Windows.
    /// Fails (returns <see langword="false"/>) without inventing a value if a needed side cannot be
    /// read as a known/supported <see cref="CpuBoostMode"/>: initialization is left incomplete for a
    /// later attempt (current product policy sections 3.2/3.3).</summary>
    private bool TryCompleteBaseline(DeviceCpuBoostSettings? previous, out DeviceCpuBoostSettings baseline, out CpuBoostSystemState current)
    {
        current = default;
        var ac = previous?.Ac;
        var dc = previous?.Dc;
        if (ac is null || dc is null)
        {
            current = _powerPolicy.Read();
            if (ac is null)
            {
                if (!current.Succeeded || current.Ac.Status != CpuBoostReadStatus.Known || current.Ac.Mode is not { } acMode)
                {
                    baseline = null!;
                    return false;
                }
                ac = acMode;
            }
            if (dc is null)
            {
                if (!current.Succeeded || current.Dc.Status != CpuBoostReadStatus.Known || current.Dc.Mode is not { } dcMode)
                {
                    baseline = null!;
                    return false;
                }
                dc = dcMode;
            }
        }
        // Device CPU Boost Toggle addendum section 3: an uninitialized baseline defaults to ON, so
        // completion never changes the effective setting on first initialization. Rebuild from the
        // existing record (rather than a fresh DeviceCpuBoostSettings) so Enabled and any unknown
        // ExtensionData PR275's additive persistence contract preserves survive baseline completion.
        var source = previous ?? new DeviceCpuBoostSettings { Enabled = true };
        baseline = source with { Ac = ac, Dc = dc };
        return true;
    }

    /// <summary>Completes and persists a baseline (missing entirely, or an incomplete legacy/partial
    /// document) from Windows in one shot. If a needed side cannot be read as a known/supported
    /// <see cref="CpuBoostMode"/>, this does not invent a fallback, does not persist anything, and
    /// does not normalize the value -- CPU Boost is simply left uninitialized/incomplete for a later
    /// startup to try again. Called only while already holding <see cref="_mutationSync"/>.</summary>
    private void TryBootstrapFromWindows(ProfileDocument document, DeviceCpuBoostSettings? previousCpuBoost)
    {
        if (!TryCompleteBaseline(previousCpuBoost, out var baseline, out var current))
        {
            AppLog.Warn("Profiles.CpuBoost", "CPU Boost bootstrap could not read a known Windows AC/DC value; CPU Boost remains uninitialized.", null,
                ("AcStatus", current.Ac.Status), ("DcStatus", current.Dc.Status));
            UpdateSnapshot(current, previousCpuBoost?.Ac, previousCpuBoost?.Dc, enabled: false, current.Succeeded ? null : current.FailureMessage);
            return;
        }

        var updatedDocument = document with
        {
            Device = document.Device with
            {
                Performance = document.Device.Performance with { CpuBoost = baseline }
            }
        };

        try
        {
            _profileStore.Save(updatedDocument);
        }
        catch (Exception exception)
        {
            AppLog.Error("Profiles.CpuBoost", "CPU Boost bootstrap persistence failed; CPU Boost remains uninitialized.", exception);
            UpdateSnapshot(current, previousCpuBoost?.Ac, previousCpuBoost?.Dc, enabled: false, exception.Message);
            return;
        }

        lock (_sync)
        {
            _document = updatedDocument;
            _persistenceWritable = true;
        }
        AppLog.Info("Profiles.CpuBoost", "CPU Boost bootstrap adopted the current Windows values.", ("Ac", baseline.Ac), ("Dc", baseline.Dc));

        if (baseline.Enabled && previousCpuBoost is not null)
        {
            // Completing an incomplete legacy/partial baseline (as opposed to a fresh first-run
            // adoption, where both sides were just read from Windows and are already in effect):
            // the side that was already persisted may differ from the current Windows value, so an
            // enabled Device policy must still reconcile Windows to the now-complete persisted
            // baseline rather than leaving a stale effective value in place for the whole session.
            ReconcileWindows(baseline.Ac!.Value, baseline.Dc!.Value, contextLabel: "startup baseline completion");
            return;
        }

        // Fresh first-run (or disabled incomplete-baseline completion) already has the exact
        // Windows read that established the complete baseline (TryCompleteBaseline populated
        // `current` while completing it); no second read/write needed.
        UpdateSnapshot(current, baseline.Ac, baseline.Dc, baseline.Enabled);
    }

    /// <summary>Sets the persisted/desired AC CPU Boost mode and applies it to Windows. DC is left
    /// completely untouched.</summary>
    internal CpuBoostMutationResult SetDeviceCpuBoostAc(CpuBoostMode mode) => Mutate(mode, mutateAc: true);

    /// <summary>Sets the persisted/desired DC CPU Boost mode and applies it to Windows. AC is left
    /// completely untouched.</summary>
    internal CpuBoostMutationResult SetDeviceCpuBoostDc(CpuBoostMode mode) => Mutate(mode, mutateAc: false);

    private CpuBoostMutationResult Mutate(CpuBoostMode mode, bool mutateAc)
    {
        // Holds the entire derive-persist-commit-apply transaction for the lifetime of this call,
        // so a concurrent AC and DC mutation can never both derive from the same starting
        // document and have one silently overwrite the other's change (see _mutationSync doc).
        lock (_mutationGate.Sync)
        {
            lock (_sync)
                if (!_persistenceWritable)
                    return new CpuBoostMutationResult(CpuBoostMutationOutcome.PersistenceFailed, "Profile state is not safe to replace.");
            var currentLoad = _profileStore.Load();
            if (!currentLoad.CanSafelyReplace)
                return new CpuBoostMutationResult(CpuBoostMutationOutcome.PersistenceFailed, "Profile state is not safe to replace.");
            var previousDocument = currentLoad.Document;
            lock (_sync) { _document = previousDocument; _persistenceWritable = true; }

            // An explicit user mutation (AC/DC or the Enabled toggle itself) is itself a form of
            // initialization: a missing or incomplete baseline is completed from Windows first (the
            // single-side mutation can never construct/persist an Enabled=true document with a null
            // other side -- current product policy section 3.2), and a freshly-created value defaults
            // to Enabled=true, same as a successful bootstrap -- never a silently-inert Enabled=false
            // the user never chose. If the baseline cannot be completed (a needed Windows side is not
            // a known/supported value), nothing is persisted and no Windows write is attempted
            // (section 3.3).
            if (!TryCompleteBaseline(previousDocument.Device.Performance.CpuBoost, out var baseline, out _))
            {
                // PersistenceFailed specifically means ProfileStore.Save() failed before any Windows
                // write was attempted; no Save() was ever attempted here, so this is a Windows-read/
                // initialization failure -- ApplyFailed, matching the existing
                // SetDeviceCpuBoostEnabled(true) baseline-establishment failure classification (no
                // new outcome enum needed).
                AppLog.Warn("Profiles.CpuBoost", "CPU Boost mutation could not establish a complete baseline from Windows; nothing was persisted.", null, ("Side", mutateAc ? "AC" : "DC"));
                return new CpuBoostMutationResult(CpuBoostMutationOutcome.ApplyFailed, "CPU Boost could not be initialized from Windows.");
            }
            var updatedCpuBoost = mutateAc ? baseline with { Ac = mode } : baseline with { Dc = mode };
            var updatedPerformance = previousDocument.Device.Performance with { CpuBoost = updatedCpuBoost };
            var updatedDevice = previousDocument.Device with { Performance = updatedPerformance };
            var updatedDocument = previousDocument with { Device = updatedDevice };

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

            if (!updatedCpuBoost.Enabled)
            {
                // Device CPU Boost Toggle addendum: while OFF, an AC/DC selection is saved but never
                // applied to Windows -- it becomes authoritative the moment the feature is turned
                // back on. (The Device page itself disables these selectors while OFF, but the
                // Runtime enforces the same invariant regardless of caller.)
                RefreshSnapshotAfterApply(updatedCpuBoost, CpuBoostApplyResult.NoOp);
                AppLog.Info("Profiles.CpuBoost", "CPU Boost desired value saved while Device CPU Boost is disabled; not applied.", ("Side", mutateAc ? "AC" : "DC"), ("Mode", mode));
                return new CpuBoostMutationResult(CpuBoostMutationOutcome.Succeeded, null);
            }

            var applyResult = _powerPolicy.Apply(mutateAc ? mode : null, mutateAc ? null : mode);
            RefreshSnapshotAfterApply(updatedCpuBoost, applyResult);

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
    }

    /// <summary>Device CPU Boost Toggle addendum: turns the Device/global CPU Boost apply path on
    /// or off. This controls only whether the Addon applies the Device/global AC/DC values -- it is
    /// not an application-wide CPU Boost master switch, and it never gates a future Game Profile
    /// CPU Boost path.
    ///
    /// OFF performs NO restoration whatsoever (no read-and-write-back, no undo of the last applied
    /// value, no restore to any prior/system value): Windows is left exactly as it is, and only
    /// future Device/global application stops. The saved AC/DC selections are never cleared, so
    /// turning the feature back ON immediately re-applies them -- never re-bootstrapped from a
    /// fresh Windows read, which only ever happens before first initialization.</summary>
    internal CpuBoostMutationResult SetDeviceCpuBoostEnabled(bool enabled)
    {
        lock (_mutationGate.Sync)
        {
            lock (_sync)
                if (!_persistenceWritable)
                    return new CpuBoostMutationResult(CpuBoostMutationOutcome.PersistenceFailed, "Profile state is not safe to replace.");
            var currentLoad = _profileStore.Load();
            if (!currentLoad.CanSafelyReplace)
                return new CpuBoostMutationResult(CpuBoostMutationOutcome.PersistenceFailed, "Profile state is not safe to replace.");
            var previousDocument = currentLoad.Document;
            lock (_sync) { _document = previousDocument; _persistenceWritable = true; }

            var previousCpuBoost = previousDocument.Device.Performance.CpuBoost;

            if (enabled && (previousCpuBoost?.Ac is null || previousCpuBoost.Dc is null))
            {
                // Turning ON an uninitialized (or incomplete-baseline) Device CPU Boost must first
                // obtain a complete AC/DC baseline -- never commit an Enabled=true document with a
                // null side. If Windows cannot currently be read, leave CPU Boost exactly as
                // uninitialized as it already was, so a later attempt (bootstrap or another Enable)
                // can still try again -- this must never permanently bypass TryBootstrapFromWindows.
                if (!TryCompleteBaseline(previousCpuBoost, out var baseline, out var current))
                {
                    AppLog.Warn("Profiles.CpuBoost", "Device CPU Boost could not be enabled: Windows AC/DC could not be read as known values.", null,
                        ("AcStatus", current.Ac.Status), ("DcStatus", current.Dc.Status));
                    UpdateSnapshot(current, previousCpuBoost?.Ac, previousCpuBoost?.Dc, enabled: false,
                        current.Succeeded ? "CPU Boost could not be initialized from Windows." : current.FailureMessage);
                    return new CpuBoostMutationResult(CpuBoostMutationOutcome.ApplyFailed, "CPU Boost could not be initialized from Windows.");
                }

                previousCpuBoost = baseline;
            }

            var updatedCpuBoost = (previousCpuBoost ?? new DeviceCpuBoostSettings()) with { Enabled = enabled };
            var updatedDocument = previousDocument with
            {
                Device = previousDocument.Device with
                {
                    Performance = previousDocument.Device.Performance with { CpuBoost = updatedCpuBoost }
                }
            };

            try
            {
                _profileStore.Save(updatedDocument);
            }
            catch (Exception exception)
            {
                AppLog.Error("Profiles.CpuBoost", "Device CPU Boost Enabled toggle persistence failed.", exception);
                return new CpuBoostMutationResult(CpuBoostMutationOutcome.PersistenceFailed, exception.Message);
            }

            lock (_sync)
            {
                _document = updatedDocument;
                _persistenceWritable = true;
            }

            if (!enabled)
            {
                AppLog.Info("Profiles.CpuBoost", "Device CPU Boost disabled; no Windows write performed.");
                RefreshSnapshotAfterApply(updatedCpuBoost, CpuBoostApplyResult.NoOp);
                return new CpuBoostMutationResult(CpuBoostMutationOutcome.Succeeded, null);
            }

            var applyResult = _powerPolicy.Apply(updatedCpuBoost.Ac, updatedCpuBoost.Dc);
            RefreshSnapshotAfterApply(updatedCpuBoost, applyResult);

            if (!applyResult.Succeeded)
            {
                AppLog.Warn("Profiles.CpuBoost", "Device CPU Boost was enabled but the Windows apply failed.", null,
                    ("AcSucceeded", applyResult.AcSucceeded), ("DcSucceeded", applyResult.DcSucceeded));
                return new CpuBoostMutationResult(CpuBoostMutationOutcome.ApplyFailed, applyResult.FailureMessage);
            }

            AppLog.Info("Profiles.CpuBoost", "Device CPU Boost enabled; saved values applied.", ("Ac", updatedCpuBoost.Ac), ("Dc", updatedCpuBoost.Dc));
            return new CpuBoostMutationResult(CpuBoostMutationOutcome.Succeeded, null);
        }
    }

    /// <summary>Applies a complete (concrete AC and DC) saved baseline to Windows and refreshes the
    /// snapshot. Only reached with both a complete baseline and Device CPU Boost Enabled -- see the
    /// incomplete-baseline and Enabled checks in <see cref="StartupReconcile"/> above.</summary>
    private void ReconcileWindows(CpuBoostMode desiredAc, CpuBoostMode desiredDc, string contextLabel)
    {
        var applyResult = _powerPolicy.Apply(desiredAc, desiredDc);
        if (!applyResult.Succeeded)
            AppLog.Warn("Profiles.CpuBoost", "CPU Boost reconcile apply failed for at least one side.", null,
                ("Context", contextLabel), ("AcSucceeded", applyResult.AcSucceeded), ("DcSucceeded", applyResult.DcSucceeded));

        var current = _powerPolicy.Read();
        UpdateSnapshot(current, desiredAc, desiredDc, enabled: true, applyResult.Succeeded ? null : applyResult.FailureMessage);
    }

    private void RefreshSnapshotAfterApply(DeviceCpuBoostSettings? cpuBoost, CpuBoostApplyResult applyResult)
    {
        var current = _powerPolicy.Read();
        UpdateSnapshot(current, cpuBoost?.Ac, cpuBoost?.Dc, cpuBoost?.Enabled ?? false, applyResult.Succeeded ? null : applyResult.FailureMessage);
    }

    private void UpdateSnapshot(CpuBoostSystemState current, CpuBoostMode? desiredAc, CpuBoostMode? desiredDc, bool enabled, string? failure = null)
    {
        lock (_sync)
        {
            _snapshot = new CpuBoostRuntimeSnapshot(
                current.Ac, current.Dc, desiredAc, desiredDc, enabled, _persistenceWritable,
                failure ?? current.FailureMessage);
        }
    }
}
