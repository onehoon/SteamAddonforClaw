using System.Diagnostics;
using SteamInputAddonforClaw.Devices.MSI.Claw;
using SteamInputAddonforClaw.Diagnostics;
using SteamInputAddonforClaw.Input;
using SteamInputAddonforClaw.VirtualOutput.Viiper;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

[Collection("AppLog")]
public sealed class CanonicalSteamControllerInputPublisherTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "CanonicalSteamControllerInputPublisherTests", Guid.NewGuid().ToString("N"));

    public CanonicalSteamControllerInputPublisherTests()
    {
        Directory.CreateDirectory(_directory);
        AppLog.DirectoryOverride = _directory;
        AppLog.MinimumLevelOverride = AppLogLevel.Debug;
    }

    public void Dispose()
    {
        AppLog.DrainForTests();
        AppLog.DirectoryOverride = null;
        AppLog.MinimumLevelOverride = AppLogLevel.Info;
        try { if (Directory.Exists(_directory)) Directory.Delete(_directory, true); } catch (IOException) { }
    }

    [Fact]
    public async Task Manual_ticks_publish_mapped_typed_state_without_frame_ownership()
    {
        var source = new Snapshot(new ControllerState(new GamepadButtons(true, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false), default, default, default, new([false, false])));
        var sink = new FakeSink(); var ticks = new ManualTicks();
        var publisher = new CanonicalSteamControllerInputPublisher(source, sink, ticks);
        publisher.Start();
        await ticks.TickAsync(); await sink.WaitForCountAsync(1);
        source.Value = new ControllerState(new GamepadButtons(false, false, true, false, false, false, false, false, false, false, false, false, false, false, false, false), default, default, default, new([false, false]));
        await ticks.TickAsync(); await sink.WaitForCountAsync(2);
        await publisher.StopAsync();

        Assert.Equal((byte)1, sink.States[0].A);
        Assert.Equal((byte)1, sink.States[1].X);
        Assert.Equal(2, publisher.PublishedStateCount);
    }

    [Fact]
    public async Task Manual_ticks_publish_unchanged_state_on_every_tick()
    {
        var state = new ControllerState(new GamepadButtons(true, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false), default, default, default, new([false, false]));
        var source = new Snapshot(state);
        var sink = new FakeSink(); var ticks = new ManualTicks();
        var publisher = new CanonicalSteamControllerInputPublisher(source, sink, ticks);
        publisher.Start();

        await ticks.TickAsync(); await sink.WaitForCountAsync(1);
        await ticks.TickAsync(); await sink.WaitForCountAsync(2);
        await ticks.TickAsync(); await sink.WaitForCountAsync(3);
        await publisher.StopAsync();

        Assert.Equal(3, sink.States.Count);
        Assert.All(sink.States, published => Assert.Equal((byte)1, published.A));
        Assert.Equal(3, publisher.PublishedStateCount);
    }

    [Fact]
    public async Task False_or_throwing_sink_reports_one_fault_and_stops()
    {
        foreach (var throwing in new[] { false, true })
        {
            var sink = new FakeSink { Accept = throwing, ThrowOnSet = throwing }; var ticks = new ManualTicks(); var faults = 0;
            var faultObserved = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var publisher = new CanonicalSteamControllerInputPublisher(new Snapshot(new ControllerState(new AuxiliaryButtonState([false, false]))), sink, ticks, _ => { faults++; faultObserved.TrySetResult(true); });
            publisher.Start(); await ticks.TickAsync();
            await faultObserved.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await publisher.StopAsync(); await publisher.StopAsync();
            Assert.Equal(1, faults);
            Assert.False(publisher.IsRunning);
        }
    }

    [Fact]
    public async Task Short_min_right_stick_and_duplicate_start_are_safe()
    {
        var state = new ControllerState(default, default, new StickState(short.MinValue, short.MinValue), default, new AuxiliaryButtonState([false, false]));
        var sink = new FakeSink(); var ticks = new ManualTicks();
        var publisher = new CanonicalSteamControllerInputPublisher(new Snapshot(state), sink, ticks);
        publisher.Start();
        Assert.Throws<InvalidOperationException>(publisher.Start);
        await ticks.TickAsync(); await sink.WaitForCountAsync(1);
        await publisher.StopAsync(); await publisher.StopAsync();
        Assert.Single(sink.States);
    }

    [Fact]
    public async Task Mapped_dpad_transition_is_logged_at_info_and_deduplicated()
    {
        var state = new ControllerState(new GamepadButtons(false, false, false, false, true, false, false, false, false, false, false, false, false, false, false, false), default, default, default, new([false, false]));
        var source = new Snapshot(state);
        var sink = new FakeSink(); var ticks = new ManualTicks();
        var publisher = new CanonicalSteamControllerInputPublisher(source, sink, ticks);
        publisher.Start();

        await ticks.TickAsync(); await sink.WaitForCountAsync(1);
        // Same state on the second tick: the mapped D-pad transition log must not repeat.
        await ticks.TickAsync(); await sink.WaitForCountAsync(2);
        await publisher.StopAsync();

        AppLog.DrainForTests();
        var log = LogFileTestHelper.ReadAllText(AppLog.CurrentLogFilePath);
        Assert.Contains("[INFO]", log);
        Assert.Equal(1, log.Split("Canonical mapped D-pad state changed", StringSplitOptions.None).Length - 1);
        Assert.Contains("Up=1", log);
        Assert.Contains("Right=0", log);
        Assert.Contains("Down=0", log);
        Assert.Contains("Left=0", log);
    }

    [Fact]
    public async Task Mapped_dpad_transition_logs_again_when_state_changes_back()
    {
        var neutral = new ControllerState(default, default, default, default, new AuxiliaryButtonState([false, false]));
        var down = neutral with { Buttons = neutral.Buttons with { DPadDown = true } };
        var source = new Snapshot(neutral);
        var sink = new FakeSink(); var ticks = new ManualTicks();
        var publisher = new CanonicalSteamControllerInputPublisher(source, sink, ticks);
        publisher.Start();

        await ticks.TickAsync(); await sink.WaitForCountAsync(1);
        source.Value = down;
        await ticks.TickAsync(); await sink.WaitForCountAsync(2);
        source.Value = neutral;
        await ticks.TickAsync(); await sink.WaitForCountAsync(3);
        await publisher.StopAsync();

        AppLog.DrainForTests();
        var log = LogFileTestHelper.ReadAllText(AppLog.CurrentLogFilePath);
        // Neutral (initial) -> Down -> Neutral is three distinct observed mapped D-pad states.
        Assert.Equal(3, log.Split("Canonical mapped D-pad state changed", StringSplitOptions.None).Length - 1);
    }

    [Fact]
    public async Task Heartbeat_reports_counts_once_the_interval_elapses_and_resets_afterward()
    {
        var state = new ControllerState(default, default, default, default, new AuxiliaryButtonState([false, false]));
        var source = new Snapshot(state);
        var sink = new FakeSink(); var ticks = new ManualTicks();
        var fakeNow = 0L;
        var publisher = new CanonicalSteamControllerInputPublisher(source, sink, ticks, timestampProvider: () => fakeNow);
        publisher.Start();

        await ticks.TickAsync(); await sink.WaitForCountAsync(1);
        await ticks.TickAsync(); await sink.WaitForCountAsync(2);
        AppLog.DrainForTests();
        Assert.DoesNotContain("publisher heartbeat", File.Exists(AppLog.CurrentLogFilePath) ? LogFileTestHelper.ReadAllText(AppLog.CurrentLogFilePath) : string.Empty);

        // Simulate one second elapsing since Start(); the third tick's post-call heartbeat check
        // must now fire, reporting all three calls made since the last (never-fired) heartbeat.
        fakeNow = Stopwatch.Frequency + 1;
        await ticks.TickAsync(); await sink.WaitForCountAsync(3);
        await publisher.StopAsync();

        AppLog.DrainForTests();
        var log = LogFileTestHelper.ReadAllText(AppLog.CurrentLogFilePath);
        var heartbeat = Assert.Single(log.Split('\n'), line => line.Contains("Canonical Steam Controller publisher heartbeat"));
        Assert.Contains("SetStateCallsLastSecond=3", heartbeat);
        Assert.Contains("TotalPublishedStateCount=3", heartbeat);
        Assert.Contains("SetStateFailures=0", heartbeat);
        Assert.Contains("MaxSetStateDurationMs=", heartbeat);
        // fakeNow jumped from 0 to Stopwatch.Frequency+1, i.e. ~1000ms elapsed for 3 calls => ~3 Hz.
        Assert.Contains("HeartbeatElapsedMs=", heartbeat);
        Assert.Contains("EffectiveSetStateHz=", heartbeat);
        var effectiveHzText = heartbeat.Split("EffectiveSetStateHz=")[1].Split(' ')[0];
        var effectiveHz = double.Parse(effectiveHzText, System.Globalization.CultureInfo.InvariantCulture);
        Assert.InRange(effectiveHz, 2.9, 3.1);
        // The manual-tick test path never drives WorkerLoop, so the M5+ timing-decomposition fields stay
        // at their zero defaults here -- this just proves they're always present in the heartbeat line,
        // not that they carry data (see the RecordXxxForTests-driven tests below for the accounting logic
        // itself, and the real-timer production tests further below for end-to-end wiring).
        Assert.Contains("TimerWakeCount=0", heartbeat);
        Assert.Contains("ExpectedTicksAt4ms=", heartbeat);
        Assert.Contains("AverageWakeToWakeMs=0", heartbeat);
        Assert.Contains("MaxWakeToWakeMs=0", heartbeat);
        Assert.Contains("AverageWaitBlockedMs=0", heartbeat);
        Assert.Contains("MaxWaitBlockedMs=0", heartbeat);
        Assert.Contains("AveragePublishWorkMs=0", heartbeat);
        Assert.Contains("MaxPublishWorkMs=0", heartbeat);
        Assert.Contains("WakeOver4_25MsCount=0", heartbeat);
        Assert.Contains("WakeOver5MsCount=0", heartbeat);
        Assert.Contains("AverageWakeLatenessMs=0", heartbeat);
        Assert.Contains("MaxWakeLatenessMs=0", heartbeat);
        Assert.Contains("SkippedDeadlineCount=0", heartbeat);
    }

    [Fact]
    public async Task TimingDiagnostics_WakeToWakeAccumulatesAverageAndMax()
    {
        var source = new Snapshot(new ControllerState(new AuxiliaryButtonState([false, false])));
        var sink = new FakeSink(); var ticks = new ManualTicks();
        var fakeNow = 0L;
        var publisher = new CanonicalSteamControllerInputPublisher(source, sink, ticks, timestampProvider: () => fakeNow);
        publisher.Start();

        var msToTicks = Stopwatch.Frequency / 1000.0;
        // Three timer wakes -> two wake-to-wake intervals: 4ms then 6ms. Average = 5ms, max = 6ms.
        publisher.RecordTimerWakeForTests((long)(0 * msToTicks));
        publisher.RecordTimerWakeForTests((long)(4 * msToTicks));
        publisher.RecordTimerWakeForTests((long)(10 * msToTicks));

        fakeNow = Stopwatch.Frequency + 1;
        await ticks.TickAsync(); await sink.WaitForCountAsync(1);
        await publisher.StopAsync();

        AppLog.DrainForTests();
        var heartbeat = Assert.Single(LogFileTestHelper.ReadAllText(AppLog.CurrentLogFilePath).Split('\n'), line => line.Contains("publisher heartbeat"));
        Assert.Contains("TimerWakeCount=3", heartbeat);
        AssertFieldInRange(heartbeat, "AverageWakeToWakeMs", 4.9, 5.1);
        AssertFieldInRange(heartbeat, "MaxWakeToWakeMs", 5.9, 6.1);
    }

    [Fact]
    public async Task TimingDiagnostics_WakeThresholdCountersIncrementAt425And5Ms()
    {
        var source = new Snapshot(new ControllerState(new AuxiliaryButtonState([false, false])));
        var sink = new FakeSink(); var ticks = new ManualTicks();
        var fakeNow = 0L;
        var publisher = new CanonicalSteamControllerInputPublisher(source, sink, ticks, timestampProvider: () => fakeNow);
        publisher.Start();

        var msToTicks = Stopwatch.Frequency / 1000.0;
        // Intervals: 4.0ms (under both thresholds), 4.3ms (over 4.25 only), 5.2ms (over both).
        publisher.RecordTimerWakeForTests(0);
        publisher.RecordTimerWakeForTests((long)(4.0 * msToTicks));
        publisher.RecordTimerWakeForTests((long)(8.3 * msToTicks));
        publisher.RecordTimerWakeForTests((long)(13.5 * msToTicks));

        fakeNow = Stopwatch.Frequency + 1;
        await ticks.TickAsync(); await sink.WaitForCountAsync(1);
        await publisher.StopAsync();

        AppLog.DrainForTests();
        var heartbeat = Assert.Single(LogFileTestHelper.ReadAllText(AppLog.CurrentLogFilePath).Split('\n'), line => line.Contains("publisher heartbeat"));
        Assert.Contains("WakeOver4_25MsCount=2", heartbeat);
        Assert.Contains("WakeOver5MsCount=1", heartbeat);
    }

    [Fact]
    public async Task TimingDiagnostics_WaitBlockedAccumulatesAverageAndMax()
    {
        var source = new Snapshot(new ControllerState(new AuxiliaryButtonState([false, false])));
        var sink = new FakeSink(); var ticks = new ManualTicks();
        var fakeNow = 0L;
        var publisher = new CanonicalSteamControllerInputPublisher(source, sink, ticks, timestampProvider: () => fakeNow);
        publisher.Start();

        var msToTicks = Stopwatch.Frequency / 1000.0;
        // Two wait-blocked samples: 4.0ms and 4.6ms. Average = 4.3ms, max = 4.6ms. AverageWaitBlockedMs
        // is averaged over TimerWakeCount (the two are always recorded together in WorkerLoop), so a
        // matching RecordTimerWakeForTests call must accompany each sample here too.
        publisher.RecordWaitBlockedForTests(0, (long)(4.0 * msToTicks));
        publisher.RecordTimerWakeForTests(0);
        publisher.RecordWaitBlockedForTests(0, (long)(4.6 * msToTicks));
        publisher.RecordTimerWakeForTests((long)(4.6 * msToTicks));

        fakeNow = Stopwatch.Frequency + 1;
        await ticks.TickAsync(); await sink.WaitForCountAsync(1);
        await publisher.StopAsync();

        AppLog.DrainForTests();
        var heartbeat = Assert.Single(LogFileTestHelper.ReadAllText(AppLog.CurrentLogFilePath).Split('\n'), line => line.Contains("publisher heartbeat"));
        AssertFieldInRange(heartbeat, "AverageWaitBlockedMs", 4.2, 4.4);
        AssertFieldInRange(heartbeat, "MaxWaitBlockedMs", 4.5, 4.7);
    }

    [Fact]
    public async Task TimingDiagnostics_PublishWorkAccumulatesAverageAndMax()
    {
        var source = new Snapshot(new ControllerState(new AuxiliaryButtonState([false, false])));
        var sink = new FakeSink(); var ticks = new ManualTicks();
        var fakeNow = 0L;
        var publisher = new CanonicalSteamControllerInputPublisher(source, sink, ticks, timestampProvider: () => fakeNow);
        publisher.Start();

        var msToTicks = Stopwatch.Frequency / 1000.0;
        // Two publish-work samples: 0.1ms and 0.3ms. Average = 0.2ms, max = 0.3ms. AveragePublishWorkMs
        // is averaged over TimerWakeCount (the two are always recorded together in WorkerLoop), so a
        // matching RecordTimerWakeForTests call must accompany each sample here too.
        publisher.RecordPublishWorkForTests(0, (long)(0.1 * msToTicks));
        publisher.RecordTimerWakeForTests(0);
        publisher.RecordPublishWorkForTests(0, (long)(0.3 * msToTicks));
        publisher.RecordTimerWakeForTests((long)(0.3 * msToTicks));

        fakeNow = Stopwatch.Frequency + 1;
        await ticks.TickAsync(); await sink.WaitForCountAsync(1);
        await publisher.StopAsync();

        AppLog.DrainForTests();
        var heartbeat = Assert.Single(LogFileTestHelper.ReadAllText(AppLog.CurrentLogFilePath).Split('\n'), line => line.Contains("publisher heartbeat"));
        AssertFieldInRange(heartbeat, "AveragePublishWorkMs", 0.15, 0.25);
        AssertFieldInRange(heartbeat, "MaxPublishWorkMs", 0.25, 0.35);
    }

    [Fact]
    public async Task TimingDiagnostics_FirstTimerWakeDoesNotInventAWakeToWakeInterval()
    {
        var source = new Snapshot(new ControllerState(new AuxiliaryButtonState([false, false])));
        var sink = new FakeSink(); var ticks = new ManualTicks();
        var fakeNow = 0L;
        var publisher = new CanonicalSteamControllerInputPublisher(source, sink, ticks, timestampProvider: () => fakeNow);
        publisher.Start();

        // A single, first-ever timer wake: no previous wake exists, so no wake-to-wake sample should be
        // recorded (an average over zero samples must report 0, not a bogus/huge computed value).
        publisher.RecordTimerWakeForTests((long)(123 * (Stopwatch.Frequency / 1000.0)));

        fakeNow = Stopwatch.Frequency + 1;
        await ticks.TickAsync(); await sink.WaitForCountAsync(1);
        await publisher.StopAsync();

        AppLog.DrainForTests();
        var heartbeat = Assert.Single(LogFileTestHelper.ReadAllText(AppLog.CurrentLogFilePath).Split('\n'), line => line.Contains("publisher heartbeat"));
        Assert.Contains("TimerWakeCount=1", heartbeat);
        Assert.Contains("AverageWakeToWakeMs=0", heartbeat);
        Assert.Contains("MaxWakeToWakeMs=0", heartbeat);
    }

    [Fact]
    public async Task TimingDiagnostics_WakeLatenessAccumulatesAverageAndMax()
    {
        var source = new Snapshot(new ControllerState(new AuxiliaryButtonState([false, false])));
        var sink = new FakeSink(); var ticks = new ManualTicks();
        var fakeNow = 0L;
        var publisher = new CanonicalSteamControllerInputPublisher(source, sink, ticks, timestampProvider: () => fakeNow);
        publisher.Start();

        var msToTicks = Stopwatch.Frequency / 1000.0;
        // Two wakes, each ~0.3ms late relative to its own scheduled deadline: deadline 4.0ms/wake 4.3ms,
        // then deadline 8.0ms/wake 8.29ms. Average lateness = (0.3+0.29)/2 = 0.295ms, max = 0.3ms.
        publisher.RecordTimerWakeForTests((long)(4.3 * msToTicks));
        publisher.RecordWakeLatenessForTests((long)(4.3 * msToTicks), (long)(4.0 * msToTicks));
        publisher.RecordTimerWakeForTests((long)(8.29 * msToTicks));
        publisher.RecordWakeLatenessForTests((long)(8.29 * msToTicks), (long)(8.0 * msToTicks));

        fakeNow = Stopwatch.Frequency + 1;
        await ticks.TickAsync(); await sink.WaitForCountAsync(1);
        await publisher.StopAsync();

        AppLog.DrainForTests();
        var heartbeat = Assert.Single(LogFileTestHelper.ReadAllText(AppLog.CurrentLogFilePath).Split('\n'), line => line.Contains("publisher heartbeat"));
        Assert.Contains("TimerWakeCount=2", heartbeat);
        AssertFieldInRange(heartbeat, "AverageWakeLatenessMs", 0.28, 0.31);
        AssertFieldInRange(heartbeat, "MaxWakeLatenessMs", 0.29, 0.31);
    }

    [Fact]
    public async Task TimingDiagnostics_WakeLatenessClampsNegativeToZero()
    {
        // A wake can be observed at or fractionally before its own scheduled deadline depending on
        // clock/timer granularity; that must report as zero lateness, not a negative value.
        var source = new Snapshot(new ControllerState(new AuxiliaryButtonState([false, false])));
        var sink = new FakeSink(); var ticks = new ManualTicks();
        var fakeNow = 0L;
        var publisher = new CanonicalSteamControllerInputPublisher(source, sink, ticks, timestampProvider: () => fakeNow);
        publisher.Start();

        var msToTicks = Stopwatch.Frequency / 1000.0;
        publisher.RecordTimerWakeForTests((long)(3.9 * msToTicks));
        publisher.RecordWakeLatenessForTests((long)(3.9 * msToTicks), (long)(4.0 * msToTicks)); // "early" by 0.1ms

        fakeNow = Stopwatch.Frequency + 1;
        await ticks.TickAsync(); await sink.WaitForCountAsync(1);
        await publisher.StopAsync();

        AppLog.DrainForTests();
        var heartbeat = Assert.Single(LogFileTestHelper.ReadAllText(AppLog.CurrentLogFilePath).Split('\n'), line => line.Contains("publisher heartbeat"));
        Assert.Contains("AverageWakeLatenessMs=0", heartbeat);
        Assert.Contains("MaxWakeLatenessMs=0", heartbeat);
    }

    [Fact]
    public async Task TimingDiagnostics_ResetAfterHeartbeatDoesNotCarryIntervalCountsOrSumsIntoTheNextWindow()
    {
        var source = new Snapshot(new ControllerState(new AuxiliaryButtonState([false, false])));
        var sink = new FakeSink(); var ticks = new ManualTicks();
        var fakeNow = 0L;
        var publisher = new CanonicalSteamControllerInputPublisher(source, sink, ticks, timestampProvider: () => fakeNow);
        publisher.Start();

        var msToTicks = Stopwatch.Frequency / 1000.0;
        publisher.RecordTimerWakeForTests(0);
        publisher.RecordTimerWakeForTests((long)(4 * msToTicks));
        publisher.RecordWaitBlockedForTests(0, (long)(4 * msToTicks));
        publisher.RecordPublishWorkForTests(0, (long)(1 * msToTicks));
        publisher.RecordWakeLatenessForTests((long)(4 * msToTicks), (long)(3.5 * msToTicks));

        fakeNow = Stopwatch.Frequency + 1;
        await ticks.TickAsync(); await sink.WaitForCountAsync(1); // first heartbeat fires here

        // A second window: only one more timer wake, no wait-blocked/publish-work samples at all this
        // time. If the previous window's sums/counts leaked through, this heartbeat would still show
        // stale non-zero wait-blocked/publish-work data or an inflated wake count.
        publisher.RecordTimerWakeForTests((long)(4 * msToTicks) + (long)(4 * msToTicks));
        fakeNow = 2 * Stopwatch.Frequency + 2;
        await ticks.TickAsync(); await sink.WaitForCountAsync(2);
        await publisher.StopAsync();

        AppLog.DrainForTests();
        var lines = LogFileTestHelper.ReadAllText(AppLog.CurrentLogFilePath).Split('\n').Where(line => line.Contains("publisher heartbeat")).ToArray();
        Assert.Equal(2, lines.Length);
        var secondHeartbeat = lines[1];
        // Only the carried-over wake-to-wake interval from the window boundary (4ms, from the last wake
        // of window 1 to the one wake of window 2) should show up -- not window 1's own first interval.
        Assert.Contains("TimerWakeCount=1", secondHeartbeat);
        Assert.Contains("AverageWaitBlockedMs=0", secondHeartbeat);
        Assert.Contains("MaxWaitBlockedMs=0", secondHeartbeat);
        Assert.Contains("AveragePublishWorkMs=0", secondHeartbeat);
        Assert.Contains("MaxPublishWorkMs=0", secondHeartbeat);
        Assert.Contains("AverageWakeLatenessMs=0", secondHeartbeat);
        Assert.Contains("MaxWakeLatenessMs=0", secondHeartbeat);
        Assert.Contains("SkippedDeadlineCount=0", secondHeartbeat);
    }

    [Fact]
    public async Task TimingDiagnostics_StopThenStartDoesNotCarryTheLastWakeTimestampAcrossTheGap()
    {
        // Unlike a heartbeat reset (which intentionally keeps _previousTimerWakeTimestamp so the
        // interval spanning the boundary is still a valid sample), a Stop-then-Start on the same
        // publisher instance can have an arbitrarily long real-world gap between them -- production
        // supports exactly this (a new routing session reusing the same publisher). Without a reset in
        // Start(), the first timer wake of the new run would be diffed against the last wake of the
        // previous run and misreported as e.g. a multi-second WakeToWake outlier.
        var source = new Snapshot(new ControllerState(new AuxiliaryButtonState([false, false])));
        var sink = new FakeSink(); var ticks = new ManualTicks();
        var fakeNow = 0L;
        var publisher = new CanonicalSteamControllerInputPublisher(source, sink, ticks, timestampProvider: () => fakeNow);
        publisher.Start();

        publisher.RecordTimerWakeForTests(0);
        fakeNow = Stopwatch.Frequency + 1;
        await ticks.TickAsync(); await sink.WaitForCountAsync(1); // heartbeat fires, leaves _hasPreviousTimerWake set
        await publisher.StopAsync();

        // Simulate a large real-world gap before the same instance is started again.
        fakeNow += 5 * Stopwatch.Frequency;
        publisher.Start();

        var secondRunWake = fakeNow;
        publisher.RecordTimerWakeForTests(secondRunWake);
        fakeNow = secondRunWake + Stopwatch.Frequency + 1;
        // ManualTicks.TickAsync() dequeues its waiter queue FIFO but a cancelled wait from the first
        // run's loop (cancelled by StopAsync, not dequeued) is still sitting at the front; one throwaway
        // tick flushes that stale entry so the next one actually reaches the restarted loop's new wait.
        await ticks.TickAsync();
        await ticks.TickAsync(); await sink.WaitForCountAsync(2);
        await publisher.StopAsync();

        AppLog.DrainForTests();
        var lines = LogFileTestHelper.ReadAllText(AppLog.CurrentLogFilePath).Split('\n').Where(line => line.Contains("publisher heartbeat")).ToArray();
        Assert.Equal(2, lines.Length);
        var afterRestart = lines[1];
        Assert.Contains("TimerWakeCount=1", afterRestart);
        Assert.Contains("AverageWakeToWakeMs=0", afterRestart);
        Assert.Contains("MaxWakeToWakeMs=0", afterRestart);
        Assert.Contains("WakeOver4_25MsCount=0", afterRestart);
        Assert.Contains("WakeOver5MsCount=0", afterRestart);
    }

    [Fact]
    public async Task Production_worker_timing_diagnostics_wire_up_end_to_end_with_the_real_timer()
    {
        // Real production path (dedicated worker + real high-resolution timer), with a fake clock that
        // forces the heartbeat to fire almost immediately in wall-clock time so this stays fast and
        // deterministic without asserting any specific Hz. For a clean run with no SetState failures,
        // every normal timer wake corresponds to exactly one successful publish, so TimerWakeCount must
        // equal PublishedStateCount. Note this does NOT by itself prove the stop event's wake is never
        // miscounted as a timer wake: StopAsync runs only after the heartbeat above is already read, and
        // WorkerLoop returns immediately on a stop wake without calling PublishCurrentStateOnce (the only
        // path that can trigger another heartbeat), so a hypothetical regression that counted the stop
        // wake would not surface as a logged heartbeat here for this assertion to catch. That guarantee
        // instead rests on WorkerLoop's `if (signaled == 0) return;` running before any counter is
        // touched (see the comment there) -- a one-line, directly-readable invariant that doesn't need
        // its own seam.
        var source = new Snapshot(new ControllerState(new AuxiliaryButtonState([false, false])));
        var sink = new FakeSink();
        var realElapsed = Stopwatch.StartNew();
        long TimestampProvider() => realElapsed.ElapsedMilliseconds >= 5 ? Stopwatch.Frequency + 1 : 0;
        var publisher = new CanonicalSteamControllerInputPublisher(source, sink, timestampProvider: TimestampProvider);

        publisher.Start();
        try
        {
            await sink.WaitForCountAsync(2, TimeSpan.FromSeconds(2));
            // Give a couple more real ticks a chance to land so at least one heartbeat has fired.
            await Task.Delay(50);
        }
        finally
        {
            await publisher.StopAsync();
        }

        AppLog.DrainForTests();
        var log = LogFileTestHelper.ReadAllText(AppLog.CurrentLogFilePath);
        var heartbeat = log.Split('\n').LastOrDefault(line => line.Contains("publisher heartbeat"));
        Assert.NotNull(heartbeat);
        var wakeCountText = heartbeat!.Split("TimerWakeCount=")[1].Split(' ')[0];
        var publishedText = heartbeat.Split("TotalPublishedStateCount=")[1].Split(' ')[0];
        Assert.Equal(int.Parse(publishedText, System.Globalization.CultureInfo.InvariantCulture), int.Parse(wakeCountText, System.Globalization.CultureInfo.InvariantCulture));
    }

    private static void AssertFieldInRange(string heartbeat, string field, double low, double high)
    {
        var text = heartbeat.Split(field + "=")[1].Split(' ')[0].TrimEnd('\r');
        var value = double.Parse(text, System.Globalization.CultureInfo.InvariantCulture);
        Assert.InRange(value, low, high);
    }

    [Fact]
    public async Task Production_worker_publishes_using_the_real_high_resolution_timer()
    {
        // No IInputReportTickSource supplied: this exercises the actual production path (dedicated
        // worker thread + WindowsHighResolutionPeriodicTimer), not the manual-tick test seam. Only
        // presence/lifecycle is asserted -- never a specific Hz -- to stay deterministic under CI load.
        var source = new Snapshot(new ControllerState(new AuxiliaryButtonState([false, false])));
        var sink = new FakeSink();
        var publisher = new CanonicalSteamControllerInputPublisher(source, sink);

        publisher.Start();
        try
        {
            await sink.WaitForCountAsync(1, TimeSpan.FromSeconds(2));
        }
        finally
        {
            await publisher.StopAsync();
        }

        Assert.True(sink.Count >= 1);
        Assert.True(publisher.PublishedStateCount >= 1);
    }

    [Fact]
    public async Task Production_worker_publishes_multiple_ticks_over_a_short_window()
    {
        // At a 4 ms period, a 200 ms window should comfortably produce more than one publish even under
        // heavy CI scheduling noise -- this is a "the timer actually recurs" check, not a rate assertion.
        var source = new Snapshot(new ControllerState(new AuxiliaryButtonState([false, false])));
        var sink = new FakeSink();
        var publisher = new CanonicalSteamControllerInputPublisher(source, sink);

        publisher.Start();
        try
        {
            await sink.WaitForCountAsync(2, TimeSpan.FromSeconds(2));
        }
        finally
        {
            await publisher.StopAsync();
        }

        Assert.True(sink.Count >= 2);
    }

    [Fact]
    public async Task Production_stop_wakes_the_worker_promptly_and_stops_publishing()
    {
        var source = new Snapshot(new ControllerState(new AuxiliaryButtonState([false, false])));
        var sink = new FakeSink();
        var publisher = new CanonicalSteamControllerInputPublisher(source, sink);
        publisher.Start();
        TimeSpan stopElapsed;
        try
        {
            await sink.WaitForCountAsync(1, TimeSpan.FromSeconds(2));
        }
        finally
        {
            // StopAsync (and its timing) must run even if the wait above throws (timeout), so no worker
            // thread or native timer handle is left behind for the rest of the test process.
            var stopwatch = Stopwatch.StartNew();
            await publisher.StopAsync();
            stopElapsed = stopwatch.Elapsed;
        }

        // The worker wakes on the stop event immediately; this is far below the 5s join safety-net
        // timeout, so a slow stop here would indicate the worker isn't actually waking on the event.
        Assert.True(stopElapsed < TimeSpan.FromSeconds(1), $"StopAsync took {stopElapsed.TotalMilliseconds} ms.");
        Assert.False(publisher.IsRunning);
    }

    [Fact]
    public async Task Production_no_SetState_call_begins_after_shutdown_completes()
    {
        var source = new Snapshot(new ControllerState(new AuxiliaryButtonState([false, false])));
        var sink = new FakeSink();
        var publisher = new CanonicalSteamControllerInputPublisher(source, sink);
        publisher.Start();
        try
        {
            await sink.WaitForCountAsync(1, TimeSpan.FromSeconds(2));
        }
        finally
        {
            await publisher.StopAsync();
        }

        var countAtStop = sink.Count;
        await Task.Delay(100);

        Assert.Equal(countAtStop, sink.Count);
    }

    [Fact]
    public async Task Production_start_stop_lifecycle_is_safe_and_restartable()
    {
        var source = new Snapshot(new ControllerState(new AuxiliaryButtonState([false, false])));
        var sink = new FakeSink();
        var publisher = new CanonicalSteamControllerInputPublisher(source, sink);

        publisher.Start();
        try
        {
            Assert.Throws<InvalidOperationException>(publisher.Start);
            await sink.WaitForCountAsync(1, TimeSpan.FromSeconds(2));
        }
        finally
        {
            await publisher.StopAsync();
        }
        await publisher.StopAsync(); // no-op, must not throw

        // Restart after a clean stop must work exactly like the first start.
        publisher.Start();
        try
        {
            await sink.WaitForCountAsync(sink.Count + 1, TimeSpan.FromSeconds(2));
        }
        finally
        {
            await publisher.StopAsync();
        }
    }

    [Fact]
    public async Task Production_SetState_returning_false_triggers_existing_fault_semantics()
    {
        var source = new Snapshot(new ControllerState(new AuxiliaryButtonState([false, false])));
        var sink = new FakeSink { Accept = false };
        var faults = 0;
        var faultObserved = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var publisher = new CanonicalSteamControllerInputPublisher(source, sink, fault: _ => { Interlocked.Increment(ref faults); faultObserved.TrySetResult(true); });

        publisher.Start();
        try
        {
            await faultObserved.Task.WaitAsync(TimeSpan.FromSeconds(2));
        }
        finally
        {
            await publisher.StopAsync();
        }

        Assert.Equal(1, faults);
        Assert.False(publisher.IsRunning);
    }

    [Fact]
    public async Task Production_SetState_throwing_triggers_existing_fault_semantics()
    {
        var source = new Snapshot(new ControllerState(new AuxiliaryButtonState([false, false])));
        var sink = new FakeSink { ThrowOnSet = true };
        var faults = 0;
        var faultObserved = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var publisher = new CanonicalSteamControllerInputPublisher(source, sink, fault: _ => { Interlocked.Increment(ref faults); faultObserved.TrySetResult(true); });

        publisher.Start();
        await faultObserved.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await publisher.StopAsync();

        Assert.Equal(1, faults);
        Assert.False(publisher.IsRunning);
    }

    [Fact]
    public async Task Production_worker_does_not_burst_catch_up_ticks_after_a_slow_publish()
    {
        // The worker is not waiting on the timer at all while a SetState call is in flight -- it only
        // re-arms the one-shot timer after the publish returns, at which point AdvanceDeadline skips
        // forward over any logical deadlines that already expired during the block and arms for the next
        // one strictly in the future. So no signals can possibly queue up while blocked, and only one
        // publish happens for the block regardless of how many periods it spanned. Block the very first
        // SetState for well beyond several 4 ms periods, then assert the total call count shortly after
        // unblocking is small (steady-cadence sized), not a burst proportional to the periods that
        // elapsed while blocked.
        var source = new Snapshot(new ControllerState(new AuxiliaryButtonState([false, false])));
        var firstCallBlocked = new ManualResetEventSlim(false);
        var releaseFirstCall = new ManualResetEventSlim(false);
        var sink = new BlockingFirstCallSink(firstCallBlocked, releaseFirstCall);
        var publisher = new CanonicalSteamControllerInputPublisher(source, sink);

        publisher.Start();
        try
        {
            Assert.True(firstCallBlocked.Wait(TimeSpan.FromSeconds(2)), "The first SetState call never started.");
            // ~25 timer periods would fire while blocked here if periods queued; they must not.
            await Task.Delay(100);
            releaseFirstCall.Set();
            // Give the worker a further short, bounded window to resume normal cadence.
            await Task.Delay(100);
        }
        finally
        {
            await publisher.StopAsync();
        }

        // Steady 4 ms cadence over ~100ms post-release would be on the order of ~25 calls; a catch-up
        // burst for the ~100ms spent blocked would add roughly that many again. Assert well below a
        // doubled/burst count without pinning an exact number (CI scheduling noise).
        Assert.True(sink.Count < 60, $"Expected no catch-up burst, but observed {sink.Count} SetState calls.");
    }

    [Fact]
    public async Task Production_stop_requested_while_SetState_is_blocked_exits_before_rearming()
    {
        // Under the earlier periodic-timer design this exercised a genuine WaitAny([timer, stopEvent])
        // race (a timer period could elapse -- and the timer re-signal itself -- while SetState was still
        // blocked, so both handles could be signaled by the time the worker returned to wait; stop, at
        // index 0, had to win that race). The one-shot timer is not waiting at all -- and so cannot become
        // signaled again -- from the moment it fires until WorkerLoop explicitly re-arms it after the
        // publish returns, so that specific both-signaled race can no longer occur here. What this test
        // verifies now: StopAsync signals the stop event while a slow SetState call is still in flight;
        // once SetState returns, the worker's stopEvent.WaitOne(0) check (run before computing the next
        // deadline or re-arming) sees stop requested and exits -- no re-arm, no second SetState call. The
        // WaitAny stop-priority ordering itself (stopEvent still at index 0) is unchanged and still
        // matters for an ordinary wait where the timer could legitimately also be signaled.
        var source = new Snapshot(new ControllerState(new AuxiliaryButtonState([false, false])));
        var firstCallBlocked = new ManualResetEventSlim(false);
        var releaseFirstCall = new ManualResetEventSlim(false);
        var sink = new BlockingFirstCallSink(firstCallBlocked, releaseFirstCall);
        var publisher = new CanonicalSteamControllerInputPublisher(source, sink);

        publisher.Start();
        try
        {
            Assert.True(firstCallBlocked.Wait(TimeSpan.FromSeconds(2)), "The first SetState call never started.");

            // Begin stopping while the worker is still blocked inside the first SetState. StopAsync's
            // stop-event Set() runs synchronously before its first await, so by the time this call
            // returns a Task, the stop event is already signaled.
            var stopTask = publisher.StopAsync();

            // Let some time elapse while still blocked, so the stop request is comfortably in place well
            // before SetState returns and the worker gets to its post-publish stop check.
            await Task.Delay(50);
            releaseFirstCall.Set();

            await stopTask.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            // No-op if the above already completed the stop; a safety net if an assertion failed first.
            await publisher.StopAsync();
        }

        Assert.Equal(1, sink.Count);
        Assert.False(publisher.IsRunning);
    }

    [Fact]
    public async Task Production_stop_fails_closed_and_preserves_state_when_the_worker_does_not_join_in_time()
    {
        // Regression: a join timeout must NOT let StopAsync complete "successfully" while the worker may
        // still be inside SetState -- the caller (ClassicSteamControllerOutputStage) proceeds straight
        // into native Gordon device removal after a successful StopAsync, which must never race a still-
        // running SetState against the native handle being torn down.
        var source = new Snapshot(new ControllerState(new AuxiliaryButtonState([false, false])));
        var firstCallBlocked = new ManualResetEventSlim(false);
        var releaseFirstCall = new ManualResetEventSlim(false);
        var sink = new BlockingFirstCallSink(firstCallBlocked, releaseFirstCall);
        var publisher = new CanonicalSteamControllerInputPublisher(source, sink)
        {
            WorkerJoinTimeoutForTests = TimeSpan.FromMilliseconds(50),
        };

        publisher.Start();
        try
        {
            Assert.True(firstCallBlocked.Wait(TimeSpan.FromSeconds(2)), "The first SetState call never started.");

            // The worker is blocked well past the 50ms join timeout, so StopAsync must throw rather than
            // return successfully.
            await Assert.ThrowsAsync<TimeoutException>(publisher.StopAsync);

            // Fail closed: the publisher must still consider itself running (worker reference retained,
            // thread genuinely still alive), so a caller cannot mistake this for a clean stop.
            Assert.True(publisher.IsRunning);
        }
        finally
        {
            // Unblock the slow call so the worker can actually exit, then let a normal StopAsync (now
            // well within budget) complete cleanup -- proving the state left behind by the failed stop is
            // still recoverable rather than corrupted.
            releaseFirstCall.Set();
            await publisher.StopAsync();
        }

        Assert.False(publisher.IsRunning);
    }

    [Fact]
    public async Task Production_worker_thread_start_failure_cleans_up_handles_and_propagates()
    {
        // Thread.Start() cannot be made to fail on demand (it only fails on rare conditions like
        // OutOfMemoryException), so this uses the test-only override seam to simulate that failure
        // deterministically and assert the timer/stop-event/worker-thread state it leaves behind is
        // clean -- not a leaked native timer handle or a _workerThread reference to a never-started
        // thread (which would make a later StopAsync's Join throw ThreadStateException instead of
        // cleaning up).
        var source = new Snapshot(new ControllerState(new AuxiliaryButtonState([false, false])));
        var sink = new FakeSink();
        var publisher = new CanonicalSteamControllerInputPublisher(source, sink)
        {
            WorkerThreadStartOverrideForTests = _ => throw new InvalidOperationException("simulated Thread.Start() failure"),
        };

        var exception = Assert.Throws<InvalidOperationException>(publisher.Start);

        Assert.Contains("worker thread", exception.Message);
        Assert.False(publisher.IsRunning);

        // The failure must be fully recoverable: a normal Start() (no override) afterward works exactly
        // like a first attempt, proving no handle or state was left behind by the failed one.
        publisher.WorkerThreadStartOverrideForTests = null;
        publisher.Start();
        try
        {
            await sink.WaitForCountAsync(1, TimeSpan.FromSeconds(2));
        }
        finally
        {
            await publisher.StopAsync();
        }
    }

    [Fact]
    public async Task Production_worker_reports_fault_and_stops_on_a_runtime_rearm_failure_without_a_fallback_scheduler()
    {
        // A real SetWaitableTimerEx re-arm failure after the timer was already successfully created and
        // armed once (e.g. an OS resource exhaustion mid-run) must report the existing publisher fault
        // and stop the worker -- not fall back to Task.Delay, a different timer, or silently keep running
        // on a stale arm. Uses the ArmForDeadlineOverrideForTests seam: it performs the real arm for the
        // very first call (so the worker starts up and gets one real, deterministic wake and publish),
        // then simulates the native call failing on every arm after that.
        var source = new Snapshot(new ControllerState(new AuxiliaryButtonState([false, false])));
        var sink = new FakeSink();
        var faults = 0;
        var faultObserved = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var publisher = new CanonicalSteamControllerInputPublisher(source, sink, fault: _ => { Interlocked.Increment(ref faults); faultObserved.TrySetResult(true); });
        var armCount = 0;
        publisher.ArmForDeadlineOverrideForTests = (timer, deadlineTicks, nowTicks) =>
        {
            var count = Interlocked.Increment(ref armCount);
            if (count == 1)
            {
                var remaining = deadlineTicks - nowTicks;
                var due100ns = CanonicalPublisherDeadlineMath.ConvertToRelativeDueTime100ns(remaining, Stopwatch.Frequency);
                timer.ArmRelative(TimeSpan.FromTicks(due100ns));
                return;
            }
            throw new InvalidOperationException("simulated SetWaitableTimerEx re-arm failure");
        };

        publisher.Start();
        try
        {
            await faultObserved.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            await publisher.StopAsync();
        }

        Assert.Equal(1, faults);
        Assert.False(publisher.IsRunning);
        // Exactly one publish from the single real arm -- no fallback scheduler kept it running.
        Assert.Equal(1, sink.Count);
    }

    [Fact]
    public async Task Production_initial_arm_failure_is_synchronous_and_restartable()
    {
        // Distinct from the runtime re-arm-failure test above: this is the very first ArmForDeadline call
        // in StartProductionWorker, before the worker thread is even created. Start() must fail closed
        // synchronously -- no worker thread, no leaked timer handle -- exactly like a timer-creation
        // failure, and the publisher must be fully usable again afterward.
        var source = new Snapshot(new ControllerState(new AuxiliaryButtonState([false, false])));
        var sink = new FakeSink();
        var publisher = new CanonicalSteamControllerInputPublisher(source, sink)
        {
            ArmForDeadlineOverrideForTests = (_, _, _) => throw new InvalidOperationException("simulated initial SetWaitableTimerEx failure"),
        };

        var exception = Assert.Throws<InvalidOperationException>(publisher.Start);

        Assert.Contains("timer", exception.Message);
        Assert.False(publisher.IsRunning);

        // Fully recoverable: removing the override and starting again works exactly like a first attempt,
        // proving no handle or state was left behind by the failed initial arm.
        publisher.ArmForDeadlineOverrideForTests = null;
        publisher.Start();
        try
        {
            await sink.WaitForCountAsync(1, TimeSpan.FromSeconds(2));
        }
        finally
        {
            await publisher.StopAsync();
        }
    }

    private sealed class BlockingFirstCallSink(ManualResetEventSlim firstCallBlocked, ManualResetEventSlim releaseFirstCall) : ICanonicalSteamControllerStateSink
    {
        private int _count;
        private int _isFirstCall = 1;
        internal int Count => Volatile.Read(ref _count);
        public bool SetState(SteamControllerDeviceState state)
        {
            Interlocked.Increment(ref _count);
            if (Interlocked.Exchange(ref _isFirstCall, 0) == 1)
            {
                firstCallBlocked.Set();
                releaseFirstCall.Wait();
            }
            return true;
        }
    }

    private sealed class Snapshot(ControllerState value) : IControllerStateSnapshotSource
    { public ControllerState Value { get; set; } = value; public ControllerState LatestState => Value; }

    private sealed class ManualTicks : IInputReportTickSource
    {
        private readonly Queue<TaskCompletionSource<bool>> _waiters = new();
        public ValueTask<bool> WaitForTickAsync(CancellationToken token)
        {
            var waiter = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            _waiters.Enqueue(waiter); token.Register(() => waiter.TrySetCanceled(token)); return new(waiter.Task);
        }
        public async Task TickAsync()
        {
            while (_waiters.Count == 0) await Task.Yield();
            _waiters.Dequeue().TrySetResult(true);
        }
    }

    private sealed class FakeSink : ICanonicalSteamControllerStateSink
    {
        // Thread-safe: the real high-resolution-timer/dedicated-worker production path calls SetState
        // from its own thread while tests observe from the test thread, unlike the manual-tick tests
        // above where everything happens on one thread.
        private readonly Lock _sync = new();
        private readonly List<SteamControllerDeviceState> _states = [];
        internal volatile bool Accept = true;
        internal volatile bool ThrowOnSet;
        internal IReadOnlyList<SteamControllerDeviceState> States { get { lock (_sync) return _states.ToArray(); } }
        internal int Count { get { lock (_sync) return _states.Count; } }
        public bool SetState(SteamControllerDeviceState state)
        {
            if (ThrowOnSet) throw new InvalidOperationException("set failed");
            lock (_sync) _states.Add(state);
            return Accept;
        }
        public async Task WaitForCountAsync(int count, TimeSpan? timeout = null)
        {
            var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(5));
            while (Count < count)
            {
                if (DateTime.UtcNow >= deadline) throw new TimeoutException($"FakeSink did not reach {count} SetState calls within the timeout (had {Count}).");
                await Task.Delay(5);
            }
        }
    }
}
