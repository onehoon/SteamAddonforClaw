using System.Diagnostics;
using SteamInputAddonforClaw.Diagnostics;

namespace SteamInputAddonforClaw.Routing;

internal readonly record struct RoutingStageOperationResult(bool Succeeded, string Reason)
{
    internal static RoutingStageOperationResult Success(string reason = "Success") => new(true, reason);
    internal static RoutingStageOperationResult Failure(string reason) => new(false, reason);
}

internal interface IRoutingPipelineStage
{
    RoutingStageKind Kind { get; }
    ValueTask<RoutingStageOperationResult> ObserveAsync(CancellationToken cancellationToken);
    ValueTask<RoutingStageOperationResult> PrepareMutationAsync(CancellationToken cancellationToken);
    ValueTask<RoutingStageOperationResult> ExecuteMutationAsync(CancellationToken cancellationToken);
    ValueTask<RoutingStageOperationResult> RollbackMutationAsync(CancellationToken cancellationToken);
}

internal static class RoutingPipelineStageOrder
{
    internal static IReadOnlyList<RoutingStageKind> Forward { get; } =
    [
        RoutingStageKind.NativeMode,
        RoutingStageKind.PhysicalInput,
        RoutingStageKind.PhysicalIsolation,
        RoutingStageKind.ThirdPartyIsolation,
        RoutingStageKind.SteamOutput,
        RoutingStageKind.XboxOutput,
        RoutingStageKind.GameBarRouting
    ];

    internal static IReadOnlyList<RoutingStageKind> Rollback { get; } =
    [
        RoutingStageKind.GameBarRouting,
        RoutingStageKind.XboxOutput,
        RoutingStageKind.SteamOutput,
        RoutingStageKind.ThirdPartyIsolation,
        RoutingStageKind.PhysicalIsolation,
        RoutingStageKind.PhysicalInput,
        RoutingStageKind.NativeMode
    ];
}

internal sealed record RoutingPipelineExecutionResult(
    bool Succeeded,
    RoutingStageKind? FailedStage,
    string Reason,
    bool RollbackSucceeded)
{
    internal static RoutingPipelineExecutionResult Success() => new(true, null, "Success", true);
}

internal sealed record RoutingPipelineRollbackResult(
    bool Succeeded,
    RoutingStageKind? FailedStage,
    string Reason);

internal static class RoutingPipelineCancellationMetadata
{
    private const string RollbackKey = "SteamInputAddonforClaw.RoutingPipelineRollback";

    internal static void Attach(OperationCanceledException exception, RoutingPipelineRollbackResult rollback) =>
        exception.Data[RollbackKey] = rollback;

    internal static bool TryGet(OperationCanceledException exception, out RoutingPipelineRollbackResult rollback)
    {
        if (exception.Data[RollbackKey] is RoutingPipelineRollbackResult result)
        {
            rollback = result;
            return true;
        }

        rollback = null!;
        return false;
    }
}

internal interface IRoutingPipelineExecutor
{
    ValueTask<RoutingPipelineExecutionResult> ExecuteAsync(RoutingPipelinePlan plan, CancellationToken cancellationToken);
    ValueTask<RoutingPipelineRollbackResult> RollbackAsync(RoutingPipelinePlan plan, CancellationToken cancellationToken);
}

internal sealed class RoutingPipelineExecutor : IRoutingPipelineExecutor
{
    private readonly IReadOnlyDictionary<RoutingStageKind, IRoutingPipelineStage> _stages;

    internal RoutingPipelineExecutor(IEnumerable<IRoutingPipelineStage> stages)
    {
        ArgumentNullException.ThrowIfNull(stages);
        var stageList = stages.ToList();
        if (stageList.Any(stage => !RoutingPipelineStageOrder.Forward.Contains(stage.Kind)))
            throw new ArgumentException("The stage registry contains an unknown stage.", nameof(stages));
        if (stageList.GroupBy(stage => stage.Kind).Any(group => group.Count() > 1))
            throw new ArgumentException("The stage registry contains a duplicate stage.", nameof(stages));
        _stages = stageList.ToDictionary(stage => stage.Kind);
    }

