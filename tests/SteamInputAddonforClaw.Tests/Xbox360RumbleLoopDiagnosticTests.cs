using System.Collections.Concurrent;
using SteamInputAddonforClaw.Contracts.Frontend;
using SteamInputAddonforClaw.Diagnostics;
using SteamInputAddonforClaw.Feedback;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

[Collection("AppLog")]
public sealed class Xbox360RumbleLoopDiagnosticTests
{
    [Fact]
    public void Captured_patterns_rotate_deterministically()
    {
        Assert.Equal(4, Xbox360RumbleLoopDiagnostic.PatternCount);
        Assert.Equal(
            ["LiesOfP-A", "LiesOfP-B", "LiesOfP-C", "LiesOfP-D", "LiesOfP-A"],
            Enumerable.Range(1, 5).Select(cycle => Xbox360RumbleLoopDiagnostic.GetPatternForCycle(cycle).Name));
        Assert.Throws<ArgumentOutOfRangeException>(() => Xbox360RumbleLoopDiagnostic.GetPatternForCycle(0));
    }

    [Fact]
    public void Captured_patterns_have_exact_independent_channels_and_one_terminal_stop()
    {
        RumbleReplayStep[][] expectedPatterns =
        [
            [new(255, 255), new(241, 241), new(217, 217), new(252, 252), new(230, 230), new(208, 208), new(0, 0)],
            [new(255, 255), new(242, 242), new(218, 218), new(204, 204), new(0, 0)],
            [new(255, 255), new(242, 242), new(217, 217), new(0, 0)],
            [new(255, 255), new(239, 239), new(219, 219), new(255, 255), new(247, 247), new(228, 228), new(209, 209), new(0, 0)]
        ];

        for (var index = 0; index < expectedPatterns.Length; index++)
        {
            var pattern = Xbox360RumbleLoopDiagnostic.GetPatternForCycle(index + 1);
            Assert.Equal(expectedPatterns[index], pattern.Steps);
            Assert.Equal(new RumbleReplayStep(0, 0), pattern.Steps[^1]);
            Assert.Single(pattern.Steps, static step => step.Left8 == 0 && step.Right8 == 0);
            Assert.All(pattern.Steps.Take(pattern.Steps.Count - 1), static step =>
            {
                Assert.NotEqual((byte)0, step.Left8);
                Assert.NotEqual((byte)0, step.Right8);
            });
        }
    }

    [Fact]
    public void Captured_patterns_preserve_later_amplitude_increases()
    {
        var patternA = Xbox360RumbleLoopDiagnostic.GetPatternForCycle(1);
        var patternD = Xbox360RumbleLoopDiagnostic.GetPatternForCycle(4);

        Assert.True(patternA.Steps[3].Left8 > patternA.Steps[2].Left8);
        Assert.True(patternD.Steps[3].Left8 > patternD.Steps[2].Left8);
    }

