using SteamInputAddonforClaw.CenterM;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class CenterMOemEventTests
{
    [Theory]
    [InlineData(0x220029u, (int)CenterMOemCode.Oem1)]
    [InlineData(0x220058u, (int)CenterMOemCode.Oem2)]
    [InlineData(0x220099u, (int)CenterMOemCode.Other)]
    [InlineData(41u, (int)CenterMOemCode.Oem1)]
    [InlineData(88u, (int)CenterMOemCode.Oem2)]
    public void Classify_maps_raw_codes(uint rawCode, int expected) =>
        Assert.Equal((CenterMOemCode)expected, CenterMOemEventMapper.Classify(rawCode));

    [Fact]
    public void TryParseRawCode_valid_numeric_value_succeeds()
    {
        Assert.True(WmiMsiEventSource.TryParseRawCode(0x220029, out var rawCode));
        Assert.Equal(0x220029u, rawCode);
    }

    [Fact]
    public void TryParseRawCode_missing_property_is_not_parsed_as_any_code() =>
        Assert.False(WmiMsiEventSource.TryParseRawCode(null, out _));

    [Fact]
    public void TryParseRawCode_malformed_property_is_not_parsed_as_any_code() =>
        Assert.False(WmiMsiEventSource.TryParseRawCode("not-a-number", out _));

    [Fact]
    public void Start_failure_is_safe_and_does_not_throw()
    {
        var adapter = new FakeManagementEventWatcherAdapter(startSucceeds: false);
        using var source = new WmiMsiEventSource(adapter);

        var started = source.Start();

        Assert.False(started);
    }

    [Fact]
    public void Repeated_Start_is_refused_and_does_not_touch_the_adapter_again()
    {
        var adapter = new FakeManagementEventWatcherAdapter(startSucceeds: true);
        using var source = new WmiMsiEventSource(adapter);

        Assert.True(source.Start());
        var callsAfterFirstStart = adapter.TryStartCallCount;
        var secondStart = source.Start();

        Assert.False(secondStart);
        Assert.Equal(callsAfterFirstStart, adapter.TryStartCallCount);
    }

    [Fact]
    public void Start_after_Dispose_is_refused_and_never_touches_the_disposed_adapter()
    {
        var adapter = new FakeManagementEventWatcherAdapter(startSucceeds: true);
        var source = new WmiMsiEventSource(adapter);
        Assert.True(source.Start());
        source.Dispose();

        var result = source.Start();

        Assert.False(result);
        Assert.Equal(1, adapter.TryStartCallCount);
    }

    [Fact]
    public void Event_arriving_after_dispose_does_not_reenter_subscribers()
    {
        var adapter = new FakeManagementEventWatcherAdapter(startSucceeds: true);
        var source = new WmiMsiEventSource(adapter);
        var receivedCount = 0;
        source.EventReceived += _ => receivedCount++;
        Assert.True(source.Start());

        source.Dispose();
        adapter.Raise(0x220029);

        Assert.Equal(0, receivedCount);
    }

    [Fact]
    public void Event_received_before_dispose_classifies_and_forwards()
    {
        var adapter = new FakeManagementEventWatcherAdapter(startSucceeds: true);
        using var source = new WmiMsiEventSource(adapter);
        MsiOemEvent? received = null;
        source.EventReceived += evt => received = evt;
        Assert.True(source.Start());

        adapter.Raise(0x220029);

        Assert.NotNull(received);
        Assert.Equal(CenterMOemCode.Oem1, received.Value.Code);
    }

    [Fact]
    public async Task Start_racing_Dispose_never_throws_never_starts_after_disposal_never_delivers_after_disposal()
    {
        var adapter = new BlockingManagementEventWatcherAdapter();
        var source = new WmiMsiEventSource(adapter);

        var startTask = Task.Run(() => source.Start());
        // Wait until Start() is genuinely inside TryStart (holding the lock), not just scheduled.
        Assert.True(adapter.TryStartEntered.Wait(TimeSpan.FromSeconds(5)));

        // Dispose() must now block on the same lock until Start() finishes -- give it a moment to
        // actually attempt that (a Task that "hasn't run yet" would make this test meaningless).
        var disposeTask = Task.Run(source.Dispose);
        await Task.Delay(50);

        adapter.ReleaseTryStart();

        var started = await startTask.WaitAsync(TimeSpan.FromSeconds(5));
        await disposeTask.WaitAsync(TimeSpan.FromSeconds(5));

        // Started successfully (TryStart returned true) before Dispose could ever run -- exactly
        // one TryStart call, never a second one triggered by any interleaving.
        Assert.True(started);
        Assert.Equal(1, adapter.TryStartCallCount);

        var receivedCount = 0;
        source.EventReceived += _ => receivedCount++;
        adapter.Raise(0x220029);
        Assert.Equal(0, receivedCount);
    }

    [Fact]
    public async Task Dispose_waits_for_an_already_admitted_inflight_callback_to_drain_before_returning()
    {
        var adapter = new FakeManagementEventWatcherAdapter(startSucceeds: true);
        var source = new WmiMsiEventSource(adapter);
        Assert.True(source.Start());

        var callbackEntered = new ManualResetEventSlim(false);
        var releaseCallback = new ManualResetEventSlim(false);
        var deliveredCount = 0;
        source.EventReceived += _ =>
        {
            deliveredCount++;
            callbackEntered.Set();
            releaseCallback.Wait(TimeSpan.FromSeconds(10));
        };

        var raiseTask = Task.Run(() => adapter.Raise(0x220029));
        // Wait until the callback has genuinely been admitted and is inside subscriber invocation,
        // not just scheduled.
        Assert.True(callbackEntered.Wait(TimeSpan.FromSeconds(5)));

        var disposeTask = Task.Run(source.Dispose);
        // Dispose() has no timeout escape (regression: it previously gave up after 5 seconds and
        // returned anyway, letting the admitted callback complete after Dispose() had already
        // returned). Hold the callback blocked past that old boundary and prove Dispose() is
        // genuinely still draining, not merely "hasn't gotten around to returning yet".
        await Task.Delay(TimeSpan.FromSeconds(6));
        Assert.False(disposeTask.IsCompleted, "Dispose() must have no timeout escape -- it must still be draining the in-flight callback.");

        releaseCallback.Set();

        await raiseTask.WaitAsync(TimeSpan.FromSeconds(5));
        await disposeTask.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(1, deliveredCount);

        // No delivery for a notification arriving after Dispose() has fully returned.
        adapter.Raise(0x220058);
        Assert.Equal(1, deliveredCount);
    }

    private sealed class FakeManagementEventWatcherAdapter(bool startSucceeds) : IManagementEventWatcherAdapter
    {
        internal int TryStartCallCount { get; private set; }

        public event Action<object?>? MsiEventArrived;

        public bool TryStart(out Exception? error)
        {
            TryStartCallCount++;
            error = startSucceeds ? null : new InvalidOperationException("WMI unavailable in test.");
            return startSucceeds;
        }

        internal void Raise(object? rawPropertyValue) => MsiEventArrived?.Invoke(rawPropertyValue);

        public void Dispose() { }
    }

    /// <summary>Blocks inside TryStart until explicitly released, so a test can deterministically
    /// prove a concurrent Dispose() cannot interleave with an in-flight Start() -- it must wait for
    /// the lock instead.</summary>
    private sealed class BlockingManagementEventWatcherAdapter : IManagementEventWatcherAdapter
    {
        private readonly ManualResetEventSlim _release = new(false);
        internal readonly ManualResetEventSlim TryStartEntered = new(false);
        internal int TryStartCallCount { get; private set; }

        public event Action<object?>? MsiEventArrived;

        public bool TryStart(out Exception? error)
        {
            TryStartCallCount++;
            TryStartEntered.Set();
            _release.Wait(TimeSpan.FromSeconds(10));
            error = null;
            return true;
        }

        internal void ReleaseTryStart() => _release.Set();
        internal void Raise(object? rawPropertyValue) => MsiEventArrived?.Invoke(rawPropertyValue);

        public void Dispose() { }
    }
}
