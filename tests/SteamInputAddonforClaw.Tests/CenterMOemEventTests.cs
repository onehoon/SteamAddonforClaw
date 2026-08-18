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

    [Fact]
    public async Task Dispose_called_reentrantly_from_within_EventReceived_does_not_deadlock()
    {
        var adapter = new FakeManagementEventWatcherAdapter(startSucceeds: true);
        var source = new WmiMsiEventSource(adapter);
        Assert.True(source.Start());

        var deliveredCount = 0;
        source.EventReceived += _ =>
        {
            deliveredCount++;
            // A subscriber that synchronously triggers its own teardown -- this must not deadlock
            // against the drain barrier that guards the ordinary (non-reentrant) Dispose() path.
            source.Dispose();
        };

        var raiseTask = Task.Run(() => adapter.Raise(0x220029));
        await raiseTask.WaitAsync(TimeSpan.FromSeconds(5)); // times out (test fails) if it deadlocks

        Assert.Equal(1, deliveredCount);

        // No delivery for a notification arriving after the reentrant Dispose() completed.
        adapter.Raise(0x220058);
        Assert.Equal(1, deliveredCount);
    }

    [Fact]
    public async Task Cross_instance_reentrancy_does_not_make_an_unrelated_source_skip_its_own_drain()
    {
        // Source A's admitted callback synchronously calls sourceB.Dispose() while B has its own
        // admitted callback blocked in flight on a different thread. B must not mistake "some
        // callback is on this thread" for "my own callback is on this thread" and must still block
        // until its own admitted callback actually drains.
        var adapterA = new FakeManagementEventWatcherAdapter(startSucceeds: true);
        var sourceA = new WmiMsiEventSource(adapterA);
        var adapterB = new FakeManagementEventWatcherAdapter(startSucceeds: true);
        var sourceB = new WmiMsiEventSource(adapterB);
        Assert.True(sourceA.Start());
        Assert.True(sourceB.Start());

        var bCallbackEntered = new ManualResetEventSlim(false);
        var releaseBCallback = new ManualResetEventSlim(false);
        sourceB.EventReceived += _ =>
        {
            bCallbackEntered.Set();
            releaseBCallback.Wait(TimeSpan.FromSeconds(10));
        };

        var bRaiseTask = Task.Run(() => adapterB.Raise(0x220029));
        Assert.True(bCallbackEntered.Wait(TimeSpan.FromSeconds(5)));

        var disposeBTask = Task.Run(() => { });
        sourceA.EventReceived += _ =>
        {
            disposeBTask = Task.Run(sourceB.Dispose);
            // Give the (wrongly reentrant-detected) Dispose() a chance to return early if the bug
            // were still present -- it must not.
            Assert.False(disposeBTask.Wait(TimeSpan.FromSeconds(1)),
                "B.Dispose() must not return before B's own in-flight callback drains, even though " +
                "it was called from inside A's callback on the same thread.");
        };
        adapterA.Raise(0x220058);

        releaseBCallback.Set();
        await bRaiseTask.WaitAsync(TimeSpan.FromSeconds(5));
        await disposeBTask.WaitAsync(TimeSpan.FromSeconds(5));

        sourceA.Dispose();
    }

    [Fact]
    public async Task Nested_same_instance_callback_then_outer_Dispose_does_not_deadlock_and_disposes_exactly_once()
    {
        // An outer admitted callback synchronously triggers a nested raise on the *same* instance;
        // the nested callback completes and unwinds first. Only then does the still-active outer
        // callback call Dispose(). The depth-aware check must still recognize the outer callback as
        // "this instance's own callback on this thread" even though the nested one already popped.
        var adapter = new FakeManagementEventWatcherAdapter(startSucceeds: true);
        var source = new WmiMsiEventSource(adapter);
        Assert.True(source.Start());

        var deliveredCount = 0;
        var nestedRaised = false;
        source.EventReceived += evt =>
        {
            deliveredCount++;
            if (!nestedRaised && evt.Code == CenterMOemCode.Oem1)
            {
                nestedRaised = true;
                adapter.Raise(0x220058); // nested, same instance, same thread -- runs and unwinds here
                source.Dispose(); // now called reentrantly from the still-active outer callback
            }
        };

        var raiseTask = Task.Run(() => adapter.Raise(0x220029));
        await raiseTask.WaitAsync(TimeSpan.FromSeconds(5)); // times out (test fails) if it deadlocks

        Assert.Equal(2, deliveredCount);
        Assert.Equal(1, adapter.DisposeCallCount);

        adapter.Raise(0x220029);
        Assert.Equal(2, deliveredCount);
    }

    [Fact]
    public async Task Adapter_dispose_happens_exactly_once_under_concurrent_reentrant_and_external_teardown()
    {
        var adapter = new FakeManagementEventWatcherAdapter(startSucceeds: true);
        var source = new WmiMsiEventSource(adapter);
        Assert.True(source.Start());

        var callbackEntered = new ManualResetEventSlim(false);
        var releaseCallback = new ManualResetEventSlim(false);
        source.EventReceived += _ =>
        {
            callbackEntered.Set();
            releaseCallback.Wait(TimeSpan.FromSeconds(10));
            source.Dispose(); // reentrant
        };

        var raiseTask = Task.Run(() => adapter.Raise(0x220029));
        Assert.True(callbackEntered.Wait(TimeSpan.FromSeconds(5)));

        // A concurrent external Dispose() races the reentrant one; it must block on the real drain
        // (it is not itself running inside the admitted callback) rather than double-disposing.
        var externalDisposeTask = Task.Run(source.Dispose);
        await Task.Delay(50);
        Assert.False(externalDisposeTask.IsCompleted);

        releaseCallback.Set();

        await raiseTask.WaitAsync(TimeSpan.FromSeconds(5));
        await externalDisposeTask.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(1, adapter.DisposeCallCount);
    }

    [Fact]
    public async Task External_Dispose_arriving_after_a_reentrant_Dispose_has_already_returned_still_waits_for_the_callback_to_drain()
    {
        // The reentrant call must return immediately (it cannot wait on itself), but a *different*,
        // non-reentrant caller that arrives afterward -- while the admitted callback is still
        // running -- must still block until that callback (and deferred disposal) actually
        // completes. Marking `_disposed` on the first call must not let a later external caller
        // skip the drain it is contractually owed.
        var adapter = new FakeManagementEventWatcherAdapter(startSucceeds: true);
        var source = new WmiMsiEventSource(adapter);
        Assert.True(source.Start());

        var reentrantDisposeReturned = new ManualResetEventSlim(false);
        var releaseCallback = new ManualResetEventSlim(false);
        source.EventReceived += _ =>
        {
            source.Dispose(); // reentrant -- must return immediately without waiting on itself
            reentrantDisposeReturned.Set();
            releaseCallback.Wait(TimeSpan.FromSeconds(10)); // callback still "in flight" here
        };

        var raiseTask = Task.Run(() => adapter.Raise(0x220029));
        Assert.True(reentrantDisposeReturned.Wait(TimeSpan.FromSeconds(5)));

        var externalDisposeTask = Task.Run(source.Dispose);
        await Task.Delay(TimeSpan.FromSeconds(1));
        Assert.False(externalDisposeTask.IsCompleted,
            "External Dispose() called after a reentrant Dispose() already returned must still " +
            "block until the still-executing admitted callback drains and disposal completes.");

        releaseCallback.Set();

        await raiseTask.WaitAsync(TimeSpan.FromSeconds(5));
        await externalDisposeTask.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(1, adapter.DisposeCallCount);

        adapter.Raise(0x220058);
    }

    private sealed class FakeManagementEventWatcherAdapter(bool startSucceeds) : IManagementEventWatcherAdapter
    {
        internal int TryStartCallCount { get; private set; }
        internal int DisposeCallCount { get; private set; }

        public event Action<object?>? MsiEventArrived;

        public bool TryStart(out Exception? error)
        {
            TryStartCallCount++;
            error = startSucceeds ? null : new InvalidOperationException("WMI unavailable in test.");
            return startSucceeds;
        }

        internal void Raise(object? rawPropertyValue) => MsiEventArrived?.Invoke(rawPropertyValue);

        public void Dispose() => DisposeCallCount++;
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
