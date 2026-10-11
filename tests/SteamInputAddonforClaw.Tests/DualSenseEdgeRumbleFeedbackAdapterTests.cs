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
        var sink = new RecordingSink(onWrite: rumble => { if (rumble == TwoMotorRumble.Stopped) stopped.TrySetResult(); });
        DualSenseOutputCallback? callback = null;
        using var adapter = DualSenseEdgeRumbleFeedbackAdapter.TryArm(
            sink, value => { callback = value; return true; }, () => true, TimeSpan.FromMilliseconds(30))!;

        callback!(1, 1, 2, 0, 0, 0, 0);
        await stopped.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(TwoMotorRumble.Stopped, sink.Writes[^1]);
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
    public void Native_callback_exceptions_are_contained()
    {
        DualSenseOutputCallback? callback = null;
        using var adapter = DualSenseEdgeRumbleFeedbackAdapter.TryArm(
            new ThrowingSink(), value => { callback = value; return true; }, () => true)!;

        var exception = Record.Exception(() => callback!(1, 1, 1, 0, 0, 0, 0));

        Assert.Null(exception);
    }

    private sealed class RecordingSink(Action<TwoMotorRumble>? onWrite = null) : IPhysicalRumbleSink
    {
        private readonly List<TwoMotorRumble> _writes = [];
        internal IReadOnlyList<TwoMotorRumble> Writes { get { lock (_writes) return _writes.ToArray(); } }
        public PhysicalRumbleWriteResult SetRumble(TwoMotorRumble rumble)
        {
            lock (_writes) _writes.Add(rumble);
            onWrite?.Invoke(rumble);
            return new(PhysicalRumbleWriteStatus.Succeeded, "Test");
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

    private sealed class ThrowingSink : IPhysicalRumbleSink
    {
        public PhysicalRumbleWriteResult SetRumble(TwoMotorRumble _) => throw new InvalidOperationException("Expected test exception.");
    }
}