    [Fact]
    public void Production_timing_uses_separate_burst_and_cycle_intervals()
    {
        Assert.Equal(TimeSpan.FromMilliseconds(65), Xbox360RumbleLoopDiagnostic.ProductionBurstStepCadence);
        Assert.Equal(TimeSpan.FromSeconds(2), Xbox360RumbleLoopDiagnostic.ProductionCycleIdle);
        Assert.Equal(TimeSpan.FromSeconds(1), Xbox360RumbleLoopDiagnostic.ProductionTerminalCallbackTimeout);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 257)]
    [InlineData(127, 32639)]
    [InlineData(255, 65535)]
    public void Existing_production_amplitude_expansion_is_reused(byte value, int expected)
        => Assert.Equal((ushort)expected, Xbox360RumbleFeedbackBridge.Expand(value));

    [Fact]
    public async Task Synchronous_terminal_callback_is_armed_before_xinput_and_nonzero_steps_do_not_wait_for_callbacks()
    {
        using var stop = new CancellationTokenSource();
        var xinput = new FakeXbox360RumbleLoopXInput();
        var diagnostic = CreateDiagnostic(xinput, TimeSpan.FromMilliseconds(1), TimeSpan.FromMilliseconds(100), () =>
            new(PhysicalRumbleWriteStatus.Succeeded, "OK"));
        var terminalCount = 0;
        var terminalCallbackSent = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondCycleStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        xinput.OnSetState = (_, left, right) =>
        {
            if (left == 0 && right == 0)
            {
                Interlocked.Increment(ref terminalCount);
                diagnostic.ObserveCallback(0, 0, terminalCount);
                terminalCallbackSent.TrySetResult();
            }
            else if (Volatile.Read(ref terminalCount) > 0)
            {
                secondCycleStarted.TrySetResult();
                stop.Cancel();
            }
            return Xbox360RumbleLoopDiagnostic.ErrorSuccess;
        };

        var run = diagnostic.RunAsync(stop.Token);
        try
        {
            await terminalCallbackSent.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await secondCycleStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Equal(2, diagnostic.Snapshot.Cycle);
            Assert.Equal(FrontendXbox360RumbleLoopState.Running, diagnostic.Snapshot.State);
            Assert.Equal((byte)0, diagnostic.Snapshot.LastObservedLeft8);
            Assert.Equal((byte)0, diagnostic.Snapshot.LastObservedRight8);
            var firstPattern = Xbox360RumbleLoopDiagnostic.GetPatternForCycle(1);
            var expectedWrites = firstPattern.Steps.Select(step => (
                Slot: 0u,
                Left: Xbox360RumbleFeedbackBridge.Expand(step.Left8),
                Right: Xbox360RumbleFeedbackBridge.Expand(step.Right8)));
            Assert.Equal(expectedWrites, xinput.SetStates.Take(firstPattern.Steps.Count));
        }
        finally
        {
            stop.Cancel();
            await run.WaitAsync(TimeSpan.FromSeconds(5));
        }
        Assert.Single(xinput.SetStates, static state => state.Left == 0 && state.Right == 0);
    }

    [Fact]
    public async Task Next_cycle_waits_for_terminal_callback_and_separate_cycle_idle()
    {
        using var stop = new CancellationTokenSource();
        var xinput = new FakeXbox360RumbleLoopXInput();
        var terminalSent = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var nextCycleStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Xbox360RumbleLoopDiagnostic? diagnostic = null;
        xinput.OnSetState = (_, left, right) =>
        {
            if (left == 0 && right == 0)
                terminalSent.TrySetResult();
            else if (diagnostic?.Snapshot.Cycle > 1)
            {
                nextCycleStarted.TrySetResult();
                stop.Cancel();
            }
            return Xbox360RumbleLoopDiagnostic.ErrorSuccess;
        };
        diagnostic = CreateDiagnostic(
            xinput,
            TimeSpan.Zero,
            TimeSpan.FromMilliseconds(100),
            () => new(PhysicalRumbleWriteStatus.Succeeded, "OK"),
            cycleIdle: TimeSpan.FromMilliseconds(500));

        var run = diagnostic.RunAsync(stop.Token);
        await terminalSent.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var firstPatternStepCount = Xbox360RumbleLoopDiagnostic.GetPatternForCycle(1).Steps.Count;
        Assert.Equal(firstPatternStepCount, xinput.SetStates.Count);
        Assert.Equal(1, diagnostic.Snapshot.Cycle);

        diagnostic.ObserveCallback(0, 0, 1);
        await Task.Delay(TimeSpan.FromMilliseconds(50));
        Assert.False(nextCycleStarted.Task.IsCompleted);
        Assert.Equal(firstPatternStepCount, xinput.SetStates.Count);

        await nextCycleStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await run.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(2, diagnostic.Snapshot.Cycle);
    }

    [Fact]
    public async Task Missing_terminal_callback_fails_once_without_a_second_host_stop()
    {
        var xinput = new FakeXbox360RumbleLoopXInput();
        var terminalSent = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var physicalStop = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var physicalStopCount = 0;
        var lifecycle = new ConcurrentQueue<string>();
        var traceCapture = new FakeXbox360UsbTraceCapture(lifecycle);
        var diagnostic = CreateDiagnostic(xinput, TimeSpan.FromMilliseconds(1), TimeSpan.FromMilliseconds(25), () =>
        {
            Interlocked.Increment(ref physicalStopCount);
            lifecycle.Enqueue("PhysicalStop");
            physicalStop.TrySetResult();
            return new(PhysicalRumbleWriteStatus.Succeeded, "OK");
        }, traceCapture);
        xinput.OnSetState = (_, left, right) =>
        {
            if (left == 0 && right == 0) terminalSent.TrySetResult();
            return Xbox360RumbleLoopDiagnostic.ErrorSuccess;
        };

        await diagnostic.RunAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));
        await Task.WhenAll(terminalSent.Task, physicalStop.Task).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(FrontendXbox360RumbleLoopState.Failed, diagnostic.Snapshot.State);
        Assert.Equal("TerminalStopMissing", diagnostic.Snapshot.FailureReason);
        Assert.Single(xinput.SetStates, static state => state.Left == 0 && state.Right == 0);
        Assert.Equal(1, physicalStopCount);
        Assert.Equal(diagnostic.Snapshot.StepCount, xinput.SetStates.Count);
        Assert.Equal(new[] { "TraceStart", "PhysicalStop", "TraceStop:TerminalStopMissing" }, lifecycle);
    }

    [Fact]
    public async Task Failed_terminal_xinput_call_is_not_misclassified_as_a_missing_callback()
    {
        var xinput = new FakeXbox360RumbleLoopXInput
        {
            OnSetState = (_, left, right) => left == 0 && right == 0 ? 5u : Xbox360RumbleLoopDiagnostic.ErrorSuccess
        };
        var physicalStops = 0;
        var traceCapture = new FakeXbox360UsbTraceCapture();
        var diagnostic = CreateDiagnostic(xinput, TimeSpan.FromMilliseconds(1), TimeSpan.FromMilliseconds(25), () =>
        {
            physicalStops++;
            return new(PhysicalRumbleWriteStatus.Succeeded, "OK");
        }, traceCapture);

        await diagnostic.RunAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(FrontendXbox360RumbleLoopState.Failed, diagnostic.Snapshot.State);
        Assert.Equal("XInputSetStateFailed", diagnostic.Snapshot.FailureReason);
        Assert.Single(xinput.SetStates, static state => state.Left == 0 && state.Right == 0);
        Assert.Equal(1, physicalStops);
        Assert.Equal(1, traceCapture.StopCount);
    }

    [Fact]
    public async Task Nonzero_xinput_failure_stops_immediately_and_requests_physical_cleanup()
    {
        var xinput = new FakeXbox360RumbleLoopXInput
        {
            OnSetState = (_, _, _) => 5
        };
        var physicalStops = 0;
        var diagnostic = CreateDiagnostic(xinput, TimeSpan.FromMilliseconds(1), TimeSpan.FromMilliseconds(25), () =>
        {
            physicalStops++;
            return new(PhysicalRumbleWriteStatus.Succeeded, "OK");
        });

        await diagnostic.RunAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(FrontendXbox360RumbleLoopState.Failed, diagnostic.Snapshot.State);
        Assert.Equal("XInputSetStateFailed", diagnostic.Snapshot.FailureReason);
        Assert.Single(xinput.SetStates);
        Assert.Equal(1, physicalStops);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Throwing_xinput_call_fails_and_requests_only_physical_cleanup(bool throwOnTerminalStop)
    {
        var xinput = new FakeXbox360RumbleLoopXInput
        {
            OnSetState = (_, left, right) =>
            {
                if (throwOnTerminalStop ? left == 0 && right == 0 : left != 0 || right != 0)
                    throw new IOException("XInput call failed");
                return Xbox360RumbleLoopDiagnostic.ErrorSuccess;
            }
        };
        var lifecycle = new ConcurrentQueue<string>();
        var traceCapture = new FakeXbox360UsbTraceCapture(lifecycle);
        var physicalStops = 0;
        var diagnostic = CreateDiagnostic(xinput, TimeSpan.Zero, TimeSpan.FromMilliseconds(25), () =>
        {
            physicalStops++;
            lifecycle.Enqueue("PhysicalStop");
            return new(PhysicalRumbleWriteStatus.Succeeded, "OK");
        }, traceCapture);

        await diagnostic.RunAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(FrontendXbox360RumbleLoopState.Failed, diagnostic.Snapshot.State);
        Assert.Equal("XInputSetStateException", diagnostic.Snapshot.FailureReason);
        Assert.Equal(throwOnTerminalStop ? Xbox360RumbleLoopDiagnostic.GetPatternForCycle(1).Steps.Count : 1, xinput.SetStates.Count);
        Assert.Equal(throwOnTerminalStop ? 1 : 0, xinput.SetStates.Count(static state => state.Left == 0 && state.Right == 0));
        Assert.Equal(1, physicalStops);
        Assert.Equal(new[] { "TraceStart", "PhysicalStop", "TraceStop:XInputSetStateException" }, lifecycle);
    }

    [Fact]
    public async Task Topology_change_at_cycle_boundary_fails_without_adopting_the_new_slot()
    {
        var xinput = new FakeXbox360RumbleLoopXInput();
        var diagnostic = CreateDiagnostic(xinput, TimeSpan.FromMilliseconds(1), TimeSpan.FromMilliseconds(100), () =>
            new(PhysicalRumbleWriteStatus.Succeeded, "OK"));
        xinput.OnSetState = (_, left, right) =>
        {
            if (left == 0 && right == 0)
            {
                xinput.ConnectedSlot = 1;
                diagnostic.ObserveCallback(0, 0, 1);
            }
            return Xbox360RumbleLoopDiagnostic.ErrorSuccess;
        };

        await diagnostic.RunAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(FrontendXbox360RumbleLoopState.Failed, diagnostic.Snapshot.State);
        Assert.Equal("XInputTopologyChanged", diagnostic.Snapshot.FailureReason);
        Assert.NotEmpty(xinput.GetStates);
        Assert.All(xinput.SetStates, state => Assert.Equal(0u, state.Slot));
        Assert.Equal(diagnostic.Snapshot.StepCount, xinput.SetStates.Count);
    }

    [Fact]
    public async Task Manual_stop_cancels_runner_and_uses_one_host_and_physical_cleanup()
    {
        using var cancellation = new CancellationTokenSource();
        var xinput = new FakeXbox360RumbleLoopXInput();
        var firstOutput = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        xinput.OnSetState = (_, left, _) =>
        {
            if (left != 0) firstOutput.TrySetResult();
            return Xbox360RumbleLoopDiagnostic.ErrorSuccess;
        };
        var physicalStops = 0;
        var lifecycle = new ConcurrentQueue<string>();
        var traceCapture = new FakeXbox360UsbTraceCapture(lifecycle);
        var diagnostic = CreateDiagnostic(xinput, TimeSpan.FromSeconds(1), TimeSpan.FromMilliseconds(100), () =>
        {
            physicalStops++;
            lifecycle.Enqueue("PhysicalStop");
            return new(PhysicalRumbleWriteStatus.Succeeded, "OK");
        }, traceCapture);
        var run = diagnostic.RunAsync(cancellation.Token);
        await firstOutput.Task.WaitAsync(TimeSpan.FromSeconds(5));

        cancellation.Cancel();
        await run.WaitAsync(TimeSpan.FromSeconds(5));
        await diagnostic.CompleteManualStopAsync();

        Assert.Equal(FrontendXbox360RumbleLoopState.Stopped, diagnostic.Snapshot.State);
        Assert.Single(xinput.SetStates, static state => state.Left == 0 && state.Right == 0);
        Assert.Equal(1, physicalStops);
        Assert.Equal(new[] { "TraceStart", "PhysicalStop", "TraceStop:ManualStop" }, lifecycle);
    }

    [Fact]
    public async Task Trace_starts_before_first_xinput_write_and_start_failure_does_not_block_the_loop()
    {
        var lifecycle = new ConcurrentQueue<string>();
        var traceCapture = new FakeXbox360UsbTraceCapture(lifecycle) { StartException = new IOException("trace unavailable") };
        var firstOutput = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cancellation = new CancellationTokenSource();
        var xinput = new FakeXbox360RumbleLoopXInput
        {
            OnSetState = (_, _, _) =>
            {
                lifecycle.Enqueue("XInputSetState");
                firstOutput.TrySetResult();
                cancellation.Cancel();
                return Xbox360RumbleLoopDiagnostic.ErrorSuccess;
            }
        };
        var diagnostic = CreateDiagnostic(xinput, TimeSpan.FromMilliseconds(1), TimeSpan.FromMilliseconds(100),
            () => new(PhysicalRumbleWriteStatus.Succeeded, "OK"), traceCapture);

        await diagnostic.RunAsync(cancellation.Token).WaitAsync(TimeSpan.FromSeconds(5));
        await firstOutput.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal("Running", diagnostic.Snapshot.Status);
        Assert.Equal(new[] { "TraceStart", "XInputSetState" }, lifecycle);
    }

    [Fact]
    public async Task Trace_stop_failure_does_not_overwrite_the_frozen_rumble_failure_result()
    {
        var xinput = new FakeXbox360RumbleLoopXInput();
        var traceCapture = new FakeXbox360UsbTraceCapture { StopException = new IOException("trace stop failed") };
        var diagnostic = CreateDiagnostic(xinput, TimeSpan.FromMilliseconds(1), TimeSpan.FromMilliseconds(10),
            () => new(PhysicalRumbleWriteStatus.Succeeded, "OK"), traceCapture);

        await diagnostic.RunAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(FrontendXbox360RumbleLoopState.Failed, diagnostic.Snapshot.State);
        Assert.Equal("TerminalStopMissing", diagnostic.Snapshot.FailureReason);
        Assert.Equal(1, traceCapture.StopCount);
    }

    private static Xbox360RumbleLoopDiagnostic CreateDiagnostic(
        FakeXbox360RumbleLoopXInput xinput,
        TimeSpan burstStepCadence,
        TimeSpan callbackTimeout,
        Func<PhysicalRumbleWriteResult> physicalStop,
        IXbox360UsbTraceCapture? traceCapture = null,
        TimeSpan? cycleIdle = null) =>
        new(0, xinput, physicalStop, burstStepCadence, callbackTimeout, traceCapture, cycleIdle ?? TimeSpan.FromMilliseconds(1));

    private sealed class FakeXbox360UsbTraceCapture(ConcurrentQueue<string>? lifecycle = null) : IXbox360UsbTraceCapture
    {
        internal Exception? StartException { get; init; }
        internal Exception? StopException { get; init; }
        internal int StopCount { get; private set; }

        public Task StartAsync(string runId, CancellationToken cancellationToken)
        {
            lifecycle?.Enqueue("TraceStart");
            if (StartException is not null) throw StartException;
            return Task.CompletedTask;
        }

        public Task StopAsync(string runId, string reason)
        {
            StopCount++;
            lifecycle?.Enqueue("TraceStop:" + reason);
            if (StopException is not null) throw StopException;
            return Task.CompletedTask;
        }
    }
}

internal sealed class FakeXbox360RumbleLoopXInput : IXbox360RumbleLoopXInput
{
    internal ConcurrentQueue<(uint Slot, ushort Left, ushort Right)> SetStates { get; } = new();
    internal ConcurrentQueue<uint> GetStates { get; } = new();
    internal Func<uint, uint>? OnGetState { get; set; }
    internal Func<uint, ushort, ushort, uint>? OnSetState { get; set; }
    internal int ConnectedSlot { get; set; }

    public uint GetState(uint slot)
    {
        GetStates.Enqueue(slot);
        return OnGetState?.Invoke(slot)
            ?? (slot == (uint)ConnectedSlot
                ? Xbox360RumbleLoopDiagnostic.ErrorSuccess
                : Xbox360RumbleLoopDiagnostic.ErrorDeviceNotConnected);
    }

    public uint SetState(uint slot, ushort leftMotor, ushort rightMotor)
    {
        SetStates.Enqueue((slot, leftMotor, rightMotor));
        return OnSetState?.Invoke(slot, leftMotor, rightMotor) ?? Xbox360RumbleLoopDiagnostic.ErrorSuccess;
    }
}
