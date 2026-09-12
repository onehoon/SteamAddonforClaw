using SteamInputAddonforClaw.Profiles;
using SteamInputAddonforClaw.Devices.Abstractions;
using SteamInputAddonforClaw.Diagnostics;

namespace SteamInputAddonforClaw.Devices.MSI.Claw;

internal enum BatteryChargeLimitMutationOutcome
{
    Succeeded,
    InvalidTarget,
    PersistenceFailed,
    ApplyFailed,
    Unavailable
}

internal sealed record BatteryChargeLimitRuntimeSnapshot(
    bool Available,
    bool PersistenceWritable,
    bool Initialized,
    bool? CurrentEnabled,
    int? CurrentLimitPercent,
    bool? DesiredEnabled,
    int? DesiredLimitPercent,
    string? LastFailure)
{
    internal static readonly BatteryChargeLimitRuntimeSnapshot Unavailable = new(false, false, false, null, null, null, null, null);
}

internal readonly record struct BatteryChargeLimitMutationResult(
    BatteryChargeLimitMutationOutcome Outcome,
    string? FailureMessage,
    BatteryChargeLimitRuntimeSnapshot Snapshot)
{
    internal bool Succeeded => Outcome == BatteryChargeLimitMutationOutcome.Succeeded;
}

/// <summary>Runtime owner for the production Device-page battery charge-limit setting. It reuses the
/// existing MSI helper transport/hardware adapter, owns only the desired/current projection, and
/// serializes profile mutations through the shared Device/Profile mutation gate.</summary>
internal sealed class MsiClawBatteryChargeLimitRuntime
{
    private readonly ProfileStore _profileStore;
    private readonly ProfileMutationGate _mutationGate;
    private readonly MsiClawBatteryChargeLimitHardware _hardware;
    private readonly bool _available;
    private readonly object _sync = new();
    private BatteryChargeLimitRuntimeSnapshot _snapshot = BatteryChargeLimitRuntimeSnapshot.Unavailable;
    private bool _accepting = true;

    internal MsiClawBatteryChargeLimitRuntime(ProfileStore profileStore, ProfileMutationGate mutationGate,
        HandheldDeviceModelId? modelId, MsiClawBatteryChargeLimitHardware hardware)
    {
        _profileStore = profileStore ?? throw new ArgumentNullException(nameof(profileStore));
        _mutationGate = mutationGate ?? throw new ArgumentNullException(nameof(mutationGate));
        _hardware = hardware ?? throw new ArgumentNullException(nameof(hardware));
        _available = modelId is { } id && MsiClawDeviceModels.All.Any(model => model.Id == id);
        _snapshot = _available
            ? new(true, true, false, null, null, null, null, null)
            : BatteryChargeLimitRuntimeSnapshot.Unavailable;
    }

    internal BatteryChargeLimitRuntimeSnapshot Snapshot
    {
        get { lock (_sync) return _snapshot; }
    }

    internal void StartupReconcile()
    {
        lock (_sync)
        {
            if (!_accepting || !_available) return;
            ReconcileLoadedProfile("Startup");
        }
    }

    internal BatteryChargeLimitRuntimeSnapshot CaptureSnapshot()
    {
        lock (_sync)
        {
            if (!_accepting || !_available) return BatteryChargeLimitRuntimeSnapshot.Unavailable;
            var loaded = _profileStore.Load();
            var current = _hardware.Read();
            _snapshot = BuildSnapshot(loaded, current.State, current.FailureMessage);
            return _snapshot;
        }
    }

    internal void Reconcile(string reason)
    {
        lock (_sync)
        {
            if (!_accepting || !_available) return;
            ReconcileLoadedProfile(reason);
        }
    }

    internal BatteryChargeLimitMutationResult SetEnabled(bool enabled)
    {
        lock (_sync)
        {
            if (!_accepting || !_available)
                return Result(BatteryChargeLimitMutationOutcome.Unavailable, "MSI battery charge-limit control is unavailable.");

            var loaded = _profileStore.Load();
            if (!loaded.CanSafelyReplace)
                return Result(BatteryChargeLimitMutationOutcome.PersistenceFailed, "Profile state is not safe to replace.", loaded);

            var current = loaded.Document.Device.Battery?.ChargeLimit;
            if (!IsValidTarget(current))
                return Result(BatteryChargeLimitMutationOutcome.InvalidTarget,
                    "A valid battery charge-limit target must be initialized before changing the enabled state.", loaded);

            return Commit(loaded, current! with { Enabled = enabled });
        }
    }

