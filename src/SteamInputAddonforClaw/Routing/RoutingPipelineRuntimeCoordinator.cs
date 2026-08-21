using SteamInputAddonforClaw.Diagnostics;
using SteamInputAddonforClaw.Power;
using SteamInputAddonforClaw.Status;

namespace SteamInputAddonforClaw.Routing;

internal readonly record struct RoutingRuntimeTerminationSnapshot(
    bool TransitionInProgress,
    bool HasPendingCleanup,
    bool ShutdownRequested);

internal readonly record struct RoutingSessionYieldRequest(ActiveRoutingPipelineSession Session);

internal interface IRoutingRuntimeSessionBoundaryParticipant
{
    ValueTask<bool> OnSteamSessionEndedAsync(CancellationToken cancellationToken);
}

internal sealed class RoutingPipelineRuntimeCoordinator : IPowerSuspendParticipant
{
    private static readonly RoutingDecision RecoveryResetDecision =
        new(RoutingDecisionKind.Indeterminate, RoutingDecisionReason.RecoveryUnsafe);
    private static readonly ControllerManagerClassification IndeterminateClassification =
        new(ControllerManagerKind.Indeterminate, ControllerManagerClassificationReason.ControllerManagerStateIndeterminate);

    private readonly ISystemStatusProvider _statusProvider;
    private readonly RoutingPipelineSessionCoordinator _sessionCoordinator;
    private readonly IReadOnlyList<IRoutingRuntimeSessionBoundaryParticipant> _sessionBoundaryParticipants;
    private readonly Func<CancellationToken, Task<bool>>? _beforeActiveSessionExit;
    private readonly SemaphoreSlim _transitionGate = new(1, 1);
    private readonly Lock _cancellationSync = new();
    private CancellationTokenSource _transitionCancellation = new();
    private int _sessionYieldRequested;
    private string? _sessionYieldReason;
    private int _shutdownRequested;
    private int _transitionOperationCount;

