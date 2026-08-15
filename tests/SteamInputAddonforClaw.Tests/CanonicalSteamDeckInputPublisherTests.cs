using System.Diagnostics;
using SteamInputAddonforClaw.Devices.MSI.Claw;
using SteamInputAddonforClaw.Diagnostics;
using SteamInputAddonforClaw.Input;
using SteamInputAddonforClaw.VirtualOutput.Viiper;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

/// <summary>
/// Steam Deck counterpart to <see cref="CanonicalSteamControllerInputPublisherTests"/>: same manual-
/// tick and production-worker coverage (scheduler, fault semantics, timing diagnostics, lifecycle
/// safety), exercised against <see cref="CanonicalSteamDeckInputPublisher"/>. Deduplicated D-pad
/// transition logging is a Gordon-specific concern (that publisher logs a mapped D-pad transition
/// message the Deck publisher does not emit) and is intentionally not mirrored here.
/// </summary>
[Collection("AppLog")]
public sealed class CanonicalSteamDeckInputPublisherTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "CanonicalSteamDeckInputPublisherTests", Guid.NewGuid().ToString("N"));

    public CanonicalSteamDeckInputPublisherTests()
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
    public async Task Manual_ticks_publish_Deck_mapped_state()
    {
        var source = new Snapshot(new ControllerState(new GamepadButtons(true, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false), default, default, default, new([false, false])));
        var sink = new FakeSink(); var ticks = new ManualTicks();
        var publisher = new CanonicalSteamDeckInputPublisher(source, sink, ticks);
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
    public async Task Unchanged_state_is_published_every_tick()
    {
        var state = new ControllerState(new GamepadButtons(true, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false), default, default, default, new([false, false]));
        var source = new Snapshot(state);
        var sink = new FakeSink(); var ticks = new ManualTicks();
        var publisher = new CanonicalSteamDeckInputPublisher(source, sink, ticks);
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
    public async Task False_sink_reports_fault_and_stops()
    {
        var sink = new FakeSink { Accept = false }; var ticks = new ManualTicks(); var faults = 0;
        var faultObserved = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var publisher = new CanonicalSteamDeckInputPublisher(new Snapshot(new ControllerState(new AuxiliaryButtonState([false, false]))), sink, ticks, _ => { faults++; faultObserved.TrySetResult(true); });
        publisher.Start(); await ticks.TickAsync();
        await faultObserved.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await publisher.StopAsync(); await publisher.StopAsync();
        Assert.Equal(1, faults);
        Assert.False(publisher.IsRunning);
    }

    [Fact]
    public async Task Throwing_sink_reports_fault_and_stops()
    {
        var sink = new FakeSink { ThrowOnSet = true }; var ticks = new ManualTicks(); var faults = 0;
        var faultObserved = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var publisher = new CanonicalSteamDeckInputPublisher(new Snapshot(new ControllerState(new AuxiliaryButtonState([false, false]))), sink, ticks, _ => { faults++; faultObserved.TrySetResult(true); });
        publisher.Start(); await ticks.TickAsync();
        await faultObserved.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await publisher.StopAsync(); await publisher.StopAsync();
        Assert.Equal(1, faults);
        Assert.False(publisher.IsRunning);
    }

    [Fact]
    public async Task Duplicate_Start_rejected()
    {
        var state = new ControllerState(default, default, new StickState(short.MinValue, short.MinValue), default, new AuxiliaryButtonState([false, false]));
        var sink = new FakeSink(); var ticks = new ManualTicks();
        var publisher = new CanonicalSteamDeckInputPublisher(new Snapshot(state), sink, ticks);
        publisher.Start();
        Assert.Throws<InvalidOperationException>(publisher.Start);
        await ticks.TickAsync(); await sink.WaitForCountAsync(1);
        await publisher.StopAsync();
        Assert.Single(sink.States);
    }

    [Fact]
    public async Task Stop_is_idempotent()
    {
        var sink = new FakeSink(); var ticks = new ManualTicks();
        var publisher = new CanonicalSteamDeckInputPublisher(new Snapshot(new ControllerState(new AuxiliaryButtonState([false, false]))), sink, ticks);
        publisher.Start();
        await ticks.TickAsync(); await sink.WaitForCountAsync(1);
        await publisher.StopAsync();
        await publisher.StopAsync(); // no-op, must not throw
        Assert.False(publisher.IsRunning);
    }

    [Fact]
    public async Task Stop_Start_restart_safe()
    {
        var sink = new FakeSink();
        var publisher = new CanonicalSteamDeckInputPublisher(new Snapshot(new ControllerState(new AuxiliaryButtonState([false, false]))), sink);

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
        await publisher.StopAsync();

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
    public async Task Heartbeat_reports_Gordon_parity_timing_fields()
    {
        var state = new ControllerState(default, default, default, default, new AuxiliaryButtonState([false, false]));
        var source = new Snapshot(state);
        var sink = new FakeSink(); var ticks = new ManualTicks();
        var fakeNow = 0L;
        var publisher = new CanonicalSteamDeckInputPublisher(source, sink, ticks, timestampProvider: () => fakeNow);
        publisher.Start();

        await ticks.TickAsync(); await sink.WaitForCountAsync(1);
        await ticks.TickAsync(); await sink.WaitForCountAsync(2);
        AppLog.DrainForTests();
        Assert.DoesNotContain("publisher heartbeat", File.Exists(AppLog.CurrentLogFilePath) ? LogFileTestHelper.ReadAllText(AppLog.CurrentLogFilePath) : string.Empty);

        fakeNow = Stopwatch.Frequency + 1;
        await ticks.TickAsync(); await sink.WaitForCountAsync(3);
        await publisher.StopAsync();

        AppLog.DrainForTests();
        var log = LogFileTestHelper.ReadAllText(AppLog.CurrentLogFilePath);
        var heartbeat = Assert.Single(log.Split('\n'), line => line.Contains("Canonical Steam Deck publisher heartbeat"));
        Assert.Contains("SetStateCallsLastSecond=3", heartbeat);
        Assert.Contains("TotalPublishedStateCount=3", heartbeat);
        Assert.Contains("SetStateFailures=0", heartbeat);
        Assert.Contains("MaxSetStateDurationMs=", heartbeat);
        Assert.Contains("HeartbeatElapsedMs=", heartbeat);
        Assert.Contains("EffectiveSetStateHz=", heartbeat);
        // Timing-decomposition parity fields (Minor 1): present with zero defaults on the manual-tick
        // path, which never drives WorkerLoop.
        Assert.Contains("TimerWakeCount=0", heartbeat);
        Assert.Contains("AverageWakeToWakeMs=0", heartbeat);
        Assert.Contains("MaxWakeToWakeMs=0", heartbeat);
        Assert.Contains("AverageWaitBlockedMs=0", heartbeat);
        Assert.Contains("MaxWaitBlockedMs=0", heartbeat);
        Assert.Contains("AveragePublishWorkMs=0", heartbeat);
        Assert.Contains("MaxPublishWorkMs=0", heartbeat);
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
        var publisher = new CanonicalSteamDeckInputPublisher(source, sink, ticks, timestampProvider: () => fakeNow);
        publisher.Start();

        var msToTicks = Stopwatch.Frequency / 1000.0;
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
    public async Task TimingDiagnostics_WaitBlockedAccumulatesAverageAndMax()
    {
        var source = new Snapshot(new ControllerState(new AuxiliaryButtonState([false, false])));
        var sink = new FakeSink(); var ticks = new ManualTicks();
        var fakeNow = 0L;
        var publisher = new CanonicalSteamDeckInputPublisher(source, sink, ticks, timestampProvider: () => fakeNow);
        publisher.Start();

        var msToTicks = Stopwatch.Frequency / 1000.0;
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
        var publisher = new CanonicalSteamDeckInputPublisher(source, sink, ticks, timestampProvider: () => fakeNow);
        publisher.Start();

        var msToTicks = Stopwatch.Frequency / 1000.0;
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
    public async Task TimingDiagnostics_WakeLatenessAccumulatesAverageAndMaxAndClampsNegativeToZero()
    {
        var source = new Snapshot(new ControllerState(new AuxiliaryButtonState([false, false])));
        var sink = new FakeSink(); var ticks = new ManualTicks();
        var fakeNow = 0L;
        var publisher = new CanonicalSteamDeckInputPublisher(source, sink, ticks, timestampProvider: () => fakeNow);
        publisher.Start();

        var msToTicks = Stopwatch.Frequency / 1000.0;
        publisher.RecordTimerWakeForTests((long)(4.3 * msToTicks));
        publisher.RecordWakeLatenessForTests((long)(4.3 * msToTicks), (long)(4.0 * msToTicks));
        publisher.RecordTimerWakeForTests((long)(8.29 * msToTicks));
        publisher.RecordWakeLatenessForTests((long)(8.29 * msToTicks), (long)(8.0 * msToTicks));
        // "Early" wake -- must clamp to zero, not go negative.
        publisher.RecordWakeLatenessForTests((long)(11.9 * msToTicks), (long)(12.0 * msToTicks));

        fakeNow = Stopwatch.Frequency + 1;
        await ticks.TickAsync(); await sink.WaitForCountAsync(1);
        await publisher.StopAsync();

        AppLog.DrainForTests();
        var heartbeat = Assert.Single(LogFileTestHelper.ReadAllText(AppLog.CurrentLogFilePath).Split('\n'), line => line.Contains("publisher heartbeat"));
        Assert.Contains("TimerWakeCount=2", heartbeat);
        AssertFieldInRange(heartbeat, "AverageWakeLatenessMs", 0.28, 0.31);
        AssertFieldInRange(heartbeat, "MaxWakeLatenessMs", 0.29, 0.31);
    }

    private static void AssertFieldInRange(string heartbeat, string field, double low, double high)
    {
        var text = heartbeat.Split(field + "=")[1].Split(' ')[0].TrimEnd('\r');
        var value = double.Parse(text, System.Globalization.CultureInfo.InvariantCulture);
        Assert.InRange(value, low, high);
    }

    [Fact]
    public async Task Production_worker_recurs()
    {
        var source = new Snapshot(new ControllerState(new AuxiliaryButtonState([false, false])));
        var sink = new FakeSink();
        var publisher = new CanonicalSteamDeckInputPublisher(source, sink);

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
    public async Task Stop_prevents_new_SetState()
    {
        var source = new Snapshot(new ControllerState(new AuxiliaryButtonState([false, false])));
        var sink = new FakeSink();
        var publisher = new CanonicalSteamDeckInputPublisher(source, sink);
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
    public async Task Initial_arm_failure_is_synchronous_and_restartable()
    {
        var source = new Snapshot(new ControllerState(new AuxiliaryButtonState([false, false])));
        var sink = new FakeSink();
        var publisher = new CanonicalSteamDeckInputPublisher(source, sink)
        {
            ArmForDeadlineOverrideForTests = (_, _, _) => throw new InvalidOperationException("simulated initial SetWaitableTimerEx failure"),
        };

        var exception = Assert.Throws<InvalidOperationException>(publisher.Start);

        Assert.Contains("timer", exception.Message);
        Assert.False(publisher.IsRunning);

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

    [Fact]
    public async Task Runtime_rearm_failure_reports_fault()
    {
        var source = new Snapshot(new ControllerState(new AuxiliaryButtonState([false, false])));
        var sink = new FakeSink();
        var faults = 0;
        var faultObserved = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var publisher = new CanonicalSteamDeckInputPublisher(source, sink, fault: _ => { Interlocked.Increment(ref faults); faultObserved.TrySetResult(true); });
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
        Assert.Equal(1, sink.Count);
    }

    [Fact]
    public async Task Thread_start_failure_cleans_up()
    {
        var source = new Snapshot(new ControllerState(new AuxiliaryButtonState([false, false])));
        var sink = new FakeSink();
        var publisher = new CanonicalSteamDeckInputPublisher(source, sink)
        {
            WorkerThreadStartOverrideForTests = _ => throw new InvalidOperationException("simulated Thread.Start() failure"),
        };

        var exception = Assert.Throws<InvalidOperationException>(publisher.Start);

        Assert.Contains("worker thread", exception.Message);
        Assert.False(publisher.IsRunning);

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
    public async Task Join_timeout_fails_closed()
    {
        var source = new Snapshot(new ControllerState(new AuxiliaryButtonState([false, false])));
        var firstCallBlocked = new ManualResetEventSlim(false);
        var releaseFirstCall = new ManualResetEventSlim(false);
        var sink = new BlockingFirstCallSink(firstCallBlocked, releaseFirstCall);
        var publisher = new CanonicalSteamDeckInputPublisher(source, sink)
        {
            WorkerJoinTimeoutForTests = TimeSpan.FromMilliseconds(50),
        };

        publisher.Start();
        try
        {
            Assert.True(firstCallBlocked.Wait(TimeSpan.FromSeconds(2)), "The first SetState call never started.");

            await Assert.ThrowsAsync<TimeoutException>(publisher.StopAsync);
            Assert.True(publisher.IsRunning);
        }
        finally
        {
            releaseFirstCall.Set();
            await publisher.StopAsync();
        }

        Assert.False(publisher.IsRunning);
    }

    [Fact]
    public async Task No_catchup_burst_after_long_SetState()
    {
        var source = new Snapshot(new ControllerState(new AuxiliaryButtonState([false, false])));
        var firstCallBlocked = new ManualResetEventSlim(false);
        var releaseFirstCall = new ManualResetEventSlim(false);
        var sink = new BlockingFirstCallSink(firstCallBlocked, releaseFirstCall);
        var publisher = new CanonicalSteamDeckInputPublisher(source, sink);

        publisher.Start();
        try
        {
            Assert.True(firstCallBlocked.Wait(TimeSpan.FromSeconds(2)), "The first SetState call never started.");
            await Task.Delay(100);
            releaseFirstCall.Set();
            await Task.Delay(100);
        }
        finally
        {
            await publisher.StopAsync();
        }

        Assert.True(sink.Count < 60, $"Expected no catch-up burst, but observed {sink.Count} SetState calls.");
    }

    [Fact]
    public async Task Worker_is_background_named_AboveNormal()
    {
        var source = new Snapshot(new ControllerState(new AuxiliaryButtonState([false, false])));
        var sink = new FakeSink();
        ThreadPriority? observedPriority = null;
        var publisher = new CanonicalSteamDeckInputPublisher(source, sink)
        {
            WorkerThreadStartOverrideForTests = thread =>
            {
                observedPriority = thread.Priority;
                Assert.Equal("SteamInputAddon.SteamDeckPublisher", thread.Name);
                Assert.True(thread.IsBackground);
                thread.Start();
            },
        };

        publisher.Start();
        try
        {
            await sink.WaitForCountAsync(1, TimeSpan.FromSeconds(2));
        }
        finally
        {
            await publisher.StopAsync();
        }

        Assert.Equal(ThreadPriority.AboveNormal, observedPriority);
    }

    private sealed class BlockingFirstCallSink(ManualResetEventSlim firstCallBlocked, ManualResetEventSlim releaseFirstCall) : ICanonicalSteamDeckStateSink
    {
        private int _count;
        private int _isFirstCall = 1;
        internal int Count => Volatile.Read(ref _count);
        public bool SetState(SteamDeckDeviceState state)
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

    private sealed class FakeSink : ICanonicalSteamDeckStateSink
    {
        private readonly Lock _sync = new();
        private readonly List<SteamDeckDeviceState> _states = [];
        internal volatile bool Accept = true;
        internal volatile bool ThrowOnSet;
        internal IReadOnlyList<SteamDeckDeviceState> States { get { lock (_sync) return _states.ToArray(); } }
        internal int Count { get { lock (_sync) return _states.Count; } }
        public bool SetState(SteamDeckDeviceState state)
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
