using System.Diagnostics;
using SteamInputAddonforClaw.Input;
using SteamInputAddonforClaw.VirtualOutput.Viiper;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

/// <summary>
/// Manual-tick and production-worker coverage for <see cref="CanonicalXbox360InputPublisher"/>: the
/// mapper-to-sink publish path, fault/stop semantics, and lifecycle (start/stop/restart) safety. This
/// mirrors the seams and style already proven by <c>CanonicalSteamDeckInputPublisherTests</c>, scoped
/// down to what the Xbox360 publisher foundation actually needs (no timing-decomposition heartbeat
/// diagnostics -- this publisher intentionally does not carry that surface).
/// </summary>
public sealed class CanonicalXbox360InputPublisherTests
{
    [Fact]
    public async Task Manual_tick_maps_state_through_the_mapper_and_reaches_the_sink()
    {
        var buttons = new GamepadButtons(true, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false);
        var state = new ControllerState(buttons, default, default, default, new AuxiliaryButtonState([false, false]));
        var source = new Snapshot(state);
        var sink = new FakeSink(); var ticks = new ManualTicks();
        var publisher = new CanonicalXbox360InputPublisher(source, sink.SetState, ticks);

        publisher.Start();
        await ticks.TickAsync(); await sink.WaitForCountAsync(1);
        await publisher.StopAsync();

        var expected = Xbox360DeviceStateMapper.Map(state);
        Assert.Equal(expected, sink.States[0]);
        Assert.Equal(Xbox360ButtonBits.A, sink.States[0].Buttons);
    }

    [Fact]
    public async Task Each_tick_uses_the_current_LatestState_not_a_state_captured_at_Start()
    {
        var buttonsA = new GamepadButtons(true, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false);
        var buttonsX = new GamepadButtons(false, false, true, false, false, false, false, false, false, false, false, false, false, false, false, false);
        var source = new Snapshot(new ControllerState(buttonsA, default, default, default, new AuxiliaryButtonState([false, false])));
        var sink = new FakeSink(); var ticks = new ManualTicks();
        var publisher = new CanonicalXbox360InputPublisher(source, sink.SetState, ticks);

        publisher.Start();
        await ticks.TickAsync(); await sink.WaitForCountAsync(1);

        source.Value = new ControllerState(buttonsX, default, default, default, new AuxiliaryButtonState([false, false]));
        await ticks.TickAsync(); await sink.WaitForCountAsync(2);
        await publisher.StopAsync();

        Assert.Equal(Xbox360ButtonBits.A, sink.States[0].Buttons);
        Assert.Equal(Xbox360ButtonBits.X, sink.States[1].Buttons);
    }

    [Fact]
    public async Task One_write_per_tick()
    {
        var source = new Snapshot(new ControllerState(new AuxiliaryButtonState([false, false])));
        var sink = new FakeSink(); var ticks = new ManualTicks();
        var publisher = new CanonicalXbox360InputPublisher(source, sink.SetState, ticks);

        publisher.Start();
        await ticks.TickAsync(); await sink.WaitForCountAsync(1);
        await ticks.TickAsync(); await sink.WaitForCountAsync(2);
        await ticks.TickAsync(); await sink.WaitForCountAsync(3);
        await publisher.StopAsync();

        Assert.Equal(3, sink.Count);
        Assert.Equal(3, publisher.PublishedStateCount);
    }

    [Fact]
    public async Task False_sink_reports_fault_once_and_stops_publishing()
    {
        var sink = new FakeSink { Accept = false }; var ticks = new ManualTicks(); var faults = 0;
        var faultObserved = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var publisher = new CanonicalXbox360InputPublisher(
            new Snapshot(new ControllerState(new AuxiliaryButtonState([false, false]))),
            sink.SetState, ticks, _ => { faults++; faultObserved.TrySetResult(true); });

        publisher.Start();
        await ticks.TickAsync();
        await faultObserved.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await publisher.StopAsync();

        Assert.Equal(1, faults);
        Assert.Equal(1, sink.Count);
        Assert.False(publisher.IsRunning);
    }