    internal RoutingPipelineRuntimeCoordinator(
        ISystemStatusProvider statusProvider,
        RoutingPipelineSessionCoordinator sessionCoordinator,
        IEnumerable<IRoutingRuntimeSessionBoundaryParticipant>? sessionBoundaryParticipants = null,
        Func<CancellationToken, Task<bool>>? beforeActiveSessionExit = null)
    {
        _statusProvider = statusProvider ?? throw new ArgumentNullException(nameof(statusProvider));
        _sessionCoordinator = sessionCoordinator ?? throw new ArgumentNullException(nameof(sessionCoordinator));
        _sessionBoundaryParticipants = (sessionBoundaryParticipants ?? []).ToArray();
        _beforeActiveSessionExit = beforeActiveSessionExit;
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

    internal async ValueTask<RoutingPipelineSessionReconcileResult> FailClosedAsync(bool yieldCurrentSteamSession = false)
    {
        if (yieldCurrentSteamSession)
            Volatile.Write(ref _sessionYieldRequested, 1);
        // Fail-close must stop any in-flight forward transition before waiting for the gate --
        // otherwise a caller reporting a fault that already invalidates the active session (e.g.
        // an owned physical-input session that just died) would sit behind a routing Enter that is
        // still free to keep mutating forward (attaching Steam output, etc.) after that authority
        // was already lost. Cancelling here routes that in-flight Enter through its own normal
        // executor-cancellation + rollback path instead. Mirrors ShutdownAsync's existing preempt.
        CancelInFlightTransition();
        Interlocked.Increment(ref _transitionOperationCount);
        var acquired = false;
        try
        {
            if (IsShutdownRequested) return RuntimeStoppedResult();
            await _transitionGate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
            acquired = true;
            if (IsShutdownRequested) return RuntimeStoppedResult();
            if (yieldCurrentSteamSession)
            {
                _sessionYieldReason = "ExternalNativeTakeover";
                AppLog.Warn("Routing.Runtime", "ExternalNativeTakeoverDetected", null,
                    ("Action", "YieldUntilSteamSessionEnd"));
            }
            using var transition = CreateTransitionCancellation(CancellationToken.None);

            return await RetireForFailCloseCoreAsync(transition.Token).ConfigureAwait(false);
        }
        finally
        {
            if (acquired) _transitionGate.Release();
            Interlocked.Decrement(ref _transitionOperationCount);
        }
    }

    internal RoutingSessionYieldRequest? RequestCurrentSessionYield()
    {
        var session = _sessionCoordinator.ActiveSession;
        if (session is null) return null;
        Volatile.Write(ref _sessionYieldRequested, 1);
        CancelInFlightTransition();
        return new RoutingSessionYieldRequest(session);
    }

    internal bool IsCurrentSessionYieldRequest(RoutingSessionYieldRequest request) =>
        (_sessionCoordinator.ActiveSession is { } active && ReferenceEquals(active, request.Session))
        || (_sessionCoordinator.PendingCleanup is { } pending && ReferenceEquals(pending.Session, request.Session));

    internal async ValueTask<RoutingPipelineSessionReconcileResult> ShutdownAsync()
    {
        Interlocked.Exchange(ref _shutdownRequested, 1);
        CancelInFlightTransition();
        Interlocked.Increment(ref _transitionOperationCount);
        var acquired = false;
        try
        {
            await _transitionGate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
            acquired = true;
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

    /// <summary>Read-only routing session state for UI presentation. Never mutates the session.</summary>
    internal RoutingOperationalState CurrentOperationalState => _sessionCoordinator.CurrentState;

    /// <summary>True only when an active session exists and its plan has the Steam output stage enabled.</summary>
    internal bool ActiveSessionHasSteamOutputEnabled =>
        _sessionCoordinator.ActiveSession?.Plan.SteamOutput == RoutingStageMode.Enabled;

    /// <summary>True when an independent interactive presentation mutation may begin.</summary>
    internal bool CanApplyInteractivePresentation =>
        !IsShutdownRequested && Volatile.Read(ref _transitionOperationCount) == 0;

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
            if (!IsShutdownRequested)
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
        Interlocked.Increment(ref _transitionOperationCount);
        AppLog.Info("Routing.Power", "Routing suspend teardown started.",
            ("Action", "SuspendTeardown"), ("ActiveSession", _sessionCoordinator.ActiveSession is not null),
            ("PendingCleanup", _sessionCoordinator.PendingCleanup is not null), ("Epoch", epoch));

        var acquired = false;
        try
        {
            await _transitionGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            acquired = true;
            if ((_sessionCoordinator.ActiveSession is not null || _sessionCoordinator.PendingCleanup is not null) &&
                _beforeActiveSessionExit is not null)
            {
                bool retiredPresentation;
                try
                {
                    retiredPresentation = await _beforeActiveSessionExit(cancellationToken).ConfigureAwait(false);
                }
                catch (Exception exception)
                {
                    AppLog.Warn("Routing.Power", "X360 presentation retirement blocked suspend teardown.",
                        exception, fields: [("Action", "SuspendTeardown"),
                            ("Reason", exception.GetType().Name)]);
                    return false;
                }

                if (!retiredPresentation)
                {
                    AppLog.Warn("Routing.Power", "X360 presentation retirement blocked suspend teardown.",
                        fields: [("Action", "SuspendTeardown"),
                            ("Reason", "Xbox360PresentationRetirementFailed")]);
                    return false;
                }
            }

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
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            AppLog.Error("Routing.Power", "Routing suspend teardown failed.", exception,
                ("Action", "SuspendTeardown"), ("Epoch", epoch));
            return false;
        }
        finally
        {
            if (acquired) _transitionGate.Release();
            Interlocked.Decrement(ref _transitionOperationCount);
        }
    }

    private CancellationTokenSource CreateTransitionCancellation(CancellationToken cancellationToken)
    {
        lock (_cancellationSync)
            return CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _transitionCancellation.Token);
    }

    private async ValueTask<RoutingPipelineSessionReconcileResult> ReconcileCoreAsync(CancellationToken cancellationToken)
    {
        var snapshot = await _statusProvider.CaptureAsync(cancellationToken).ConfigureAwait(false);
        if (IsSessionYieldAdmissionClosed && snapshot.RoutingDecision.Kind == RoutingDecisionKind.Eligible)
        {
            if (_sessionCoordinator.ActiveSession is not null || _sessionCoordinator.PendingCleanup is not null)
                return await RetireForFailCloseCoreAsync(cancellationToken).ConfigureAwait(false);
            AppLog.Debug("Routing.Runtime", "RouteDemandIgnored",
                ("Reason", "ExternalNativeTakeoverLatched"), ("Decision", snapshot.RoutingDecision.Kind),
                ("OperationalState", RoutingOperationalState.Passive), ("Action", "Yield"));
            return new(true, _sessionCoordinator.CurrentState, RoutingActionKind.None, "ExternalNativeTakeoverLatched");
        }
        var classification = Classify(snapshot.ControllerSoftware);
        if (_sessionCoordinator.ActiveSession is not null &&
            _sessionCoordinator.PendingCleanup is null &&
            snapshot.RoutingDecision.Kind != RoutingDecisionKind.Eligible &&
            _beforeActiveSessionExit is not null)
        {
            bool retired;
            try
            {
                retired = await _beforeActiveSessionExit(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                AppLog.Warn("Routing.Runtime", "X360 presentation retirement blocked outer route exit.",
                    exception, fields: [("Reason", exception.GetType().Name)]);
                retired = false;
            }

            if (!retired)
            {
                AppLog.Warn("Routing.Runtime", "X360 presentation retirement blocked outer route exit.",
                    fields: [("Reason", "Xbox360PresentationRetirementFailed")]);
                return new(false, _sessionCoordinator.CurrentState, RoutingActionKind.ExitOverride,
                    "Xbox360PresentationRetirementFailed");
            }
        }

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
        if (IsSessionYieldAdmissionClosed)
        {
            var hadCommittedYield = _sessionYieldReason is not null;
            _sessionYieldReason = null;
            Volatile.Write(ref _sessionYieldRequested, 0);
            if (hadCommittedYield)
                AppLog.Info("Routing.Runtime", "ExternalNativeTakeoverYieldCleared", ("Reason", "SteamSessionEnded"));
        }
        return result;
    }

    private async ValueTask<RoutingPipelineSessionReconcileResult> RetireForFailCloseCoreAsync(CancellationToken cancellationToken)
    {
        if ((_sessionCoordinator.ActiveSession is not null || _sessionCoordinator.PendingCleanup is not null) &&
            _beforeActiveSessionExit is not null)
        {
            bool retired;
            try { retired = await _beforeActiveSessionExit(cancellationToken).ConfigureAwait(false); }
            catch (Exception exception)
            {
                AppLog.Warn("Routing.Runtime", "X360 presentation retirement blocked outer fail-close rollback.", exception,
                    ("Action", "FailClosed"), ("Reason", exception.GetType().Name));
                return new(false, _sessionCoordinator.CurrentState, RoutingActionKind.None, "Xbox360PresentationRetirementFailed");
            }
            if (!retired)
                return new(false, _sessionCoordinator.CurrentState, RoutingActionKind.None, "Xbox360PresentationRetirementFailed");
        }

        return await _sessionCoordinator.ReconcileAsync(RecoveryResetDecision, IndeterminateClassification, cancellationToken).ConfigureAwait(false);
    }

    private bool IsSessionYieldAdmissionClosed =>
        Volatile.Read(ref _sessionYieldRequested) != 0 || _sessionYieldReason is not null;

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
