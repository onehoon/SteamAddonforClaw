using SteamInputAddonforClaw.Feedback;
using SteamInputAddonforClaw.VirtualOutput.Viiper;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class DualSenseEdgeRumbleFeedbackAdapterTests
{
    [Fact]
    public void Callback_expands_and_maps_large_and_small_motors_while_ignoring_led_fields()
    {
        var sink = new RecordingSink();
        DualSenseOutputCallback? callback = null;
        using var adapter = DualSenseEdgeRumbleFeedbackAdapter.TryArm(sink, value => { callback = value; return true; }, () => true)!;

        callback!(1, 12, 34, 255, 128, 64, 7);
        callback(1, 0, 0, 10, 20, 30, 3);

        Assert.Equal(new TwoMotorRumble(34 * 257, 12 * 257), sink.Writes[0]);
        Assert.Equal(TwoMotorRumble.Stopped, sink.Writes[1]);
    }

    [Fact]
    public async Task Nonzero_callback_gets_bounded_deadman_stop()
    {
        var stopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var sink = new RecordingSink(onWrite: (rumble, _) => { if (rumble == TwoMotorRumble.Stopped) stopped.TrySetResult(); });
        DualSenseOutputCallback? callback = null;
        using var adapter = DualSenseEdgeRumbleFeedbackAdapter.TryArm(
            sink, value => { callback = value; return true; }, () => true, TimeSpan.FromMilliseconds(30))!;

        callback!(1, 1, 2, 0, 0, 0, 0);
        await stopped.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(TwoMotorRumble.Stopped, sink.Writes[^1]);
    }

    [Fact]
    public async Task Failed_explicit_stop_preserves_pending_safety_stop_and_fires_only_once()
    {
        var safetyStopWritten = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var sink = new RecordingSink(
            onWrite: (_, writeNumber) => { if (writeNumber == 3) safetyStopWritten.TrySetResult(); },
            resultFactory: (_, writeNumber) => Result(writeNumber switch
            {
                1 => PhysicalRumbleWriteStatus.Succeeded,
                2 => PhysicalRumbleWriteStatus.Failed,
                _ => PhysicalRumbleWriteStatus.Succeeded,
            }));
        DualSenseOutputCallback? callback = null;
        using var adapter = DualSenseEdgeRumbleFeedbackAdapter.TryArm(
            sink, value => { callback = value; return true; }, () => true, TimeSpan.FromMilliseconds(1600))!;

        callback!(1, 1, 2, 0, 0, 0, 0);
        await Task.Delay(TimeSpan.FromMilliseconds(700));
        callback(1, 0, 0, 0, 0, 0, 0);

        Assert.Equal(2, sink.Writes.Count);
        Assert.Equal(TwoMotorRumble.Stopped, sink.Writes[1]);
        // The original deadline is about 900 ms away. A reset 1600 ms timer would miss this bound.
        await safetyStopWritten.Task.WaitAsync(TimeSpan.FromMilliseconds(1200));
        await Task.Delay(TimeSpan.FromMilliseconds(1700));

        Assert.Equal(3, sink.Writes.Count);
        Assert.Equal(TwoMotorRumble.Stopped, sink.Writes[2]);
    }

    [Fact]
    public async Task Successful_explicit_stop_cancels_pending_safety_stop()
    {
        var sink = new RecordingSink();
        DualSenseOutputCallback? callback = null;
        using var adapter = DualSenseEdgeRumbleFeedbackAdapter.TryArm(
            sink, value => { callback = value; return true; }, () => true, TimeSpan.FromMilliseconds(60))!;

        callback!(1, 1, 2, 0, 0, 0, 0);
        callback(1, 0, 0, 0, 0, 0, 0);
        await Task.Delay(TimeSpan.FromMilliseconds(120));

        Assert.Equal(2, sink.Writes.Count);
        Assert.Equal(TwoMotorRumble.Stopped, sink.Writes[1]);
    }

    [Fact]
    public async Task New_nonzero_callback_rearms_safety_stop_after_failed_stop()
    {
        var safetyStopWritten = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var sink = new RecordingSink(
            onWrite: (_, writeNumber) => { if (writeNumber == 4) safetyStopWritten.TrySetResult(); },
            resultFactory: (_, writeNumber) => Result(writeNumber switch
            {
                2 => PhysicalRumbleWriteStatus.Failed,
                _ => PhysicalRumbleWriteStatus.Succeeded,
            }));
        DualSenseOutputCallback? callback = null;
        using var adapter = DualSenseEdgeRumbleFeedbackAdapter.TryArm(
            sink, value => { callback = value; return true; }, () => true, TimeSpan.FromMilliseconds(180))!;

        callback!(1, 1, 2, 0, 0, 0, 0);
        callback(1, 0, 0, 0, 0, 0, 0);
        await Task.Delay(TimeSpan.FromMilliseconds(90));
        callback(1, 3, 4, 0, 0, 0, 0);

        await Task.Delay(TimeSpan.FromMilliseconds(120));
        Assert.Equal(3, sink.Writes.Count);
        await safetyStopWritten.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(TwoMotorRumble.Stopped, sink.Writes[^1]);
    }

    [Fact]
    public async Task Dispose_after_failed_stop_cancels_retained_safety_stop()
    {
        var sink = new RecordingSink(resultFactory: (_, writeNumber) => Result(
            writeNumber == 2 ? PhysicalRumbleWriteStatus.Failed : PhysicalRumbleWriteStatus.Succeeded));
        DualSenseOutputCallback? callback = null;
        var adapter = DualSenseEdgeRumbleFeedbackAdapter.TryArm(
            sink, value => { callback = value; return true; }, () => true, TimeSpan.FromMilliseconds(60))!;

        callback!(1, 1, 2, 0, 0, 0, 0);
        callback(1, 0, 0, 0, 0, 0, 0);
        adapter.Dispose();
        await Task.Delay(TimeSpan.FromMilliseconds(120));

        Assert.Equal(2, sink.Writes.Count);
    }

    [Theory]
    [InlineData("Unavailable")]
    [InlineData("Disposed")]
    public async Task Unavailable_or_disposed_explicit_stop_keeps_existing_cancellation_policy(
        string stopStatusName)
    {
        var stopStatus = Enum.Parse<PhysicalRumbleWriteStatus>(stopStatusName);
        var sink = new RecordingSink(resultFactory: (_, writeNumber) => Result(
            writeNumber == 2 ? stopStatus : PhysicalRumbleWriteStatus.Succeeded));
        DualSenseOutputCallback? callback = null;
        using var adapter = DualSenseEdgeRumbleFeedbackAdapter.TryArm(
            sink, value => { callback = value; return true; }, () => true, TimeSpan.FromMilliseconds(60))!;

        callback!(1, 1, 2, 0, 0, 0, 0);
        callback(1, 0, 0, 0, 0, 0, 0);
        await Task.Delay(TimeSpan.FromMilliseconds(120));

        Assert.Equal(2, sink.Writes.Count);
    }

    [Fact]
    public async Task Failed_stop_throw_is_contained_and_preserves_pending_safety_stop()
    {
        var safetyStopWritten = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var sink = new RecordingSink(
            onWrite: (_, writeNumber) => { if (writeNumber == 3) safetyStopWritten.TrySetResult(); },
            resultFactory: (_, writeNumber) => writeNumber == 2
                ? throw new InvalidOperationException("Expected explicit STOP exception.")
                : Result(PhysicalRumbleWriteStatus.Succeeded));
        DualSenseOutputCallback? callback = null;
        using var adapter = DualSenseEdgeRumbleFeedbackAdapter.TryArm(
            sink, value => { callback = value; return true; }, () => true, TimeSpan.FromMilliseconds(60))!;

        callback!(1, 1, 2, 0, 0, 0, 0);
        var exception = Record.Exception(() => callback(1, 0, 0, 0, 0, 0, 0));
        await safetyStopWritten.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Null(exception);
        Assert.Equal(3, sink.Writes.Count);
        Assert.Equal(TwoMotorRumble.Stopped, sink.Writes[2]);
    }

    [Fact]
    public async Task Dispose_clears_callback_and_drains_inflight_writer_before_final_stop()
    {
        var sink = new BlockingSink();
        DualSenseOutputCallback? callback = null;
        var clearObserved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var adapter = DualSenseEdgeRumbleFeedbackAdapter.TryArm(
            sink,
            value => { callback = value; return true; },
            () => { clearObserved.TrySetResult(); return true; })!;

        var nativeCallback = Task.Run(() => callback!(1, 10, 20, 0, 0, 0, 0));
        await sink.WriteEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var dispose = Task.Run(adapter.Dispose);
        await clearObserved.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.False(dispose.IsCompleted);

        sink.ReleaseWrite.TrySetResult();
        await nativeCallback;
        await dispose;
        sink.SetRumble(TwoMotorRumble.Stopped);
        callback!(1, 255, 255, 255, 255, 255, 255);

        Assert.Equal(TwoMotorRumble.Stopped, sink.Writes[^1]);
        Assert.Equal(2, sink.Writes.Count);
    }

    [Fact]
    public async Task Native_callback_exceptions_are_contained()
    {
        var safetyStopWritten = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var sink = new RecordingSink(
            onWrite: (rumble, writeNumber) =>
            {
                if (writeNumber == 2 && rumble == TwoMotorRumble.Stopped)
                    safetyStopWritten.TrySetResult();
            },
            resultFactory: (_, writeNumber) => writeNumber == 1
                ? throw new InvalidOperationException("Expected nonzero write exception.")
                : Result(PhysicalRumbleWriteStatus.Succeeded));
        DualSenseOutputCallback? callback = null;
        using var adapter = DualSenseEdgeRumbleFeedbackAdapter.TryArm(
            sink, value => { callback = value; return true; }, () => true, TimeSpan.FromMilliseconds(30))!;

        var exception = Record.Exception(() => callback!(1, 1, 1, 0, 0, 0, 0));
        await safetyStopWritten.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Null(exception);
        Assert.Equal(TwoMotorRumble.Stopped, sink.Writes[^1]);
    }

    private static PhysicalRumbleWriteResult Result(PhysicalRumbleWriteStatus status) => new(status, "Test");

    private sealed class RecordingSink(
        Action<TwoMotorRumble, int>? onWrite = null,
        Func<TwoMotorRumble, int, PhysicalRumbleWriteResult>? resultFactory = null) : IPhysicalRumbleSink
    {
        private readonly List<TwoMotorRumble> _writes = [];
        internal IReadOnlyList<TwoMotorRumble> Writes { get { lock (_writes) return _writes.ToArray(); } }
        public PhysicalRumbleWriteResult SetRumble(TwoMotorRumble rumble)
        {
            int writeNumber;
            lock (_writes)
            {
                _writes.Add(rumble);
                writeNumber = _writes.Count;
            }
            onWrite?.Invoke(rumble, writeNumber);
            return resultFactory?.Invoke(rumble, writeNumber) ?? Result(PhysicalRumbleWriteStatus.Succeeded);
        }
    }

    private sealed class BlockingSink : IPhysicalRumbleSink
    {
        private readonly List<TwoMotorRumble> _writes = [];
        internal TaskCompletionSource WriteEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource ReleaseWrite { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal IReadOnlyList<TwoMotorRumble> Writes { get { lock (_writes) return _writes.ToArray(); } }
        public PhysicalRumbleWriteResult SetRumble(TwoMotorRumble rumble)
        {
            lock (_writes) _writes.Add(rumble);
            WriteEntered.TrySetResult();
            ReleaseWrite.Task.GetAwaiter().GetResult();
            return new(PhysicalRumbleWriteStatus.Succeeded, "Test");
        }
    }
}
