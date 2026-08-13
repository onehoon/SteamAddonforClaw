using SteamInputAddonforClaw.Diagnostics;
using SteamInputAddonforClaw.Power;
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

internal sealed class RoutingPipelineRuntimeCoordinator : IPowerTransitionParticipant
{
    private static readonly RoutingDecision RecoveryResetDecision =
        new(RoutingDecisionKind.Indeterminate, RoutingDecisionReason.RecoveryUnsafe);
    private static readonly ControllerManagerClassification IndeterminateClassification =
        new(ControllerManagerKind.Indeterminate, ControllerManagerClassificationReason.ControllerManagerStateIndeterminate);

    private readonly ISystemStatusProvider _statusProvider;
    private readonly RoutingPipelineSessionCoordinator _sessionCoordinator;
    private readonly IReadOnlyList<IRoutingRuntimeSessionBoundaryParticipant> _sessionBoundaryParticipants;
    private readonly SemaphoreSlim _transitionGate = new(1, 1);
    private readonly Lock _cancellationSync = new();
    private CancellationTokenSource _transitionCancellation = new();
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
            using var transition = CreateTransitionCancellation(cancellationToken);
            return await ReconcileCoreAsync(transition.Token).ConfigureAwait(false);
        }
        finally
        {
            if (acquired) _transitionGate.Release();
            Interlocked.Decrement(ref _transitionOperationCount);
        }
    }

    internal async ValueTask<bool> ReconcileFreshAfterResumeAsync(CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _transitionOperationCount);
        var acquired = false;
        try
        {
            if (IsShutdownRequested) return false;
            await _transitionGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            acquired = true;
            if (IsShutdownRequested) return false;
            using var transition = CreateTransitionCancellation(cancellationToken);
            return await ReconcileFreshAfterResumeCoreAsync(transition.Token).ConfigureAwait(false);
        }
        finally
        {
            if (acquired) _transitionGate.Release();
            Interlocked.Decrement(ref _transitionOperationCount);
        }
    }

    /// <summary>
    /// True while the current process still owns an active routing session, a pending cleanup
    /// from the frozen suspend-time plan, or an in-flight transition still holding
    /// _transitionGate (e.g. a routing Enter that was still running when suspend cancelled it
    /// and quiesce timed out before that transition released the gate). All three mean
    /// current-process routing cleanup has not been retired yet. Used on resume to decide
    /// whether canonical pipeline cleanup should be retried before falling back to recovery
    /// journal replay -- an in-flight transition must still gate the journal fallback even
    /// though it hasn't reached the point of recording ActiveSession/PendingCleanup yet.
    /// </summary>
    internal bool HasResidualSessionState =>
        Volatile.Read(ref _transitionOperationCount) > 0
        || _sessionCoordinator.ActiveSession is not null
        || _sessionCoordinator.PendingCleanup is not null;

    /// <summary>
    /// Retries the canonical frozen-plan pipeline cleanup for whatever routing session/pending
    /// cleanup the current process still owns from before suspend. This does not capture
    /// SystemStatus, does not evaluate RunningAppID, and does not create a new routing session --
    /// it only retires the existing one. Intended to run on resume, before recovery journal
    /// replay, while forward mutation permission is still closed.
    /// </summary>
    internal async ValueTask<bool> RetryResidualCleanupForResumeAsync(CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _transitionOperationCount);
        var acquired = false;
        try
        {
            if (IsShutdownRequested) return false;
            await _transitionGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            acquired = true;
            if (IsShutdownRequested) return false;
            return await RetireResidualSessionCoreAsync(cancellationToken).ConfigureAwait(false);
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
            using var transition = CreateTransitionCancellation(CancellationToken.None);
            return await _sessionCoordinator.ReconcileAsync(
                RecoveryResetDecision,
                IndeterminateClassification,
                transition.Token).ConfigureAwait(false);
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
            CancelInFlightTransition();
            return await _sessionCoordinator.ReconcileAsync(
                new RoutingDecision(RoutingDecisionKind.WaitingForSteam, RoutingDecisionReason.SteamInactive),
                IndeterminateClassification,
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

    internal void CancelInFlightTransition()
    {
        lock (_cancellationSync)
        {
            _transitionCancellation.Cancel();
            _transitionCancellation = new CancellationTokenSource();
        }
    }

    public string Name => "RoutingPipelineRuntime";

    public async Task<bool> QuiesceForSuspendAsync(
        DateTimeOffset deadline,
        long cycle,
        long epoch,
        CancellationToken cancellationToken)
    {
        CancelInFlightTransition();
        AppLog.Info("Routing.Power", "Routing suspend teardown started.",
            ("Action", "SuspendTeardown"), ("ActiveSession", _sessionCoordinator.ActiveSession is not null),
            ("PendingCleanup", _sessionCoordinator.PendingCleanup is not null), ("Epoch", epoch));

        await _transitionGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var result = await _sessionCoordinator.ReconcileAsync(
                RecoveryResetDecision,
                IndeterminateClassification,
                CancellationToken.None).ConfigureAwait(false);
            var retired = result.Succeeded && _sessionCoordinator.ActiveSession is null && _sessionCoordinator.PendingCleanup is null;
            AppLog.Info("Routing.Power", "Routing suspend teardown completed.",
                ("Action", "SuspendTeardown"), ("Result", retired ? "Passive" : "Failed"),
                ("FrozenPlanRetired", retired), ("ActiveSession", _sessionCoordinator.ActiveSession is not null),
                ("PendingCleanup", _sessionCoordinator.PendingCleanup is not null), ("Epoch", epoch));
            return retired;
        }
        catch (Exception exception)
        {
            AppLog.Error("Routing.Power", "Routing suspend teardown failed.", exception,
                ("Action", "SuspendTeardown"), ("Epoch", epoch));
            return false;
        }
        finally { _transitionGate.Release(); }
    }

    public Task<bool> ReconcileAfterResumeAsync(long cycle, long epoch, CancellationToken cancellationToken) => Task.FromResult(true);

    private CancellationTokenSource CreateTransitionCancellation(CancellationToken cancellationToken)
    {
        lock (_cancellationSync)
            return CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _transitionCancellation.Token);
    }

    private async ValueTask<RoutingPipelineSessionReconcileResult> ReconcileCoreAsync(CancellationToken cancellationToken)
    {
        var snapshot = await _statusProvider.CaptureAsync(cancellationToken).ConfigureAwait(false);
        var classification = Classify(snapshot.ControllerSoftware);
        var result = await _sessionCoordinator.ReconcileAsync(
            snapshot.RoutingDecision,
            classification,
            cancellationToken).ConfigureAwait(false);
        if (IsSteamSessionEnded(snapshot.RoutingDecision) && result.Succeeded &&
            _sessionCoordinator.ActiveSession is null && _sessionCoordinator.PendingCleanup is null)
            return await ApplySessionBoundaryAsync(result).ConfigureAwait(false);
        return result;
    }

    private async ValueTask<bool> ReconcileFreshAfterResumeCoreAsync(CancellationToken cancellationToken)
    {
        // RecoveryManager restores hardware before this boundary. Retire the old frozen
        // pipeline session first; never reuse it for post-recovery re-entry.
        if (!await RetireResidualSessionCoreAsync(CancellationToken.None).ConfigureAwait(false))
            return false;

        var result = await ReconcileCoreAsync(cancellationToken).ConfigureAwait(false);
        AppLog.Info("Routing.Runtime", "Post-recovery routing reconciliation completed.",
            ("Succeeded", result.Succeeded), ("Action", result.Action), ("Reason", result.Reason));
        return result.Succeeded;
    }

    /// <summary>
    /// Retires whatever routing session/pending cleanup the process currently owns by
    /// reconciling to the recovery-reset decision against the existing frozen plan. Shared by
    /// <see cref="ReconcileFreshAfterResumeCoreAsync"/> (post-journal-recovery re-entry) and
    /// <see cref="RetryResidualCleanupForResumeAsync"/> (pre-journal-recovery residual cleanup).
    /// </summary>
    private async ValueTask<bool> RetireResidualSessionCoreAsync(CancellationToken cancellationToken)
    {
        var result = await _sessionCoordinator.ReconcileAsync(
            RecoveryResetDecision,
            IndeterminateClassification,
            cancellationToken).ConfigureAwait(false);
        return result.Succeeded && _sessionCoordinator.ActiveSession is null && _sessionCoordinator.PendingCleanup is null;
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
