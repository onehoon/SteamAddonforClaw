using SteamInputAddonforClaw.Diagnostics;
using SteamInputAddonforClaw.Status;

namespace SteamInputAddonforClaw.Routing;

internal readonly record struct RoutingRuntimeTerminationSnapshot(
    bool TransitionInProgress,
    bool HasPendingCleanup,
    bool ShutdownRequested);

internal interface IRoutingRuntimeSessionBoundaryParticipant
{
    ValueTask<bool> OnSteamSessionEndedAsync(CancellationToken cancellationToken);
}

internal sealed class RoutingPipelineRuntimeCoordinator
{
    private static readonly RoutingDecision RecoveryResetDecision =
        new(RoutingDecisionKind.Indeterminate, RoutingDecisionReason.RecoveryUnsafe);
    private static readonly ControllerManagerClassification IndeterminateClassification =
        new(ControllerManagerKind.Indeterminate, ControllerManagerClassificationReason.ControllerManagerStateIndeterminate);

    private readonly ISystemStatusProvider _statusProvider;
    private readonly RoutingPipelineSessionCoordinator _sessionCoordinator;
    private readonly IReadOnlyList<IRoutingRuntimeSessionBoundaryParticipant> _sessionBoundaryParticipants;
    private readonly SemaphoreSlim _transitionGate = new(1, 1);
    private int _shutdownRequested;
    private int _transitionOperationCount;

    internal RoutingPipelineRuntimeCoordinator(
        ISystemStatusProvider statusProvider,
        RoutingPipelineSessionCoordinator sessionCoordinator,
        IEnumerable<IRoutingRuntimeSessionBoundaryParticipant>? sessionBoundaryParticipants = null)
    {
        _statusProvider = statusProvider ?? throw new ArgumentNullException(nameof(statusProvider));
        _sessionCoordinator = sessionCoordinator ?? throw new ArgumentNullException(nameof(sessionCoordinator));
        _sessionBoundaryParticipants = (sessionBoundaryParticipants ?? []).ToArray();
    }

