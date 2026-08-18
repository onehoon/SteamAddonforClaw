using SteamInputAddonforClaw.CenterM;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class Oem1GestureRecognizerTests
{
    [Fact]
    public void Disabled_emits_single_immediately_without_scheduling()
    {
        var delay = new ControlledDelay();
        using var recognizer = Create(false, delay);
        var results = Collect(recognizer);

        recognizer.OnPress();

        Assert.Equal([Oem1Gesture.Single], results);
        Assert.Empty(delay.ActivePending);
    }

    [Fact]
    public void Disabled_repeated_presses_each_emit_one_single()
    {
        using var recognizer = Create(false);
        var results = Collect(recognizer);

        recognizer.OnPress();
        recognizer.OnPress();

        Assert.Equal([Oem1Gesture.Single, Oem1Gesture.Single], results);
    }

    [Fact]
    public void First_press_waits_when_double_is_enabled()
    {
        var delay = new ControlledDelay();
        using var recognizer = Create(true, delay);
        var results = Collect(recognizer);

        recognizer.OnPress();

        Assert.Empty(results);
        Assert.NotEmpty(delay.Pending);
    }

    [Fact]
    public async Task Timeout_emits_one_single()
    {
        var delay = new ControlledDelay();
        using var recognizer = Create(true, delay);
        var results = Collect(recognizer);

        recognizer.OnPress();
        await delay.CompleteNextAsync();

        Assert.Equal([Oem1Gesture.Single], results);
    }

    [Fact]
    public void Second_press_emits_double_and_cancels_pending_single()
    {
        var delay = new ControlledDelay();
        using var recognizer = Create(true, delay);
        var results = Collect(recognizer);

        recognizer.OnPress();
        recognizer.OnPress();

        Assert.Equal([Oem1Gesture.Double], results);
        Assert.Empty(delay.ActivePending);
    }

    [Fact]
    public async Task Stale_timeout_after_double_emits_nothing()
    {
        var delay = new ControlledDelay { IgnoreCancellation = true };
        using var recognizer = Create(true, delay);
        var results = Collect(recognizer);

        recognizer.OnPress();
        var stale = delay.Pending.Single();
        recognizer.OnPress();
        await stale.CompleteAsync();

        Assert.Equal([Oem1Gesture.Double], results);
    }

    [Fact]
    public async Task Press_after_single_starts_a_fresh_sequence()
    {
        var delay = new ControlledDelay();
        using var recognizer = Create(true, delay);
        var results = Collect(recognizer);

        recognizer.OnPress();
        await delay.CompleteNextAsync();
        recognizer.OnPress();

        Assert.Equal([Oem1Gesture.Single], results);
        Assert.NotEmpty(delay.Pending);
    }

    [Fact]
    public async Task Press_after_double_starts_a_fresh_sequence()
    {
        var delay = new ControlledDelay();
        using var recognizer = Create(true, delay);
        var results = Collect(recognizer);

        recognizer.OnPress();
        recognizer.OnPress();
        recognizer.OnPress();
        await delay.CompleteNextAsync();

        Assert.Equal([Oem1Gesture.Double, Oem1Gesture.Single], results);
    }

    [Fact]
    public async Task Reset_cancels_pending_press_and_stale_completion_is_ignored()
    {
        var delay = new ControlledDelay { IgnoreCancellation = true };
        using var recognizer = Create(true, delay);
        var results = Collect(recognizer);

        recognizer.OnPress();
        var stale = delay.Pending.Single();
        recognizer.Reset();
        await stale.CompleteAsync();

        Assert.Empty(results);
    }

    [Fact]
    public async Task New_press_after_reset_works_normally()
    {
        var delay = new ControlledDelay();
        using var recognizer = Create(true, delay);
        var results = Collect(recognizer);

        recognizer.OnPress();
        recognizer.Reset();
        recognizer.OnPress();
        await delay.CompleteNextAsync();

        Assert.Equal([Oem1Gesture.Single], results);
    }

    [Fact]
    public async Task Reset_after_double_is_idle_and_has_no_side_effect()
    {
        var delay = new ControlledDelay();
        using var recognizer = Create(true, delay);
        var results = Collect(recognizer);

        recognizer.OnPress();
        recognizer.Reset();
        recognizer.Reset();
        await Task.CompletedTask;

        Assert.Empty(results);
    }

    [Fact]
    public async Task Three_presses_produce_double_then_single_sequence()
    {
        var delay = new ControlledDelay();
        using var recognizer = Create(true, delay);
        var results = Collect(recognizer);

        recognizer.OnPress();
        recognizer.OnPress();
        recognizer.OnPress();
        await delay.CompleteNextAsync();

        Assert.Equal([Oem1Gesture.Double, Oem1Gesture.Single], results);
    }

    [Fact]
    public async Task Completing_the_same_timeout_twice_emits_only_once()
    {
        var delay = new ControlledDelay();
        using var recognizer = Create(true, delay);
        var results = Collect(recognizer);

        recognizer.OnPress();
        var pending = delay.Pending.Single();
        await pending.CompleteAsync();
        await pending.CompleteAsync();

        Assert.Equal([Oem1Gesture.Single], results);
    }

    [Fact]
    public void Negative_enabled_window_is_rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Create(true, new ControlledDelay(), TimeSpan.FromMilliseconds(-1)));
    }

    [Fact]
    public async Task Timeout_wins_before_second_press_and_second_press_starts_new_sequence()
    {
        var delay = new ControlledDelay();
        using var recognizer = Create(true, delay);
        var results = Collect(recognizer);

        recognizer.OnPress();
        await delay.CompleteNextAsync();
        recognizer.OnPress();

        Assert.Equal([Oem1Gesture.Single], results);
        Assert.NotEmpty(delay.Pending);
    }

    [Fact]
    public async Task Stale_timeout_cannot_affect_newer_sequence()
    {
        var delay = new ControlledDelay { IgnoreCancellation = true };
        using var recognizer = Create(true, delay);
        var results = Collect(recognizer);

        recognizer.OnPress();
        var stale = delay.Pending.Single();
        recognizer.Reset();
        recognizer.OnPress();
        var current = delay.Pending.Last();
        await stale.CompleteAsync();
        Assert.Empty(results);
        await current.CompleteAsync();

        Assert.Equal([Oem1Gesture.Single], results);
    }

    [Fact]
    public async Task Press_after_window_expiry_emits_single_and_starts_a_new_sequence()
    {
        var delay = new ControlledDelay();
        var clock = new ControlledClock();
        using var recognizer = Create(true, delay, clock: clock);
        var results = Collect(recognizer);

        recognizer.OnPress();
        clock.Advance(TimeSpan.FromMilliseconds(251));
        recognizer.OnPress();
        await delay.CompleteNextAsync();

        Assert.Equal([Oem1Gesture.Single, Oem1Gesture.Single], results);
    }

    [Fact]
    public async Task Reset_reentrant_from_delivery_is_a_synchronous_boundary()
    {
        var delay = new ControlledDelay();
        using var recognizer = Create(true, delay);
        var results = new List<Oem1Gesture>();
        recognizer.GestureRecognized += gesture =>
        {
            results.Add(gesture);
            recognizer.Reset();
        };

        recognizer.OnPress();
        await delay.CompleteNextAsync();
        recognizer.OnPress();

        Assert.Equal([Oem1Gesture.Single], results);
    }

    [Fact]
    public async Task Successful_timeout_disposes_its_cancellation_source_once()
    {
        var delay = new ControlledDelay();
        var cancellation = new TrackingCancellationSourceFactory();
        using var recognizer = Create(true, delay, cancellationSourceFactory: cancellation);

        recognizer.OnPress();
        await delay.CompleteNextAsync();

        var source = Assert.Single(cancellation.Created);
        Assert.Equal(1, source.DisposeCount);
    }

    [Fact]
    public void Invalid_enabled_window_is_rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Create(true, new ControlledDelay(), TimeSpan.Zero));
    }

    [Fact]
    public async Task Disposed_recognizer_ignores_stale_completion_and_rejects_new_press()
    {
        var delay = new ControlledDelay { IgnoreCancellation = true };
        var recognizer = Create(true, delay);
        var results = Collect(recognizer);
        recognizer.OnPress();
        var stale = delay.Pending.Single();
        recognizer.Dispose();
        await stale.CompleteAsync();

        Assert.Empty(results);
        Assert.Throws<ObjectDisposedException>(recognizer.OnPress);
    }

    private static Oem1GestureRecognizer Create(
        bool enabled,
        IOem1GestureDelay? delay = null,
        TimeSpan? window = null,
        IOem1GestureClock? clock = null,
        IOem1GestureCancellationSourceFactory? cancellationSourceFactory = null) =>
        new(enabled, window ?? TimeSpan.FromMilliseconds(250), delay, clock, cancellationSourceFactory);

    private static List<Oem1Gesture> Collect(Oem1GestureRecognizer recognizer)
    {
        var results = new List<Oem1Gesture>();
        recognizer.GestureRecognized += results.Add;
        return results;
    }

    private sealed class ControlledDelay : IOem1GestureDelay
    {
        internal bool IgnoreCancellation { get; init; }
        internal List<PendingDelay> Pending { get; } = [];
        internal IEnumerable<PendingDelay> ActivePending => Pending.Where(p => !p.IsCompleted);

        public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
        {
            var pending = new PendingDelay(cancellationToken, IgnoreCancellation);
            Pending.Add(pending);
            return pending.Task;
        }

        internal async Task CompleteNextAsync() => await Pending.Last().CompleteAsync();
    }

    private sealed class ControlledClock : IOem1GestureClock
    {
        private long _timestamp;

        public long GetTimestamp() => _timestamp;

        public TimeSpan GetElapsedTime(long startTimestamp, long endTimestamp) =>
            TimeSpan.FromTicks(endTimestamp - startTimestamp);

        internal void Advance(TimeSpan elapsed) => _timestamp += elapsed.Ticks;
    }

    private sealed class TrackingCancellationSourceFactory : IOem1GestureCancellationSourceFactory
    {
        internal List<TrackingCancellationSource> Created { get; } = [];

        public IOem1GestureCancellationSource Create()
        {
            var source = new TrackingCancellationSource();
            Created.Add(source);
            return source;
        }
    }

    private sealed class TrackingCancellationSource : IOem1GestureCancellationSource
    {
        private readonly CancellationTokenSource _source = new();

        internal int DisposeCount { get; private set; }
        public CancellationToken Token => _source.Token;
        public void Cancel() => _source.Cancel();

        public void Dispose()
        {
            DisposeCount++;
            _source.Dispose();
        }
    }

    private sealed class PendingDelay
    {
        private readonly TaskCompletionSource<object?> _completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly CancellationTokenRegistration _registration;
        private readonly CancellationToken _cancellationToken;
        private readonly bool _ignoreCancellation;

        internal PendingDelay(CancellationToken cancellationToken, bool ignoreCancellation)
        {
            _cancellationToken = cancellationToken;
            _ignoreCancellation = ignoreCancellation;
            if (!ignoreCancellation)
                _registration = cancellationToken.Register(() => _completion.TrySetCanceled(cancellationToken));
        }

        internal Task Task => _completion.Task;
        internal bool IsCompleted => _completion.Task.IsCompleted;

        internal async Task CompleteAsync()
        {
            if (!_ignoreCancellation && _cancellationToken.IsCancellationRequested)
                _completion.TrySetCanceled(_cancellationToken);
            else
                _completion.TrySetResult(null);

            await _completion.Task.ConfigureAwait(false);
            await Task.Yield();
        }
    }
}
