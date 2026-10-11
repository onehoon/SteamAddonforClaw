using System.Diagnostics;
using SteamInputAddonforClaw.Input;
using SteamInputAddonforClaw.VirtualOutput.Viiper;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class CanonicalDualSenseEdgeInputPublisherTests
{
    [Fact]
    public async Task Deterministic_tick_publishes_once_and_stop_joins_then_writes_neutral()
    {
        var ticks = new ManualTicks();
        var sink = new RecordingSink();
        var now = 0L;
        var publisher = new CanonicalDualSenseEdgeInputPublisher(
            new Snapshot(new ControllerState(new GamepadButtons(true, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false), default, default, default, default)),
            sink,
            ticks,
            timestampProvider: () => Volatile.Read(ref now));

        publisher.Start();
        Assert.Empty(sink.States);
        ticks.Signal();
        await sink.WaitForCountAsync(1);
        Volatile.Write(ref now, Stopwatch.Frequency);
        ticks.Signal();
        await sink.WaitForCountAsync(2);
        Assert.True(SpinWait.SpinUntil(() => publisher.HeartbeatCount == 1, TimeSpan.FromSeconds(2)));
        await publisher.StopAsync();

        Assert.False(publisher.IsRunning);
        Assert.Equal(2, publisher.PublishedStateCount);
        Assert.Equal(0x20u, sink.States[0].Buttons);
        Assert.Equal(default, sink.States[^1]);
    }

    [Fact]
    public async Task Failed_state_write_reports_one_fault_and_stops_publishing()
    {
        var sink = new RecordingSink { RejectWrites = 1 };
        var faults = 0;
        var publisher = new CanonicalDualSenseEdgeInputPublisher(
            new Snapshot(default), sink, fault: _ => Interlocked.Increment(ref faults));

        Assert.False(publisher.PublishCurrentStateOnceForTests());
        Assert.False(publisher.PublishCurrentStateOnceForTests());
        Assert.Equal(1, faults);
        Assert.Equal(0, publisher.PublishedStateCount);
        await publisher.StopAsync();
        Assert.Equal(default, sink.States[^1]);
    }

    [Fact]
    public void Rear_button_suppression_is_applied_before_typed_state_write()
    {
        var sink = new RecordingSink();
        var state = new ControllerState(default, default, default, default, new AuxiliaryButtonState([true, true]));
        var publisher = new CanonicalDualSenseEdgeInputPublisher(
            new Snapshot(state), sink,
            rearButtonSuppressionProvider: (_, slot) => slot == AuxiliaryButtonSlot.RightRear);

        Assert.True(publisher.PublishCurrentStateOnceForTests());

        Assert.Equal(0x00400000u, sink.States[0].Buttons); // left rear/M2 -> L4; right rear/M1 suppressed
    }

    private sealed class Snapshot(ControllerState state) : IControllerStateSnapshotSource
    {
        public ControllerState LatestState => state;
    }

    private sealed class ManualTicks : IInputReportTickSource
    {
        private readonly SemaphoreSlim _signal = new(0);
        public ValueTask<bool> WaitForTickAsync(CancellationToken cancellationToken) => new(WaitAsync(cancellationToken));
        internal void Signal() => _signal.Release();
        private async Task<bool> WaitAsync(CancellationToken token)
        {
            await _signal.WaitAsync(token);
            return true;
        }
    }

    private sealed class RecordingSink : ICanonicalDualSenseEdgeStateSink
    {
        private readonly object _gate = new();
        private readonly List<DSDeviceState> _states = [];
        private readonly SemaphoreSlim _changed = new(0);
        internal int RejectWrites { get; set; }
        internal IReadOnlyList<DSDeviceState> States { get { lock (_gate) return _states.ToArray(); } }

        public bool SetState(DSDeviceState state)
        {
            lock (_gate) _states.Add(state);
            _changed.Release();
            if (RejectWrites <= 0) return true;
            RejectWrites--;
            return false;
        }

        internal async Task WaitForCountAsync(int count)
        {
            while (States.Count < count)
                await _changed.WaitAsync(TimeSpan.FromSeconds(2));
        }
    }
}
