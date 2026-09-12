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

            ProfileLoadResult? loadedForFailure = null;
            BatteryChargeLimitMutationOutcome? failureOutcome = null;
            string? failureMessage = null;
            DeviceBatteryChargeLimitSettings? desired = null;
            lock (_mutationGate.Sync)
            {
                var loaded = _profileStore.Load();
                if (!loaded.CanSafelyReplace)
                {
                    loadedForFailure = loaded;
                    failureOutcome = BatteryChargeLimitMutationOutcome.PersistenceFailed;
                    failureMessage = "Profile state is not safe to replace.";
                }
                else if (!IsValidTarget(loaded.Document.Device.Battery?.ChargeLimit))
                {
                    loadedForFailure = loaded;
                    failureOutcome = BatteryChargeLimitMutationOutcome.InvalidTarget;
                    failureMessage = "A valid battery charge-limit target must be initialized before changing the enabled state.";
                }
                else
                {
                    var current = loaded.Document.Device.Battery!.ChargeLimit!;
                    desired = current with { Enabled = enabled };
                    try { _profileStore.Save(WithBatteryChargeLimit(loaded.Document, desired)); }
                    catch (Exception exception)
                    {
                        loadedForFailure = loaded;
                        failureOutcome = BatteryChargeLimitMutationOutcome.PersistenceFailed;
                        failureMessage = exception.Message;
                        AppLog.Error("Profiles.Battery", "Battery charge-limit persistence failed; hardware was not changed.", exception);
                    }
                }
            }

            if (failureOutcome is { } outcome)
                return Result(outcome, failureMessage!, loadedForFailure);
            var apply = Apply(desired!);
            return new(apply.Outcome, apply.FailureMessage, _snapshot);
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

            ProfileLoadResult? loadedForFailure = null;
            BatteryChargeLimitMutationOutcome? failureOutcome = null;
            string? failureMessage = null;
            DeviceBatteryChargeLimitSettings? desired = null;
            bool? observedEnabled = null;

            lock (_mutationGate.Sync)
            {
                var loaded = _profileStore.Load();
                if (!loaded.CanSafelyReplace)
                {
                    loadedForFailure = loaded;
                    failureOutcome = BatteryChargeLimitMutationOutcome.PersistenceFailed;
                    failureMessage = "Profile state is not safe to replace.";
                }
                else if (IsValidTarget(loaded.Document.Device.Battery?.ChargeLimit))
                {
                    desired = new DeviceBatteryChargeLimitSettings
                    {
                        Enabled = loaded.Document.Device.Battery!.ChargeLimit!.Enabled,
                        LimitPercent = percent
                    };
                    try { _profileStore.Save(WithBatteryChargeLimit(loaded.Document, desired)); }
                    catch (Exception exception)
                    {
                        loadedForFailure = loaded;
                        failureOutcome = BatteryChargeLimitMutationOutcome.PersistenceFailed;
                        failureMessage = exception.Message;
                        AppLog.Error("Profiles.Battery", "Battery charge-limit persistence failed; hardware was not changed.", exception);
                    }
                }
            }

            if (failureOutcome is { } initialOutcome)
                return Result(initialOutcome, failureMessage!, loadedForFailure);

            // An uninitialized/invalid persisted target needs the current enable state to form a
            // complete desired pair. The read is deliberately outside ProfileMutationGate.
            if (desired is null)
            {
                observedEnabled = ReadEnabledForInitialization();
                if (observedEnabled is null)
                    return Result(BatteryChargeLimitMutationOutcome.Unavailable, "BatteryLimit read failed.");

                lock (_mutationGate.Sync)
                {
                    var loaded = _profileStore.Load();
                    if (!loaded.CanSafelyReplace)
                    {
                        loadedForFailure = loaded;
                        failureOutcome = BatteryChargeLimitMutationOutcome.PersistenceFailed;
                        failureMessage = "Profile state is not safe to replace.";
                    }
                    else
                    {
                        var saved = loaded.Document.Device.Battery?.ChargeLimit;
                        desired = new DeviceBatteryChargeLimitSettings
                        {
                            // Prefer a concurrently committed valid target from this fresh load;
                            // otherwise use the observation captured before entering the gate.
                            Enabled = IsValidTarget(saved) ? saved!.Enabled : observedEnabled.Value,
                            LimitPercent = percent
                        };
                        try { _profileStore.Save(WithBatteryChargeLimit(loaded.Document, desired)); }
                        catch (Exception exception)
                        {
                            loadedForFailure = loaded;
                            failureOutcome = BatteryChargeLimitMutationOutcome.PersistenceFailed;
                            failureMessage = exception.Message;
                            AppLog.Error("Profiles.Battery", "Battery charge-limit persistence failed; hardware was not changed.", exception);
                        }
                    }
                }
            }

            if (failureOutcome is { } outcome)
                return Result(outcome, failureMessage!, loadedForFailure);
            var apply = Apply(desired!);
            return new(apply.Outcome, apply.FailureMessage, _snapshot);
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

    private static ProfileDocument WithBatteryChargeLimit(ProfileDocument document, DeviceBatteryChargeLimitSettings desired) => document with
    {
        Device = document.Device with
        {
            Battery = (document.Device.Battery ?? new DeviceBatterySettings()) with { ChargeLimit = desired }
        }
    };

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
                lock (_mutationGate.Sync)
                {
                    // Reload after the observation. A concurrent Device/Profile mutation wins if
                    // it initialized Battery while the hardware was being read.
                    loaded = _profileStore.Load();
                    if (!loaded.CanSafelyReplace)
                    {
                        _snapshot = BuildSnapshot(loaded, observed.State, "Profile state is not safe to replace.");
                        return;
                    }

                    var freshDesired = loaded.Document.Device.Battery?.ChargeLimit;
                    if (freshDesired is not null)
                        desired = freshDesired;
                    else
                    {
                        var updated = WithBatteryChargeLimit(loaded.Document, desired);
                        _profileStore.Save(updated);
                        loaded = new(updated, ProfileLoadStatus.Loaded);
                    }
                }
            }
            catch (Exception exception)
            {
                AppLog.Error("Profiles.Battery", "Battery charge-limit bootstrap persistence failed; hardware was not changed.", exception);
                _snapshot = BuildSnapshot(loaded, observed.State, exception.Message);
                return;
            }
        }

        if (!IsValidTarget(desired) && desired is not null)
        {
            _snapshot = BuildSnapshot(loaded, observed.State,
                "The persisted battery charge-limit target is outside the 60% to 100% 5% product range.");
            return;
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
