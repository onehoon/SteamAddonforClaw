using SteamInputAddonforClaw.Power;
using SteamInputAddonforClaw.Recovery;
using SteamInputAddonforClaw.Steam;
using SteamInputAddonforClaw.Devices.Abstractions;
using System.Text.Json;
using SteamInputAddonforClaw.Diagnostics;
using SteamInputAddonforClaw.Routing;

namespace SteamInputAddonforClaw.Devices.MSI.Claw;

internal sealed class MsiClawNativeModeSessionCoordinator : IAsyncDisposable, IPowerTransitionParticipant, IMsiClawNativeModeStageSession, IRoutingRuntimeSessionBoundaryParticipant, IRoutingRecoverySessionProvider
{
    private readonly MsiClawNativeStateManager _nativeState;
    private readonly RecoveryManager _recovery;
    private readonly PowerMutationGate _powerGate;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private DeviceNativeStateSnapshot? _snapshot;
    private bool _active;
    private bool _recoveryBoundaryOwned;
    private Guid? _recoverySessionId;
    private readonly Func<bool>? _mutationAllowed;
    private CancellationTokenSource? _safetyMonitor;
    private bool _vetoLatched;
    private long _decisionGeneration;
    private readonly Action<string>? _markRecoveryUnsafe;
    private readonly Lock _recoveryStateSync = new();

    internal MsiClawNativeModeSessionCoordinator(MsiClawNativeStateManager nativeState, RecoveryManager recovery, PowerMutationGate powerGate, Func<bool>? mutationAllowed = null, Action<string>? markRecoveryUnsafe = null)
    { _nativeState = nativeState; _recovery = recovery; _powerGate = powerGate; _mutationAllowed = mutationAllowed; _markRecoveryUnsafe = markRecoveryUnsafe; }

    public string Name => "MsiClawNativeModeSession";

    public Task<bool> QuiesceForSuspendAsync(DateTimeOffset deadline, long cycle, long epoch, CancellationToken cancellationToken)
    {
        // Keep the in-memory session intent. Recovery restores the journaled native snapshot,
        // and the post-resume reconciliation re-enters DirectInput when Test Mode is still on.
        return Task.FromResult(true);
    }

    public Task<bool> ReconcileAfterResumeAsync(long cycle, long epoch, CancellationToken cancellationToken)
    {
        // RecoveryManager may have restored the original snapshot. Force the next effective
        // session observation to perform a fresh capture and transition.
        _active = false;
        _safetyMonitor?.Cancel();
        _safetyMonitor?.Dispose();
        _safetyMonitor = null;
        if (_recoveryBoundaryOwned)
        {
            if (_recovery.HasIncompleteRecovery) return Task.FromResult(false);
            // RecoveryManager restored the journaled state and deleted the journal before
            // participant reconciliation. The next entry must capture a fresh snapshot.
            _snapshot = null;
            lock (_recoveryStateSync) { _recoveryBoundaryOwned = false; _recoverySessionId = null; }
        }
        return Task.FromResult(true);
    }

    internal Task<bool> ObserveRoutingDecisionAsync(RoutingDecision decision, long generation, CancellationToken cancellationToken = default)
        => ObserveRoutingDecisionCoreAsync(decision, generation, cancellationToken);

    internal Task<bool> ReconcileRoutingDecisionAsync(RoutingDecision decision, long generation, CancellationToken cancellationToken = default)
        => ObserveRoutingDecisionCoreAsync(decision, generation, cancellationToken);

    public bool IsActive => _active;
    public bool HasOwnedRecoveryBoundary { get { lock (_recoveryStateSync) return _recoveryBoundaryOwned; } }
    public Guid? CurrentRecoverySessionId { get { lock (_recoveryStateSync) return _recoveryBoundaryOwned ? _recoverySessionId : null; } }

