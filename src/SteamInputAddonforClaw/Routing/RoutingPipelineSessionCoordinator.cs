using SteamInputAddonforClaw.Diagnostics;
using SteamInputAddonforClaw.Status;

namespace SteamInputAddonforClaw.Routing;

internal sealed record ActiveRoutingPipelineSession(RoutingPipelinePlan Plan);

internal enum RoutingOperationalState { Passive, OverrideActive }
internal enum RoutingActionKind { None, EnterOverride, ExitOverride }

internal sealed record PendingRoutingPipelineCleanup(
    ActiveRoutingPipelineSession Session,
    RoutingActionKind OriginAction);

internal sealed record RoutingPipelineSessionReconcileResult(
    bool Succeeded,
    RoutingOperationalState State,
    RoutingActionKind Action,
    string Reason);

internal sealed class RoutingPipelineSessionCoordinator
{
    private readonly IRoutingPipelineExecutor _pipelineExecutor;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Lock _sessionSync = new();
    private ActiveRoutingPipelineSession? _activeSession;
    private ActiveRoutingPipelineSession? _enteringSession;
    private PendingRoutingPipelineCleanup? _pendingCleanup;

    internal RoutingPipelineSessionCoordinator(IRoutingPipelineExecutor pipelineExecutor)
    {
        _pipelineExecutor = pipelineExecutor ?? throw new ArgumentNullException(nameof(pipelineExecutor));
    }

    internal RoutingOperationalState CurrentState => ActiveSession is null
        ? RoutingOperationalState.Passive
        : RoutingOperationalState.OverrideActive;

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

