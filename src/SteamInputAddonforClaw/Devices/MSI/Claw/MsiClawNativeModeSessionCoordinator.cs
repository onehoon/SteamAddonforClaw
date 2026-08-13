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
    private readonly RecoverySafetyState _recoverySafety;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private DeviceNativeStateSnapshot? _snapshot;
    private bool _active;
    private bool _recoveryBoundaryOwned;
    private Guid? _recoverySessionId;
    private long _decisionGeneration;
    private long? _unsafeRecoveryVersion;
    private bool _routingFaultLatched;
    private readonly Lock _recoveryStateSync = new();

    internal MsiClawNativeModeSessionCoordinator(MsiClawNativeStateManager nativeState, RecoveryManager recovery, PowerMutationGate powerGate, RecoverySafetyState recoverySafety)
    {
        _nativeState = nativeState; _recovery = recovery; _powerGate = powerGate; _recoverySafety = recoverySafety;
    }

    public string Name => "MsiClawNativeModeSession";

    public Task<bool> QuiesceForSuspendAsync(DateTimeOffset deadline, long cycle, long epoch, CancellationToken cancellationToken)
    {
        // The canonical routing runtime owns complete suspend teardown through the pipeline.
        // NativeMode must not independently restore around pipeline rollback barriers.
        return Task.FromResult(true);
    }

    public Task<bool> ReconcileAfterResumeAsync(long cycle, long epoch, CancellationToken cancellationToken)
    {
        // RecoveryManager may have restored the original snapshot. Force the next effective
        // session observation to perform a fresh capture and transition.
        _active = false;
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

    internal async Task LatchRoutingFaultAsync(string reason, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { LatchRoutingFaultCore(reason); }
        finally { _gate.Release(); }
    }

    public async ValueTask<bool> OnSteamSessionEndedAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_active || _recoveryBoundaryOwned || _recovery.HasIncompleteRecovery)
                return false;
            _routingFaultLatched = false;
            AppLog.Debug("NativeMode", "RoutingFaultLatchCleared", ("Reason", "SteamSessionEnded"));
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
        if (_routingFaultLatched) return MsiClawNativeModePreflightResult.Failure("RoutingFaultLatched");
        if (_recoverySafety.Current != RecoverySafety.Safe)
        {
            AppLog.Debug("NativeMode", "NativeForwardMutationDenied", ("RecoverySafety", _recoverySafety.Current));
            return MsiClawNativeModePreflightResult.Failure("RecoverySafetyNotSafe");
        }
        if (!_powerGate.IsOpen || !_powerGate.TryAcquire(out _)) return MsiClawNativeModePreflightResult.Failure("PowerGateClosed");
        var captured = _nativeState.CaptureSnapshot();
        if (captured.Snapshot is null) return MsiClawNativeModePreflightResult.Failure("SnapshotUnavailable");
        if (!captured.AllowsMutation) return MsiClawNativeModePreflightResult.Failure("SnapshotDoesNotAllowMutation");
        var original = captured.Snapshot.Payload.Deserialize<MsiClawNativeStatePayload>();
        if (original is null) return MsiClawNativeModePreflightResult.Failure("PayloadInvalid");
        if (original.IdentityConfidence != MsiClawIdentityConfidence.Strong) return MsiClawNativeModePreflightResult.Failure("PhysicalIdentityNotStrong");
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
        if (_recoverySafety.Current != RecoverySafety.Safe) return MsiClawNativeModeEnterResult.Failure(false, "RecoverySafetyNotSafe");
        var journal = _recovery.BeginDeviceNativeStateMutation(captured);
        if (journal.Status != RecoveryStatus.Success) return MsiClawNativeModeEnterResult.Failure(false, "RecoveryJournalUnavailable");
        _snapshot = captured.Snapshot;
        lock (_recoveryStateSync) { _recoveryBoundaryOwned = true; _recoverySessionId = journal.Journal!.RecoverySessionId; }
        var identity = MsiClawPhysicalIdentity.FromPayload(original);
        var result = await _nativeState.SwitchModeAsync(MsiClawNativeMode.DirectInput, identity, cancellationToken).ConfigureAwait(false);
        if (!result.Succeeded) return MsiClawNativeModeEnterResult.Failure(true, result.Reason);
        if (!_powerGate.IsCurrent(token)) return MsiClawNativeModeEnterResult.Failure(true, "PowerGateChangedAfterModeSwitch");
        _active = true;
        return MsiClawNativeModeEnterResult.Success();
    }

    private async Task<bool> StopCoreLockedAsync(CancellationToken cancellationToken, bool reportFailure = true)
    {
        if (!_active && !_recoveryBoundaryOwned) return true;
        if (_snapshot is null) { if (reportFailure) MarkRecoveryUnsafe("NativeSnapshotMissingDuringRestore"); return false; }
        if (!_powerGate.TryAcquireCleanup(out var token))
        {
            AppLog.Debug("NativeMode", "NativeRecoveryDeferredByPowerGate", ("RecoveryBoundaryOwned", _recoveryBoundaryOwned));
            return false;
        }
        NativeStateRestoreResult restored;
        try { restored = await _nativeState.RestoreSnapshotAsync(_snapshot, cancellationToken).ConfigureAwait(false); }
        catch
        {
            if (reportFailure) MarkRecoveryUnsafe("NativeRestoreFailed");
            throw;
        }
        if (!restored.Restored || !_powerGate.IsCurrentCleanup(token)) { if (reportFailure) MarkRecoveryUnsafe("NativeRestoreFailed"); return false; }
        if (_recoverySessionId is not { } recoverySessionId)
        {
            if (reportFailure) MarkRecoveryUnsafe("RecoverySessionIdMissingDuringRestore");
            return false;
        }
        var completed = _recovery.CompleteDeviceNativeStateMutation(recoverySessionId);
        if (completed.Status != RecoveryStatus.Success) { if (reportFailure) MarkRecoveryUnsafe("RecoveryJournalCleanupFailed"); return false; }
        _snapshot = null; _active = false;
        lock (_recoveryStateSync) { _recoveryBoundaryOwned = false; _recoverySessionId = null; }
        var recoverySafetyCleared = false;
        if (!_recovery.HasIncompleteRecovery && _unsafeRecoveryVersion is { } unsafeVersion &&
            _powerGate.TryCommitMutation(token, () => recoverySafetyCleared = _recoverySafety.TrySet(unsafeVersion, RecoverySafety.Safe)) && recoverySafetyCleared)
            _unsafeRecoveryVersion = null;
        AppLog.Debug("NativeMode", "NativeRecoveryVerified", ("JournalRemaining", _recovery.HasIncompleteRecovery), ("RecoverySafety", _recoverySafety.Current));
        return true;
    }

    internal async Task FailClosedAsync(string reason, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            LatchRoutingFaultCore(reason);
            await StopCoreLockedAsync(cancellationToken, reportFailure: true).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

    public async ValueTask DisposeAsync()
    { try { await StopAsync(CancellationToken.None).ConfigureAwait(false); } catch { } _gate.Dispose(); }

    private void LatchRoutingFaultCore(string reason)
    {
        _decisionGeneration++;
        _routingFaultLatched = true;
        AppLog.Debug("NativeMode", "RoutingFaultLatched", ("Reason", reason));
    }

    private void MarkRecoveryUnsafe(string reason)
    {
        if (_unsafeRecoveryVersion is { } ownedVersion && _recoverySafety.IsCurrent(ownedVersion, RecoverySafety.Unsafe))
        {
            AppLog.Debug("NativeMode", "NativeRecoveryUnsafeAlreadyOwned", ("Reason", reason));
            return;
        }
        _unsafeRecoveryVersion = null;
        if (_recoverySafety.TryClaimUnsafe(out var claimedVersion))
            _unsafeRecoveryVersion = claimedVersion;
        else
            AppLog.Debug("NativeMode", "NativeRecoveryUnsafeOwnedByAnotherComponent", ("Reason", reason), ("RecoverySafety", _recoverySafety.Current));
        AppLog.Error("Recovery", "MSI native mode recovery became unsafe.", new InvalidOperationException(reason), ("Reason", reason));
    }
}