    [Fact]
    public async Task Throwing_sink_reports_fault_once_with_no_uncontrolled_loop()
    {
        var sink = new FakeSink { ThrowOnSet = true }; var ticks = new ManualTicks(); var faults = 0;
        var faultObserved = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var publisher = new CanonicalXbox360InputPublisher(
            new Snapshot(new ControllerState(new AuxiliaryButtonState([false, false]))),
            sink.SetState, ticks, _ => { faults++; faultObserved.TrySetResult(true); });

        publisher.Start();
        await ticks.TickAsync();
        await faultObserved.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await publisher.StopAsync();

        Assert.Equal(1, faults);
        Assert.False(publisher.IsRunning);
    }

    [Fact]
    public async Task Stop_prevents_further_publishing()
    {
        var source = new Snapshot(new ControllerState(new AuxiliaryButtonState([false, false])));
        var sink = new FakeSink(); var ticks = new ManualTicks();
        var publisher = new CanonicalXbox360InputPublisher(source, sink.SetState, ticks);

        publisher.Start();
        await ticks.TickAsync(); await sink.WaitForCountAsync(1);
        await publisher.StopAsync();

        var countAtStop = sink.Count;
        // TickAsync() would wait forever here: the only queued waiter is the stopped run's
        // cancelled one, which it skips, and no new waiter can ever be enqueued once stopped.
        // TryTick() is the non-blocking equivalent -- it must report there was nothing to tick.
        Assert.False(ticks.TryTick());

        Assert.Equal(countAtStop, sink.Count);
        Assert.False(publisher.IsRunning);
    }

    [Fact]
    public async Task Restart_after_a_clean_stop_publishes_again()
    {
        var source = new Snapshot(new ControllerState(new AuxiliaryButtonState([false, false])));
        var sink = new FakeSink(); var ticks = new ManualTicks();
        var publisher = new CanonicalXbox360InputPublisher(source, sink.SetState, ticks);

        publisher.Start();
        await ticks.TickAsync(); await sink.WaitForCountAsync(1);
        await publisher.StopAsync();
        Assert.False(publisher.IsRunning);

        publisher.Start();
        // ManualTicks.TickAsync() already skips a stale cancelled waiter left over from the first
        // run (cancelled by StopAsync, never dequeued) before resolving one, so a single tick here
        // reaches the restarted loop's new wait -- no throwaway tick needed.
        await ticks.TickAsync(); await sink.WaitForCountAsync(2);
        await publisher.StopAsync();

        Assert.Equal(2, sink.Count);
        Assert.False(publisher.IsRunning);
    }

    [Fact]
    public async Task Faulted_manual_tick_run_cannot_restart_until_StopAsync_cleans_previous_resources()
    {
        var sink = new FakeSink { Accept = false };
        var ticks = new ManualTicks();
        var faulted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var publisher = new CanonicalXbox360InputPublisher(
            new Snapshot(new ControllerState(new AuxiliaryButtonState([false, false]))),
            sink.SetState, ticks, _ => faulted.TrySetResult(true));

        publisher.Start();
        await ticks.TickAsync();
        await faulted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(publisher.IsRunning);

        // The sink already self-terminated the run (IsRunning is false), but StopAsync() has not run
        // yet, so the previous run's CTS/task are still owned -- Start() must still refuse.
        Assert.Throws<InvalidOperationException>(publisher.Start);

        await publisher.StopAsync();
        sink.Accept = true;
        publisher.Start();
        await ticks.TickAsync(); await sink.WaitForCountAsync(2);
        await publisher.StopAsync();
    }

