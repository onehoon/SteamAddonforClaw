using System.Diagnostics;

namespace SteamInputAddonforClaw.VirtualOutput.Viiper;

/// <summary>
/// Pure, deterministically-testable arithmetic for the monotonic absolute-deadline scheduler used by
/// <see cref="CanonicalSteamControllerInputPublisher"/>'s production worker. Kept separate from the
/// publisher's instance state so the deadline-advance and clock-conversion logic can be tested directly
/// without needing a running worker thread or real timer.
/// </summary>
internal static class CanonicalPublisherDeadlineMath
{
    /// <summary>Converts a <see cref="TimeSpan"/> period into an equivalent duration in
    /// <see cref="Stopwatch"/> ticks for the given <paramref name="frequency"/> (<see cref="Stopwatch.Frequency"/>
    /// in production; injectable here so tests aren't tied to the actual machine's QPC frequency).</summary>
    internal static long StopwatchTicksFromTimeSpan(TimeSpan span, long frequency)
    {
        if (frequency <= 0) throw new ArgumentOutOfRangeException(nameof(frequency), frequency, "Frequency must be positive.");
        // decimal (128-bit) avoids overflow for any realistic period/frequency combination and keeps
        // full precision through the division; Math.Round to the nearest whole tick rather than
        // truncating, so a period does not end up systematically slightly short.
        var ticks = (decimal)span.Ticks * frequency / TimeSpan.TicksPerSecond;
        return (long)Math.Round(ticks, MidpointRounding.AwayFromZero);
    }

    /// <summary>Converts a positive duration expressed in <see cref="Stopwatch"/> ticks into the
    /// equivalent number of 100ns units (the unit <c>SetWaitableTimerEx</c>'s relative due time and
    /// <see cref="TimeSpan.Ticks"/> both use). Always rounds up (ceiling) and never returns less than 1:
    /// a genuinely-positive-but-tiny remaining duration must still arm a positive one-shot wait rather
    /// than truncating to zero (which <see cref="WindowsHighResolutionOneShotTimer.ArmRelative"/> would
    /// reject as non-positive).</summary>
    internal static long ConvertToRelativeDueTime100ns(long remainingStopwatchTicks, long frequency)
    {
        if (remainingStopwatchTicks <= 0) throw new ArgumentOutOfRangeException(nameof(remainingStopwatchTicks), remainingStopwatchTicks, "The remaining duration must be positive.");
        if (frequency <= 0) throw new ArgumentOutOfRangeException(nameof(frequency), frequency, "Frequency must be positive.");

        var hundredNsUnits = (decimal)remainingStopwatchTicks * TimeSpan.TicksPerSecond / frequency;
        var rounded = (long)Math.Ceiling(hundredNsUnits);
        return rounded < 1 ? 1 : rounded;
    }

    /// <summary>Result of <see cref="AdvanceDeadline"/>: the next logical deadline to arm for, and how
    /// many additional logical deadlines beyond the normal single-period step were skipped (never waited
    /// for) because the worker was already past them by the time it finished the current publish.</summary>
    internal readonly record struct DeadlineAdvanceResult(long NextDeadlineTicks, long SkippedCount);

    /// <summary>
    /// Advances the logical deadline schedule by exactly one period, then skips forward over any further
    /// whole periods that have already expired relative to <paramref name="nowTicks"/>, so the returned
    /// deadline is always strictly in the future. This is what keeps the schedule on the fixed
    /// 4/8/12/16ms... grid instead of drifting: the *next* deadline is always computed from the *previous
    /// deadline*, never from "now" -- "now" is only used to detect and skip deadlines that are already
    /// unreachable, never to redefine the grid itself.
    /// </summary>
    /// <remarks>
    /// In the steady-state case (the worker keeps up, even if each individual wake lands slightly late),
    /// <see cref="DeadlineAdvanceResult.SkippedCount"/> is always 0 -- advancing by exactly one period is
    /// the normal, expected behavior every iteration and is not itself counted as a "skip." A skip is
    /// only counted for a whole additional period that gets bypassed with no wait or publish of its own,
    /// which should be rare (a genuine multi-period stall) rather than routine.
    /// </remarks>
    internal static DeadlineAdvanceResult AdvanceDeadline(long currentDeadlineTicks, long periodTicks, long nowTicks)
    {
        if (periodTicks <= 0) throw new ArgumentOutOfRangeException(nameof(periodTicks), periodTicks, "The period must be positive.");

        checked
        {
            var next = currentDeadlineTicks + periodTicks;
            if (next > nowTicks) return new DeadlineAdvanceResult(next, 0);

            // next is already unusable (at or before "now"): skip forward by whole periods until the
            // deadline is strictly in the future. behindBy >= 0 here.
            var behindBy = nowTicks - next;
            var additionalSkips = behindBy / periodTicks + 1;
            next += additionalSkips * periodTicks;
            return new DeadlineAdvanceResult(next, additionalSkips);
        }
    }
}
