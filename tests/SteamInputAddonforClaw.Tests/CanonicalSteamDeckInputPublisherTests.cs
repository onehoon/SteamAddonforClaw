using System.Diagnostics;
using SteamInputAddonforClaw.Diagnostics;
using SteamInputAddonforClaw.Input;
using SteamInputAddonforClaw.VirtualOutput.Viiper;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

/// <summary>
/// Manual-tick and production-worker coverage (scheduler, fault semantics, timing diagnostics,
/// lifecycle safety) for <see cref="CanonicalSteamDeckInputPublisher"/>.
/// </summary>
[Collection("AppLog")]
public sealed class CanonicalSteamDeckInputPublisherTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "CanonicalSteamDeckInputPublisherTests", Guid.NewGuid().ToString("N"));

    public CanonicalSteamDeckInputPublisherTests()
    {
        Directory.CreateDirectory(_directory);
        AppLog.DirectoryOverride = _directory;
        AppLog.MinimumLevelOverride = AppLogLevel.Debug;
    }

    public void Dispose()
    {
        AppLog.MinimumLevelOverride = AppLogLevel.Off;
        AppLog.DrainForTests();
        AppLog.DirectoryOverride = null;
        if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
    }

    [Fact]
    public async Task Manual_ticks_publish_Deck_mapped_state()
    {
        var source = new Snapshot(new ControllerState(new GamepadButtons(true, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false), default, default, default, new([false, false])));
        var sink = new FakeSink(); var ticks = new ManualTicks();
        var publisher = new CanonicalSteamDeckInputPublisher(source, sink, ticks);
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
    public async Task Pulse_is_merged_between_mapper_and_sink_then_expires()
    {
        var time = new FakeTimeProvider();
        var overlay = new SteamDeckSystemButtonOverlay(time);
        var source = new Snapshot(new ControllerState(new GamepadButtons(true, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false), default, default, default, new([false, false])));
        var sink = new FakeSink(); var ticks = new ManualTicks();
        var publisher = new CanonicalSteamDeckInputPublisher(source, sink, ticks, systemButtonOverlay: overlay);
        publisher.Start();

        // Before any pulse is requested: normal mapped state, QuickAccess neutral.
        await ticks.TickAsync(); await sink.WaitForCountAsync(1);
        Assert.Equal((byte)1, sink.States[0].A);
        Assert.Equal((byte)0, sink.States[0].QuickAccess);

        // Overlay requests a pulse out-of-band (as a future caller would); the next publish tick
        // (mapper -> overlay -> sink) must carry it while leaving the mapped fields untouched.
        overlay.RequestQuickAccessPulse();
        await ticks.TickAsync(); await sink.WaitForCountAsync(2);
        Assert.Equal((byte)1, sink.States[1].A);
        Assert.Equal((byte)1, sink.States[1].QuickAccess);

        // After the pulse duration elapses, normal publication continues with QuickAccess neutral.
        time.Advance(TimeSpan.FromMilliseconds(101));
        await ticks.TickAsync(); await sink.WaitForCountAsync(3);
        Assert.Equal((byte)1, sink.States[2].A);
        Assert.Equal((byte)0, sink.States[2].QuickAccess);

        await publisher.StopAsync();
    }

    [Fact]
    public async Task Steam_pulse_summary_counts_only_successful_asserted_publishes_and_confirms_release()
    {
        var line = await PublishPulseAndReadSummary(static overlay => overlay.RequestSteamPulse(), static state => state.Steam);

        Assert.Contains("Button=Steam", line);
        Assert.Contains("ActiveSetStateCount=2", line);
        Assert.Contains("FirstPublishDelayMs=", line);
        Assert.Contains("PublishedHighDurationMs=", line);
        Assert.Contains("ReleasePublished=True", line);
        Assert.Contains("SetStateFailures=0", line);
    }

    [Fact]
    public async Task Repeated_same_button_requests_share_one_pulse_diagnostic()
    {
        var fakeNow = 0L;
        var time = new FakeTimeProvider();
        var overlay = new SteamDeckSystemButtonOverlay(time, () => fakeNow);
        var source = new Snapshot(new ControllerState(new AuxiliaryButtonState([false, false])));
        var sink = new FakeSink();
        var ticks = new ManualTicks();
        var publisher = new CanonicalSteamDeckInputPublisher(source, sink, ticks, timestampProvider: () => fakeNow, systemButtonOverlay: overlay);
        publisher.Start();

        overlay.RequestSteamPulse();
        fakeNow = 1;
        await ticks.TickAsync(); await sink.WaitForCountAsync(1);
        time.Advance(TimeSpan.FromMilliseconds(50));
        fakeNow = 50;
        overlay.RequestSteamPulse();
        await ticks.TickAsync(); await sink.WaitForCountAsync(2);
        time.Advance(TimeSpan.FromMilliseconds(101));
        fakeNow = 151;
        await ticks.TickAsync(); await sink.WaitForCountAsync(3);
        await publisher.StopAsync();

        AppLog.DrainForTests();
        var line = Assert.Single(LogFileTestHelper.ReadAllText(AppLog.CurrentLogFilePath).Split('\n'), line => line.Contains("PulseCompleted"));
        Assert.Contains("PulseId=1", line);
        Assert.Contains("RequestCount=2", line);
        Assert.Contains("ActiveSetStateCount=2", line);
    }

    [Fact]
    public async Task QuickAccess_pulse_summary_counts_only_successful_asserted_publishes_and_confirms_release()
    {
        var line = await PublishPulseAndReadSummary(static overlay => overlay.RequestQuickAccessPulse(), static state => state.QuickAccess);

        Assert.Contains("Button=QuickAccess", line);
        Assert.Contains("ActiveSetStateCount=2", line);
        Assert.Contains("ReleasePublished=True", line);
        Assert.Contains("SetStateFailures=0", line);
    }

    [Fact]
    public async Task First_ordinary_input_is_logged_once_and_system_button_only_does_not_satisfy_it()
    {
        var fakeNow = 0L;
        var source = new Snapshot(new ControllerState(
            new GamepadButtons(false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false),
            new StickState(-1, 1), default, new TriggerState(7, 7), new([false, false])));
        var sink = new FakeSink();
        var ticks = new ManualTicks();
        var time = new FakeTimeProvider();
        var overlay = new SteamDeckSystemButtonOverlay(time, () => fakeNow);
        var publisher = new CanonicalSteamDeckInputPublisher(source, sink, ticks, timestampProvider: () => fakeNow, systemButtonOverlay: overlay);
        publisher.Start();

        await ticks.TickAsync(); await sink.WaitForCountAsync(1);
        overlay.RequestQuickAccessPulse();
        fakeNow = 10;
        await ticks.TickAsync(); await sink.WaitForCountAsync(2);
        source.Value = new ControllerState(new GamepadButtons(true, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false), default, default, default, new([false, false]));
        fakeNow = 20;
        await ticks.TickAsync(); await sink.WaitForCountAsync(3);
        source.Value = new ControllerState(new AuxiliaryButtonState([false, false]));
        fakeNow = 30;
        await ticks.TickAsync(); await sink.WaitForCountAsync(4);
        await publisher.StopAsync();

        AppLog.DrainForTests();
        var log = LogFileTestHelper.ReadAllText(AppLog.CurrentLogFilePath);
        Assert.Equal(1, log.Split('\n').Count(line => line.Contains("FirstOrdinaryInputPublished")));
        Assert.Contains("ElapsedSincePublisherStartMs=", log);
    }

    [Fact]
    public void Failed_asserted_publish_is_not_counted_but_is_reported_in_the_summary()
    {
        var now = 0L;
        var time = new FakeTimeProvider();
        var overlay = new SteamDeckSystemButtonOverlay(time, () => now);
        overlay.RequestSteamPulse();
        var asserted = overlay.Apply(default);
        overlay.RecordPublishResult(asserted, accepted: false, now);
        now = 1;
        overlay.RecordPublishResult(asserted, accepted: true, now);
        time.Advance(TimeSpan.FromMilliseconds(101));
        now = 2;
        var released = overlay.Apply(default);
        overlay.RecordPublishResult(released, accepted: true, now);

        AppLog.DrainForTests();
        var line = Assert.Single(LogFileTestHelper.ReadAllText(AppLog.CurrentLogFilePath).Split('\n'), line => line.Contains("PulseCompleted"));
        Assert.Contains("ActiveSetStateCount=1", line);
        Assert.Contains("SetStateFailures=1", line);
    }

    [Fact]
    public void Overlapping_pulses_are_logged_without_changing_the_output()
    {
        var time = new FakeTimeProvider();
        var overlay = new SteamDeckSystemButtonOverlay(time);
        overlay.RequestSteamPulse();
        time.Advance(TimeSpan.FromMilliseconds(25));
        overlay.RequestQuickAccessPulse();

        var state = overlay.Apply(default);
        Assert.Equal((byte)1, state.Steam);
        Assert.Equal((byte)1, state.QuickAccess);
        AppLog.DrainForTests();
        var log = LogFileTestHelper.ReadAllText(AppLog.CurrentLogFilePath);
        Assert.Contains("QuickAccess pulse requested", log);
        Assert.Contains("OtherSystemButtonActive=True", log);
    }

    private async Task<string> PublishPulseAndReadSummary(Action<SteamDeckSystemButtonOverlay> request, Func<SteamDeckDeviceState, byte> button)
    {
        var fakeNow = 0L;
        var time = new FakeTimeProvider();
        var overlay = new SteamDeckSystemButtonOverlay(time, () => fakeNow);
        var source = new Snapshot(new ControllerState(new AuxiliaryButtonState([false, false])));
        var sink = new FakeSink();
        var ticks = new ManualTicks();
        var publisher = new CanonicalSteamDeckInputPublisher(source, sink, ticks, timestampProvider: () => fakeNow, systemButtonOverlay: overlay);
        publisher.Start();

        request(overlay);
        fakeNow = 1;
        await ticks.TickAsync(); await sink.WaitForCountAsync(1);
        Assert.Equal((byte)1, button(sink.States[0]));
        fakeNow = 50;
        await ticks.TickAsync(); await sink.WaitForCountAsync(2);
        Assert.Equal((byte)1, button(sink.States[1]));
        time.Advance(TimeSpan.FromMilliseconds(101));
        fakeNow = 102;
        await ticks.TickAsync(); await sink.WaitForCountAsync(3);
        Assert.Equal((byte)0, button(sink.States[2]));
        await publisher.StopAsync();

        AppLog.DrainForTests();
        return Assert.Single(LogFileTestHelper.ReadAllText(AppLog.CurrentLogFilePath).Split('\n'), line => line.Contains("PulseCompleted"));
    }

    [Fact]
    public async Task Unchanged_state_is_published_every_tick()
    {
        var state = new ControllerState(new GamepadButtons(true, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false), default, default, default, new([false, false]));
        var source = new Snapshot(state);
        var sink = new FakeSink(); var ticks = new ManualTicks();
        var publisher = new CanonicalSteamDeckInputPublisher(source, sink, ticks);
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
    public async Task False_sink_reports_fault_and_stops()
    {
        var sink = new FakeSink { Accept = false }; var ticks = new ManualTicks(); var faults = 0;
        var faultObserved = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var publisher = new CanonicalSteamDeckInputPublisher(new Snapshot(new ControllerState(new AuxiliaryButtonState([false, false]))), sink, ticks, _ => { faults++; faultObserved.TrySetResult(true); });
        publisher.Start(); await ticks.TickAsync();
        await faultObserved.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await publisher.StopAsync(); await publisher.StopAsync();
        Assert.Equal(1, faults);
        Assert.False(publisher.IsRunning);
    }

    [Fact]
    public async Task Throwing_sink_reports_fault_and_stops()
    {
        var sink = new FakeSink { ThrowOnSet = true }; var ticks = new ManualTicks(); var faults = 0;
        var faultObserved = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var publisher = new CanonicalSteamDeckInputPublisher(new Snapshot(new ControllerState(new AuxiliaryButtonState([false, false]))), sink, ticks, _ => { faults++; faultObserved.TrySetResult(true); });
        publisher.Start(); await ticks.TickAsync();
        await faultObserved.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await publisher.StopAsync(); await publisher.StopAsync();
        Assert.Equal(1, faults);
        Assert.False(publisher.IsRunning);
    }

    [Fact]
    public async Task Duplicate_Start_rejected()
    {
        var state = new ControllerState(default, default, new StickState(short.MinValue, short.MinValue), default, new AuxiliaryButtonState([false, false]));
        var sink = new FakeSink(); var ticks = new ManualTicks();
        var publisher = new CanonicalSteamDeckInputPublisher(new Snapshot(state), sink, ticks);
        publisher.Start();
        Assert.Throws<InvalidOperationException>(publisher.Start);
        await ticks.TickAsync(); await sink.WaitForCountAsync(1);
        await publisher.StopAsync();
        Assert.Single(sink.States);
    }

    [Fact]
    public async Task Stop_is_idempotent()
    {
        var sink = new FakeSink(); var ticks = new ManualTicks();
        var publisher = new CanonicalSteamDeckInputPublisher(new Snapshot(new ControllerState(new AuxiliaryButtonState([false, false]))), sink, ticks);
        publisher.Start();
        await ticks.TickAsync(); await sink.WaitForCountAsync(1);
        await publisher.StopAsync();
        await publisher.StopAsync(); // no-op, must not throw
        Assert.False(publisher.IsRunning);
    }

    [Fact]
    public async Task Stop_Start_restart_safe()
    {
        var sink = new FakeSink();
        var publisher = new CanonicalSteamDeckInputPublisher(new Snapshot(new ControllerState(new AuxiliaryButtonState([false, false]))), sink);

        publisher.Start();
        try
        {
            Assert.Throws<InvalidOperationException>(publisher.Start);
            await sink.WaitForCountAsync(1, TimeSpan.FromSeconds(2));
        }
        finally
        {
            await publisher.StopAsync();
        }
        await publisher.StopAsync();

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
    public async Task Heartbeat_reports_timing_decomposition_fields()
    {
        var state = new ControllerState(default, default, default, default, new AuxiliaryButtonState([false, false]));
        var source = new Snapshot(state);
        var sink = new FakeSink(); var ticks = new ManualTicks();
        var fakeNow = 0L;
        var publisher = new CanonicalSteamDeckInputPublisher(source, sink, ticks, timestampProvider: () => fakeNow);
        publisher.Start();

        await ticks.TickAsync(); await sink.WaitForCountAsync(1);
        await ticks.TickAsync(); await sink.WaitForCountAsync(2);
        AppLog.DrainForTests();
        Assert.DoesNotContain("publisher heartbeat", File.Exists(AppLog.CurrentLogFilePath) ? LogFileTestHelper.ReadAllText(AppLog.CurrentLogFilePath) : string.Empty);

        fakeNow = Stopwatch.Frequency + 1;
        await ticks.TickAsync(); await sink.WaitForCountAsync(3);
        await publisher.StopAsync();

        AppLog.DrainForTests();
        var log = LogFileTestHelper.ReadAllText(AppLog.CurrentLogFilePath);
        var heartbeat = Assert.Single(log.Split('\n'), line => line.Contains("Canonical Steam Deck publisher heartbeat"));
        Assert.Contains("SetStateCallsLastSecond=3", heartbeat);
        Assert.Contains("TotalPublishedStateCount=3", heartbeat);
        Assert.Contains("SetStateFailures=0", heartbeat);
        Assert.Contains("MaxSetStateDurationMs=", heartbeat);
        Assert.Contains("HeartbeatElapsedMs=", heartbeat);
        Assert.Contains("EffectiveSetStateHz=", heartbeat);
        // Timing-decomposition fields: present with zero defaults on the manual-tick path, which
        // never drives WorkerLoop.
        Assert.Contains("TimerWakeCount=0", heartbeat);
        Assert.Contains("ExpectedTicksAt4ms=", heartbeat);
        Assert.Contains("AverageWakeToWakeMs=0", heartbeat);
        Assert.Contains("MaxWakeToWakeMs=0", heartbeat);
        Assert.Contains("AverageWaitBlockedMs=0", heartbeat);
        Assert.Contains("MaxWaitBlockedMs=0", heartbeat);
        Assert.Contains("AveragePublishWorkMs=0", heartbeat);
        Assert.Contains("MaxPublishWorkMs=0", heartbeat);
        Assert.Contains("WakeOver4_25MsCount=0", heartbeat);
        Assert.Contains("WakeOver5MsCount=0", heartbeat);
        Assert.Contains("AverageWakeLatenessMs=0", heartbeat);
        Assert.Contains("MaxWakeLatenessMs=0", heartbeat);
        Assert.Contains("SkippedDeadlineCount=0", heartbeat);
    }

    [Fact]
    public async Task TimingDiagnostics_WakeOverThresholdCountsIncrementWhenIntervalExceedsThresholds()
    {
        var source = new Snapshot(new ControllerState(new AuxiliaryButtonState([false, false])));
        var sink = new FakeSink(); var ticks = new ManualTicks();
        var fakeNow = 0L;
        var publisher = new CanonicalSteamDeckInputPublisher(source, sink, ticks, timestampProvider: () => fakeNow);
        publisher.Start();

        var msToTicks = Stopwatch.Frequency / 1000.0;
        publisher.RecordTimerWakeForTests((long)(0 * msToTicks));
        publisher.RecordTimerWakeForTests((long)(4 * msToTicks)); // 4ms interval: under both thresholds
        publisher.RecordTimerWakeForTests((long)(10 * msToTicks)); // 6ms interval: over both thresholds

        fakeNow = Stopwatch.Frequency + 1;
        await ticks.TickAsync(); await sink.WaitForCountAsync(1);
        await publisher.StopAsync();

        AppLog.DrainForTests();
        var heartbeat = Assert.Single(LogFileTestHelper.ReadAllText(AppLog.CurrentLogFilePath).Split('\n'), line => line.Contains("publisher heartbeat"));
        Assert.Contains("WakeOver4_25MsCount=1", heartbeat);
        Assert.Contains("WakeOver5MsCount=1", heartbeat);
    }

    [Fact]
    public async Task TimingDiagnostics_WakeToWakeAccumulatesAverageAndMax()
    {
        var source = new Snapshot(new ControllerState(new AuxiliaryButtonState([false, false])));
        var sink = new FakeSink(); var ticks = new ManualTicks();
        var fakeNow = 0L;
        var publisher = new CanonicalSteamDeckInputPublisher(source, sink, ticks, timestampProvider: () => fakeNow);
        publisher.Start();

        var msToTicks = Stopwatch.Frequency / 1000.0;
        publisher.RecordTimerWakeForTests((long)(0 * msToTicks));
        publisher.RecordTimerWakeForTests((long)(4 * msToTicks));
        publisher.RecordTimerWakeForTests((long)(10 * msToTicks));

        fakeNow = Stopwatch.Frequency + 1;
        await ticks.TickAsync(); await sink.WaitForCountAsync(1);
        await publisher.StopAsync();

        AppLog.DrainForTests();
        var heartbeat = Assert.Single(LogFileTestHelper.ReadAllText(AppLog.CurrentLogFilePath).Split('\n'), line => line.Contains("publisher heartbeat"));
        Assert.Contains("TimerWakeCount=3", heartbeat);
        AssertFieldInRange(heartbeat, "AverageWakeToWakeMs", 4.9, 5.1);
        AssertFieldInRange(heartbeat, "MaxWakeToWakeMs", 5.9, 6.1);
    }

    [Fact]
    public async Task TimingDiagnostics_WaitBlockedAccumulatesAverageAndMax()
    {
        var source = new Snapshot(new ControllerState(new AuxiliaryButtonState([false, false])));
        var sink = new FakeSink(); var ticks = new ManualTicks();
        var fakeNow = 0L;
        var publisher = new CanonicalSteamDeckInputPublisher(source, sink, ticks, timestampProvider: () => fakeNow);
        publisher.Start();

        var msToTicks = Stopwatch.Frequency / 1000.0;
        publisher.RecordWaitBlockedForTests(0, (long)(4.0 * msToTicks));
        publisher.RecordTimerWakeForTests(0);
        publisher.RecordWaitBlockedForTests(0, (long)(4.6 * msToTicks));
        publisher.RecordTimerWakeForTests((long)(4.6 * msToTicks));

        fakeNow = Stopwatch.Frequency + 1;
        await ticks.TickAsync(); await sink.WaitForCountAsync(1);
        await publisher.StopAsync();

        AppLog.DrainForTests();
        var heartbeat = Assert.Single(LogFileTestHelper.ReadAllText(AppLog.CurrentLogFilePath).Split('\n'), line => line.Contains("publisher heartbeat"));
        AssertFieldInRange(heartbeat, "AverageWaitBlockedMs", 4.2, 4.4);
        AssertFieldInRange(heartbeat, "MaxWaitBlockedMs", 4.5, 4.7);
    }

    [Fact]
    public async Task TimingDiagnostics_PublishWorkAccumulatesAverageAndMax()
    {
        var source = new Snapshot(new ControllerState(new AuxiliaryButtonState([false, false])));
        var sink = new FakeSink(); var ticks = new ManualTicks();
        var fakeNow = 0L;
        var publisher = new CanonicalSteamDeckInputPublisher(source, sink, ticks, timestampProvider: () => fakeNow);
        publisher.Start();

        var msToTicks = Stopwatch.Frequency / 1000.0;
        publisher.RecordPublishWorkForTests(0, (long)(0.1 * msToTicks));
        publisher.RecordTimerWakeForTests(0);
        publisher.RecordPublishWorkForTests(0, (long)(0.3 * msToTicks));
        publisher.RecordTimerWakeForTests((long)(0.3 * msToTicks));

        fakeNow = Stopwatch.Frequency + 1;
        await ticks.TickAsync(); await sink.WaitForCountAsync(1);
        await publisher.StopAsync();

        AppLog.DrainForTests();
        var heartbeat = Assert.Single(LogFileTestHelper.ReadAllText(AppLog.CurrentLogFilePath).Split('\n'), line => line.Contains("publisher heartbeat"));
        AssertFieldInRange(heartbeat, "AveragePublishWorkMs", 0.15, 0.25);
        AssertFieldInRange(heartbeat, "MaxPublishWorkMs", 0.25, 0.35);
    }

    [Fact]
    public async Task TimingDiagnostics_WakeLatenessAccumulatesAverageAndMaxAndClampsNegativeToZero()
    {
        var source = new Snapshot(new ControllerState(new AuxiliaryButtonState([false, false])));
        var sink = new FakeSink(); var ticks = new ManualTicks();
        var fakeNow = 0L;
        var publisher = new CanonicalSteamDeckInputPublisher(source, sink, ticks, timestampProvider: () => fakeNow);
        publisher.Start();

        var msToTicks = Stopwatch.Frequency / 1000.0;
        publisher.RecordTimerWakeForTests((long)(4.3 * msToTicks));
        publisher.RecordWakeLatenessForTests((long)(4.3 * msToTicks), (long)(4.0 * msToTicks));
        publisher.RecordTimerWakeForTests((long)(8.29 * msToTicks));
        publisher.RecordWakeLatenessForTests((long)(8.29 * msToTicks), (long)(8.0 * msToTicks));
        // "Early" wake -- must clamp to zero, not go negative.
        publisher.RecordWakeLatenessForTests((long)(11.9 * msToTicks), (long)(12.0 * msToTicks));

        fakeNow = Stopwatch.Frequency + 1;
        await ticks.TickAsync(); await sink.WaitForCountAsync(1);
        await publisher.StopAsync();

        AppLog.DrainForTests();
        var heartbeat = Assert.Single(LogFileTestHelper.ReadAllText(AppLog.CurrentLogFilePath).Split('\n'), line => line.Contains("publisher heartbeat"));
        Assert.Contains("TimerWakeCount=2", heartbeat);
        AssertFieldInRange(heartbeat, "AverageWakeLatenessMs", 0.28, 0.31);
        AssertFieldInRange(heartbeat, "MaxWakeLatenessMs", 0.29, 0.31);
    }

    private static void AssertFieldInRange(string heartbeat, string field, double low, double high)
    {
        var text = heartbeat.Split(field + "=")[1].Split(' ')[0].TrimEnd('\r');
        var value = double.Parse(text, System.Globalization.CultureInfo.InvariantCulture);
        Assert.InRange(value, low, high);
    }

    [Fact]
    public async Task TimingDiagnostics_FirstTimerWakeDoesNotInventAWakeToWakeInterval()
    {
        var source = new Snapshot(new ControllerState(new AuxiliaryButtonState([false, false])));
        var sink = new FakeSink(); var ticks = new ManualTicks();
        var fakeNow = 0L;
        var publisher = new CanonicalSteamDeckInputPublisher(source, sink, ticks, timestampProvider: () => fakeNow);
        publisher.Start();

        // A single, first-ever timer wake: no previous wake exists, so no wake-to-wake sample should be
        // recorded (an average over zero samples must report 0, not a bogus/huge computed value).
        publisher.RecordTimerWakeForTests((long)(123 * (Stopwatch.Frequency / 1000.0)));

        fakeNow = Stopwatch.Frequency + 1;
        await ticks.TickAsync(); await sink.WaitForCountAsync(1);
        await publisher.StopAsync();

        AppLog.DrainForTests();
        var heartbeat = Assert.Single(LogFileTestHelper.ReadAllText(AppLog.CurrentLogFilePath).Split('\n'), line => line.Contains("publisher heartbeat"));
        Assert.Contains("TimerWakeCount=1", heartbeat);
        Assert.Contains("AverageWakeToWakeMs=0", heartbeat);
        Assert.Contains("MaxWakeToWakeMs=0", heartbeat);
    }

    [Fact]
    public async Task TimingDiagnostics_ResetAfterHeartbeatDoesNotCarryIntervalCountsOrSumsIntoTheNextWindow()
    {
        var source = new Snapshot(new ControllerState(new AuxiliaryButtonState([false, false])));
        var sink = new FakeSink(); var ticks = new ManualTicks();
        var fakeNow = 0L;
        var publisher = new CanonicalSteamDeckInputPublisher(source, sink, ticks, timestampProvider: () => fakeNow);
        publisher.Start();

        var msToTicks = Stopwatch.Frequency / 1000.0;
        publisher.RecordTimerWakeForTests(0);
        publisher.RecordTimerWakeForTests((long)(4 * msToTicks));
        publisher.RecordWaitBlockedForTests(0, (long)(4 * msToTicks));
        publisher.RecordPublishWorkForTests(0, (long)(1 * msToTicks));
        publisher.RecordWakeLatenessForTests((long)(4 * msToTicks), (long)(3.5 * msToTicks));

        fakeNow = Stopwatch.Frequency + 1;
        await ticks.TickAsync(); await sink.WaitForCountAsync(1); // first heartbeat fires here

        // A second window: only one more timer wake, no wait-blocked/publish-work samples at all this
        // time. If the previous window's sums/counts leaked through, this heartbeat would still show
        // stale non-zero wait-blocked/publish-work data or an inflated wake count.
        publisher.RecordTimerWakeForTests((long)(4 * msToTicks) + (long)(4 * msToTicks));
        fakeNow = 2 * Stopwatch.Frequency + 2;
        await ticks.TickAsync(); await sink.WaitForCountAsync(2);
        await publisher.StopAsync();

        AppLog.DrainForTests();
        var lines = LogFileTestHelper.ReadAllText(AppLog.CurrentLogFilePath).Split('\n').Where(line => line.Contains("publisher heartbeat")).ToArray();
        Assert.Equal(2, lines.Length);
        var secondHeartbeat = lines[1];
        // Only the carried-over wake-to-wake interval from the window boundary (4ms, from the last wake
        // of window 1 to the one wake of window 2) should show up -- not window 1's own first interval.
        Assert.Contains("TimerWakeCount=1", secondHeartbeat);
        Assert.Contains("AverageWaitBlockedMs=0", secondHeartbeat);
        Assert.Contains("MaxWaitBlockedMs=0", secondHeartbeat);
        Assert.Contains("AveragePublishWorkMs=0", secondHeartbeat);
        Assert.Contains("MaxPublishWorkMs=0", secondHeartbeat);
        Assert.Contains("AverageWakeLatenessMs=0", secondHeartbeat);
        Assert.Contains("MaxWakeLatenessMs=0", secondHeartbeat);
        Assert.Contains("SkippedDeadlineCount=0", secondHeartbeat);
    }

    [Fact]
    public async Task TimingDiagnostics_StopThenStartDoesNotCarryTheLastWakeTimestampAcrossTheGap()
    {
        // Unlike a heartbeat reset (which intentionally keeps _previousTimerWakeTimestamp so the
        // interval spanning the boundary is still a valid sample), a Stop-then-Start on the same
        // publisher instance can have an arbitrarily long real-world gap between them -- production
        // supports exactly this (a new routing session reusing the same publisher). Without a reset in
        // Start(), the first timer wake of the new run would be diffed against the last wake of the
        // previous run and misreported as e.g. a multi-second WakeToWake outlier.
        var source = new Snapshot(new ControllerState(new AuxiliaryButtonState([false, false])));
        var sink = new FakeSink(); var ticks = new ManualTicks();
        var fakeNow = 0L;
        var publisher = new CanonicalSteamDeckInputPublisher(source, sink, ticks, timestampProvider: () => fakeNow);
        publisher.Start();

        publisher.RecordTimerWakeForTests(0);
        fakeNow = Stopwatch.Frequency + 1;
        await ticks.TickAsync(); await sink.WaitForCountAsync(1); // heartbeat fires, leaves _hasPreviousTimerWake set
        await publisher.StopAsync();

        // Simulate a large real-world gap before the same instance is started again.
        fakeNow += 5 * Stopwatch.Frequency;
        publisher.Start();

        var secondRunWake = fakeNow;
        publisher.RecordTimerWakeForTests(secondRunWake);
        fakeNow = secondRunWake + Stopwatch.Frequency + 1;
        // ManualTicks.TickAsync() dequeues its waiter queue FIFO but a cancelled wait from the first
        // run's loop (cancelled by StopAsync, not dequeued) is still sitting at the front; one throwaway
        // tick flushes that stale entry so the next one actually reaches the restarted loop's new wait.
        await ticks.TickAsync();
        await ticks.TickAsync(); await sink.WaitForCountAsync(2);
        await publisher.StopAsync();

        AppLog.DrainForTests();
        var lines = LogFileTestHelper.ReadAllText(AppLog.CurrentLogFilePath).Split('\n').Where(line => line.Contains("publisher heartbeat")).ToArray();
        Assert.Equal(2, lines.Length);
        var afterRestart = lines[1];
        Assert.Contains("TimerWakeCount=1", afterRestart);
        Assert.Contains("AverageWakeToWakeMs=0", afterRestart);
        Assert.Contains("MaxWakeToWakeMs=0", afterRestart);
        Assert.Contains("WakeOver4_25MsCount=0", afterRestart);
        Assert.Contains("WakeOver5MsCount=0", afterRestart);
    }

    [Fact]
    public async Task Production_worker_timing_diagnostics_wire_up_end_to_end_with_the_real_timer()
    {
        // Real production path (dedicated worker + real high-resolution timer), with a fake clock that
        // forces the heartbeat to fire almost immediately in wall-clock time so this stays fast and
        // deterministic without asserting any specific Hz. For a clean run with no SetState failures,
        // every normal timer wake corresponds to exactly one successful publish, so TimerWakeCount must
        // equal PublishedStateCount. Note this does NOT by itself prove the stop event's wake is never
        // miscounted as a timer wake: StopAsync runs only after the heartbeat above is already read, and
        // WorkerLoop returns immediately on a stop wake without calling PublishCurrentStateOnce (the only
        // path that can trigger another heartbeat), so a hypothetical regression that counted the stop
        // wake would not surface as a logged heartbeat here for this assertion to catch. That guarantee
        // instead rests on WorkerLoop's `if (signaled == 0) return;` running before any counter is
        // touched -- a one-line, directly-readable invariant that doesn't need its own seam.
        var source = new Snapshot(new ControllerState(new AuxiliaryButtonState([false, false])));
        var sink = new FakeSink();
        var realElapsed = Stopwatch.StartNew();
        long TimestampProvider() => realElapsed.ElapsedMilliseconds >= 5 ? Stopwatch.Frequency + 1 : 0;
        var publisher = new CanonicalSteamDeckInputPublisher(source, sink, timestampProvider: TimestampProvider);

        publisher.Start();
        try
        {
            await sink.WaitForCountAsync(2, TimeSpan.FromSeconds(2));
            // Give a couple more real ticks a chance to land so at least one heartbeat has fired.
            await Task.Delay(50);
        }
        finally
        {
            await publisher.StopAsync();
        }

        AppLog.DrainForTests();
        var log = LogFileTestHelper.ReadAllText(AppLog.CurrentLogFilePath);
        var heartbeat = log.Split('\n').LastOrDefault(line => line.Contains("publisher heartbeat"));
        Assert.NotNull(heartbeat);
        var wakeCountText = heartbeat!.Split("TimerWakeCount=")[1].Split(' ')[0];
        var publishedText = heartbeat.Split("TotalPublishedStateCount=")[1].Split(' ')[0];
        Assert.Equal(int.Parse(publishedText, System.Globalization.CultureInfo.InvariantCulture), int.Parse(wakeCountText, System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task Production_stop_requested_while_SetState_is_blocked_exits_before_rearming()
    {
        // Under the earlier periodic-timer design this exercised a genuine WaitAny([timer, stopEvent])
        // race (a timer period could elapse -- and the timer re-signal itself -- while SetState was still
        // blocked, so both handles could be signaled by the time the worker returned to wait; stop, at
        // index 0, had to win that race). The one-shot timer is not waiting at all -- and so cannot become
        // signaled again -- from the moment it fires until WorkerLoop explicitly re-arms it after the
        // publish returns, so that specific both-signaled race can no longer occur here. What this test
        // verifies now: StopAsync signals the stop event while a slow SetState call is still in flight;
        // once SetState returns, the worker's stopEvent.WaitOne(0) check (run before computing the next
        // deadline or re-arming) sees stop requested and exits -- no re-arm, no second SetState call. The
        // WaitAny stop-priority ordering itself (stopEvent still at index 0) is unchanged and still
        // matters for an ordinary wait where the timer could legitimately also be signaled.
        var source = new Snapshot(new ControllerState(new AuxiliaryButtonState([false, false])));
        var firstCallBlocked = new ManualResetEventSlim(false);
        var releaseFirstCall = new ManualResetEventSlim(false);
        var sink = new BlockingFirstCallSink(firstCallBlocked, releaseFirstCall);
        var publisher = new CanonicalSteamDeckInputPublisher(source, sink);

        publisher.Start();
        try
        {
            Assert.True(firstCallBlocked.Wait(TimeSpan.FromSeconds(2)), "The first SetState call never started.");

            // Begin stopping while the worker is still blocked inside the first SetState. StopAsync's
            // stop-event Set() runs synchronously before its first await, so by the time this call
            // returns a Task, the stop event is already signaled.
            var stopTask = publisher.StopAsync();

            // Let some time elapse while still blocked, so the stop request is comfortably in place well
            // before SetState returns and the worker gets to its post-publish stop check.
            await Task.Delay(50);
            releaseFirstCall.Set();

            await stopTask.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            // No-op if the above already completed the stop; a safety net if an assertion failed first.
            await publisher.StopAsync();
        }

        Assert.Equal(1, sink.Count);
        Assert.False(publisher.IsRunning);
    }

    [Fact]
    public async Task Production_worker_recurs()
    {
        var source = new Snapshot(new ControllerState(new AuxiliaryButtonState([false, false])));
        var sink = new FakeSink();
        var publisher = new CanonicalSteamDeckInputPublisher(source, sink);

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
    public async Task Stop_prevents_new_SetState()
    {
        var source = new Snapshot(new ControllerState(new AuxiliaryButtonState([false, false])));
        var sink = new FakeSink();
        var publisher = new CanonicalSteamDeckInputPublisher(source, sink);
        publisher.Start();
        try
        {
            await sink.WaitForCountAsync(1, TimeSpan.FromSeconds(2));
        }
        finally
        {
            await publisher.StopAsync();
        }

        var countAtStop = sink.Count;
        await Task.Delay(100);

        Assert.Equal(countAtStop, sink.Count);
    }

    [Fact]
    public async Task Initial_arm_failure_is_synchronous_and_restartable()
    {
        var source = new Snapshot(new ControllerState(new AuxiliaryButtonState([false, false])));
        var sink = new FakeSink();
        var publisher = new CanonicalSteamDeckInputPublisher(source, sink)
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
    public async Task Runtime_rearm_failure_reports_fault()
    {
        var source = new Snapshot(new ControllerState(new AuxiliaryButtonState([false, false])));
        var sink = new FakeSink();
        var faults = 0;
        var faultObserved = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var publisher = new CanonicalSteamDeckInputPublisher(source, sink, fault: _ => { Interlocked.Increment(ref faults); faultObserved.TrySetResult(true); });
        var armCount = 0;
        publisher.ArmForDeadlineOverrideForTests = (timer, deadlineTicks, nowTicks) =>
        {
            var count = Interlocked.Increment(ref armCount);
            if (count == 1)
            {
                var remaining = deadlineTicks - nowTicks;
                var due100ns = CanonicalPublisherDeadlineMath.ConvertToRelativeDueTime100ns(remaining, Stopwatch.Frequency);
                timer.ArmRelative(TimeSpan.FromTicks(due100ns));
                return;
            }
            throw new InvalidOperationException("simulated SetWaitableTimerEx re-arm failure");
        };

        publisher.Start();
        try
        {
            await faultObserved.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            await publisher.StopAsync();
        }

        Assert.Equal(1, faults);
        Assert.False(publisher.IsRunning);
        Assert.Equal(1, sink.Count);
    }

    [Fact]
    public async Task Thread_start_failure_cleans_up()
    {
        var source = new Snapshot(new ControllerState(new AuxiliaryButtonState([false, false])));
        var sink = new FakeSink();
        var publisher = new CanonicalSteamDeckInputPublisher(source, sink)
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
    public async Task Join_timeout_fails_closed()
    {
        var source = new Snapshot(new ControllerState(new AuxiliaryButtonState([false, false])));
        var firstCallBlocked = new ManualResetEventSlim(false);
        var releaseFirstCall = new ManualResetEventSlim(false);
        var sink = new BlockingFirstCallSink(firstCallBlocked, releaseFirstCall);
        var publisher = new CanonicalSteamDeckInputPublisher(source, sink)
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

    [Fact]
    public async Task No_catchup_burst_after_long_SetState()
    {
        var source = new Snapshot(new ControllerState(new AuxiliaryButtonState([false, false])));
        var firstCallBlocked = new ManualResetEventSlim(false);
        var releaseFirstCall = new ManualResetEventSlim(false);
        var sink = new BlockingFirstCallSink(firstCallBlocked, releaseFirstCall);
        var publisher = new CanonicalSteamDeckInputPublisher(source, sink);

        publisher.Start();
        try
        {
            Assert.True(firstCallBlocked.Wait(TimeSpan.FromSeconds(2)), "The first SetState call never started.");
            await Task.Delay(100);
            releaseFirstCall.Set();
            await Task.Delay(100);
        }
        finally
        {
            await publisher.StopAsync();
        }

        Assert.True(sink.Count < 60, $"Expected no catch-up burst, but observed {sink.Count} SetState calls.");
    }

    [Fact]
    public async Task Worker_is_background_named_AboveNormal()
    {
        var source = new Snapshot(new ControllerState(new AuxiliaryButtonState([false, false])));
        var sink = new FakeSink();
        ThreadPriority? observedPriority = null;
        var publisher = new CanonicalSteamDeckInputPublisher(source, sink)
        {
            WorkerThreadStartOverrideForTests = thread =>
            {
                observedPriority = thread.Priority;
                Assert.Equal("SteamInputAddon.SteamDeckPublisher", thread.Name);
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

    private sealed class BlockingFirstCallSink(ManualResetEventSlim firstCallBlocked, ManualResetEventSlim releaseFirstCall) : ICanonicalSteamDeckStateSink
    {
        private int _count;
        private int _isFirstCall = 1;
        internal int Count => Volatile.Read(ref _count);
        public bool SetState(SteamDeckDeviceState state)
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

    /// <summary>Minimal deterministic <see cref="TimeProvider"/> stub shared with
    /// SteamDeckSystemButtonOverlayTests' scope -- kept local here to avoid a cross-file test dependency.</summary>
    private sealed class FakeTimeProvider : TimeProvider
    {
        private DateTimeOffset _now = DateTimeOffset.UnixEpoch;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan by) => _now += by;
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

    private sealed class FakeSink : ICanonicalSteamDeckStateSink
    {
        private readonly Lock _sync = new();
        private readonly List<SteamDeckDeviceState> _states = [];
        internal volatile bool Accept = true;
        internal volatile bool ThrowOnSet;
        internal IReadOnlyList<SteamDeckDeviceState> States { get { lock (_sync) return _states.ToArray(); } }
        internal int Count { get { lock (_sync) return _states.Count; } }
        public bool SetState(SteamDeckDeviceState state)
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
