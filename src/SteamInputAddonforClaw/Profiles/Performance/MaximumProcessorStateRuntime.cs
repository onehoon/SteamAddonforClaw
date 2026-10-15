using SteamInputAddonforClaw.Diagnostics;
using SteamInputAddonforClaw.Profiles;

namespace SteamInputAddonforClaw.Profiles.Performance;

internal enum MaximumProcessorStateMutationOutcome
{
    Succeeded,
    InvalidTarget,
    PersistenceFailed,
    ApplyFailed
}

internal readonly record struct MaximumProcessorStateMutationResult(MaximumProcessorStateMutationOutcome Outcome, string? FailureMessage)
{
    public bool Succeeded => Outcome == MaximumProcessorStateMutationOutcome.Succeeded;
}

internal sealed record MaximumProcessorStateRuntimeSnapshot(
    MaximumProcessorStateSideReading AcCurrent,
    MaximumProcessorStateSideReading DcCurrent,
    int? AcDesired,
    int? DcDesired,
    bool Enabled,
    bool ConfigurationValid,
    bool PersistenceWritable,
    string? LastFailure)
{
    public static readonly MaximumProcessorStateRuntimeSnapshot Empty = new(
        MaximumProcessorStateSideReading.Unavailable,
        MaximumProcessorStateSideReading.Unavailable,
        100, 100, false, false, false, null);
}

/// <summary>Device-only owner for the PROCTHROTTLEMAX desired pair. Missing configuration is
/// disabled with prospective 100/100 selections; reading it never persists or writes Windows.</summary>
internal sealed class MaximumProcessorStateRuntime
{
    private const int DefaultPercent = 100;
    private readonly ProfileStore _profileStore;
    private readonly ProfileMutationGate _mutationGate;
    private readonly IMaximumProcessorStatePowerPolicy _powerPolicy;
    private readonly Lock _sync = new();
    private MaximumProcessorStateRuntimeSnapshot _snapshot = MaximumProcessorStateRuntimeSnapshot.Empty;

    internal MaximumProcessorStateRuntime(
        ProfileStore profileStore,
        IMaximumProcessorStatePowerPolicy? powerPolicy = null,
        ProfileMutationGate? mutationGate = null)
    {
        _profileStore = profileStore ?? throw new ArgumentNullException(nameof(profileStore));
        _powerPolicy = powerPolicy ?? new WindowsMaximumProcessorStatePowerPolicy();
        _mutationGate = mutationGate ?? new ProfileMutationGate();
    }

    internal MaximumProcessorStateRuntimeSnapshot Snapshot { get { lock (_sync) return _snapshot; } }

    internal MaximumProcessorStateRuntimeSnapshot Capture()
    {
        lock (_mutationGate.Sync)
        {
            var loaded = _profileStore.Load();
            return RefreshSnapshot(loaded);
        }
    }

    internal void StartupReconcile() => ReconcileCore("startup");

    internal void Reconcile() => ReconcileCore("lifecycle");

    internal MaximumProcessorStateMutationResult SetEnabled(bool enabled)
    {
        lock (_mutationGate.Sync)
        {
            var loaded = _profileStore.Load();
            if (!loaded.CanSafelyReplace)
            {
                RefreshSnapshot(loaded);
                return new(MaximumProcessorStateMutationOutcome.PersistenceFailed, "Profile state is not safe to replace.");
            }

            var previous = loaded.Document.Device.Performance.MaximumProcessorState;
            if (previous is not null && !IsValid(previous))
            {
                RefreshSnapshot(loaded, "The saved Maximum Processor State configuration is invalid.");
                return new(MaximumProcessorStateMutationOutcome.InvalidTarget, "The saved Maximum Processor State configuration is invalid.");
            }

            var updatedSettings = (previous ?? new DeviceMaximumProcessorStateSettings
            {
                Enabled = false,
                Ac = DefaultPercent,
                Dc = DefaultPercent
            }) with { Enabled = enabled };
            var updatedDocument = WithSettings(loaded.Document, updatedSettings);
            if (!TrySave(updatedDocument, previous, enabled ? "Enable" : "Disable", out var persistenceFailure))
                return new(MaximumProcessorStateMutationOutcome.PersistenceFailed, persistenceFailure);

            if (!enabled)
            {
                RefreshSnapshot(_profileStore.Load());
                return new(MaximumProcessorStateMutationOutcome.Succeeded, null);
            }

            return ApplySaved(updatedSettings, "enabled mutation");
        }
    }

