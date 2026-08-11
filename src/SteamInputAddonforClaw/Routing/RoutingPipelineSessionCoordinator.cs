using SteamInputAddonforClaw.Diagnostics;
using SteamInputAddonforClaw.Status;

namespace SteamInputAddonforClaw.Routing;

internal sealed record ActiveRoutingPipelineSession(
    RoutingEnvironmentStrategyKind StrategyKind,
    ControllerManagerClassification Classification,
    RoutingPipelinePlan Plan);

internal sealed record RoutingPipelineSessionReconcileResult(
    bool Succeeded,
    RoutingOperationalState State,
    RoutingActionKind Action,
    string Reason);

internal sealed class RoutingPipelineSessionCoordinator
{
    private readonly RoutingCoordinator _routingCoordinator = new();
    private readonly IRoutingEnvironmentStrategyResolver _strategyResolver;
    private readonly IRoutingPipelineExecutor _pipelineExecutor;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Lock _sessionSync = new();
    private ActiveRoutingPipelineSession? _activeSession;

    internal RoutingPipelineSessionCoordinator(
        IRoutingEnvironmentStrategyResolver strategyResolver,
        IRoutingPipelineExecutor pipelineExecutor)
    {
        _strategyResolver = strategyResolver ?? throw new ArgumentNullException(nameof(strategyResolver));
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

    internal async ValueTask<RoutingPipelineSessionReconcileResult> ReconcileAsync(
        RoutingDecision decision,
        ControllerManagerClassification classification,
        RoutingExperimentOptions experimentOptions,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var actionPlan = _routingCoordinator.Plan(decision);
            if (actionPlan.Action == RoutingActionKind.None)
                return Success(actionPlan.Action, actionPlan.Reason.ToString());

            return actionPlan.Action switch
            {
                RoutingActionKind.EnterOverride => await EnterAsync(actionPlan, classification, experimentOptions, cancellationToken).ConfigureAwait(false),
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
        RoutingExperimentOptions experimentOptions,
        CancellationToken cancellationToken)
    {
        if (ActiveSession is not null)
            return Failure(actionPlan.Action, "ActiveSessionAlreadyExists");

        var strategy = _strategyResolver.Resolve(classification);
        if (strategy.Kind == RoutingEnvironmentStrategyKind.Unsupported)
            return Failure(actionPlan.Action, "UnsupportedEnvironmentStrategy");

        var plan = RoutingExperimentPlanBuilder.Build(strategy, experimentOptions);
        var candidate = new ActiveRoutingPipelineSession(strategy.Kind, classification, plan);
        LogPlan("Enter candidate", candidate, actionPlan);

        RoutingPipelineExecutionResult execution;
        try
        {
            execution = await _pipelineExecutor.ExecuteAsync(plan, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            ClearActiveSession();
            throw;
        }

        if (!execution.Succeeded)
        {
            ClearActiveSession();
            return Failure(actionPlan.Action, $"PipelineEnterFailed:{execution.Reason}");
        }

        if (!_routingCoordinator.TryCommit(actionPlan))
        {
            await _pipelineExecutor.RollbackAsync(plan, CancellationToken.None).ConfigureAwait(false);
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

    private void ClearActiveSession()
    {
        lock (_sessionSync) _activeSession = null;
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
