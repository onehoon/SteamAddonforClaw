using SteamInputAddonforClaw.CenterM;
using SteamInputAddonforClaw.Routing;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class Oem1ActionDispatcherTests
{
    private static RoutingRuntimeStatusSnapshot StatusWithSteamOutput(bool active, bool available = true) =>
        new(Available: available, OperationalState: RoutingOperationalState.OverrideActive, SteamOutputActive: active, NativeDirectInputActive: false);

    private static Oem1ActionDispatcher CreateDispatcher(
        Func<RoutingRuntimeStatusSnapshot> captureRoutingStatus,
        Action? requestQuickAccessPulse = null,
        Action? launchBigPicture = null,
        Oem1ActionBindings? normalBindings = null,
        Oem1ActionBindings? routingBindings = null) =>
        new(
            normalBindings ?? Oem1ActionBindings.NormalDefault,
            routingBindings ?? Oem1ActionBindings.RoutingActiveDefault,
            captureRoutingStatus,
            requestQuickAccessPulse ?? (() => { }),
            launchBigPicture ?? (() => { }));

    // ---- Normal mapping independence (work order requirement) ----

    [Fact]
    public void Routing_runtime_unavailable_single_launches_big_picture_not_quick_access()
    {
        var pulseCount = 0;
        var bigPictureCount = 0;
        var dispatcher = CreateDispatcher(
            () => RoutingRuntimeStatusSnapshot.Unavailable,
            () => pulseCount++,
            () => bigPictureCount++);

        var ok = dispatcher.Dispatch(new Oem1GesturePolicyRequest(Oem1Gesture.Single));

        Assert.True(ok);
        Assert.Equal(1, bigPictureCount);
        Assert.Equal(0, pulseCount);
    }

    [Fact]
    public void Routing_feature_disabled_single_launches_big_picture_not_quick_access()
    {
        var pulseCount = 0;
        var bigPictureCount = 0;
        var dispatcher = CreateDispatcher(
            () => StatusWithSteamOutput(active: false, available: false),
            () => pulseCount++,
            () => bigPictureCount++);

        dispatcher.Dispatch(new Oem1GesturePolicyRequest(Oem1Gesture.Single));

        Assert.Equal(1, bigPictureCount);
        Assert.Equal(0, pulseCount);
    }

    [Fact]
    public void Routing_enabled_but_inactive_single_launches_big_picture_not_quick_access()
    {
        var pulseCount = 0;
        var bigPictureCount = 0;
        var dispatcher = CreateDispatcher(
            () => StatusWithSteamOutput(active: false, available: true),
            () => pulseCount++,
            () => bigPictureCount++);

        dispatcher.Dispatch(new Oem1GesturePolicyRequest(Oem1Gesture.Single));

        Assert.Equal(1, bigPictureCount);
        Assert.Equal(0, pulseCount);
    }

    [Fact]
    public void Actual_steam_deck_output_active_single_requests_quick_access_not_big_picture()
    {
        var pulseCount = 0;
        var bigPictureCount = 0;
        var dispatcher = CreateDispatcher(
            () => StatusWithSteamOutput(active: true),
            () => pulseCount++,
            () => bigPictureCount++);

        dispatcher.Dispatch(new Oem1GesturePolicyRequest(Oem1Gesture.Single));

        Assert.Equal(1, pulseCount);
        Assert.Equal(0, bigPictureCount);
    }

    [Fact]
    public void Route_stopping_returns_to_big_picture_on_the_very_next_press_without_reconfiguration()
    {
        var pulseCount = 0;
        var bigPictureCount = 0;
        var steamOutputActive = true;
        var dispatcher = CreateDispatcher(
            () => StatusWithSteamOutput(steamOutputActive),
            () => pulseCount++,
            () => bigPictureCount++);

        dispatcher.Dispatch(new Oem1GesturePolicyRequest(Oem1Gesture.Single));
        Assert.Equal(1, pulseCount);

        steamOutputActive = false;
        dispatcher.Dispatch(new Oem1GesturePolicyRequest(Oem1Gesture.Single));

        Assert.Equal(1, bigPictureCount);
        Assert.Equal(1, pulseCount);
    }

    // ---- Mapping semantics ----

    [Fact]
    public void Normal_default_double_is_none()
    {
        var pulseCount = 0;
        var bigPictureCount = 0;
        var dispatcher = CreateDispatcher(
            () => StatusWithSteamOutput(false),
            () => pulseCount++,
            () => bigPictureCount++);

        dispatcher.Dispatch(new Oem1GesturePolicyRequest(Oem1Gesture.Double));

        Assert.Equal(0, pulseCount);
        Assert.Equal(0, bigPictureCount);
    }

    [Fact]
    public void Routing_active_default_double_is_none()
    {
        var pulseCount = 0;
        var dispatcher = CreateDispatcher(
            () => StatusWithSteamOutput(true),
            () => pulseCount++);

        dispatcher.Dispatch(new Oem1GesturePolicyRequest(Oem1Gesture.Double));

        Assert.Equal(0, pulseCount);
    }

    [Fact]
    public void Two_binding_domains_are_independently_selectable()
    {
        var pulseCount = 0;
        var bigPictureCount = 0;
        var normal = new Oem1ActionBindings(Single: Oem1Action.None, Double: Oem1Action.SteamBigPicture);
        var routingActive = new Oem1ActionBindings(Single: Oem1Action.None, Double: Oem1Action.SteamQuickAccess);
        var dispatcher = CreateDispatcher(
            () => StatusWithSteamOutput(true),
            () => pulseCount++,
            () => bigPictureCount++,
            normalBindings: normal,
            routingBindings: routingActive);

        dispatcher.Dispatch(new Oem1GesturePolicyRequest(Oem1Gesture.Single));
        Assert.Equal(0, pulseCount);
        Assert.Equal(0, bigPictureCount);

        dispatcher.Dispatch(new Oem1GesturePolicyRequest(Oem1Gesture.Double));
        Assert.Equal(1, pulseCount);
    }

    [Fact]
    public void Routing_status_is_evaluated_fresh_at_dispatch_time()
    {
        var pulseCount = 0;
        var bigPictureCount = 0;
        var steamOutputActive = false;
        var dispatcher = CreateDispatcher(
            () => StatusWithSteamOutput(steamOutputActive),
            () => pulseCount++,
            () => bigPictureCount++);

        dispatcher.Dispatch(new Oem1GesturePolicyRequest(Oem1Gesture.Single));
        Assert.Equal(0, pulseCount);
        Assert.Equal(1, bigPictureCount);

        steamOutputActive = true;
        dispatcher.Dispatch(new Oem1GesturePolicyRequest(Oem1Gesture.Single));
        Assert.Equal(1, pulseCount);
        Assert.Equal(1, bigPictureCount);
    }

    [Fact]
    public void Bridge_policy_request_reaches_dispatcher_and_launches_big_picture_when_inactive()
    {
        var bigPictureCount = 0;
        var dispatcher = CreateDispatcher(
            () => StatusWithSteamOutput(false),
            launchBigPicture: () => bigPictureCount++);
        var source = new ImmediateMsiEventSource();
        var recognizer = new Oem1GestureRecognizer(
            doubleClickEnabled: false,
            TimeSpan.FromMilliseconds(250),
            new ImmediateDelay(),
            new ZeroClock());
        using var bridge = new Oem1EventGestureBridge(source, recognizer);
        bridge.PolicyRequested += request => dispatcher.Dispatch(request);
        bridge.SetCustomAuthority(true);

        source.Emit(new MsiOemEvent(41, CenterMOemCode.Oem1));

        Assert.Equal(1, bigPictureCount);
    }

    // ---- Failure ----

    [Fact]
    public void Big_picture_backend_exception_causes_dispatch_to_report_failure()
    {
        var dispatcher = CreateDispatcher(
            () => StatusWithSteamOutput(false),
            launchBigPicture: () => throw new InvalidOperationException("steam launch failed"));

        var ok = dispatcher.Dispatch(new Oem1GesturePolicyRequest(Oem1Gesture.Single));

        Assert.False(ok);
    }

    [Fact]
    public void Quick_access_pulse_exception_causes_dispatch_to_report_failure()
    {
        var dispatcher = CreateDispatcher(
            () => StatusWithSteamOutput(true),
            requestQuickAccessPulse: () => throw new InvalidOperationException("pulse failed"));

        var ok = dispatcher.Dispatch(new Oem1GesturePolicyRequest(Oem1Gesture.Single));

        Assert.False(ok);
    }

    [Fact]
    public void Routing_status_capture_exception_causes_dispatch_to_report_failure()
    {
        // Review fix (BLOCKER): status capture and domain resolution must share the same failure
        // boundary as action execution -- if this throws, it must still be treated as an OEM1
        // replacement-action failure (fail-open), not escape Dispatch uncaught (which would leave
        // Oem1EventGestureBridge merely logging a subscriber failure without ever reaching
        // OnOem1ActionFailed, so suppression could remain armed with no action ever selected).
        var dispatcher = CreateDispatcher(() => throw new InvalidOperationException("status unavailable"));

        var ok = dispatcher.Dispatch(new Oem1GesturePolicyRequest(Oem1Gesture.Single));

        Assert.False(ok);
    }

    [Fact]
    public void Routing_unavailable_is_not_a_dispatch_failure()
    {
        var dispatcher = CreateDispatcher(() => RoutingRuntimeStatusSnapshot.Unavailable);

        var ok = dispatcher.Dispatch(new Oem1GesturePolicyRequest(Oem1Gesture.Single));

        Assert.True(ok);
    }

    private sealed class ImmediateMsiEventSource : IMsiEventSource
    {
        public event Action<MsiOemEvent>? EventReceived;
        public bool Start() => true;
        internal void Emit(MsiOemEvent value) => EventReceived?.Invoke(value);
        public void Dispose() { }
    }

    private sealed class ImmediateDelay : IOem1GestureDelay
    {
        public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class ZeroClock : IOem1GestureClock
    {
        public long GetTimestamp() => 0;
        public TimeSpan GetElapsedTime(long startTimestamp, long endTimestamp) => TimeSpan.Zero;
    }
}