    internal MaximumProcessorStateMutationResult SetAc(int percent) => SetSide(percent, isAc: true);
    internal MaximumProcessorStateMutationResult SetDc(int percent) => SetSide(percent, isAc: false);

    private MaximumProcessorStateMutationResult SetSide(int percent, bool isAc)
    {
        lock (_mutationGate.Sync)
        {
            if (!WindowsMaximumProcessorStatePowerPolicy.IsAllowed(percent))
            {
                RefreshSnapshot(_profileStore.Load(), $"Only 99% and 100% are supported for {(isAc ? "AC" : "DC")}.");
                return new(MaximumProcessorStateMutationOutcome.InvalidTarget, "Only 99% and 100% are supported.");
            }

            var loaded = _profileStore.Load();
            if (!loaded.CanSafelyReplace)
            {
                RefreshSnapshot(loaded);
                return new(MaximumProcessorStateMutationOutcome.PersistenceFailed, "Profile state is not safe to replace.");
            }

            var previous = loaded.Document.Device.Performance.MaximumProcessorState;
            if (previous is null || !IsValid(previous) || previous.Enabled != true)
            {
                RefreshSnapshot(loaded, "Maximum Processor State must be enabled before changing an AC or DC value.");
                return new(MaximumProcessorStateMutationOutcome.InvalidTarget, "Maximum Processor State must be enabled before changing an AC or DC value.");
            }

            var updatedSettings = isAc ? previous with { Ac = percent } : previous with { Dc = percent };
            var updatedDocument = WithSettings(loaded.Document, updatedSettings);
            if (!TrySave(updatedDocument, previous, isAc ? "AC" : "DC", out var persistenceFailure))
                return new(MaximumProcessorStateMutationOutcome.PersistenceFailed, persistenceFailure);

            var applied = _powerPolicy.Apply(isAc ? percent : null, isAc ? null : percent);
            return CompleteApply(updatedSettings, applied, isAc ? "AC" : "DC");
        }
    }

    private MaximumProcessorStateMutationResult ApplySaved(DeviceMaximumProcessorStateSettings settings, string context)
    {
        var applied = _powerPolicy.Apply(settings.Ac, settings.Dc);
        return CompleteApply(settings, applied, context);
    }

    private MaximumProcessorStateMutationResult CompleteApply(
        DeviceMaximumProcessorStateSettings settings,
        MaximumProcessorStateApplyResult applied,
        string context)
    {
        if (!applied.Succeeded)
            AppLog.Warn("Profiles.MaximumProcessorState", "Desired maximum processor state was saved but Windows apply failed.", null,
                ("Context", context), ("AcSucceeded", applied.AcSucceeded), ("DcSucceeded", applied.DcSucceeded));
        var current = _powerPolicy.Read();
        SetSnapshot(CreateSnapshot(current, settings, persistenceWritable: true,
            applied.Succeeded ? current.FailureMessage : applied.FailureMessage ?? current.FailureMessage));
        return applied.Succeeded
            ? new(MaximumProcessorStateMutationOutcome.Succeeded, null)
            : new(MaximumProcessorStateMutationOutcome.ApplyFailed, applied.FailureMessage ?? "The Windows maximum processor state could not be applied.");
    }

