using SteamInputAddonforClaw.Routing;

namespace SteamInputAddonforClaw.Devices.MSI.Claw;

internal sealed record MsiClawNativeModePreflightResult(bool Succeeded, string Reason)
{
    internal static MsiClawNativeModePreflightResult Success() => new(true, "Ready");
    internal static MsiClawNativeModePreflightResult Failure(string reason) => new(false, reason);
}

internal sealed record MsiClawNativeModeEnterResult(bool Succeeded, bool RequiresRollback, string Reason)
{
    internal static MsiClawNativeModeEnterResult Success(string reason = "NativeModeEntered") => new(true, true, reason);
    internal static MsiClawNativeModeEnterResult Failure(bool requiresRollback, string reason) => new(false, requiresRollback, reason);
}

internal interface IMsiClawNativeModeStageSession
{
    bool IsActive { get; }
    bool HasOwnedRecoveryBoundary { get; }
    ValueTask<MsiClawNativeModePreflightResult> InspectForPipelineAsync(CancellationToken cancellationToken);
    Task<MsiClawNativeModeEnterResult> EnterForPipelineAsync(CancellationToken cancellationToken);
    Task<bool> ExitForPipelineAsync(CancellationToken cancellationToken);
}

internal sealed class MsiClawNativeModeStage : IRoutingPipelineStage
{
    private readonly IMsiClawNativeModeStageSession _session;
    private bool _prepared;
    private bool _ownsMutation;

    internal MsiClawNativeModeStage(IMsiClawNativeModeStageSession session)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
    }

    public RoutingStageKind Kind => RoutingStageKind.NativeMode;

    public async ValueTask<RoutingStageOperationResult> ObserveAsync(CancellationToken cancellationToken)
    {
        var preflight = await _session.InspectForPipelineAsync(cancellationToken).ConfigureAwait(false);
        return preflight.Succeeded
            ? RoutingStageOperationResult.Success(preflight.Reason)
            : RoutingStageOperationResult.Failure(preflight.Reason);
    }

    public async ValueTask<RoutingStageOperationResult> PrepareMutationAsync(CancellationToken cancellationToken)
    {
        if (_prepared || _ownsMutation) return RoutingStageOperationResult.Failure("AlreadyPrepared");
        if (_session.IsActive) return RoutingStageOperationResult.Failure("AlreadyActive");

        var preflight = await _session.InspectForPipelineAsync(cancellationToken).ConfigureAwait(false);
        if (!preflight.Succeeded) return RoutingStageOperationResult.Failure(preflight.Reason);

        _prepared = true;
        return RoutingStageOperationResult.Success(preflight.Reason);
    }

    public async ValueTask<RoutingStageOperationResult> ExecuteMutationAsync(CancellationToken cancellationToken)
    {
        if (!_prepared) return RoutingStageOperationResult.Failure("NativeModeNotPrepared");

        var entered = await _session.EnterForPipelineAsync(cancellationToken).ConfigureAwait(false);
        if (!entered.Succeeded || !_session.IsActive)
        {
            _prepared = false;
            _ownsMutation = entered.RequiresRollback || _session.HasOwnedRecoveryBoundary;
            return RoutingStageOperationResult.Failure(entered.Reason);
        }

        _prepared = false;
        _ownsMutation = entered.RequiresRollback && _session.HasOwnedRecoveryBoundary;
        if (!_ownsMutation)
        {
            return RoutingStageOperationResult.Failure("NativeModeRecoveryBoundaryMissing");
        }

        return RoutingStageOperationResult.Success(entered.Reason);
    }

    public async ValueTask<RoutingStageOperationResult> RollbackMutationAsync(CancellationToken cancellationToken)
    {
        if (!_ownsMutation)
        {
            _prepared = false;
            return RoutingStageOperationResult.Success("NoOwnedNativeModeSession");
        }

        var restored = await _session.ExitForPipelineAsync(cancellationToken).ConfigureAwait(false);
        if (!restored || _session.IsActive || _session.HasOwnedRecoveryBoundary)
            return RoutingStageOperationResult.Failure("NativeModeRestoreFailed");

        _ownsMutation = false;
        _prepared = false;
        return RoutingStageOperationResult.Success("NativeModeRestored");
    }
}