    [Fact]
    public async Task Faulted_production_worker_run_cannot_restart_until_StopAsync_cleans_previous_resources()
    {
        var source = new Snapshot(new ControllerState(new AuxiliaryButtonState([false, false])));
        var sink = new FakeSink { Accept = false };
        var faulted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var publisher = new CanonicalXbox360InputPublisher(source, sink.SetState, fault: _ => faulted.TrySetResult(true));

        publisher.Start();
        await faulted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        // The worker thread can take a moment to actually return after the fault is reported.
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(2);
        while (publisher.IsRunning && DateTime.UtcNow < deadline) await Task.Delay(5);
        Assert.False(publisher.IsRunning);

        // The worker already self-terminated (IsRunning is false), but StopAsync() has not run yet, so
        // the previous run's timer/stop-event/thread are still owned -- Start() must still refuse.
        Assert.Throws<InvalidOperationException>(publisher.Start);

        await publisher.StopAsync();
        sink.Accept = true;
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
    public async Task Duplicate_Start_rejected()
    {
        var source = new Snapshot(new ControllerState(new AuxiliaryButtonState([false, false])));
        var sink = new FakeSink(); var ticks = new ManualTicks();
        var publisher = new CanonicalXbox360InputPublisher(source, sink.SetState, ticks);

        publisher.Start();
        Assert.Throws<InvalidOperationException>(publisher.Start);
        await ticks.TickAsync(); await sink.WaitForCountAsync(1);
        await publisher.StopAsync();

        Assert.Equal(1, sink.Count);
    }

    [Fact]
    public async Task Production_worker_is_background_named_AboveNormal_with_absolute_4ms_deadline()
    {
        var source = new Snapshot(new ControllerState(new AuxiliaryButtonState([false, false])));
        var sink = new FakeSink();
        ThreadPriority? observedPriority = null;
        var publisher = new CanonicalXbox360InputPublisher(source, sink.SetState)
        {
            WorkerThreadStartOverrideForTests = thread =>
            {
                observedPriority = thread.Priority;
                Assert.Equal("SteamInputAddon.Xbox360Publisher", thread.Name);
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

    [Fact]
    public async Task Production_worker_recurs_at_the_scheduled_cadence()
    {
        var source = new Snapshot(new ControllerState(new AuxiliaryButtonState([false, false])));
        var sink = new FakeSink();
        var publisher = new CanonicalXbox360InputPublisher(source, sink.SetState);

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
    public async Task Initial_arm_failure_is_synchronous_and_restartable()
    {
        var source = new Snapshot(new ControllerState(new AuxiliaryButtonState([false, false])));
        var sink = new FakeSink();
        var publisher = new CanonicalXbox360InputPublisher(source, sink.SetState)
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
    public async Task Thread_start_failure_cleans_up_and_allows_a_later_start()
    {
        var source = new Snapshot(new ControllerState(new AuxiliaryButtonState([false, false])));
        var sink = new FakeSink();
        var publisher = new CanonicalXbox360InputPublisher(source, sink.SetState)
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
    public async Task Join_timeout_fails_closed_and_does_not_report_success_while_worker_may_still_be_alive()
    {
        var source = new Snapshot(new ControllerState(new AuxiliaryButtonState([false, false])));
        var firstCallBlocked = new ManualResetEventSlim(false);
        var releaseFirstCall = new ManualResetEventSlim(false);
        var sink = new BlockingFirstCallSink(firstCallBlocked, releaseFirstCall);
        var publisher = new CanonicalXbox360InputPublisher(source, sink.SetState)
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

    private sealed class BlockingFirstCallSink(ManualResetEventSlim firstCallBlocked, ManualResetEventSlim releaseFirstCall)
    {
        private int _count;
        private int _isFirstCall = 1;
        internal int Count => Volatile.Read(ref _count);
        public bool SetState(Xbox360DeviceState state)
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
            while (true)
            {
                while (_waiters.Count == 0) await Task.Yield();
                // A prior run's final WaitForTickAsync call (queued just before StopAsync's
                // cancellation resolved it) can still be sitting at the front of this shared queue
                // on restart -- skip already-completed/canceled waiters rather than resolving them
                // a second time, so a fresh tick reaches the current run's actual waiter.
                var waiter = _waiters.Dequeue();
                if (waiter.Task.IsCompleted) continue;
                waiter.TrySetResult(true);
                return;
            }
        }

        /// <summary>Non-blocking equivalent of <see cref="TickAsync"/>: resolves the next
        /// not-yet-completed waiter if one is queued and returns true, or returns false immediately
        /// if there is nothing to tick (e.g. after the publisher has stopped and no new waiter can
        /// ever be enqueued) -- unlike <see cref="TickAsync"/>, this never waits.</summary>
        public bool TryTick()
        {
            while (_waiters.Count > 0)
            {
                var waiter = _waiters.Dequeue();
                if (waiter.Task.IsCompleted) continue;
                waiter.TrySetResult(true);
                return true;
            }

            return false;
        }
    }

    private sealed class FakeSink
    {
        private readonly Lock _sync = new();
        private readonly List<Xbox360DeviceState> _states = [];
        internal volatile bool Accept = true;
        internal volatile bool ThrowOnSet;
        internal IReadOnlyList<Xbox360DeviceState> States { get { lock (_sync) return _states.ToArray(); } }
        internal int Count { get { lock (_sync) return _states.Count; } }
        public bool SetState(Xbox360DeviceState state)
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
