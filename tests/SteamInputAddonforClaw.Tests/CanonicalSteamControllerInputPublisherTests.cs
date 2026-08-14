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
        // A synchronization (auto-reset) waitable timer does not queue multiple missed signals -- if the
        // worker is busy for several period-lengths, only one signal is still pending when it comes back
        // to wait, so it publishes once and resumes normal cadence instead of firing a backlog of
        // "catch up" calls. Block the very first SetState for well beyond several 4 ms periods, then
        // assert the total call count shortly after unblocking is small (steady-cadence sized), not a
        // burst proportional to the periods that elapsed while blocked.
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
    public async Task Production_stop_wins_the_race_when_both_stop_and_timer_are_signaled()
    {
        // Regression for WaitAny([timer, stopEvent]) returning the lowest signaled index: if the worker
        // is still inside a slow SetState when StopAsync signals the stop event, and a timer period also
        // elapses before SetState returns, both handles are signaled by the time the worker waits again.
        // Stop must win that race -- the worker must exit without starting a second SetState call.
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

            // Let several 4 ms periods elapse while still blocked, so the timer is also signaled by the
            // time the worker returns to WaitAny -- this is the race window from the review.
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