    public async ValueTask<bool> OnSteamSessionEndedAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_active || _recoveryBoundaryOwned || _recovery.HasIncompleteRecovery)
                return false;
            _vetoLatched = false;
            return true;
        }
        finally { _gate.Release(); }
    }

    public async ValueTask<MsiClawNativeModePreflightResult> InspectForPipelineAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { return InspectCoreLocked(); }
        finally { _gate.Release(); }
    }

    public async Task<MsiClawNativeModeEnterResult> EnterForPipelineAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await StartCoreLockedAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            return MsiClawNativeModeEnterResult.Failure(_recoveryBoundaryOwned, exception.GetType().Name);
        }
        finally { _gate.Release(); }
    }

    public async Task<bool> ExitForPipelineAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { return await StopCoreLockedAsync(cancellationToken).ConfigureAwait(false); }
        finally { _gate.Release(); }
    }

    private async Task<bool> ObserveRoutingDecisionCoreAsync(RoutingDecision decision, long generation, CancellationToken cancellationToken)
    {
        AppLog.Info("NativeMode", "Native routing reconciliation observed.", ("Decision", decision.Kind), ("Reason", decision.Reason), ("Generation", generation), ("Action", decision.Kind == RoutingDecisionKind.Eligible ? "EnterNativeOverride" : "RemainPassiveOrRestore"));
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (generation < _decisionGeneration)
            {
                AppLog.Info("NativeMode", "Stale canonical routing decision discarded.", ("Generation", generation), ("CurrentGeneration", _decisionGeneration));
                return false;
            }
            _decisionGeneration = generation;
            if (decision.Kind == RoutingDecisionKind.WaitingForSteam && decision.Reason == RoutingDecisionReason.SteamInactive)
                _vetoLatched = false;

            if (decision.Kind == RoutingDecisionKind.Eligible)
            {
                await StartCoreLockedAsync(cancellationToken).ConfigureAwait(false);
                return _active;
            }

            return await StopCoreLockedAsync(cancellationToken).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

    private async Task StartAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { await StartCoreLockedAsync(cancellationToken).ConfigureAwait(false); }
        finally { _gate.Release(); }
    }

    private async Task<bool> StopAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { return await StopCoreLockedAsync(cancellationToken).ConfigureAwait(false); }
        finally { _gate.Release(); }
    }

    private MsiClawNativeModePreflightResult InspectCoreLocked()
    {
        if (_active) return MsiClawNativeModePreflightResult.Failure("AlreadyActive");
        if (_recoveryBoundaryOwned) return MsiClawNativeModePreflightResult.Failure("RecoveryBoundaryAlreadyOwned");
        if (_vetoLatched) return MsiClawNativeModePreflightResult.Failure("SessionVetoLatched");
        if (_mutationAllowed is not null && !_mutationAllowed()) return MsiClawNativeModePreflightResult.Failure("MutationNotAllowed");
        if (!_powerGate.IsOpen || !_powerGate.TryAcquire(out _)) return MsiClawNativeModePreflightResult.Failure("PowerGateClosed");
        var captured = _nativeState.CaptureSnapshot();
        if (captured.Snapshot is null) return MsiClawNativeModePreflightResult.Failure("SnapshotUnavailable");
        if (!captured.AllowsMutation) return MsiClawNativeModePreflightResult.Failure("SnapshotDoesNotAllowMutation");
        var original = captured.Snapshot.Payload.Deserialize<MsiClawNativeStatePayload>();
        if (original is null) return MsiClawNativeModePreflightResult.Failure("PayloadInvalid");
        if (original.Mode != MsiClawNativeMode.XInput) return MsiClawNativeModePreflightResult.Failure("OriginalModeUnsupported");
        return MsiClawNativeModePreflightResult.Success();
    }

    private async Task<MsiClawNativeModeEnterResult> StartCoreLockedAsync(CancellationToken cancellationToken)
    {
        var preflight = InspectCoreLocked();
        if (!preflight.Succeeded) return MsiClawNativeModeEnterResult.Failure(_recoveryBoundaryOwned, preflight.Reason);
        if (!_powerGate.TryAcquire(out var token)) return MsiClawNativeModeEnterResult.Failure(false, "PowerGateClosed");
        var captured = _nativeState.CaptureSnapshot();
        if (!captured.AllowsMutation || captured.Snapshot is null) return MsiClawNativeModeEnterResult.Failure(false, "SnapshotUnavailable");
        var original = captured.Snapshot.Payload.Deserialize<MsiClawNativeStatePayload>();
        if (original is null) return MsiClawNativeModeEnterResult.Failure(false, "PayloadInvalid");
        if (original.Mode != MsiClawNativeMode.XInput) return MsiClawNativeModeEnterResult.Failure(false, "OriginalModeUnsupported");
        if (!_powerGate.IsCurrent(token)) return MsiClawNativeModeEnterResult.Failure(false, "PowerGateClosed");
        var journal = _recovery.BeginDeviceNativeStateMutation(captured);
        if (journal.Status != RecoveryStatus.Success) return MsiClawNativeModeEnterResult.Failure(false, "RecoveryJournalUnavailable");
        _snapshot = captured.Snapshot;
        lock (_recoveryStateSync) { _recoveryBoundaryOwned = true; _recoverySessionId = journal.Journal!.RecoverySessionId; }
        var identity = new MsiClawPhysicalIdentity(original.ContainerId, original.ParentInstanceId, original.InstanceId ?? string.Empty, MsiClawHardware.VendorId, original.ProductId, original.IdentityConfidence);
        var result = await _nativeState.SwitchModeAsync(MsiClawNativeMode.DirectInput, identity, cancellationToken).ConfigureAwait(false);
        if (!result.Succeeded) return MsiClawNativeModeEnterResult.Failure(true, result.Reason);
        if (!_powerGate.IsCurrent(token)) return MsiClawNativeModeEnterResult.Failure(true, "PowerGateChangedAfterModeSwitch");
        _active = true;
        _safetyMonitor = new CancellationTokenSource();
        _ = MonitorSafetyAsync(_safetyMonitor.Token);
        return MsiClawNativeModeEnterResult.Success();
    }

    private async Task<bool> StopCoreLockedAsync(CancellationToken cancellationToken, bool reportFailure = true)
    {
        if (!_active && !_recoveryBoundaryOwned) return true;
        if (_snapshot is null) { if (reportFailure) MarkRecoveryUnsafe("NativeSnapshotMissingDuringRestore"); return false; }
        if (!_powerGate.TryAcquire(out var token)) { if (reportFailure) MarkRecoveryUnsafe("PowerGateAcquireFailedDuringRestore"); return false; }
        var restored = await _nativeState.RestoreSnapshotAsync(_snapshot, cancellationToken).ConfigureAwait(false);
        if (!restored.Restored || !_powerGate.IsCurrent(token)) { if (reportFailure) MarkRecoveryUnsafe("NativeRestoreFailed"); return false; }
        if (_recoverySessionId is not { } recoverySessionId)
        {
            if (reportFailure) MarkRecoveryUnsafe("RecoverySessionIdMissingDuringRestore");
            return false;
        }
        var completed = _recovery.CompleteDeviceNativeStateMutation(recoverySessionId);
        if (completed.Status != RecoveryStatus.Success) { if (reportFailure) MarkRecoveryUnsafe("RecoveryJournalCleanupFailed"); return false; }
        _safetyMonitor?.Cancel(); _safetyMonitor?.Dispose(); _safetyMonitor = null; _snapshot = null; _active = false;
        lock (_recoveryStateSync) { _recoveryBoundaryOwned = false; _recoverySessionId = null; }
        return true;
    }

    internal async Task FailClosedAsync(string reason, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _decisionGeneration++;
            bool restored;
            try { restored = await StopCoreLockedAsync(cancellationToken, reportFailure: false).ConfigureAwait(false); }
            catch
            {
                MarkRecoveryUnsafe(reason);
                throw;
            }
            _powerGate.Close();
            MarkRecoveryUnsafe(reason);
        }
        finally { _gate.Release(); }
    }

    public async ValueTask DisposeAsync()
    { try { await StopAsync(CancellationToken.None).ConfigureAwait(false); } catch { } _safetyMonitor?.Cancel(); _safetyMonitor?.Dispose(); _gate.Dispose(); }

    private async Task MonitorSafetyAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken).ConfigureAwait(false);
                if (_mutationAllowed is not null && !_mutationAllowed())
                {
                    _vetoLatched = true;
                    AppLog.Warn("NativeMode", "External controller appeared during MSI native override; restoring original mode.", null);
                    var restored = await StopAsync(cancellationToken).ConfigureAwait(false);
                    if (!restored) AppLog.Error("NativeMode", "MSI native override restore failed after external controller hot-plug.", new InvalidOperationException("RecoveryUnsafe"));
                    return;
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
    }

    private void MarkRecoveryUnsafe(string reason)
    {
        _powerGate.Close();
        _markRecoveryUnsafe?.Invoke(reason);
    }
}
