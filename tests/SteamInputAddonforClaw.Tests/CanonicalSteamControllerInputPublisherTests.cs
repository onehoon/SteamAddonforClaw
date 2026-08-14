using SteamInputAddonforClaw.Devices.MSI.Claw;
using SteamInputAddonforClaw.Input;
using SteamInputAddonforClaw.VirtualOutput.Viiper;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class CanonicalSteamControllerInputPublisherTests
{
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
        internal bool Accept = true;
        internal bool ThrowOnSet;
        internal List<SteamControllerDeviceState> States { get; } = [];
        public bool SetState(SteamControllerDeviceState state)
        {
            if (ThrowOnSet) throw new InvalidOperationException("set failed");
            States.Add(state); return Accept;
        }
        public async Task WaitForCountAsync(int count) { while (States.Count < count) await Task.Yield(); }
    }
}
