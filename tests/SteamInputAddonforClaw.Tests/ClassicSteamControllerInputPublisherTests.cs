using System.Buffers.Binary;
using SteamInputAddonforClaw.Input;
using SteamInputAddonforClaw.Devices.MSI.Claw;
using SteamInputAddonforClaw.VirtualOutput.Viiper;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class ClassicSteamControllerInputPublisherTests
{
    [Fact]
    public async Task ManualTicksRepeatUnchangedStateAndAdvanceFrames()
    {
        var source = new Snapshot(new ControllerState(new GamepadButtons(true, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false), default, default, default, new([false, false])));
        var runtime = new FakeRuntime();
        var ticks = new ManualTicks();
        var publisher = new ClassicSteamControllerInputPublisher(source, runtime, 7, ticks);
        publisher.Start();

        await ticks.TickAsync(); await runtime.WaitForCountAsync(1);
        await ticks.TickAsync(); await runtime.WaitForCountAsync(2);
        await ticks.TickAsync(); await runtime.WaitForCountAsync(3);
        await publisher.StopAsync();

        Assert.Equal([1u, 2u, 3u], runtime.Reports.Select(r => BinaryPrimitives.ReadUInt32LittleEndian(r[4..8])));
        Assert.All(runtime.Reports, report => Assert.Equal(0x80, report[8] & 0x80));
    }

    [Fact]
    public async Task SnapshotReplacementIsUsedOnTheNextTick()
    {
        var source = new Snapshot(new ControllerState(new AuxiliaryButtonState([false, false])));
        var runtime = new FakeRuntime(); var ticks = new ManualTicks();
        var publisher = new ClassicSteamControllerInputPublisher(source, runtime, 7, ticks);
        publisher.Start();
        await ticks.TickAsync(); await runtime.WaitForCountAsync(1);
        source.Value = new ControllerState(new GamepadButtons(false, true, false, false, false, false, false, false, false, false, false, false, false, false, false, false), default, default, default, new([false, false]));
        await ticks.TickAsync(); await runtime.WaitForCountAsync(2);
        await publisher.StopAsync();
        Assert.Equal(0x20, runtime.Reports[1][8] & 0x20);
    }

    [Fact]
    public async Task FailureStopsAndNotifiesOnce()
    {
        var runtime = new FakeRuntime { Accept = false }; var ticks = new ManualTicks(); var faults = 0;
        var publisher = new ClassicSteamControllerInputPublisher(new Snapshot(new ControllerState(new AuxiliaryButtonState([false, false]))), runtime, 7, ticks, _ => faults++);
        publisher.Start(); await ticks.TickAsync(); await publisher.StopAsync();
        Assert.Equal(1, faults); Assert.Single(runtime.Reports);
    }

    [Fact]
    public async Task SendExceptionStopsAndNotifiesOnce()
    {
        var runtime = new FakeRuntime { ThrowOnSend = true }; var ticks = new ManualTicks(); var faults = 0;
        var publisher = new ClassicSteamControllerInputPublisher(new Snapshot(new ControllerState(new AuxiliaryButtonState([false, false]))), runtime, 7, ticks, _ => faults++);
        publisher.Start(); await ticks.TickAsync(); await publisher.StopAsync(); await publisher.StopAsync();
        Assert.Equal(1, faults); Assert.Single(runtime.Reports);
    }

    [Fact]
    public async Task RightStickAtShortMinValueDoesNotFaultThePublisher()
    {
        var state = new ControllerState(default, default, new StickState(short.MinValue, short.MinValue), default, new AuxiliaryButtonState([false, false]));
        var runtime = new FakeRuntime(); var ticks = new ManualTicks(); var faults = 0;
        var publisher = new ClassicSteamControllerInputPublisher(new Snapshot(state), runtime, 7, ticks, _ => faults++);

        publisher.Start(); await ticks.TickAsync();
        // Bound the wait: on a regression, Write() throws before SetInput ever runs, so
        // runtime.Reports never grows -- an unbounded wait for a report count would hang.
        Assert.True(SpinWait.SpinUntil(() => faults > 0 || runtime.Reports.Count > 0, TimeSpan.FromSeconds(5)));
        await publisher.StopAsync();

        Assert.Equal(0, faults);
        Assert.Single(runtime.Reports);
    }

    [Fact]
    public async Task DuplicateStartIsRejectedAndStopIsIdempotent()
    {
        var publisher = new ClassicSteamControllerInputPublisher(new Snapshot(new ControllerState(new AuxiliaryButtonState([false, false]))), new FakeRuntime(), 7, new ManualTicks());
        publisher.Start();
        Assert.Throws<InvalidOperationException>(publisher.Start);
        await publisher.StopAsync(); await publisher.StopAsync();
    }

    private sealed class Snapshot(ControllerState value) : IControllerStateSnapshotSource
    { public ControllerState Value { get; set; } = value; public ControllerState LatestState => Value; }
    private sealed class ManualTicks : IInputReportTickSource
    {
        private readonly Queue<TaskCompletionSource<bool>> _waiters = new();
        public ValueTask<bool> WaitForTickAsync(CancellationToken token)
        { var waiter = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously); _waiters.Enqueue(waiter); token.Register(() => waiter.TrySetCanceled(token)); return new(waiter.Task); }
        public async Task TickAsync()
        {
            while (_waiters.Count == 0) await Task.Yield();
            _waiters.Dequeue().TrySetResult(true);
        }
    }
    private sealed class FakeRuntime : IViiperRuntime
    {
        public bool Accept = true; public bool ThrowOnSend; public List<byte[]> Reports { get; } = [];
        public IReadOnlyCollection<uint> OwnedDeviceIds => [7]; public uint BusId => 1;
        public void Start() { } public uint CreateDevice() => 7;
        public bool SetNeutral(uint id) => true;
        public bool SetInput(uint id, byte[] report) { Reports.Add(report); if (ThrowOnSend) throw new InvalidOperationException("send failed"); return Accept; }
        public ViiperDeviceRemovalResult RemoveDevice(uint bus, uint id) => new(true, true);
        public void StopIfUnused() { } public void Dispose() { }
        public async Task WaitForCountAsync(int count) { while (Reports.Count < count) await Task.Yield(); }
    }
}