    internal async ValueTask<RoutingPipelineSessionReconcileResult> ReconcileAsync(CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _transitionOperationCount);
        var acquired = false;
        try
        {
            if (IsShutdownRequested) return RuntimeStoppedResult();
            await _transitionGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            acquired = true;
            if (IsShutdownRequested) return RuntimeStoppedResult();
            return await ReconcileCoreAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            if (acquired) _transitionGate.Release();
            Interlocked.Decrement(ref _transitionOperationCount);
        }
    }

    internal async ValueTask<bool> ReconcileAfterRecoveryAsync(CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _transitionOperationCount);
        var acquired = false;
        try
        {
            if (IsShutdownRequested) return false;
            await _transitionGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            acquired = true;
            if (IsShutdownRequested) return false;
            return await ReconcileAfterRecoveryCoreAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            if (acquired) _transitionGate.Release();
            Interlocked.Decrement(ref _transitionOperationCount);
        }
    }

    internal async ValueTask<RoutingPipelineSessionReconcileResult> FailClosedAsync()
    {
        Interlocked.Increment(ref _transitionOperationCount);
        var acquired = false;
        try
        {
            if (IsShutdownRequested) return RuntimeStoppedResult();
            await _transitionGate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
            acquired = true;
            if (IsShutdownRequested) return RuntimeStoppedResult();
            return await _sessionCoordinator.ReconcileAsync(
                RecoveryResetDecision,
                IndeterminateClassification,
                RoutingExperimentOptions.None,
                CancellationToken.None).ConfigureAwait(false);
        }
        finally
        {
            if (acquired) _transitionGate.Release();
            Interlocked.Decrement(ref _transitionOperationCount);
        }
    }

    internal async ValueTask<RoutingPipelineSessionReconcileResult> ShutdownAsync()
    {
        Interlocked.Exchange(ref _shutdownRequested, 1);
        Interlocked.Increment(ref _transitionOperationCount);
        var acquired = false;
        try
        {
            await _transitionGate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
            acquired = true;
            return await _sessionCoordinator.ReconcileAsync(
                new RoutingDecision(RoutingDecisionKind.WaitingForSteam, RoutingDecisionReason.SteamInactive),
                IndeterminateClassification,
                RoutingExperimentOptions.None,
                CancellationToken.None).ConfigureAwait(false);
        }
        finally
        {
            if (acquired) _transitionGate.Release();
            Interlocked.Decrement(ref _transitionOperationCount);
        }
    }

    internal RoutingRuntimeTerminationSnapshot CaptureTerminationSnapshot() =>
        new(
            Volatile.Read(ref _transitionOperationCount) > 0,
            _sessionCoordinator.PendingCleanup is not null,
            IsShutdownRequested);

    private async ValueTask<RoutingPipelineSessionReconcileResult> ReconcileCoreAsync(CancellationToken cancellationToken)
    {
        var snapshot = await _statusProvider.CaptureAsync(cancellationToken).ConfigureAwait(false);
        var classification = Classify(snapshot.ControllerSoftware);
        var result = await _sessionCoordinator.ReconcileAsync(
            snapshot.RoutingDecision,
            classification,
            RoutingExperimentOptions.None,
            cancellationToken).ConfigureAwait(false);
        if (IsSteamSessionEnded(snapshot.RoutingDecision) && result.Succeeded &&
            _sessionCoordinator.ActiveSession is null && _sessionCoordinator.PendingCleanup is null)
            return await ApplySessionBoundaryAsync(result).ConfigureAwait(false);
        return result;
    }

    private async ValueTask<bool> ReconcileAfterRecoveryCoreAsync(CancellationToken cancellationToken)
    {
        // RecoveryManager restores hardware before this boundary. Retire the old frozen
        // pipeline session first; never reuse it for post-recovery re-entry.
        var retirement = await _sessionCoordinator.ReconcileAsync(
            RecoveryResetDecision,
            IndeterminateClassification,
            RoutingExperimentOptions.None,
            CancellationToken.None).ConfigureAwait(false);
        if (!retirement.Succeeded || _sessionCoordinator.ActiveSession is not null || _sessionCoordinator.PendingCleanup is not null)
            return false;

        var result = await ReconcileCoreAsync(cancellationToken).ConfigureAwait(false);
        AppLog.Info("Routing.Runtime", "Post-recovery routing reconciliation completed.",
            ("Succeeded", result.Succeeded), ("Action", result.Action), ("Reason", result.Reason));
        return result.Succeeded;
    }

    private async ValueTask<RoutingPipelineSessionReconcileResult> ApplySessionBoundaryAsync(RoutingPipelineSessionReconcileResult result)
    {
        foreach (var participant in _sessionBoundaryParticipants)
        {
            try
            {
                if (!await participant.OnSteamSessionEndedAsync(CancellationToken.None).ConfigureAwait(false))
                    return new(false, result.State, result.Action, "SteamSessionBoundaryFailed");
            }
            catch (Exception exception)
            {
                AppLog.Error("Routing.Runtime", "Steam session boundary participant failed.", exception);
                return new(false, result.State, result.Action, "SteamSessionBoundaryFailed");
            }
        }
        return result;
    }

    private bool IsShutdownRequested => Volatile.Read(ref _shutdownRequested) != 0;
    private static bool IsSteamSessionEnded(RoutingDecision decision) =>
        decision.Kind == RoutingDecisionKind.WaitingForSteam && decision.Reason == RoutingDecisionReason.SteamInactive;
    private RoutingPipelineSessionReconcileResult RuntimeStoppedResult() =>
        new(true, _sessionCoordinator.CurrentState, RoutingActionKind.None, "RuntimeShuttingDown");

    private static ControllerManagerClassification Classify(IReadOnlyList<ControllerSoftwareStatus> software)
    {
        if (!ControllerSoftwareSnapshot.TryCreate(software, out var snapshot) || snapshot is null)
            return IndeterminateClassification;
        return ControllerManagerClassifier.Classify(snapshot);
    }
}