    private void ReconcileCore(string context)
    {
        lock (_mutationGate.Sync)
        {
            var loaded = _profileStore.Load();
            if (!loaded.CanSafelyReplace)
            {
                RefreshSnapshot(loaded);
                return;
            }

            var settings = loaded.Document.Device.Performance.MaximumProcessorState;
            if (settings is null || !IsValid(settings) || settings.Enabled != true)
            {
                var failure = settings is null || IsValid(settings)
                    ? null
                    : "The saved Maximum Processor State configuration is invalid.";
                RefreshSnapshot(loaded, failure);
                if (failure is not null)
                    AppLog.Warn("Profiles.MaximumProcessorState", "Invalid saved maximum processor state was not applied.");
                return;
            }

            var applied = _powerPolicy.Apply(settings.Ac, settings.Dc);
            if (!applied.Succeeded)
                AppLog.Warn("Profiles.MaximumProcessorState", "Maximum processor state lifecycle reconcile failed.", null,
                    ("Context", context), ("AcSucceeded", applied.AcSucceeded), ("DcSucceeded", applied.DcSucceeded));
            var current = _powerPolicy.Read();
            SetSnapshot(CreateSnapshot(current, settings, persistenceWritable: true,
                applied.Succeeded ? current.FailureMessage : applied.FailureMessage ?? current.FailureMessage));
        }
    }

    private MaximumProcessorStateRuntimeSnapshot RefreshSnapshot(ProfileLoadResult loaded, string? failure = null)
    {
        var current = _powerPolicy.Read();
        var safe = loaded.CanSafelyReplace;
        var settings = safe ? loaded.Document.Device.Performance.MaximumProcessorState : null;
        var valid = safe && (settings is null || IsValid(settings));
        var resolvedFailure = !safe
            ? "Profile state is not safe to replace."
            : !valid
                ? "The saved Maximum Processor State configuration is invalid."
                : failure ?? current.FailureMessage;
        var snapshot = CreateSnapshot(current, safe ? settings : null, safe, resolvedFailure) with { ConfigurationValid = valid };
        if (!safe) snapshot = snapshot with { AcDesired = null, DcDesired = null };
        SetSnapshot(snapshot);
        return snapshot;
    }

    private bool TrySave(ProfileDocument document, DeviceMaximumProcessorStateSettings? previous, string operation, out string? failure)
    {
        try
        {
            _profileStore.Save(document);
            failure = null;
            return true;
        }
        catch (Exception exception)
        {
            AppLog.Error("Profiles.MaximumProcessorState", "Maximum processor state persistence failed; Windows was not changed.", exception,
                ("Operation", operation));
            RefreshSnapshot(new ProfileLoadResult(document with
            {
                Device = document.Device with
                {
                    Performance = document.Device.Performance with { MaximumProcessorState = previous }
                }
            }, ProfileLoadStatus.Loaded), exception.Message);
            failure = exception.Message;
            return false;
        }
    }

    private static ProfileDocument WithSettings(ProfileDocument document, DeviceMaximumProcessorStateSettings settings)
        => document with
        {
            Device = document.Device with
            {
                Performance = document.Device.Performance with { MaximumProcessorState = settings }
            }
        };

    private static bool IsValid(DeviceMaximumProcessorStateSettings settings)
        => settings.Enabled is not null
            && settings.Ac is { } ac && WindowsMaximumProcessorStatePowerPolicy.IsAllowed(ac)
            && settings.Dc is { } dc && WindowsMaximumProcessorStatePowerPolicy.IsAllowed(dc);

    private static MaximumProcessorStateRuntimeSnapshot CreateSnapshot(
        MaximumProcessorStateSystemState current,
        DeviceMaximumProcessorStateSettings? settings,
        bool persistenceWritable,
        string? failure)
    {
        var valid = settings is null || IsValid(settings);
        return new MaximumProcessorStateRuntimeSnapshot(
            current.Ac,
            current.Dc,
            settings is null ? DefaultPercent : valid ? settings.Ac : null,
            settings is null ? DefaultPercent : valid ? settings.Dc : null,
            settings?.Enabled == true,
            valid,
            persistenceWritable,
            failure ?? current.FailureMessage);
    }

    private void SetSnapshot(MaximumProcessorStateRuntimeSnapshot snapshot)
    {
        lock (_sync) _snapshot = snapshot;
    }
}
