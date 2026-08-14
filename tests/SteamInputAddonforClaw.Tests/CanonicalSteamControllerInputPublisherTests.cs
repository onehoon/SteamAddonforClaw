using System.Diagnostics;
using SteamInputAddonforClaw.Devices.MSI.Claw;
using SteamInputAddonforClaw.Diagnostics;
using SteamInputAddonforClaw.Input;
using SteamInputAddonforClaw.VirtualOutput.Viiper;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

[Collection("AppLog")]
public sealed class CanonicalSteamControllerInputPublisherTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "CanonicalSteamControllerInputPublisherTests", Guid.NewGuid().ToString("N"));

    public CanonicalSteamControllerInputPublisherTests()
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
    public async Task Manual_ticks_publish_unchanged_state_on_every_tick()
    {
        var state = new ControllerState(new GamepadButtons(true, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false), default, default, default, new([false, false]));
        var source = new Snapshot(state);
        var sink = new FakeSink(); var ticks = new ManualTicks();
        var publisher = new CanonicalSteamControllerInputPublisher(source, sink, ticks);
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

    [Fact]
    public async Task Mapped_dpad_transition_is_logged_at_info_and_deduplicated()
    {
        var state = new ControllerState(new GamepadButtons(false, false, false, false, true, false, false, false, false, false, false, false, false, false, false, false), default, default, default, new([false, false]));
        var source = new Snapshot(state);
        var sink = new FakeSink(); var ticks = new ManualTicks();
        var publisher = new CanonicalSteamControllerInputPublisher(source, sink, ticks);
        publisher.Start();

        await ticks.TickAsync(); await sink.WaitForCountAsync(1);
        // Same state on the second tick: the mapped D-pad transition log must not repeat.
        await ticks.TickAsync(); await sink.WaitForCountAsync(2);
        await publisher.StopAsync();

        AppLog.DrainForTests();
        var log = File.ReadAllText(AppLog.CurrentLogFilePath);
        Assert.Contains("[INFO]", log);
        Assert.Equal(1, log.Split("Canonical mapped D-pad state changed", StringSplitOptions.None).Length - 1);
        Assert.Contains("Up=1", log);
        Assert.Contains("Right=0", log);
        Assert.Contains("Down=0", log);
        Assert.Contains("Left=0", log);
    }

    [Fact]
    public async Task Mapped_dpad_transition_logs_again_when_state_changes_back()
    {
        var neutral = new ControllerState(default, default, default, default, new AuxiliaryButtonState([false, false]));
        var down = neutral with { Buttons = neutral.Buttons with { DPadDown = true } };
        var source = new Snapshot(neutral);
        var sink = new FakeSink(); var ticks = new ManualTicks();
        var publisher = new CanonicalSteamControllerInputPublisher(source, sink, ticks);
        publisher.Start();

        await ticks.TickAsync(); await sink.WaitForCountAsync(1);
        source.Value = down;
        await ticks.TickAsync(); await sink.WaitForCountAsync(2);
        source.Value = neutral;
        await ticks.TickAsync(); await sink.WaitForCountAsync(3);
        await publisher.StopAsync();

        AppLog.DrainForTests();
        var log = File.ReadAllText(AppLog.CurrentLogFilePath);
        // Neutral (initial) -> Down -> Neutral is three distinct observed mapped D-pad states.
        Assert.Equal(3, log.Split("Canonical mapped D-pad state changed", StringSplitOptions.None).Length - 1);
    }

    [Fact]
    public async Task Heartbeat_reports_counts_once_the_interval_elapses_and_resets_afterward()
    {
        var state = new ControllerState(default, default, default, default, new AuxiliaryButtonState([false, false]));
        var source = new Snapshot(state);
        var sink = new FakeSink(); var ticks = new ManualTicks();
        var fakeNow = 0L;
        var publisher = new CanonicalSteamControllerInputPublisher(source, sink, ticks, timestampProvider: () => fakeNow);
        publisher.Start();

        await ticks.TickAsync(); await sink.WaitForCountAsync(1);
        await ticks.TickAsync(); await sink.WaitForCountAsync(2);
        AppLog.DrainForTests();
        Assert.DoesNotContain("publisher heartbeat", File.Exists(AppLog.CurrentLogFilePath) ? File.ReadAllText(AppLog.CurrentLogFilePath) : string.Empty);

        // Simulate one second elapsing since Start(); the third tick's post-call heartbeat check
        // must now fire, reporting all three calls made since the last (never-fired) heartbeat.
        fakeNow = Stopwatch.Frequency + 1;
        await ticks.TickAsync(); await sink.WaitForCountAsync(3);
        await publisher.StopAsync();

        AppLog.DrainForTests();
        var log = File.ReadAllText(AppLog.CurrentLogFilePath);
        var heartbeat = Assert.Single(log.Split('\n'), line => line.Contains("Canonical Steam Controller publisher heartbeat"));
        Assert.Contains("SetStateCallsLastSecond=3", heartbeat);
        Assert.Contains("TotalPublishedStateCount=3", heartbeat);
        Assert.Contains("SetStateFailures=0", heartbeat);
        Assert.Contains("MaxSetStateDurationMs=", heartbeat);
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
