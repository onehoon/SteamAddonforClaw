using System.Globalization;
using SteamInputAddonforClaw.Contracts.DeviceProfiles;
using SteamInputAddonforClaw.Diagnostics;
using SteamInputAddonforClaw.Profiles;

namespace SteamInputAddonforClaw.Profiles.Performance;

internal enum PowerModeMutationOutcome { Succeeded, PersistenceFailed, ApplyFailed }
internal readonly record struct PowerModeMutationResult(PowerModeMutationOutcome Outcome, string? FailureMessage);
internal sealed record PowerModeRuntimeSnapshot(PowerModeSideReading AcCurrent, PowerModeSideReading DcCurrent, WindowsPowerMode? AcDesired, WindowsPowerMode? DcDesired, bool Enabled, bool PersistenceWritable, string? LastFailure)
{ public static readonly PowerModeRuntimeSnapshot Empty = new(PowerModeSideReading.Unavailable, PowerModeSideReading.Unavailable, null, null, false, false, null); }

internal sealed class PowerModeRuntime
{
    private readonly ProfileStore _store; private readonly IPowerModePolicy _policy; private readonly ProfileMutationGate _gate; private readonly Lock _sync = new();
    private PowerModeRuntimeSnapshot _snapshot = PowerModeRuntimeSnapshot.Empty;
    private Func<uint> _actualAppIdSource = static () => 0;
    internal PowerModeRuntime(ProfileStore store, IPowerModePolicy? policy = null, ProfileMutationGate? gate = null, ProfileMutationGate? mutationGate = null) { _store = store ?? throw new ArgumentNullException(nameof(store)); _policy = policy ?? new WindowsPowerModePolicy(); _gate = gate ?? mutationGate ?? new ProfileMutationGate(); }
    internal PowerModeRuntimeSnapshot Snapshot { get { lock (_sync) return _snapshot; } }
    internal void SetActualAppIdSource(Func<uint> source) => _actualAppIdSource = source ?? throw new ArgumentNullException(nameof(source));
    internal void StartupReconcile(uint appId = 0) => Reconcile(appId, true);
    internal void Reconcile(uint appId) => Reconcile(appId, false);
    internal PowerModeApplyResult ReconcileWithResult(uint appId)
    {
        lock (_gate.Sync)
        {
            var loaded = _store.Load();
            if (!loaded.CanSafelyReplace) { Update(_policy.Read(), null, false); return new(false, false, "Profile state is not safe to replace."); }
            var device = loaded.Document.Device.Performance.PowerMode;
            var applied = ApplyEffective(loaded.Document, device, null);
            Update(_policy.Read(), device, true, applied.Succeeded ? null : applied.FailureMessage);
            return applied;
        }
    }
    private void Reconcile(uint appId, bool startup)
    {
        lock (_gate.Sync)
        {
            var loaded = _store.Load();
            if (!loaded.CanSafelyReplace) { Update(_policy.Read(), null, false); return; }
            var device = loaded.Document.Device.Performance.PowerMode;
            if (startup && device is null) { Bootstrap(loaded.Document); return; }
            var applied = ApplyEffective(loaded.Document, device, null); Update(_policy.Read(), device, true, applied.Succeeded ? null : applied.FailureMessage);
        }
    }
    private void Bootstrap(ProfileDocument document)
    {
        var state = _policy.Read();
        if (!state.Succeeded || state.Ac.Mode is not { } ac || state.Dc.Mode is not { } dc) { Update(state, null, false); return; }
        var next = document with { Device = document.Device with { Performance = document.Device.Performance with { PowerMode = new DevicePowerModeSettings { Enabled = true, Ac = ac, Dc = dc } } } };
        try { _store.Save(next); Update(state, next.Device.Performance.PowerMode, true); }
        catch (Exception ex) { AppLog.Error("Profiles.PowerMode", "Power Mode bootstrap persistence failed.", ex); Update(state, null, true, ex.Message); }
    }
    internal PowerModeMutationResult SetDeviceAc(WindowsPowerMode mode) => Mutate(d => d with { Ac = mode }, true);
    internal PowerModeMutationResult SetDeviceDc(WindowsPowerMode mode) => Mutate(d => d with { Dc = mode }, false);
    internal PowerModeMutationResult SetEnabled(bool enabled)
    {
        lock (_gate.Sync)
        {
            var loaded = _store.Load();
            if (!loaded.CanSafelyReplace || loaded.Document.Device.Performance.PowerMode is not { } device) { Update(_policy.Read(), null, loaded.CanSafelyReplace); return new(PowerModeMutationOutcome.PersistenceFailed, "Power Mode is unavailable."); }
            var updated = device with { Enabled = enabled }; var updatedDocument = WithDevicePowerMode(loaded.Document, updated);
            try { _store.Save(updatedDocument); } catch (Exception ex) { Update(_policy.Read(), device, true, ex.Message); return new(PowerModeMutationOutcome.PersistenceFailed, ex.Message); }
            if (!enabled) { Update(_policy.Read(), updated, true); return new(PowerModeMutationOutcome.Succeeded, null); }
            return ApplyPersisted(updatedDocument, updated);
        }
    }
    private PowerModeMutationResult Mutate(Func<DevicePowerModeSettings, DevicePowerModeSettings> update, bool ac)
    {
        lock (_gate.Sync)
        {
            var loaded = _store.Load();
            if (!loaded.CanSafelyReplace || loaded.Document.Device.Performance.PowerMode is not { } device) { Update(_policy.Read(), null, loaded.CanSafelyReplace); return new(PowerModeMutationOutcome.PersistenceFailed, "Power Mode is unavailable."); }
            var updated = update(device); var updatedDocument = WithDevicePowerMode(loaded.Document, updated);
            try { _store.Save(updatedDocument); } catch (Exception ex) { Update(_policy.Read(), device, true, ex.Message); return new(PowerModeMutationOutcome.PersistenceFailed, ex.Message); }
            return ApplyPersisted(updatedDocument, updated, ac);
        }
    }
    private PowerModeMutationResult ApplyPersisted(ProfileDocument document, DevicePowerModeSettings device, bool? mutatedAcSide = null)
    {
        try { var applied = ApplyEffective(document, device, mutatedAcSide); Update(_policy.Read(), device, true, applied.Succeeded ? null : applied.FailureMessage); return applied.Succeeded ? new(PowerModeMutationOutcome.Succeeded, null) : new(PowerModeMutationOutcome.ApplyFailed, applied.FailureMessage); }
        catch (Exception ex) { Update(_policy.Read(), device, true, ex.Message); return new(PowerModeMutationOutcome.ApplyFailed, ex.Message); }
    }
    private PowerModeApplyResult ApplyEffective(ProfileDocument document, DevicePowerModeSettings? device, bool? mutatedAcSide)
    {
        var appId = _actualAppIdSource();
        if (appId > 0 && document.Games.TryGetValue(appId.ToString(CultureInfo.InvariantCulture), out var game) && game.Enabled && game.Performance.PowerMode is { Enabled: true } gamePower) return _policy.Apply(gamePower.Ac, gamePower.Dc);
        if (device is not { Enabled: true }) return PowerModeApplyResult.NoOp;
        return mutatedAcSide switch { true => _policy.Apply(device.Ac, null), false => _policy.Apply(null, device.Dc), _ => _policy.Apply(device.Ac, device.Dc) };
    }
    private static ProfileDocument WithDevicePowerMode(ProfileDocument document, DevicePowerModeSettings powerMode) => document with { Device = document.Device with { Performance = document.Device.Performance with { PowerMode = powerMode } } };
    private void Update(PowerModeSystemState state, DevicePowerModeSettings? desired, bool persistenceWritable, string? lastFailure = null) { lock (_sync) _snapshot = new(state.Ac, state.Dc, desired?.Ac, desired?.Dc, desired?.Enabled == true, persistenceWritable, lastFailure ?? state.FailureMessage); }
}