    public async ValueTask<RoutingPipelineExecutionResult> ExecuteAsync(RoutingPipelinePlan plan, CancellationToken cancellationToken)
    {
        var rollbackCandidates = new List<(RoutingStageKind Kind, IRoutingPipelineStage? Stage)>();
        RoutingStageKind? currentStage = null;

        try
        {
            foreach (var kind in RoutingPipelineStageOrder.Forward)
            {
                currentStage = kind;
                var mode = plan.GetMode(kind);
                if (mode == RoutingStageMode.Disabled) continue;
                if (!_stages.TryGetValue(kind, out var stage))
                    return await FailAsync(kind, "StageImplementationMissing", rollbackCandidates).ConfigureAwait(false);

                if (mode == RoutingStageMode.ObserveOnly)
                {
                    var started = Stopwatch.GetTimestamp();
                    var observation = await stage.ObserveAsync(cancellationToken).ConfigureAwait(false);
                    AppLog.Debug("Routing", "Stage operation", ("Stage", kind), ("Phase", "Observe"), ("Result", observation.Succeeded ? "Success" : "Failure"), ("Reason", observation.Reason), ("ElapsedMs", (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds));
                    if (!observation.Succeeded)
                        return await FailAsync(kind, observation.Reason, rollbackCandidates).ConfigureAwait(false);
                    continue;
                }

                if (mode != RoutingStageMode.Enabled)
                    return await FailAsync(kind, "UnknownStageMode", rollbackCandidates).ConfigureAwait(false);

                rollbackCandidates.Add((kind, stage));
                var prepareStarted = Stopwatch.GetTimestamp();
                var preparation = await stage.PrepareMutationAsync(cancellationToken).ConfigureAwait(false);
                AppLog.Debug("Routing", "Stage operation", ("Stage", kind), ("Phase", "Prepare"), ("Result", preparation.Succeeded ? "Success" : "Failure"), ("Reason", preparation.Reason), ("ElapsedMs", (long)Stopwatch.GetElapsedTime(prepareStarted).TotalMilliseconds));
                if (!preparation.Succeeded)
                    return await FailAsync(kind, preparation.Reason, rollbackCandidates).ConfigureAwait(false);

                var executeStarted = Stopwatch.GetTimestamp();
                var execution = await stage.ExecuteMutationAsync(cancellationToken).ConfigureAwait(false);
                AppLog.Debug("Routing", "Stage operation", ("Stage", kind), ("Phase", "Execute"), ("Result", execution.Succeeded ? "Success" : "Failure"), ("Reason", execution.Reason), ("ElapsedMs", (long)Stopwatch.GetElapsedTime(executeStarted).TotalMilliseconds));
                if (!execution.Succeeded)
                    return await FailAsync(kind, execution.Reason, rollbackCandidates).ConfigureAwait(false);
            }

            return RoutingPipelineExecutionResult.Success();
        }
        catch (OperationCanceledException exception) when (cancellationToken.IsCancellationRequested)
        {
            var rollback = await RollbackCandidatesAsync(rollbackCandidates).ConfigureAwait(false);
            RoutingPipelineCancellationMetadata.Attach(exception, rollback);
            throw;
        }
        catch (Exception exception)
        {
            var rollback = await RollbackCandidatesAsync(rollbackCandidates).ConfigureAwait(false);
            return new(false, currentStage, exception.GetType().Name, rollback.Succeeded);
        }
    }

    public async ValueTask<RoutingPipelineRollbackResult> RollbackAsync(RoutingPipelinePlan plan, CancellationToken cancellationToken)
    {
        var stages = new List<(RoutingStageKind Kind, IRoutingPipelineStage? Stage)>();
        foreach (var kind in RoutingPipelineStageOrder.Rollback)
        {
            if (plan.GetMode(kind) != RoutingStageMode.Enabled) continue;
            stages.Add((kind, _stages.GetValueOrDefault(kind)));
        }

        var firstFailure = await RollbackStagesAsync(stages).ConfigureAwait(false);
        return firstFailure ?? new(true, null, "Success");
    }

    private async ValueTask<RoutingPipelineExecutionResult> FailAsync(RoutingStageKind failedStage, string reason, IReadOnlyList<(RoutingStageKind Kind, IRoutingPipelineStage? Stage)> rollbackCandidates)
    {
        var rollback = await RollbackCandidatesAsync(rollbackCandidates).ConfigureAwait(false);
        return new(false, failedStage, reason, rollback.Succeeded);
    }

    private async ValueTask<RoutingPipelineRollbackResult> RollbackCandidatesAsync(IReadOnlyList<(RoutingStageKind Kind, IRoutingPipelineStage? Stage)> candidates)
        => await RollbackStagesAsync(candidates.Reverse().ToList()).ConfigureAwait(false) ?? new(true, null, "Success");

    private async ValueTask<RoutingPipelineRollbackResult?> RollbackStagesAsync(IReadOnlyList<(RoutingStageKind Kind, IRoutingPipelineStage? Stage)> stages)
    {
        RoutingPipelineRollbackResult? firstFailure = null;
        foreach (var entry in stages)
        {
            if (entry.Stage is null)
            {
                firstFailure ??= new(false, entry.Kind, "StageImplementationMissing");
                continue;
            }

            try
            {
                var started = Stopwatch.GetTimestamp();
                var result = await entry.Stage.RollbackMutationAsync(CancellationToken.None).ConfigureAwait(false);
                AppLog.Debug("Routing", "Stage operation", ("Stage", entry.Kind), ("Phase", "Rollback"), ("Result", result.Succeeded ? "Success" : "Failure"), ("Reason", result.Reason), ("ElapsedMs", (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds));
                if (!result.Succeeded)
                    firstFailure ??= new(false, entry.Kind, result.Reason);
            }
            catch (Exception exception)
            {
                firstFailure ??= new(false, entry.Kind, exception.GetType().Name);
            }
        }

        return firstFailure;
    }
}