    internal BatteryChargeLimitMutationResult SetPercent(int percent)
    {
        lock (_sync)
        {
            if (!_accepting || !_available)
                return Result(BatteryChargeLimitMutationOutcome.Unavailable, "MSI battery charge-limit control is unavailable.");
            if (!MsiClawBatteryChargeLimitHardware.IsProductValue(percent))
                return Result(BatteryChargeLimitMutationOutcome.InvalidTarget,
                    "The battery limit must be 60% to 100% in 5% steps.");

            var loaded = _profileStore.Load();
            if (!loaded.CanSafelyReplace)
                return Result(BatteryChargeLimitMutationOutcome.PersistenceFailed, "Profile state is not safe to replace.", loaded);

            var saved = loaded.Document.Device.Battery?.ChargeLimit;
            var enabled = IsValidTarget(saved) ? saved!.Enabled : ReadEnabledForInitialization();
            if (enabled is null)
                return Result(BatteryChargeLimitMutationOutcome.Unavailable, "BatteryLimit read failed.", loaded);

            return Commit(loaded, new DeviceBatteryChargeLimitSettings { Enabled = enabled.Value, LimitPercent = percent });
        }
    }

    internal void BeginShutdown()
    {
        lock (_sync) _accepting = false;
    }

    private bool? ReadEnabledForInitialization()
    {
        var current = _hardware.Read();
        return current.Succeeded ? current.State!.Value.Enabled : null;
    }

    private BatteryChargeLimitMutationResult Commit(ProfileLoadResult loaded, DeviceBatteryChargeLimitSettings desired)
    {
        var updated = loaded.Document with
        {
            Device = loaded.Document.Device with
            {
                Battery = (loaded.Document.Device.Battery ?? new DeviceBatterySettings()) with { ChargeLimit = desired }
            }
        };

        try
        {
            lock (_mutationGate.Sync) _profileStore.Save(updated);
        }
        catch (Exception exception)
        {
            AppLog.Error("Profiles.Battery", "Battery charge-limit persistence failed; hardware was not changed.", exception);
            return Result(BatteryChargeLimitMutationOutcome.PersistenceFailed, exception.Message, loaded);
        }

        var apply = Apply(desired);
        return new(apply.Outcome, apply.FailureMessage, _snapshot);
    }

    private void ReconcileLoadedProfile(string reason)
    {
        ProfileLoadResult loaded;
        lock (_mutationGate.Sync) loaded = _profileStore.Load();
        if (!loaded.CanSafelyReplace)
        {
            var current = _hardware.Read();
            _snapshot = BuildSnapshot(loaded, current.State, current.FailureMessage);
            return;
        }

        var desired = loaded.Document.Device.Battery?.ChargeLimit;
        if (!IsValidTarget(desired) && desired is not null)
        {
            var current = _hardware.Read();
            _snapshot = BuildSnapshot(loaded, current.State,
                "The persisted battery charge-limit target is outside the 60% to 100% 5% product range.");
            return;
        }

        var observed = _hardware.Read();
        if (desired is null && observed.Succeeded && observed.State is { IsProductValue: true } state)
        {
            // First-run bootstrap records the observed product value and deliberately performs no
            // hardware write. The existing raw helper remains the sole hardware authority.
            desired = new DeviceBatteryChargeLimitSettings { Enabled = state.Enabled, LimitPercent = state.LimitPercent };
            try
            {
                var updated = loaded.Document with
                {
                    Device = loaded.Document.Device with
                    {
                        Battery = (loaded.Document.Device.Battery ?? new DeviceBatterySettings()) with { ChargeLimit = desired }
                    }
                };
                lock (_mutationGate.Sync) _profileStore.Save(updated);
                loaded = new(updated, ProfileLoadStatus.Loaded);
            }
            catch (Exception exception)
            {
                AppLog.Error("Profiles.Battery", "Battery charge-limit bootstrap persistence failed; hardware was not changed.", exception);
                _snapshot = BuildSnapshot(loaded, observed.State, exception.Message);
                return;
            }
        }

        if (desired is null)
        {
            _snapshot = BuildSnapshot(loaded, observed.State, observed.FailureMessage);
            return;
        }

        Apply(desired, observed);
    }