    internal ActiveRoutingPipelineSession? EnteringSession
    {
        get
        {
            lock (_sessionSync) return _enteringSession;
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

            if (ActiveSession is null)
            {
                if (decision.Kind != RoutingDecisionKind.Eligible)
                    return Success(RoutingActionKind.None, "AlreadyPassive");

                LogAction(RoutingActionKind.EnterOverride, decision);
                return await EnterAsync(classification, cancellationToken).ConfigureAwait(false);
            }

            if (decision.Kind == RoutingDecisionKind.Eligible)
                return Success(RoutingActionKind.None, "AlreadyActive");

            LogAction(RoutingActionKind.ExitOverride, decision);
            return await ExitAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async ValueTask<RoutingPipelineSessionReconcileResult> EnterAsync(
        ControllerManagerClassification classification,
        CancellationToken cancellationToken)
    {
        if (ActiveSession is not null)
            return Failure(RoutingActionKind.EnterOverride, "ActiveSessionAlreadyExists");

        if (!TrySelectEnvironmentPlan(classification, out var plan))
            return Failure(RoutingActionKind.EnterOverride, "UnsupportedEnvironmentStrategy");

        var candidate = new ActiveRoutingPipelineSession(plan);
        SetEnteringSession(candidate);
        LogPlan("Enter candidate", classification, plan, RoutingActionKind.EnterOverride);

        try
        {
            RoutingPipelineExecutionResult execution;
            try
            {
                execution = await _pipelineExecutor.ExecuteAsync(plan, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException exception)
            {
                if (RoutingPipelineCancellationMetadata.TryGet(exception, out var rollback) && !rollback.Succeeded)
                    SetPendingCleanup(new(candidate, RoutingActionKind.EnterOverride));
                else
                    ClearActiveSession();
                throw;
            }

            if (!execution.Succeeded)
            {
                if (!execution.RollbackSucceeded)
                    SetPendingCleanup(new(candidate, RoutingActionKind.EnterOverride));
                else
                    ClearActiveSession();
                return Failure(RoutingActionKind.EnterOverride, $"PipelineEnterFailed:{execution.Reason}");
            }

            SetActiveSession(candidate);
            AppLog.Info("Routing.Session", "Routing session entered.",
                ("Action", RoutingActionKind.EnterOverride), ("Result", "Active"),
                ("Classification", classification.Kind));
            return Success(RoutingActionKind.EnterOverride, "EnteredOverride");
        }
        finally { ClearEnteringSession(candidate); }
    }

    private async ValueTask<RoutingPipelineSessionReconcileResult> ExitAsync(
        CancellationToken cancellationToken)
    {
        var session = ActiveSession;
        if (session is null)
            return Failure(RoutingActionKind.ExitOverride, "ActiveSessionMissing");

        AppLog.Info("Routing.Session", "Routing session exit started.",
            ("Action", RoutingActionKind.ExitOverride), ("PlanSource", "FrozenSession"));

        var rollback = await _pipelineExecutor.RollbackAsync(session.Plan, cancellationToken).ConfigureAwait(false);
        if (!rollback.Succeeded)
        {
            SetPendingCleanup(new(session, RoutingActionKind.ExitOverride));
            AppLog.Warn("Routing.Session", "Routing session rollback failed.", fields:
                [("Action", RoutingActionKind.ExitOverride), ("Result", "RollbackFailed"), ("FailedStage", rollback.FailedStage),
                 ("Reason", rollback.Reason), ("SessionPreserved", true)]);
            return Failure(RoutingActionKind.ExitOverride, $"PipelineRollbackFailed:{rollback.Reason}");
        }

        ClearActiveSession();
        AppLog.Info("Routing.Session", "Routing session exited.",
            ("Action", RoutingActionKind.ExitOverride), ("Result", "Passive"));
        return Success(RoutingActionKind.ExitOverride, "ExitedOverride");
    }

    private void SetActiveSession(ActiveRoutingPipelineSession session)
    {
        lock (_sessionSync) _activeSession = session;
    }

    private void SetEnteringSession(ActiveRoutingPipelineSession session)
    {
        lock (_sessionSync) _enteringSession = session;
    }

    private void ClearEnteringSession(ActiveRoutingPipelineSession session)
    {
        lock (_sessionSync)
        {
            if (ReferenceEquals(_enteringSession, session)) _enteringSession = null;
        }
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
            return Failure(pending.OriginAction, $"PendingCleanupFailed:{rollback.Reason}");

        if (pending.OriginAction == RoutingActionKind.ExitOverride)
            ClearActiveSession();

        ClearPendingCleanup();
        return new(true, CurrentState, pending.OriginAction, "PendingCleanupCompleted");
    }

    private RoutingPipelineSessionReconcileResult Success(RoutingActionKind action, string reason) =>
        new(true, CurrentState, action, reason);

    private RoutingPipelineSessionReconcileResult Failure(RoutingActionKind action, string reason) =>
        new(false, CurrentState, action, reason);

    private static bool TrySelectEnvironmentPlan(
        ControllerManagerClassification classification,
        out RoutingPipelinePlan plan)
    {
        switch (classification.Kind)
        {
            case ControllerManagerKind.None:
                plan = RoutingPipelinePlan.StockCenterM;
                return true;
            case ControllerManagerKind.ClawTweaks:
                plan = RoutingPipelinePlan.AllDisabled;
                return true;
            default:
                plan = RoutingPipelinePlan.AllDisabled;
                return false;
        }
    }

    private static void LogAction(RoutingActionKind action, RoutingDecision decision)
    {
        AppLog.Info("Routing", "Routing action planned.",
            ("Current", action == RoutingActionKind.EnterOverride ? RoutingOperationalState.Passive : RoutingOperationalState.OverrideActive),
            ("Action", action),
            ("Target", action == RoutingActionKind.EnterOverride ? RoutingOperationalState.OverrideActive : RoutingOperationalState.Passive),
            ("Decision", decision.Kind),
            ("Reason", ActionReason(decision, action)));
    }

    private static string ActionReason(RoutingDecision decision, RoutingActionKind action) => action switch
    {
        RoutingActionKind.EnterOverride => "RoutingBecameEligible",
        RoutingActionKind.ExitOverride when decision.Kind == RoutingDecisionKind.WaitingForSteam => "SteamSessionEnded",
        RoutingActionKind.ExitOverride when decision.Kind == RoutingDecisionKind.SetupRequired => "SetupRequired",
        RoutingActionKind.ExitOverride when decision.Kind == RoutingDecisionKind.Indeterminate && decision.Reason == RoutingDecisionReason.RecoveryUnsafe => "RecoveryUnsafe",
        RoutingActionKind.ExitOverride when decision.Kind == RoutingDecisionKind.Indeterminate => "IndeterminateState",
        RoutingActionKind.ExitOverride when decision.Kind == RoutingDecisionKind.Passive && decision.Reason == RoutingDecisionReason.ControllerEnvironmentUnsupported => "ControllerEnvironmentUnsupported",
        RoutingActionKind.ExitOverride => "RoutingNoLongerEligible",
        _ => "AlreadyPassive"
    };

    private static void LogPlan(
        string message,
        ControllerManagerClassification classification,
        RoutingPipelinePlan plan,
        RoutingActionKind action)
    {
        AppLog.Info("Routing.Session", message,
            ("Action", action), ("Classification", classification.Kind), ("NativeMode", plan.NativeMode),
            ("PhysicalInput", plan.PhysicalInput), ("PhysicalIsolation", plan.PhysicalIsolation),
            ("ThirdPartyIsolation", plan.ThirdPartyIsolation), ("SteamOutput", plan.SteamOutput),
            ("XboxOutput", plan.XboxOutput), ("GameBarRouting", plan.GameBarRouting));
    }
}
