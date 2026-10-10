using System.Diagnostics;
using System.Collections.Concurrent;
using SteamInputAddonforClaw.Contracts.BackButtons;
using SteamInputAddonforClaw.Diagnostics;
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
[Collection("AppLog")]
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
    public async Task Each_tick_uses_the_current_back_button_mapping_without_restarting_the_publisher()
    {
        var source = new Snapshot(new ControllerState(new AuxiliaryButtonState([false, true])));
        var sink = new FakeSink(); var ticks = new ManualTicks();
        var mapping = new BackButtonMappingSettings(Xbox360BackButtonTarget.A, Xbox360BackButtonTarget.Disabled);
        var publisher = new CanonicalXbox360InputPublisher(
            source,
            sink.SetState,
            ticks,
            backButtonMappingProvider: () => mapping);

        publisher.Start();
        await ticks.TickAsync(); await sink.WaitForCountAsync(1);
        mapping = mapping with { M1 = Xbox360BackButtonTarget.B };
        await ticks.TickAsync(); await sink.WaitForCountAsync(2);
        await publisher.StopAsync();

        Assert.Equal(Xbox360ButtonBits.A, sink.States[0].Buttons);
        Assert.Equal(Xbox360ButtonBits.B, sink.States[1].Buttons);
    }

    [Fact]
    public async Task Cached_override_and_latest_global_mapping_are_selected_on_each_tick()
    {
        var source = new Snapshot(new ControllerState(new AuxiliaryButtonState([false, true])));
        var sink = new FakeSink(); var ticks = new ManualTicks();
        var global = new BackButtonMappingSettings(Xbox360BackButtonTarget.A, Xbox360BackButtonTarget.Disabled);
        BackButtonMappingSettings? cachedOverride = null;
        var publisher = new CanonicalXbox360InputPublisher(
            source,
            sink.SetState,
            ticks,
            backButtonMappingProvider: () => cachedOverride ?? global);

        publisher.Start();
        await ticks.TickAsync(); await sink.WaitForCountAsync(1);
        cachedOverride = new(Xbox360BackButtonTarget.B, Xbox360BackButtonTarget.Disabled);
        await ticks.TickAsync(); await sink.WaitForCountAsync(2);
        global = new(Xbox360BackButtonTarget.X, Xbox360BackButtonTarget.Disabled);
        await ticks.TickAsync(); await sink.WaitForCountAsync(3);
        cachedOverride = null;
        await ticks.TickAsync(); await sink.WaitForCountAsync(4);
        await publisher.StopAsync();

        Assert.Equal(Xbox360ButtonBits.A, sink.States[0].Buttons);
        Assert.Equal(Xbox360ButtonBits.B, sink.States[1].Buttons);
        Assert.Equal(Xbox360ButtonBits.B, sink.States[2].Buttons);
        Assert.Equal(Xbox360ButtonBits.X, sink.States[3].Buttons);
    }

    [Fact]
    public async Task Each_tick_uses_current_raw_rear_state_and_current_mapping_together()
    {
        var source = new Snapshot(new ControllerState(new AuxiliaryButtonState([false, true])));
        var sink = new FakeSink(); var ticks = new ManualTicks();
        var mapping = new BackButtonMappingSettings(Xbox360BackButtonTarget.A, Xbox360BackButtonTarget.Disabled);
        var publisher = new CanonicalXbox360InputPublisher(
            source,
            sink.SetState,
            ticks,
            backButtonMappingProvider: () => mapping);

        publisher.Start();
        await ticks.TickAsync(); await sink.WaitForCountAsync(1);
        source.Value = new ControllerState(new AuxiliaryButtonState([false, false]));
        mapping = mapping with { M1 = Xbox360BackButtonTarget.B };
        await ticks.TickAsync(); await sink.WaitForCountAsync(2);
        await publisher.StopAsync();

        Assert.Equal(Xbox360ButtonBits.A, sink.States[0].Buttons);
        Assert.Equal(0u, sink.States[1].Buttons);
    }

    [Fact]
    public async Task Each_tick_uses_the_current_rear_suppression_decision_without_restarting()
    {
        var source = new Snapshot(new ControllerState(new AuxiliaryButtonState([false, true])));
        var sink = new FakeSink(); var ticks = new ManualTicks();
        var suppressM1 = true;
        var publisher = new CanonicalXbox360InputPublisher(
            source,
            sink.SetState,
            ticks,
            backButtonMappingProvider: () => new BackButtonMappingSettings(Xbox360BackButtonTarget.A, Xbox360BackButtonTarget.Disabled),
            rearButtonSuppressionProvider: (_, slot) => suppressM1 && slot == AuxiliaryButtonSlot.RightRear);

        publisher.Start();
        await ticks.TickAsync(); await sink.WaitForCountAsync(1);
        suppressM1 = false;
        await ticks.TickAsync(); await sink.WaitForCountAsync(2);
        await publisher.StopAsync();

        Assert.Equal(0u, sink.States[0].Buttons);
        Assert.Equal(Xbox360ButtonBits.A, sink.States[1].Buttons);
    }

    [Fact]
    public async Task Default_mapping_provider_keeps_rear_buttons_disabled()
    {
        var source = new Snapshot(new ControllerState(new AuxiliaryButtonState([false, true])));
        var sink = new FakeSink(); var ticks = new ManualTicks();
        var publisher = new CanonicalXbox360InputPublisher(source, sink.SetState, ticks);

        publisher.Start();
        await ticks.TickAsync(); await sink.WaitForCountAsync(1);
        await publisher.StopAsync();

        Assert.Equal(0u, sink.States[0].Buttons);
        Assert.Equal((byte)0, sink.States[0].LT);
        Assert.Equal((byte)0, sink.States[0].RT);
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
    public async Task Production_worker_is_background_AboveNormal_and_starts_from_a_4ms_absolute_deadline()
    {
        var origin = 123_456L;
        long observedDeadline = 0;
        long observedNow = 0;
        ThreadPriority? observedPriority = null;
        var qosThreadId = 0;
        PublisherThreadQoS.NativeCallOverrideForTests = _ => { qosThreadId = Environment.CurrentManagedThreadId; return (true, 0); };

        var publisher = new CanonicalXbox360InputPublisher(
            new Snapshot(new ControllerState(new AuxiliaryButtonState([false, false]))),
            _ => true,
            timestampProvider: () => origin)
        {
            ArmForDeadlineOverrideForTests = (timer, deadline, now) =>
            {
                observedDeadline = deadline;
                observedNow = now;
                // Keep the timer out of the test; StopAsync wakes the worker through the stop event.
                timer.ArmRelative(TimeSpan.FromSeconds(30));
            },
            WorkerThreadStartOverrideForTests = thread =>
            {
                observedPriority = thread.Priority;
                Assert.True(thread.IsBackground);
                Assert.Equal("SteamInputAddon.Xbox360Publisher", thread.Name);
                thread.Start();
            },
        };

        try
        {
            publisher.Start();
            await publisher.StopAsync();
        }
        finally
        {
            PublisherThreadQoS.NativeCallOverrideForTests = null;
        }

        var expectedPeriodTicks = CanonicalPublisherDeadlineMath.StopwatchTicksFromTimeSpan(
            TimeSpan.FromMilliseconds(4),
            Stopwatch.Frequency);

        Assert.Equal(origin, observedNow);
        Assert.Equal(expectedPeriodTicks, observedDeadline - observedNow);
        Assert.Equal(ThreadPriority.AboveNormal, observedPriority);
        Assert.NotEqual(0, qosThreadId);
    }

    [Fact]
    public async Task Production_worker_starts_idle_at_8ms_and_changes_live_period_without_restart_or_state_loss()
    {
        var origin = 123_456L;
        var now = origin;
        var requires250Hz = 0;
        var arms = new ConcurrentQueue<(WindowsHighResolutionOneShotTimer Timer, long Deadline, long Now)>();
        var buttons = new GamepadButtons(true, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false);
        var expectedState = Xbox360DeviceStateMapper.Map(new ControllerState(
            buttons, default, default, default, new AuxiliaryButtonState([false, false])));
        var sink = new FakeSink();
        using var armObserved = new ManualResetEventSlim(false);
        var publisher = new CanonicalXbox360InputPublisher(
            new Snapshot(new ControllerState(buttons, default, default, default, new AuxiliaryButtonState([false, false]))),
            sink.SetState,
            timestampProvider: () => Volatile.Read(ref now),
            requires250Hz: () => Volatile.Read(ref requires250Hz) != 0)
        {
            ArmForDeadlineOverrideForTests = (activeTimer, deadline, at) =>
            {
                activeTimer.ArmRelative(TimeSpan.FromSeconds(30));
                arms.Enqueue((activeTimer, deadline, at));
                armObserved.Set();
            },
        };

        try
        {
            publisher.Start();
            Assert.True(armObserved.Wait(TimeSpan.FromSeconds(2)));
            Assert.True(arms.TryDequeue(out var initial));
            var idleTicks = CanonicalPublisherDeadlineMath.StopwatchTicksFromTimeSpan(TimeSpan.FromMilliseconds(8), Stopwatch.Frequency);
            Assert.Equal(idleTicks, initial.Deadline - initial.Now);

            armObserved.Reset();
            Volatile.Write(ref requires250Hz, 1);
            now = checked(origin + idleTicks);
            initial.Timer.ArmRelative(TimeSpan.FromTicks(1));
            Assert.True(armObserved.Wait(TimeSpan.FromSeconds(2)));
            await sink.WaitForCountAsync(1, TimeSpan.FromSeconds(2));
            Assert.Equal(expectedState, sink.States[0]);
            Assert.True(arms.TryDequeue(out var active));
            var activeTicks = CanonicalPublisherDeadlineMath.StopwatchTicksFromTimeSpan(TimeSpan.FromMilliseconds(4), Stopwatch.Frequency);
            Assert.Equal(activeTicks, active.Deadline - active.Now);
            Assert.Same(initial.Timer, active.Timer);
            Assert.True(publisher.IsRunning);

            armObserved.Reset();
            Volatile.Write(ref requires250Hz, 0);
            now = checked(now + activeTicks);
            active.Timer.ArmRelative(TimeSpan.FromTicks(1));
            Assert.True(armObserved.Wait(TimeSpan.FromSeconds(2)));
            await sink.WaitForCountAsync(2, TimeSpan.FromSeconds(2));
            Assert.True(arms.TryDequeue(out var idleAgain));
            Assert.Equal(idleTicks, idleAgain.Deadline - idleAgain.Now);
            Assert.Same(initial.Timer, idleAgain.Timer);
            Assert.True(publisher.IsRunning);
            Assert.All(sink.States, state => Assert.Equal(expectedState, state));
        }
        finally
        {
            await publisher.StopAsync();
        }
    }

    [Fact]
    public async Task Cadence_rearm_failure_reports_fail_closed_without_a_false_applied_success_log()
    {
        var previousDirectory = AppLog.DirectoryOverride;
        var previousLevel = AppLog.MinimumLevelOverride;
        var directory = Path.Combine(Path.GetTempPath(), $"SteamInput.CadenceLog.{Guid.NewGuid():N}");
        AppLog.DirectoryOverride = directory;
        AppLog.MinimumLevelOverride = AppLogLevel.Debug;
        var origin = 123_456L;
        var now = origin;
        var requires250Hz = 0;
        var armCount = 0;
        WindowsHighResolutionOneShotTimer? timer = null;
        var fault = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var publisher = new CanonicalXbox360InputPublisher(
            new Snapshot(new ControllerState(new AuxiliaryButtonState([false, false]))),
            _ => true,
            fault: _ => fault.TrySetResult(),
            timestampProvider: () => Volatile.Read(ref now),
            requires250Hz: () => Volatile.Read(ref requires250Hz) != 0)
        {
            ArmForDeadlineOverrideForTests = (activeTimer, _, _) =>
            {
                if (Interlocked.Increment(ref armCount) == 2)
                    throw new InvalidOperationException("simulated cadence re-arm failure");
                timer = activeTimer;
                activeTimer.ArmRelative(TimeSpan.FromSeconds(30));
            },
        };

        try
        {
            publisher.Start();
            Volatile.Write(ref requires250Hz, 1);
            now = checked(origin + CanonicalPublisherDeadlineMath.StopwatchTicksFromTimeSpan(TimeSpan.FromMilliseconds(8), Stopwatch.Frequency));
            timer!.ArmRelative(TimeSpan.FromTicks(1));
            await fault.Task.WaitAsync(TimeSpan.FromSeconds(2));
            await publisher.StopAsync();

            AppLog.DrainForTests();
            var log = AppLog.ReadAllTextForTests(AppLog.CurrentLogFilePath);
            Assert.Contains("Event=CadenceApplyFailed", log, StringComparison.Ordinal);
            Assert.DoesNotContain("AppliedHz=250", log, StringComparison.Ordinal);
        }
        finally
        {
            await publisher.StopAsync();
            AppLog.DrainForTests();
            AppLog.DirectoryOverride = previousDirectory;
            AppLog.MinimumLevelOverride = previousLevel;
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
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
