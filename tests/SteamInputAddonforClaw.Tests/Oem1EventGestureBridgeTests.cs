using SteamInputAddonforClaw.CenterM;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class Oem1EventGestureBridgeTests
{
    [Fact]
    public void Authority_defaults_to_false()
    {
        using var fixture = Create();
        fixture.Source.Emit(Oem1());
        Assert.Empty(fixture.Results);
    }

    [Fact]
    public void Repeated_oem1_events_remain_ignored_until_authority_is_enabled()
    {
        using var fixture = Create();
        fixture.Source.Emit(Oem1());
        fixture.Source.Emit(Oem1());

        Assert.Empty(fixture.Results);
    }

    [Fact]
    public void Authority_enables_oem1_and_disabling_blocks_future_input()
    {
        using var fixture = Create();
        fixture.Bridge.SetCustomAuthority(true);
        fixture.Source.Emit(Oem1());
        fixture.Bridge.SetCustomAuthority(false);
        fixture.Source.Emit(Oem1());

        Assert.Equal([new(Oem1Gesture.Single)], fixture.Results);
    }

    [Fact]
    public void Repeated_authority_assignment_is_idempotent()
    {
        using var fixture = Create(doubleEnabled: true);
        fixture.Bridge.SetCustomAuthority(true);
        fixture.Bridge.SetCustomAuthority(true);
        fixture.Source.Emit(Oem1());

        Assert.Single(fixture.Delay.Pending);
    }

    [Fact]
    public void Oem1_is_forwarded_only_when_authority_is_active()
    {
        using var fixture = Create(doubleEnabled: true);
        fixture.Bridge.SetCustomAuthority(true);
        fixture.Source.Emit(Oem1());

        Assert.Single(fixture.Delay.Pending);
    }

    [Fact]
    public void Oem2_and_other_are_always_ignored()
    {
        using var fixture = Create();
        fixture.Bridge.SetCustomAuthority(true);
        fixture.Source.Emit(Oem2());
        fixture.Source.Emit(Other());

        Assert.Empty(fixture.Delay.Pending);
        Assert.Empty(fixture.Results);
    }

    [Fact]
    public void Other_does_not_cancel_a_pending_oem1_gesture()
    {
        using var fixture = Create(doubleEnabled: true);
        fixture.Bridge.SetCustomAuthority(true);
        fixture.Source.Emit(Oem1());
        fixture.Source.Emit(Other());

        Assert.Single(fixture.Delay.Pending);
    }

    [Fact]
    public async Task Oem2_between_oem1_presses_does_not_disturb_double()
    {
        using var fixture = Create(doubleEnabled: true);
        fixture.Bridge.SetCustomAuthority(true);
        fixture.Source.Emit(Oem1());
        fixture.Source.Emit(Oem2());
        fixture.Source.Emit(Oem1());
        await Task.Yield();

        Assert.Equal([new(Oem1Gesture.Double)], fixture.Results);
    }

    [Fact]
    public async Task Oem2_does_not_cancel_pending_single()
    {
        using var fixture = Create(doubleEnabled: true);
        fixture.Bridge.SetCustomAuthority(true);
        fixture.Source.Emit(Oem1());
        fixture.Source.Emit(Oem2());
        await fixture.Delay.CompleteLastAsync();

        Assert.Equal([new(Oem1Gesture.Single)], fixture.Results);
    }

    [Fact]
    public void Single_only_produces_one_policy_request()
    {
        using var fixture = Create();
        fixture.Bridge.SetCustomAuthority(true);
        fixture.Source.Emit(Oem1());

        Assert.Equal([new(Oem1Gesture.Single)], fixture.Results);
    }

    [Fact]
    public async Task Two_valid_oem1_presses_produce_only_double()
    {
        using var fixture = Create(doubleEnabled: true);
        fixture.Bridge.SetCustomAuthority(true);
        fixture.Source.Emit(Oem1());
        fixture.Source.Emit(Oem1());
        await Task.Yield();

        Assert.Equal([new(Oem1Gesture.Double)], fixture.Results);
    }

    [Fact]
    public async Task Successful_double_has_the_expected_policy_gesture()
    {
        using var fixture = Create(doubleEnabled: true);
        fixture.Bridge.SetCustomAuthority(true);
        fixture.Source.Emit(Oem1());
        fixture.Source.Emit(Oem1());
        await Task.Yield();

        Assert.Equal(Oem1Gesture.Double, Assert.Single(fixture.Results).Gesture);
    }

    [Fact]
    public async Task Authority_loss_resets_pending_single_and_stale_completion_is_ignored()
    {
        using var fixture = Create(doubleEnabled: true);
        fixture.Bridge.SetCustomAuthority(true);
        fixture.Source.Emit(Oem1());
        var stale = fixture.Delay.Pending.Last();
        fixture.Bridge.SetCustomAuthority(false);
        await stale.CompleteAsync(ignoreCancellation: true);

        Assert.Empty(fixture.Results);
    }

    [Fact]
    public async Task Authority_loss_does_not_emit_after_timeout_completion()
    {
        using var fixture = Create(doubleEnabled: true);
        fixture.Bridge.SetCustomAuthority(true);
        fixture.Source.Emit(Oem1());
        fixture.Bridge.SetCustomAuthority(false);
        await fixture.Delay.Pending.Last().CompleteAsync(ignoreCancellation: true);

        Assert.Empty(fixture.Results);
    }

    [Fact]
    public async Task Reactivation_does_not_admit_old_epoch_completion()
    {
        using var fixture = Create(doubleEnabled: true);
        fixture.Bridge.SetCustomAuthority(true);
        fixture.Source.Emit(Oem1());
        var stale = fixture.Delay.Pending.Last();
        fixture.Bridge.SetCustomAuthority(false);
        fixture.Bridge.SetCustomAuthority(true);
        await stale.CompleteAsync(ignoreCancellation: true);

        Assert.Empty(fixture.Results);
    }

    [Fact]
    public void Fresh_oem1_after_reactivation_works()
    {
        using var fixture = Create();
        fixture.Bridge.SetCustomAuthority(true);
        fixture.Bridge.SetCustomAuthority(false);
        fixture.Bridge.SetCustomAuthority(true);
        fixture.Source.Emit(Oem1());

        Assert.Equal([new(Oem1Gesture.Single)], fixture.Results);
    }

    [Fact]
    public void Fresh_oem1_after_reactivation_is_not_blocked_by_old_event_state()
    {
        using var fixture = Create();
        fixture.Bridge.SetCustomAuthority(true);
        fixture.Bridge.SetCustomAuthority(false);
        fixture.Bridge.SetCustomAuthority(true);
        fixture.Source.Emit(Oem1());

        Assert.Single(fixture.Results);
    }

    [Fact]
    public void Reentrant_policy_revocation_does_not_deadlock_or_revoke_before_delivery()
    {
        using var fixture = Create();
        fixture.Bridge.PolicyRequested += _ => fixture.Bridge.SetCustomAuthority(false);
        fixture.Bridge.SetCustomAuthority(true);
        fixture.Source.Emit(Oem1());

        Assert.Single(fixture.Results);
        fixture.Source.Emit(Oem1());
        Assert.Single(fixture.Results);
    }

    [Fact]
    public async Task Dispose_resets_pending_and_blocks_stale_delivery()
    {
        var fixture = Create(doubleEnabled: true);
        fixture.Bridge.SetCustomAuthority(true);
        fixture.Source.Emit(Oem1());
        var stale = fixture.Delay.Pending.Last();
        fixture.Bridge.Dispose();
        await stale.CompleteAsync(ignoreCancellation: true);
        fixture.Source.Emit(Oem1());

        Assert.Empty(fixture.Results);
        fixture.Bridge.Dispose();
    }

    [Fact]
    public void Dispose_does_not_dispose_the_injected_event_source_or_start_it()
    {
        var fixture = Create();
        fixture.Bridge.Dispose();

        Assert.False(fixture.Source.StartCalled);
        Assert.False(fixture.Source.Disposed);
    }

    [Fact]
    public void Dispose_removes_event_admission()
    {
        var fixture = Create();
        fixture.Bridge.SetCustomAuthority(true);
        fixture.Bridge.Dispose();
        fixture.Source.Emit(Oem1());

        Assert.Empty(fixture.Results);
    }

    [Fact]
    public void Throwing_policy_subscriber_does_not_escape_event_source_emit()
    {
        using var fixture = Create();
        fixture.Bridge.PolicyRequested += _ => throw new InvalidOperationException("policy bug");
        fixture.Bridge.SetCustomAuthority(true);

        var exception = Record.Exception(() => fixture.Source.Emit(Oem1()));

        Assert.Null(exception);
    }

    [Fact]
    public void Processing_continues_after_throwing_policy_subscriber()
    {
        using var fixture = Create();
        var throwOnce = true;
        fixture.Bridge.PolicyRequested += _ =>
        {
            if (throwOnce)
            {
                throwOnce = false;
                throw new InvalidOperationException("policy bug");
            }
        };
        fixture.Bridge.SetCustomAuthority(true);
        fixture.Source.Emit(Oem1());
        fixture.Source.Emit(Oem1());

        Assert.Equal(2, fixture.Results.Count);
    }

    [Fact]
    public void Policy_request_contains_only_the_gesture_semantic()
    {
        using var fixture = Create();
        fixture.Bridge.SetCustomAuthority(true);
        fixture.Source.Emit(Oem1());

        Assert.Equal(new Oem1GesturePolicyRequest(Oem1Gesture.Single), Assert.Single(fixture.Results));
    }

    [Fact]
    public async Task Concurrent_timeout_event_and_authority_revocation_complete_without_deadlock()
    {
        using var fixture = Create(doubleEnabled: true);
        fixture.Bridge.SetCustomAuthority(true);
        fixture.Source.Emit(Oem1());

        var timeout = fixture.Delay.CompleteLastAsync();
        var revocation = Task.Run(() => fixture.Bridge.SetCustomAuthority(false));
        await Task.WhenAll(timeout, revocation);

        Assert.InRange(fixture.Results.Count, 0, 1);
    }

    [Fact]
    public async Task Contested_timeout_and_event_ordering_completes_without_lock_inversion()
    {
        var eventOperationEntered = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseEventOperation = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var armEventBarrier = false;
        using var fixture = Create(
            doubleEnabled: true,
            recognizerOperationEntered: () =>
            {
                if (armEventBarrier)
                {
                    eventOperationEntered.TrySetResult(null);
                    releaseEventOperation.Task.GetAwaiter().GetResult();
                }
            });
        fixture.Bridge.SetCustomAuthority(true);
        fixture.Source.Emit(Oem1());
        fixture.Clock.Advance(TimeSpan.FromMilliseconds(251));
        armEventBarrier = true;

        var policyEntered = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Bridge.PolicyRequested += _ =>
        {
            policyEntered.TrySetResult(null);
            fixture.Bridge.SetCustomAuthority(false);
        };

        var eventTask = Task.Run(() => fixture.Source.Emit(Oem1()));
        await eventOperationEntered.Task;
        var timeout = fixture.Delay.CompleteLastAsync();
        await policyEntered.Task;
        releaseEventOperation.TrySetResult(null);
        await Task.WhenAll(timeout, eventTask);

        var resultCount = fixture.Results.Count;
        fixture.Source.Emit(Oem1());
        Assert.Equal(resultCount, fixture.Results.Count);
    }

    [Fact]
    public async Task Revocation_invalidation_does_not_wait_for_reentrant_policy_delivery()
    {
        var policyEntered = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var invalidationEntered = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var allowPolicyReentry = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var fixture = Create(
            doubleEnabled: true,
            authorityInvalidationEntered: () => invalidationEntered.TrySetResult(null));

        fixture.Bridge.PolicyRequested += _ =>
        {
            policyEntered.TrySetResult(null);
            allowPolicyReentry.Task.GetAwaiter().GetResult();
            fixture.Bridge.SetCustomAuthority(false);
        };
        fixture.Bridge.SetCustomAuthority(true);
        fixture.Source.Emit(Oem1());

        var timeoutTask = fixture.Delay.CompleteLastAsync();
        await policyEntered.Task;

        var revokeTask = Task.Run(() => fixture.Bridge.SetCustomAuthority(false));
        await invalidationEntered.Task;
        allowPolicyReentry.TrySetResult(null);

        await Task.WhenAll(timeoutTask, revokeTask);
        Assert.Empty(fixture.Results.Skip(1));
    }

    [Fact]
    public async Task Delivery_admitted_before_reactivation_cannot_cross_the_new_authority_epoch()
    {
        var deliveryEntered = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseDelivery = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var armDeliveryBarrier = false;
        using var fixture = Create(
            doubleEnabled: true,
            deliveryBeforeNotification: _ =>
            {
                if (!armDeliveryBarrier)
                    return;

                deliveryEntered.TrySetResult(null);
                releaseDelivery.Task.GetAwaiter().GetResult();
            });

        fixture.Bridge.SetCustomAuthority(true);
        fixture.Source.Emit(Oem1());
        armDeliveryBarrier = true;
        var timeoutTask = fixture.Delay.CompleteLastAsync();
        await deliveryEntered.Task;

        fixture.Bridge.SetCustomAuthority(false);
        fixture.Bridge.SetCustomAuthority(true);
        releaseDelivery.TrySetResult(null);
        await timeoutTask;

        Assert.Empty(fixture.Results);
    }

    [Fact]
    public void Bridge_does_not_query_or_depend_on_lifecycle_coordinator()
    {
        using var fixture = Create();

        Assert.NotNull(fixture.Bridge);
        Assert.False(fixture.Source.StartCalled);
    }

    private static MsiOemEvent Oem1() => new(41, CenterMOemCode.Oem1);
    private static MsiOemEvent Oem2() => new(88, CenterMOemCode.Oem2);
    private static MsiOemEvent Other() => new(99, CenterMOemCode.Other);

    private static Fixture Create(
        bool doubleEnabled = false,
        Action? recognizerOperationEntered = null,
        Action? authorityInvalidationEntered = null,
        Action<long>? deliveryBeforeNotification = null)
    {
        var source = new FakeMsiEventSource();
        var delay = new ControlledDelay();
        var clock = new ControlledClock();
        var recognizer = new Oem1GestureRecognizer(
            doubleEnabled,
            TimeSpan.FromMilliseconds(250),
            delay,
            clock,
            deliveryBeforeNotification: deliveryBeforeNotification);
        var bridge = new Oem1EventGestureBridge(
            source,
            recognizer,
            recognizerOperationEntered,
            authorityInvalidationEntered);
        var results = new List<Oem1GesturePolicyRequest>();
        bridge.PolicyRequested += results.Add;
        return new Fixture(source, delay, clock, bridge, results);
    }

    private sealed record Fixture(
        FakeMsiEventSource Source,
        ControlledDelay Delay,
        ControlledClock Clock,
        Oem1EventGestureBridge Bridge,
        List<Oem1GesturePolicyRequest> Results) : IDisposable
    {
        public void Dispose()
        {
            Bridge.Dispose();
        }

    }

    private sealed class FakeMsiEventSource : IMsiEventSource
    {
        internal bool StartCalled { get; private set; }
        internal bool Disposed { get; private set; }
        public event Action<MsiOemEvent>? EventReceived;
        public bool Start()
        {
            StartCalled = true;
            return true;
        }

        internal void Emit(MsiOemEvent value) => EventReceived?.Invoke(value);
        public void Dispose() => Disposed = true;
    }

    private sealed class ControlledDelay : IOem1GestureDelay
    {
        internal List<PendingDelay> Pending { get; } = [];

        public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
        {
            var pending = new PendingDelay(cancellationToken);
            Pending.Add(pending);
            return pending.Task;
        }

        internal Task CompleteLastAsync() => Pending.Last().CompleteAsync();
    }

    private sealed class ControlledClock : IOem1GestureClock
    {
        private long _timestamp;

        public long GetTimestamp() => _timestamp;
        public TimeSpan GetElapsedTime(long startTimestamp, long endTimestamp) =>
            TimeSpan.FromTicks(endTimestamp - startTimestamp);
        internal void Advance(TimeSpan elapsed) => _timestamp += elapsed.Ticks;
    }

    private sealed class PendingDelay(CancellationToken cancellationToken)
    {
        private readonly TaskCompletionSource<object?> _completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal Task Task => _completion.Task;

        internal async Task CompleteAsync(bool ignoreCancellation = false)
        {
            if (!ignoreCancellation && cancellationToken.IsCancellationRequested)
                _completion.TrySetCanceled(cancellationToken);
            else
                _completion.TrySetResult(null);

            await _completion.Task.ConfigureAwait(false);
            await Task.Yield();
        }
    }
}
