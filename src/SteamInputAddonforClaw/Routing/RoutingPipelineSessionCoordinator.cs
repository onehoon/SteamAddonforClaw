using SteamInputAddonforClaw.Diagnostics;
using SteamInputAddonforClaw.Status;

namespace SteamInputAddonforClaw.Routing;

internal enum RoutingEnvironmentStrategyKind
{
    StockCenterM = 0,
    ClawTweaks = 1,
    Unsupported = 2
}

internal sealed record ActiveRoutingPipelineSession(
    RoutingEnvironmentStrategyKind StrategyKind,
    ControllerManagerClassification Classification,
    RoutingPipelinePlan Plan);

internal sealed record PendingRoutingPipelineCleanup(
    ActiveRoutingPipelineSession Session,
    RoutingActionPlan ActionPlan);

internal sealed record RoutingPipelineSessionReconcileResult(
    bool Succeeded,
    RoutingOperationalState State,
    RoutingActionKind Action,
    string Reason);

internal sealed class RoutingPipelineSessionCoordinator
{
    private readonly RoutingCoordinator _routingCoordinator = new();
    private readonly IRoutingPipelineExecutor _pipelineExecutor;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Lock _sessionSync = new();
    private ActiveRoutingPipelineSession? _activeSession;
    private PendingRoutingPipelineCleanup? _pendingCleanup;

    internal RoutingPipelineSessionCoordinator(IRoutingPipelineExecutor pipelineExecutor)
    {
        _pipelineExecutor = pipelineExecutor ?? throw new ArgumentNullException(nameof(pipelineExecutor));
    }

    internal RoutingOperationalState CurrentState => _routingCoordinator.CurrentState;

    internal ActiveRoutingPipelineSession? ActiveSession
    {
        get
        {
            lock (_sessionSync) return _activeSession;
        }
    }

    internal PendingRoutingPipelineCleanup? PendingCleanup
    {
        get
        {
            lock (_sessionSync) return _pendingCleanup;
        }
    }

