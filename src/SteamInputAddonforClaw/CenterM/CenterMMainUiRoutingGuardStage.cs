using SteamInputAddonforClaw.Routing;

namespace SteamInputAddonforClaw.CenterM;

/// <summary>
/// Adapts <see cref="CenterMMainUiRoutingGuard"/> to the generic routing pipeline. Arms on
/// <see cref="ExecuteMutationAsync"/> (scheduled first among the native MSI stages in
/// <see cref="RoutingPipelineStageOrder.Forward"/>, after route-bound Win+G protection) and disarms
/// on <see cref="RollbackMutationAsync"/> (scheduled last among those stages in
/// <see cref="RoutingPipelineStageOrder.Rollback"/>, after native-mode restoration and before
/// Win+G protection is released). The generic pipeline only ever sees this stage/its Kind -- it never learns
/// MSI-specific identifiers like the MainUI process name or mutex name, which stay inside
/// <see cref="CenterMMainUiRoutingGuard"/>.
/// </summary>
internal sealed class CenterMMainUiRoutingGuardStage : IRoutingPipelineStage
{
    private readonly CenterMMainUiRoutingGuard _guard;

    internal CenterMMainUiRoutingGuardStage(CenterMMainUiRoutingGuard guard)
    {
        _guard = guard ?? throw new ArgumentNullException(nameof(guard));
    }

    public RoutingStageKind Kind => RoutingStageKind.CenterMGuard;

    public ValueTask<RoutingStageOperationResult> ObserveAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(RoutingStageOperationResult.Success("CenterMGuardObserveNotApplicable"));
    }

    public ValueTask<RoutingStageOperationResult> PrepareMutationAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(RoutingStageOperationResult.Success("Ready"));
    }

    public async ValueTask<RoutingStageOperationResult> ExecuteMutationAsync(CancellationToken cancellationToken)
    {
        var result = await _guard.ArmAsync(cancellationToken).ConfigureAwait(false);
        return result == CenterMMainUiRoutingGuardResult.Armed
            ? RoutingStageOperationResult.Success("CenterMGuardArmed")
            : RoutingStageOperationResult.Failure(result.ToString());
    }

    public async ValueTask<RoutingStageOperationResult> RollbackMutationAsync(CancellationToken cancellationToken)
    {
        var disarmed = await _guard.DisarmAsync(cancellationToken).ConfigureAwait(false);
        return disarmed
            ? RoutingStageOperationResult.Success("CenterMGuardDisarmed")
            : RoutingStageOperationResult.Failure("CenterMGuardDisarmCleanupUnconfirmed");
    }
}