    private BatteryChargeLimitMutationResult Apply(DeviceBatteryChargeLimitSettings desired,
        MsiBatteryChargeLimitReadResult? initialRead = null)
    {
        var read = initialRead ?? _hardware.Read();
        if (!read.Succeeded || read.State is not { } current)
        {
            _snapshot = BuildSnapshotFromDesired(desired, null, read.FailureMessage);
            return new(BatteryChargeLimitMutationOutcome.ApplyFailed, read.FailureMessage ?? "BatteryLimit read failed.", _snapshot);
        }

        var actual = current;
        if (desired.Enabled)
        {
            if (actual.LimitPercent != desired.LimitPercent)
            {
                var result = _hardware.SetPercent(desired.LimitPercent);
                if (!result.Succeeded)
                    return ApplyFailure(desired, result);
                actual = result.State!.Value;
            }
            if (actual.Enabled != desired.Enabled)
            {
                var result = _hardware.SetEnabled(true);
                if (!result.Succeeded)
                    return ApplyFailure(desired, result);
                actual = result.State!.Value;
            }
        }
        else
        {
            if (actual.Enabled != desired.Enabled)
            {
                var result = _hardware.SetEnabled(false);
                if (!result.Succeeded)
                    return ApplyFailure(desired, result);
                actual = result.State!.Value;
            }
            if (actual.LimitPercent != desired.LimitPercent)
            {
                var result = _hardware.SetPercent(desired.LimitPercent);
                if (!result.Succeeded)
                    return ApplyFailure(desired, result);
                actual = result.State!.Value;
            }
        }

        _snapshot = BuildSnapshotFromDesired(desired, actual, null);
        return new(BatteryChargeLimitMutationOutcome.Succeeded, null, _snapshot);
    }

    private BatteryChargeLimitMutationResult ApplyFailure(DeviceBatteryChargeLimitSettings desired,
        MsiBatteryChargeLimitMutationResult result)
    {
        _snapshot = BuildSnapshotFromDesired(desired, result.State, result.FailureMessage);
        return new(BatteryChargeLimitMutationOutcome.ApplyFailed, result.FailureMessage, _snapshot);
    }

    private BatteryChargeLimitMutationResult Result(BatteryChargeLimitMutationOutcome outcome, string message,
        ProfileLoadResult? loaded = null)
    {
        var current = _hardware.Read();
        _snapshot = loaded is null
            ? new(true, true, false, current.State?.Enabled, current.State?.LimitPercent, null, null, message)
            : BuildSnapshot(loaded, current.State, message);
        return new(outcome, message, _snapshot);
    }

    private static bool IsValidTarget(DeviceBatteryChargeLimitSettings? settings) =>
        settings is not null && MsiClawBatteryChargeLimitHardware.IsProductValue(settings.LimitPercent);

    private BatteryChargeLimitRuntimeSnapshot BuildSnapshot(ProfileLoadResult loaded,
        MsiBatteryChargeLimitState? current, string? failure)
    {
        var desired = loaded.Document.Device.Battery?.ChargeLimit;
        var valid = IsValidTarget(desired);
        return new(true, loaded.CanSafelyReplace, valid, current?.Enabled, current?.LimitPercent,
            valid ? desired!.Enabled : null, valid ? desired!.LimitPercent : null, failure);
    }

    private static BatteryChargeLimitRuntimeSnapshot BuildSnapshotFromDesired(DeviceBatteryChargeLimitSettings desired,
        MsiBatteryChargeLimitState? current, string? failure) =>
        new(true, true, true, current?.Enabled, current?.LimitPercent, desired.Enabled, desired.LimitPercent, failure);
}
