using SteamInputAddonforClaw.VirtualOutput.Viiper;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

/// <summary>
/// Deterministic, pure tests for the monotonic absolute-deadline scheduling arithmetic used by
/// <see cref="CanonicalSteamControllerInputPublisher"/>'s production worker. None of these depend on a
/// real timer, a real clock, or wall-clock timing -- every input (including "frequency") is injected.
/// </summary>
public sealed class CanonicalPublisherDeadlineMathTests
{
    private const long FrequencyHz = 10_000_000; // a common real QPC frequency, used as a representative value
    private static long Ms(double milliseconds) => (long)Math.Round(milliseconds * FrequencyHz / 1000.0);

    [Fact]
    public void StopwatchTicksFromTimeSpan_ConvertsFourMillisecondsForA10MhzFrequency()
    {
        var ticks = CanonicalPublisherDeadlineMath.StopwatchTicksFromTimeSpan(TimeSpan.FromMilliseconds(4), FrequencyHz);
        Assert.Equal(40_000L, ticks); // 4ms * 10,000,000 / 1000 = 40,000 ticks
    }

    [Fact]
    public void StopwatchTicksFromTimeSpan_ConvertsFourMillisecondsForANonRoundFrequency()
    {
        // A frequency that does not divide evenly (e.g. some real-world QPC frequencies): must round to
        // the nearest tick rather than truncating, so the period is not systematically slightly short.
        const long frequency = 3_000_000;
        var ticks = CanonicalPublisherDeadlineMath.StopwatchTicksFromTimeSpan(TimeSpan.FromMilliseconds(4), frequency);
        Assert.Equal(12_000L, ticks); // 4ms * 3,000,000 / 1000 = 12,000 exactly
    }

