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
    public void Sequences_are_deterministic_descending_and_end_in_one_exact_stop()
    {
        var sequences = Enumerable.Range(1, 500)
            .Select(Xbox360RumbleLoopDiagnostic.CreateSequence)
            .ToArray();

        foreach (var (sequence, cycle) in sequences.Select((sequence, index) => (sequence, index + 1)))
        {
            Assert.Equal(sequence, Xbox360RumbleLoopDiagnostic.CreateSequence(cycle));
            Assert.InRange(sequence.Length - 1, 6, 10);
            Assert.InRange(sequence[0], (byte)128, (byte)220);
            Assert.InRange(sequence[^2], (byte)4, (byte)16);
            Assert.Equal((byte)0, sequence[^1]);
            Assert.All(sequence.Take(sequence.Length - 1), value => Assert.NotEqual((byte)0, value));
            Assert.True(sequence.Take(sequence.Length - 1).Zip(sequence.Skip(1), (left, right) => left > right).All(static descending => descending));
        }

        Assert.NotEqual(sequences[0], sequences[1]);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 257)]
    [InlineData(127, 32639)]
    [InlineData(255, 65535)]
    public void Existing_production_amplitude_expansion_is_reused(byte value, int expected)
        => Assert.Equal((ushort)expected, Xbox360RumbleFeedbackBridge.Expand(value));

    [Fact]
    public async Task Synchronous_terminal_callback_is_armed_before_xinput_and_allows_the_next_cycle()
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
            Assert.All(xinput.SetStates, static state => Assert.Equal(state.Left, state.Right));
        }
        finally
        {
            stop.Cancel();
            await run.WaitAsync(TimeSpan.FromSeconds(5));
        }
        Assert.Single(xinput.SetStates, static state => state.Left == 0 && state.Right == 0);
    }

    [Fact]
    public async Task Missing_terminal_callback_fails_once_without_a_second_host_stop()
    {
        var xinput = new FakeXbox360RumbleLoopXInput();
        var terminalSent = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var physicalStop = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var physicalStopCount = 0;
        var diagnostic = CreateDiagnostic(xinput, TimeSpan.FromMilliseconds(1), TimeSpan.FromMilliseconds(25), () =>
        {
            Interlocked.Increment(ref physicalStopCount);
            physicalStop.TrySetResult();
            return new(PhysicalRumbleWriteStatus.Succeeded, "OK");
        });
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
    }

    [Fact]
    public async Task Failed_terminal_xinput_call_is_not_misclassified_as_a_missing_callback()
    {
        var xinput = new FakeXbox360RumbleLoopXInput
        {
            OnSetState = (_, left, right) => left == 0 && right == 0 ? 5u : Xbox360RumbleLoopDiagnostic.ErrorSuccess
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
        Assert.Single(xinput.SetStates, static state => state.Left == 0 && state.Right == 0);
        Assert.Equal(1, physicalStops);
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
        var diagnostic = CreateDiagnostic(xinput, TimeSpan.FromSeconds(1), TimeSpan.FromMilliseconds(100), () =>
        {
            physicalStops++;
            return new(PhysicalRumbleWriteStatus.Succeeded, "OK");
        });
        var run = diagnostic.RunAsync(cancellation.Token);
        await firstOutput.Task.WaitAsync(TimeSpan.FromSeconds(5));

        cancellation.Cancel();
        await run.WaitAsync(TimeSpan.FromSeconds(5));
        diagnostic.CompleteManualStop();

        Assert.Equal(FrontendXbox360RumbleLoopState.Stopped, diagnostic.Snapshot.State);
        Assert.Single(xinput.SetStates, static state => state.Left == 0 && state.Right == 0);
        Assert.Equal(1, physicalStops);
    }

    private static Xbox360RumbleLoopDiagnostic CreateDiagnostic(
        FakeXbox360RumbleLoopXInput xinput,
        TimeSpan cadence,
        TimeSpan callbackTimeout,
        Func<PhysicalRumbleWriteResult> physicalStop) =>
        new(0, xinput, physicalStop, cadence, callbackTimeout);
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
