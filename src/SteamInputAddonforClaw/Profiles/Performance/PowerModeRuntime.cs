using SteamInputAddonforClaw.Contracts.DeviceProfiles;
using SteamInputAddonforClaw.Diagnostics;
using SteamInputAddonforClaw.Profiles;

namespace SteamInputAddonforClaw.Profiles.Performance;

internal enum PowerModeMutationOutcome { Succeeded, PersistenceFailed, ApplyFailed }
internal readonly record struct PowerModeMutationResult(PowerModeMutationOutcome Outcome, string? FailureMessage);
internal sealed record PowerModeRuntimeSnapshot(PowerModeSideReading AcCurrent, PowerModeSideReading DcCurrent, WindowsPowerMode? AcDesired, WindowsPowerMode? DcDesired, bool Enabled, bool PersistenceWritable, string? LastFailure)
{
    public static readonly PowerModeRuntimeSnapshot Empty = new(PowerModeSideReading.Unavailable, PowerModeSideReading.Unavailable, null, null, false, false, null);
}

internal sealed class PowerModeRuntime
{
    private readonly ProfileStore _store; private readonly IPowerModePolicy _policy; private readonly ProfileMutationGate _gate; private readonly Lock _sync = new();
    private PowerModeRuntimeSnapshot _snapshot = PowerModeRuntimeSnapshot.Empty;
    internal PowerModeRuntime(ProfileStore store, IPowerModePolicy? policy = null, ProfileMutationGate? gate = null, ProfileMutationGate? mutationGate = null) { _store = store; _policy = policy ?? new WindowsPowerModePolicy(); _gate = gate ?? mutationGate ?? new(); }
    internal PowerModeRuntimeSnapshot Snapshot { get { lock (_sync) return _snapshot; } }
    internal void StartupReconcile(uint appId = 0) => Reconcile(appId, true);
    internal void Reconcile(uint appId) => Reconcile(appId, false);
    private void Reconcile(uint appId, bool startup)
    {
        lock (_gate.Sync)
        {
            var loaded = _store.Load(); if (!loaded.CanSafelyReplace) { Update(_policy.Read(), null); return; }
            var device = loaded.Document.Device.Performance.PowerMode;
            if (startup && device is null) { Bootstrap(loaded.Document); return; }
            if (loaded.Document.Games.TryGetValue(appId.ToString(), out var game) && game.Enabled && game.Performance.PowerMode is { } gp) { Apply(gp.Ac, gp.Dc, device); return; }
            if (device is { Enabled: true } d) Apply(d.Ac, d.Dc, device); else Update(_policy.Read(), device);
        }
    }
    private void Bootstrap(ProfileDocument document)
    {
        var state = _policy.Read(); if (!state.Succeeded || state.Ac.Mode is not { } ac || state.Dc.Mode is not { } dc) { Update(state, null); return; }
        var next = document with { Device = document.Device with { Performance = document.Device.Performance with { PowerMode = new DevicePowerModeSettings { Enabled = true, Ac = ac, Dc = dc } } } };
        try { _store.Save(next); Update(state, next.Device.Performance.PowerMode); } catch (Exception ex) { AppLog.Error("Profiles.PowerMode", "Power Mode bootstrap persistence failed.", ex); Update(state, null); }
    }
    internal PowerModeMutationResult SetDeviceAc(WindowsPowerMode mode) => Mutate(d => d with { Ac = mode }, true);
    internal PowerModeMutationResult SetDeviceDc(WindowsPowerMode mode) => Mutate(d => d with { Dc = mode }, false);
    internal PowerModeMutationResult SetEnabled(bool enabled)
    {
        lock (_gate.Sync) { var l = _store.Load(); if (!l.CanSafelyReplace || l.Document.Device.Performance.PowerMode is not { } d) return new(PowerModeMutationOutcome.PersistenceFailed, "Power Mode is unavailable."); try { _store.Save(l.Document with { Device = l.Document.Device with { Performance = l.Document.Device.Performance with { PowerMode = d with { Enabled = enabled } } } }); if (!enabled) { Update(_policy.Read(), d with { Enabled = false }); return new(PowerModeMutationOutcome.Succeeded, null); } var applied = _policy.Apply(d.Ac, d.Dc); Update(_policy.Read(), d); return applied.Succeeded ? new(PowerModeMutationOutcome.Succeeded, null) : new(PowerModeMutationOutcome.ApplyFailed, applied.FailureMessage); } catch (Exception ex) { return new(PowerModeMutationOutcome.ApplyFailed, ex.Message); } }
    }
    private PowerModeMutationResult Mutate(Func<DevicePowerModeSettings, DevicePowerModeSettings> update, bool ac)
    {
        lock (_gate.Sync) { var l = _store.Load(); if (!l.CanSafelyReplace || l.Document.Device.Performance.PowerMode is not { } d) return new(PowerModeMutationOutcome.PersistenceFailed, "Power Mode is unavailable."); var next = update(d); try { _store.Save(l.Document with { Device = l.Document.Device with { Performance = l.Document.Device.Performance with { PowerMode = next } } }); var result = _policy.Apply(ac ? next.Ac : null, ac ? null : next.Dc); Update(_policy.Read(), next); return result.Succeeded ? new(PowerModeMutationOutcome.Succeeded, null) : new(PowerModeMutationOutcome.ApplyFailed, result.FailureMessage); } catch (Exception ex) { return new(PowerModeMutationOutcome.PersistenceFailed, ex.Message); } }
    }
    private void Apply(WindowsPowerMode ac, WindowsPowerMode dc, DevicePowerModeSettings? device) { var result = _policy.Apply(ac, dc); Update(_policy.Read(), device); if (!result.Succeeded) AppLog.Warn("Profiles.PowerMode", "Power Mode reconcile failed.", null, ("Failure", result.FailureMessage)); }
    private void Update(PowerModeSystemState state, DevicePowerModeSettings? d) { lock (_sync) _snapshot = new(state.Ac, state.Dc, d?.Ac, d?.Dc, d?.Enabled == true, true, state.FailureMessage); }
}