    [Fact]
    public void StopwatchTicksFromTimeSpan_RejectsNonPositiveFrequency()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CanonicalPublisherDeadlineMath.StopwatchTicksFromTimeSpan(TimeSpan.FromMilliseconds(4), 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => CanonicalPublisherDeadlineMath.StopwatchTicksFromTimeSpan(TimeSpan.FromMilliseconds(4), -1));
    }

    [Fact]
    public void ConvertToRelativeDueTime100ns_ConvertsExactMillisecondForA10MhzFrequency()
    {
        var due = CanonicalPublisherDeadlineMath.ConvertToRelativeDueTime100ns(Ms(4), FrequencyHz);
        Assert.Equal(40_000L, due); // 4ms in 100ns units
    }

    [Fact]
    public void ConvertToRelativeDueTime100ns_NeverTruncatesASmallPositiveDurationToZero()
    {
        // The smallest possible positive Stopwatch-tick duration (1 raw tick at 10MHz = 100ns exactly)
        // must still convert to a positive due time.
        var due = CanonicalPublisherDeadlineMath.ConvertToRelativeDueTime100ns(1, FrequencyHz);
        Assert.True(due >= 1);
    }

    [Fact]
    public void ConvertToRelativeDueTime100ns_RoundsUpRatherThanDown()
    {
        // At a higher frequency than 10MHz, a single raw tick is less than 100ns; ceiling must still
        // produce at least 1 (never 0), even though a literal truncation would.
        const long frequency = 100_000_000; // 100MHz -> 1 raw tick = 10ns
        var due = CanonicalPublisherDeadlineMath.ConvertToRelativeDueTime100ns(1, frequency);
        Assert.Equal(1L, due);
    }

    [Fact]
    public void ConvertToRelativeDueTime100ns_RejectsNonPositiveRemainingTicks()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CanonicalPublisherDeadlineMath.ConvertToRelativeDueTime100ns(0, FrequencyHz));
        Assert.Throws<ArgumentOutOfRangeException>(() => CanonicalPublisherDeadlineMath.ConvertToRelativeDueTime100ns(-1, FrequencyHz));
    }

    [Fact]
    public void AdvanceDeadline_NormalOnTimeCase_AdvancesExactlyOnePeriodWithNoSkips()
    {
        var period = Ms(4);
        // Deadline was 4ms (already fired/published for); "now" (right after that publish) is still
        // well before the next natural deadline of 8ms.
        var result = CanonicalPublisherDeadlineMath.AdvanceDeadline(Ms(4), period, Ms(4.05));

        Assert.Equal(Ms(8), result.NextDeadlineTicks);
        Assert.Equal(0, result.SkippedCount);
    }

    [Fact]
    public void AdvanceDeadline_ConstantWakeLatenessDoesNotAccumulateAcrossMultipleIterations()
    {
        // Reproduces the class-level remarks' worked example: a consistent ~0.29ms wake lateness (and
        // ~0.04ms publish work) must not shift the logical grid -- each next deadline must land back on
        // the fixed 4/8/12ms... grid, not drift to 4.33/8.66/12.99ms...
        var period = Ms(4);

        var first = CanonicalPublisherDeadlineMath.AdvanceDeadline(Ms(4), period, Ms(4.33));
        Assert.Equal(Ms(8), first.NextDeadlineTicks);
        Assert.Equal(0, first.SkippedCount);

        var second = CanonicalPublisherDeadlineMath.AdvanceDeadline(first.NextDeadlineTicks, period, Ms(8.33));
        Assert.Equal(Ms(12), second.NextDeadlineTicks);
        Assert.Equal(0, second.SkippedCount);

        var third = CanonicalPublisherDeadlineMath.AdvanceDeadline(second.NextDeadlineTicks, period, Ms(12.33));
        Assert.Equal(Ms(16), third.NextDeadlineTicks);
        Assert.Equal(0, third.SkippedCount);
    }

    [Fact]
    public void AdvanceDeadline_OneFullPeriodMissed_SkipsExactlyOneDeadline()
    {
        var period = Ms(4);
        // The deadline associated with the just-finished publish was 12ms, but the worker did not get
        // back around to advancing/re-arming until 17ms -- an entire extra 16ms deadline has already
        // expired by then, so it must be bypassed (never waited for or published for) on the way to 20ms.
        var result = CanonicalPublisherDeadlineMath.AdvanceDeadline(Ms(12), period, Ms(17));

        Assert.Equal(Ms(20), result.NextDeadlineTicks);
        Assert.Equal(1, result.SkippedCount);
    }

    [Fact]
    public void AdvanceDeadline_MultiplePeriodsMissed_SkipsExactlyAsManyDeadlinesAsExpired()
    {
        var period = Ms(4);
        // From 12ms: the natural next step is 16ms. now=25ms means 16ms, 20ms, and 24ms are all already
        // expired by the time we get to advance, so all three are skipped and the next usable deadline
        // (the first one strictly after 25ms) is 28ms.
        var result = CanonicalPublisherDeadlineMath.AdvanceDeadline(Ms(12), period, Ms(25));

        Assert.Equal(Ms(28), result.NextDeadlineTicks);
        Assert.Equal(3, result.SkippedCount);
    }

    [Fact]
    public void AdvanceDeadline_ExactBoundaryCase_TreatsEqualDeadlineAndNowAsAlreadyUnusable()
    {
        var period = Ms(4);
        // The natural next deadline (8ms) is not strictly in the future relative to "now" -- it must be
        // treated as already unusable and skipped, landing on 12ms with exactly one skip, not armed for
        // a zero/negative remaining duration.
        var result = CanonicalPublisherDeadlineMath.AdvanceDeadline(Ms(4), period, Ms(8));

        Assert.Equal(Ms(12), result.NextDeadlineTicks);
        Assert.Equal(1, result.SkippedCount);
    }

    [Fact]
    public void AdvanceDeadline_AlwaysReturnsADeadlineStrictlyAfterNow()
    {
        var period = Ms(4);
        foreach (var laterBy in new[] { 0.0, 0.01, 3.99, 4.0, 4.01, 20.0, 100.0 })
        {
            var result = CanonicalPublisherDeadlineMath.AdvanceDeadline(Ms(12), period, Ms(12) + Ms(laterBy));
            Assert.True(result.NextDeadlineTicks > Ms(12) + Ms(laterBy), $"laterBy={laterBy}: next={result.NextDeadlineTicks} must exceed now");
        }
    }

    [Fact]
    public void AdvanceDeadline_RejectsNonPositivePeriod()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CanonicalPublisherDeadlineMath.AdvanceDeadline(Ms(4), 0, Ms(5)));
        Assert.Throws<ArgumentOutOfRangeException>(() => CanonicalPublisherDeadlineMath.AdvanceDeadline(Ms(4), -1, Ms(5)));
    }
}