    internal async ValueTask<RoutingPipelineSessionReconcileResult> ReconcileAsync(
        RoutingDecision decision,
        ControllerManagerClassification classification,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (PendingCleanup is not null)
            {
                var cleanupResult = await RetryPendingCleanupAsync().ConfigureAwait(false);
                if (!cleanupResult.Succeeded) return cleanupResult;
                return cleanupResult;
            }

            var actionPlan = _routingCoordinator.Plan(decision);
            if (actionPlan.Action == RoutingActionKind.None)
                return Success(actionPlan.Action, actionPlan.Reason.ToString());

            return actionPlan.Action switch
            {
                RoutingActionKind.EnterOverride => await EnterAsync(actionPlan, classification, cancellationToken).ConfigureAwait(false),
                RoutingActionKind.ExitOverride => await ExitAsync(actionPlan, cancellationToken).ConfigureAwait(false),
                _ => Failure(actionPlan.Action, "UnknownRoutingAction")
            };
        }
        finally
        {
            _gate.Release();
        }
    }

    private async ValueTask<RoutingPipelineSessionReconcileResult> EnterAsync(
        RoutingActionPlan actionPlan,
        ControllerManagerClassification classification,
        CancellationToken cancellationToken)
    {
        if (ActiveSession is not null)
            return Failure(actionPlan.Action, "ActiveSessionAlreadyExists");

        if (!TrySelectEnvironmentPlan(classification, out var environmentKind, out var plan))
            return Failure(actionPlan.Action, "UnsupportedEnvironmentStrategy");

        var candidate = new ActiveRoutingPipelineSession(environmentKind, classification, plan);
        LogPlan("Enter candidate", candidate, actionPlan);

        RoutingPipelineExecutionResult execution;
        try
        {
            execution = await _pipelineExecutor.ExecuteAsync(plan, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException exception)
        {
            if (RoutingPipelineCancellationMetadata.TryGet(exception, out var rollback) && !rollback.Succeeded)
                SetPendingCleanup(new(candidate, actionPlan));
            else
                ClearActiveSession();
            throw;
        }

        if (!execution.Succeeded)
        {
            if (!execution.RollbackSucceeded)
                SetPendingCleanup(new(candidate, actionPlan));
            else
                ClearActiveSession();
            return Failure(actionPlan.Action, $"PipelineEnterFailed:{execution.Reason}");
        }

        if (!_routingCoordinator.TryCommit(actionPlan))
        {
            var rollback = await _pipelineExecutor.RollbackAsync(plan, CancellationToken.None).ConfigureAwait(false);
            if (!rollback.Succeeded)
                SetPendingCleanup(new(candidate, actionPlan));
            ClearActiveSession();
            return Failure(actionPlan.Action, "RoutingCommitFailedAfterEnter");
        }

        SetActiveSession(candidate);
        AppLog.Info("Routing.Session", "Routing session entered.",
            ("Action", actionPlan.Action), ("Result", "Active"), ("Strategy", candidate.StrategyKind),
            ("Classification", candidate.Classification.Kind));
        return Success(actionPlan.Action, "EnteredOverride");
    }

    private async ValueTask<RoutingPipelineSessionReconcileResult> ExitAsync(
        RoutingActionPlan actionPlan,
        CancellationToken cancellationToken)
    {
        var session = ActiveSession;
        if (session is null)
            return Failure(actionPlan.Action, "ActiveSessionMissing");

        AppLog.Info("Routing.Session", "Routing session exit started.",
            ("Action", actionPlan.Action), ("Strategy", session.StrategyKind), ("PlanSource", "FrozenSession"));

        var rollback = await _pipelineExecutor.RollbackAsync(session.Plan, cancellationToken).ConfigureAwait(false);
        if (!rollback.Succeeded)
        {
            SetPendingCleanup(new(session, actionPlan));
            AppLog.Warn("Routing.Session", "Routing session rollback failed.", fields:
                [("Action", actionPlan.Action), ("Result", "RollbackFailed"), ("FailedStage", rollback.FailedStage),
                 ("Reason", rollback.Reason), ("SessionPreserved", true)]);
            return Failure(actionPlan.Action, $"PipelineRollbackFailed:{rollback.Reason}");
        }

        if (!_routingCoordinator.TryCommit(actionPlan))
        {
            AppLog.Error("Routing.Session", "Routing commit failed after rollback.",
                new InvalidOperationException("Routing state commit failed after successful pipeline rollback."));
            return Failure(actionPlan.Action, "RoutingCommitFailedAfterRollback");
        }

        ClearActiveSession();
        AppLog.Info("Routing.Session", "Routing session exited.",
            ("Action", actionPlan.Action), ("Result", "Passive"));
        return Success(actionPlan.Action, "ExitedOverride");
    }

    private void SetActiveSession(ActiveRoutingPipelineSession session)
    {
        lock (_sessionSync) _activeSession = session;
    }

    private void SetPendingCleanup(PendingRoutingPipelineCleanup cleanup)
    {
        lock (_sessionSync) _pendingCleanup = cleanup;
    }

    private void ClearActiveSession()
    {
        lock (_sessionSync) _activeSession = null;
    }

    private void ClearPendingCleanup()
    {
        lock (_sessionSync) _pendingCleanup = null;
    }

    private async ValueTask<RoutingPipelineSessionReconcileResult> RetryPendingCleanupAsync()
    {
        var pending = PendingCleanup;
        if (pending is null) return Success(RoutingActionKind.None, "NoPendingCleanup");

        var rollback = await _pipelineExecutor.RollbackAsync(pending.Session.Plan, CancellationToken.None).ConfigureAwait(false);
        if (!rollback.Succeeded)
            return Failure(pending.ActionPlan.Action, $"PendingCleanupFailed:{rollback.Reason}");

        if (pending.ActionPlan.Action == RoutingActionKind.ExitOverride && !_routingCoordinator.TryCommit(pending.ActionPlan))
            return Failure(pending.ActionPlan.Action, "RoutingCommitFailedAfterPendingCleanup");

        if (pending.ActionPlan.Action == RoutingActionKind.ExitOverride)
            ClearActiveSession();

        ClearPendingCleanup();
        return new(true, CurrentState, pending.ActionPlan.Action, "PendingCleanupCompleted");
    }

    private RoutingPipelineSessionReconcileResult Success(RoutingActionKind action, string reason) =>
        new(true, action switch
        {
            RoutingActionKind.EnterOverride => RoutingOperationalState.OverrideActive,
            RoutingActionKind.ExitOverride => RoutingOperationalState.Passive,
            _ => CurrentState
        }, action, reason);

    private RoutingPipelineSessionReconcileResult Failure(RoutingActionKind action, string reason) =>
        new(false, CurrentState, action, reason);

    private static bool TrySelectEnvironmentPlan(
        ControllerManagerClassification classification,
        out RoutingEnvironmentStrategyKind environmentKind,
        out RoutingPipelinePlan plan)
    {
        switch (classification.Kind)
        {
            case ControllerManagerKind.None:
                environmentKind = RoutingEnvironmentStrategyKind.StockCenterM;
                plan = RoutingPipelinePlan.StockCenterM;
                return true;
            case ControllerManagerKind.ClawTweaks:
                environmentKind = RoutingEnvironmentStrategyKind.ClawTweaks;
                plan = RoutingPipelinePlan.AllDisabled;
                return true;
            default:
                environmentKind = RoutingEnvironmentStrategyKind.Unsupported;
                plan = RoutingPipelinePlan.AllDisabled;
                return false;
        }
    }

    private static void LogPlan(string message, ActiveRoutingPipelineSession session, RoutingActionPlan actionPlan)
    {
        var plan = session.Plan;
        AppLog.Info("Routing.Session", message,
            ("Action", actionPlan.Action), ("Strategy", session.StrategyKind),
            ("Classification", session.Classification.Kind), ("NativeMode", plan.NativeMode),
            ("PhysicalInput", plan.PhysicalInput), ("PhysicalIsolation", plan.PhysicalIsolation),
            ("ThirdPartyIsolation", plan.ThirdPartyIsolation), ("SteamOutput", plan.SteamOutput),
            ("XboxOutput", plan.XboxOutput), ("GameBarRouting", plan.GameBarRouting));
    }
}
