using SteamInputAddonforClaw.Diagnostics;
using SteamInputAddonforClaw.Status;

namespace SteamInputAddonforClaw.Routing;

internal sealed class RoutingPipelineRuntimeCoordinator
{
    private static readonly RoutingDecision RecoveryResetDecision =
        new(RoutingDecisionKind.Indeterminate, RoutingDecisionReason.RecoveryUnsafe);
    private static readonly ControllerManagerClassification IndeterminateClassification =
        new(ControllerManagerKind.Indeterminate, ControllerManagerClassificationReason.ControllerManagerStateIndeterminate);

    private readonly ISystemStatusProvider _statusProvider;
    private readonly RoutingPipelineSessionCoordinator _sessionCoordinator;

    internal RoutingPipelineRuntimeCoordinator(
        ISystemStatusProvider statusProvider,
        RoutingPipelineSessionCoordinator sessionCoordinator)
    {
        _statusProvider = statusProvider ?? throw new ArgumentNullException(nameof(statusProvider));
        _sessionCoordinator = sessionCoordinator ?? throw new ArgumentNullException(nameof(sessionCoordinator));
    }

    internal async ValueTask<RoutingPipelineSessionReconcileResult> ReconcileAsync(CancellationToken cancellationToken)
    {
        var snapshot = await _statusProvider.CaptureAsync(cancellationToken).ConfigureAwait(false);
        var classification = Classify(snapshot.ControllerSoftware);
        return await _sessionCoordinator.ReconcileAsync(
            snapshot.RoutingDecision,
            classification,
            RoutingExperimentOptions.None,
            cancellationToken).ConfigureAwait(false);
    }

    internal async ValueTask<bool> ReconcileAfterRecoveryAsync(CancellationToken cancellationToken)
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

        var snapshot = await _statusProvider.CaptureAsync(cancellationToken).ConfigureAwait(false);
        var classification = Classify(snapshot.ControllerSoftware);
        var result = await _sessionCoordinator.ReconcileAsync(
            snapshot.RoutingDecision,
            classification,
            RoutingExperimentOptions.None,
            cancellationToken).ConfigureAwait(false);
        AppLog.Info("Routing.Runtime", "Post-recovery routing reconciliation completed.",
            ("Succeeded", result.Succeeded), ("Action", result.Action), ("Reason", result.Reason));
        return result.Succeeded;
    }

    internal async ValueTask<RoutingPipelineSessionReconcileResult> ShutdownAsync()
    {
        var result = await _sessionCoordinator.ReconcileAsync(
            new RoutingDecision(RoutingDecisionKind.WaitingForSteam, RoutingDecisionReason.SteamInactive),
            IndeterminateClassification,
            RoutingExperimentOptions.None,
            CancellationToken.None).ConfigureAwait(false);
        AppLog.Info("Routing.Runtime", "Shutdown routing reconciliation completed.",
            ("Succeeded", result.Succeeded), ("Action", result.Action), ("Reason", result.Reason));
        return result;
    }

    private static ControllerManagerClassification Classify(IReadOnlyList<ControllerSoftwareStatus> software)
    {
        if (!ControllerSoftwareSnapshot.TryCreate(software, out var snapshot) || snapshot is null)
            return IndeterminateClassification;
        return ControllerManagerClassifier.Classify(snapshot);
    }
}
