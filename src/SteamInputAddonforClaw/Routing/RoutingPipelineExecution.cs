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
    // WinGProtection is the outer routing protection: it arms before any MSI/native mutation and
    // releases last. CenterMGuard remains first/last among the native MSI routing stages, so real
    // Center M MainUI cannot become operational while PID1902 ownership is still expected.
    internal static IReadOnlyList<RoutingStageKind> Forward { get; } =
    [
        RoutingStageKind.WinGProtection,
        RoutingStageKind.HidHideBaseline,
        RoutingStageKind.CenterMGuard,
        RoutingStageKind.NativeMode,
        RoutingStageKind.PhysicalInput,
        RoutingStageKind.PhysicalIsolation,
        RoutingStageKind.SteamOutput,
        RoutingStageKind.XboxOutput,
        RoutingStageKind.GameBarRouting
    ];

    internal static IReadOnlyList<RoutingStageKind> Rollback { get; } =
    [
        RoutingStageKind.GameBarRouting,
        RoutingStageKind.XboxOutput,
        RoutingStageKind.SteamOutput,
        RoutingStageKind.PhysicalInput,
        RoutingStageKind.NativeMode,
        RoutingStageKind.PhysicalIsolation,
        RoutingStageKind.CenterMGuard,
        RoutingStageKind.HidHideBaseline,
        RoutingStageKind.WinGProtection,
    ];

    internal static bool IsRollbackFailureBarrier(RoutingStageKind kind) =>
        kind is RoutingStageKind.SteamOutput or RoutingStageKind.PhysicalInput or RoutingStageKind.NativeMode;
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
    private static int _nextExecutionId;
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
        var executionId = Interlocked.Increment(ref _nextExecutionId);
        var totalStarted = Stopwatch.GetTimestamp();
        using var traceScope = RoutingTraceContext.Begin(executionId);
        AppLog.Debug("RoutingTrace", "Routing activation started.", ("RoutingExecution", executionId));
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
                    return await FailAsync(kind, "StageImplementationMissing", rollbackCandidates, executionId, totalStarted).ConfigureAwait(false);

                if (mode == RoutingStageMode.ObserveOnly)
                {
                    var started = Stopwatch.GetTimestamp();
                    var observation = await stage.ObserveAsync(cancellationToken).ConfigureAwait(false);
                    LogStage(executionId, kind, "Observe", observation, started);
                    if (!observation.Succeeded)
                        return await FailAsync(kind, observation.Reason, rollbackCandidates, executionId, totalStarted).ConfigureAwait(false);
                    continue;
                }

                if (mode != RoutingStageMode.Enabled)
                    return await FailAsync(kind, "UnknownStageMode", rollbackCandidates, executionId, totalStarted).ConfigureAwait(false);

                rollbackCandidates.Add((kind, stage));
                var prepareStarted = Stopwatch.GetTimestamp();
                var preparation = await stage.PrepareMutationAsync(cancellationToken).ConfigureAwait(false);
                LogStage(executionId, kind, "Prepare", preparation, prepareStarted);
                if (!preparation.Succeeded)
                    return await FailAsync(kind, preparation.Reason, rollbackCandidates, executionId, totalStarted).ConfigureAwait(false);

                var executeStarted = Stopwatch.GetTimestamp();
                var execution = await stage.ExecuteMutationAsync(cancellationToken).ConfigureAwait(false);
                LogStage(executionId, kind, "Execute", execution, executeStarted);
                if (!execution.Succeeded)
                    return await FailAsync(kind, execution.Reason, rollbackCandidates, executionId, totalStarted).ConfigureAwait(false);
            }

            AppLog.Debug("RoutingTrace", "Routing activation completed.", ("RoutingExecution", executionId), ("Result", "Success"), ("TotalMs", Elapsed(totalStarted)));
            return RoutingPipelineExecutionResult.Success();
        }
        catch (OperationCanceledException exception) when (cancellationToken.IsCancellationRequested)
        {
            var rollbackStarted = Stopwatch.GetTimestamp();
            var rollback = await RollbackCandidatesAsync(rollbackCandidates, executionId).ConfigureAwait(false);
            RoutingPipelineCancellationMetadata.Attach(exception, rollback);
            AppLog.Debug("RoutingTrace", "Routing activation cancelled.", ("RoutingExecution", executionId), ("Result", "Failure"), ("TotalMs", Elapsed(totalStarted)), ("RollbackSucceeded", rollback.Succeeded), ("RollbackMs", Elapsed(rollbackStarted)));
            throw;
        }
        catch (Exception exception)
        {
            var rollbackStarted = Stopwatch.GetTimestamp();
            var rollback = await RollbackCandidatesAsync(rollbackCandidates, executionId).ConfigureAwait(false);
            AppLog.Debug("RoutingTrace", "Routing activation failed.", ("RoutingExecution", executionId), ("Result", "Failure"), ("FailedStage", currentStage), ("Reason", exception.GetType().Name), ("TotalMs", Elapsed(totalStarted)), ("RollbackSucceeded", rollback.Succeeded), ("RollbackMs", Elapsed(rollbackStarted)));
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

        var executionId = Interlocked.Increment(ref _nextExecutionId);
        var started = Stopwatch.GetTimestamp();
        using var traceScope = RoutingTraceContext.Begin(executionId);
        var firstFailure = await RollbackStagesAsync(stages, executionId).ConfigureAwait(false);
        AppLog.Debug("RoutingTrace", "Routing rollback completed.", ("RoutingExecution", executionId), ("Result", firstFailure is null ? "Success" : "Failure"), ("Reason", firstFailure?.Reason ?? "Success"), ("TotalMs", Elapsed(started)));
        return firstFailure ?? new(true, null, "Success");
    }

    private async ValueTask<RoutingPipelineExecutionResult> FailAsync(RoutingStageKind failedStage, string reason, IReadOnlyList<(RoutingStageKind Kind, IRoutingPipelineStage? Stage)> rollbackCandidates, int executionId, long totalStarted)
    {
        var rollbackStarted = Stopwatch.GetTimestamp();
        var rollback = await RollbackCandidatesAsync(rollbackCandidates, executionId).ConfigureAwait(false);
        AppLog.Debug("RoutingTrace", "Routing activation failed.", ("RoutingExecution", executionId), ("Result", "Failure"), ("FailedStage", failedStage), ("Reason", reason), ("TotalMs", Elapsed(totalStarted)), ("RollbackSucceeded", rollback.Succeeded), ("RollbackMs", Elapsed(rollbackStarted)));
        return new(false, failedStage, reason, rollback.Succeeded);
    }

    private async ValueTask<RoutingPipelineRollbackResult> RollbackCandidatesAsync(IReadOnlyList<(RoutingStageKind Kind, IRoutingPipelineStage? Stage)> candidates, int? executionId)
        => await RollbackStagesAsync(OrderForRollback(candidates), executionId).ConfigureAwait(false) ?? new(true, null, "Success");

    private static List<(RoutingStageKind Kind, IRoutingPipelineStage? Stage)> OrderForRollback(IReadOnlyList<(RoutingStageKind Kind, IRoutingPipelineStage? Stage)> candidates)
    {
        var byKind = candidates.ToDictionary(entry => entry.Kind);
        return RoutingPipelineStageOrder.Rollback
            .Where(byKind.ContainsKey)
            .Select(kind => byKind[kind])
            .ToList();
    }

    private async ValueTask<RoutingPipelineRollbackResult?> RollbackStagesAsync(IReadOnlyList<(RoutingStageKind Kind, IRoutingPipelineStage? Stage)> stages, int? executionId)
    {
        RoutingPipelineRollbackResult? firstFailure = null;
        foreach (var entry in stages)
        {
            if (entry.Stage is null)
            {
                firstFailure ??= new(false, entry.Kind, "StageImplementationMissing");
                if (RoutingPipelineStageOrder.IsRollbackFailureBarrier(entry.Kind))
                {
                    await RollbackWinGProtectionBestEffortAsync(stages, executionId).ConfigureAwait(false);
                    break;
                }
                continue;
            }

            try
            {
                var started = Stopwatch.GetTimestamp();
                var result = await entry.Stage.RollbackMutationAsync(CancellationToken.None).ConfigureAwait(false);
                AppLog.Debug("RoutingTrace", "Routing stage timing.", ("RoutingExecution", executionId), ("Stage", entry.Kind), ("Phase", "Rollback"), ("Result", result.Succeeded ? "Success" : "Failure"), ("Reason", result.Reason), ("ElapsedMs", Elapsed(started)));
                if (!result.Succeeded)
                {
                    firstFailure ??= new(false, entry.Kind, result.Reason);
                    if (RoutingPipelineStageOrder.IsRollbackFailureBarrier(entry.Kind))
                    {
                        await RollbackWinGProtectionBestEffortAsync(stages, executionId).ConfigureAwait(false);
                        break;
                    }
                }
            }
            catch (Exception exception)
            {
                firstFailure ??= new(false, entry.Kind, exception.GetType().Name);
                if (RoutingPipelineStageOrder.IsRollbackFailureBarrier(entry.Kind))
                {
                    await RollbackWinGProtectionBestEffortAsync(stages, executionId).ConfigureAwait(false);
                    break;
                }
            }
        }

        return firstFailure;
    }

    private static async ValueTask RollbackWinGProtectionBestEffortAsync(
        IReadOnlyList<(RoutingStageKind Kind, IRoutingPipelineStage? Stage)> stages,
        int? executionId)
    {
        var entry = stages.FirstOrDefault(static candidate => candidate.Kind == RoutingStageKind.WinGProtection);
        if (entry.Stage is null) return;

        try
        {
            var started = Stopwatch.GetTimestamp();
            var result = await entry.Stage.RollbackMutationAsync(CancellationToken.None).ConfigureAwait(false);
            AppLog.Debug("RoutingTrace", "Routing stage timing.",
                ("RoutingExecution", executionId), ("Stage", entry.Kind), ("Phase", "Rollback"),
                ("Result", result.Succeeded ? "Success" : "Failure"), ("Reason", result.Reason),
                ("ElapsedMs", Elapsed(started)), ("BestEffort", true));
        }
        catch (Exception exception)
        {
            AppLog.Warn("RoutingTrace", "Best-effort WinGProtection rollback failed.", exception,
                ("RoutingExecution", executionId), ("Stage", RoutingStageKind.WinGProtection));
        }
    }

    private static long Elapsed(long started) => (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds;
    private static void LogStage(int executionId, RoutingStageKind stage, string phase, RoutingStageOperationResult result, long started)
    {
        var elapsed = Elapsed(started);
        AppLog.Debug("RoutingTrace", "Routing stage timing.", ("RoutingExecution", executionId), ("Stage", stage), ("Phase", phase), ("Result", result.Succeeded ? "Success" : "Failure"), ("Reason", result.Reason), ("ElapsedMs", elapsed));
    }
}
