using System.Diagnostics;
using SteamInputAddonforClaw.Contracts.Frontend;
using SteamInputAddonforClaw.Input.DirectInput;
using SteamInputAddonforClaw.VirtualOutput.Viiper;

namespace SteamInputAddonforClaw.Devices.MSI.Claw;

internal sealed class MsiClawInputCadenceCollector : IDisposable
{
    private readonly object _gate = new();
    private readonly long _requestedDurationMs;
    private readonly long _startedAt;
    private readonly WindowsHighResolutionOneShotTimer? _timer;
    private readonly TaskCompletionSource<FrontendPid1902InputCadenceResult> _completion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly List<double> _distinctIntervalsMs = [];
    private DirectInputState? _lastDistinctState;
    private long _firstDistinctAt;
    private long _lastDistinctAt;
    private int _successfulReadCount;
    private int _distinctStateCount;
    private int _duplicateReadCount;
    private int _completed;
    private bool _timerDisposed;

    internal MsiClawInputCadenceCollector(
        TimeSpan requestedDuration,
        long? startedAt = null,
        WindowsHighResolutionOneShotTimer? timer = null)
    {
        if (requestedDuration <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(requestedDuration));
        _requestedDurationMs = (long)requestedDuration.TotalMilliseconds;
        _startedAt = startedAt ?? Stopwatch.GetTimestamp();
        _timer = timer;
    }

    internal Task<FrontendPid1902InputCadenceResult> Completion => _completion.Task;
    internal bool IsActive => Volatile.Read(ref _completed) == 0;

    internal void Observe(DirectInputState state, long timestamp)
    {
        FrontendPid1902InputCadenceResult? result = null;
        lock (_gate)
        {
            if (_completed != 0) return;

            _successfulReadCount++;
            if (_lastDistinctState is null || !RawStateEquals(_lastDistinctState, state))
            {
                _distinctStateCount++;
                if (_lastDistinctState is not null)
                    _distinctIntervalsMs.Add(ElapsedMilliseconds(_lastDistinctAt, timestamp));
                else
                    _firstDistinctAt = timestamp;

                _lastDistinctAt = timestamp;
                _lastDistinctState = Clone(state);
            }
            else
            {
                _duplicateReadCount++;
            }

            if (ElapsedMilliseconds(_startedAt, timestamp) >= _requestedDurationMs)
            {
                result = CompleteLocked(FrontendPid1902InputCadenceOutcome.Completed, "Completed", timestamp);
                DisposeTimerLocked();
            }
        }

        if (result is not null) _completion.TrySetResult(result);
    }

    internal void Cancel(string status = "Cancelled.") => Complete(FrontendPid1902InputCadenceOutcome.Cancelled, status, Stopwatch.GetTimestamp());

    internal void Fail(string status) => Complete(FrontendPid1902InputCadenceOutcome.Failed, status, Stopwatch.GetTimestamp());

    internal bool WaitForNextSample(TimeSpan interval, CancellationToken sessionCancellation)
    {
        // Serialize the bounded timer wait with completion/disposal; session cancellation wakes WaitAny promptly.
        lock (_gate)
        {
            if (_completed != 0) return false;
            var timer = _timer ?? throw new InvalidOperationException("The cadence diagnostic timer is unavailable.");
            timer.ArmRelative(interval);
            var signaled = WaitHandle.WaitAny([timer, sessionCancellation.WaitHandle]);
            return signaled == 0 && _completed == 0;
        }
    }

    public void Dispose()
    {
        lock (_gate)
            DisposeTimerLocked();
    }

    private void DisposeTimerLocked()
    {
        if (_timerDisposed) return;
        _timerDisposed = true;
        _timer?.Dispose();
    }

    private void Complete(FrontendPid1902InputCadenceOutcome outcome, string status, long timestamp)
    {
        FrontendPid1902InputCadenceResult result;
        lock (_gate)
        {
            if (_completed != 0) return;
            result = CompleteLocked(outcome, status, timestamp);
            DisposeTimerLocked();
        }

        _completion.TrySetResult(result);
    }

    private FrontendPid1902InputCadenceResult CompleteLocked(FrontendPid1902InputCadenceOutcome outcome, string status, long timestamp)
    {
        _completed = 1;
        var actualDurationMs = ElapsedMilliseconds(_startedAt, timestamp);
        var actualDurationSeconds = Stopwatch.GetElapsedTime(_startedAt, timestamp).TotalSeconds;
        double? observedReadHz = actualDurationSeconds > 0 ? _successfulReadCount / actualDurationSeconds : null;
        double? duplicatePercent = _successfulReadCount > 0 ? _duplicateReadCount * 100d / _successfulReadCount : null;
        double? distinctStateHz = _distinctStateCount >= 2
            ? (_distinctStateCount - 1) / Stopwatch.GetElapsedTime(_firstDistinctAt, _lastDistinctAt).TotalSeconds
            : null;

        if (distinctStateHz is { } rate && double.IsInfinity(rate)) distinctStateHz = null;

        var intervals = _distinctIntervalsMs.OrderBy(value => value).ToArray();
        return new FrontendPid1902InputCadenceResult(
            outcome,
            status,
            _requestedDurationMs,
            actualDurationMs,
            _successfulReadCount,
            observedReadHz,
            _distinctStateCount,
            _duplicateReadCount,
            duplicatePercent,
            distinctStateHz,
            intervals.Length == 0 ? null : intervals[0],
            intervals.Length == 0 ? null : intervals.Average(),
            Median(intervals),
            PercentileNearestRank(intervals, 0.95),
            intervals.Length == 0 ? null : intervals[^1]);
    }

    internal static bool RawStateEquals(DirectInputState left, DirectInputState right) =>
        left.X == right.X &&
        left.Y == right.Y &&
        left.Z == right.Z &&
        left.RotationX == right.RotationX &&
        left.RotationY == right.RotationY &&
        left.RotationZ == right.RotationZ &&
        left.Buttons.SequenceEqual(right.Buttons) &&
        left.PointOfViewControllers.SequenceEqual(right.PointOfViewControllers);

    internal static double? Median(IReadOnlyList<double> sortedValues)
    {
        if (sortedValues.Count == 0) return null;
        var middle = sortedValues.Count / 2;
        return sortedValues.Count % 2 == 1
            ? sortedValues[middle]
            : (sortedValues[middle - 1] + sortedValues[middle]) / 2d;
    }

    internal static double? PercentileNearestRank(IReadOnlyList<double> sortedValues, double percentile)
    {
        if (sortedValues.Count == 0) return null;
        var rank = Math.Max(1, (int)Math.Ceiling(sortedValues.Count * percentile));
        return sortedValues[Math.Min(rank, sortedValues.Count) - 1];
    }

    private static DirectInputState Clone(DirectInputState state) =>
        new(state.Buttons.ToArray(), state.X, state.Y, state.Z, state.RotationX, state.RotationY, state.RotationZ, state.PointOfViewControllers.ToArray());

    private static long ElapsedMilliseconds(long startedAt, long endedAt) =>
        (long)Stopwatch.GetElapsedTime(startedAt, endedAt).TotalMilliseconds;
}
