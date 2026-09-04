using SteamInputAddonforClaw.Devices.MSI.Claw;
using SteamInputAddonforClaw.FrontendTransport;
using SteamInputAddonforClaw.Input;
using SteamInputAddonforClaw.Input.DirectInput;
using SteamInputAddonforClaw.Lifecycle;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class OverlayControllerInputRouterTests
{
    private static GamepadButtons Buttons(
        bool up = false, bool down = false, bool left = false, bool right = false,
        bool a = false, bool b = false, bool lb = false, bool rb = false) =>
        new(a, b, false, false, up, right, down, left, lb, rb, false, false, false, false, false, false);

    private static ControllerState State(GamepadButtons buttons) => new(buttons, default, default, default, default);

    private static StickState Stick(int x, int y) => new((short)x, (short)y);

    private static ControllerState Sticks(StickState left = default, StickState right = default) =>
        new(default, left, right, default, default);

    private sealed class Delivery
    {
        private readonly List<OverlayNavigationAction> _actions = [];
        private readonly SemaphoreSlim _signal = new(0);
        public Func<OverlayNavigationAction, Task> Callback => action =>
        {
            lock (_actions) _actions.Add(action);
            _signal.Release();
            return Task.CompletedTask;
        };
        // Cumulative: waits until at least <paramref name="count"/> actions have been delivered in
        // total, so a test may call it more than once as it drives successive edges.
        public async Task<IReadOnlyList<OverlayNavigationAction>> WaitForAsync(int count)
        {
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
            while (true)
            {
                lock (_actions)
                    if (_actions.Count >= count) return _actions.ToArray();
                var remaining = deadline - DateTime.UtcNow;
                Assert.True(remaining > TimeSpan.Zero && await _signal.WaitAsync(remaining));
            }
        }
        public IReadOnlyList<OverlayNavigationAction> Snapshot() { lock (_actions) return _actions.ToArray(); }
    }

    private sealed class FakeSource : IMsiClawPreparedInputSource
    {
        public ControllerState LatestState { get; set; }
        public bool IsRunning => true;
        public event EventHandler<ControllerState>? StateChanged;
        public void Raise(GamepadButtons buttons) { LatestState = State(buttons); StateChanged?.Invoke(this, LatestState); }
        public void RaiseState(ControllerState state) { LatestState = state; StateChanged?.Invoke(this, state); }
        public void ResetLatestStateToNeutral() => LatestState = default;
        public MsiClawInputStartResult StartPrepared(DirectInputDeviceDescriptor descriptor) => new(MsiClawInputStartStatus.Started, "");
        public Task<bool> WaitForFirstValidStateAsync(CancellationToken cancellationToken) => Task.FromResult(true);
        public Task StopAsync() => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    [Fact]
    public async Task Controls_already_held_when_capture_starts_emit_no_action()
    {
        var source = new FakeSource { LatestState = State(Buttons(up: true, a: true, b: true)) };
        var delivery = new Delivery();
        using var router = new OverlayControllerInputRouter(source, delivery.Callback);
        router.Start();

        source.Raise(Buttons(up: true, a: true, b: true)); // same state, no edge
        await Task.Delay(100);
        Assert.Empty(delivery.Snapshot());
    }

    [Fact]
    public async Task Each_dpad_rising_edge_emits_the_matching_navigation_action()
    {
        var source = new FakeSource();
        var delivery = new Delivery();
        using var router = new OverlayControllerInputRouter(source, delivery.Callback);
        router.Start();

        source.Raise(Buttons(up: true));
        source.Raise(Buttons());
        source.Raise(Buttons(down: true));
        source.Raise(Buttons());
        source.Raise(Buttons(left: true));
        source.Raise(Buttons());
        source.Raise(Buttons(right: true));

        var actions = await delivery.WaitForAsync(4);
        Assert.Equal(
            new[] { OverlayNavigationAction.NavigateUp, OverlayNavigationAction.NavigateDown, OverlayNavigationAction.NavigateLeft, OverlayNavigationAction.NavigateRight },
            actions);
    }

    [Fact]
    public async Task A_rising_edge_maps_to_accept_and_B_rising_edge_maps_to_back()
    {
        var source = new FakeSource();
        var delivery = new Delivery();
        using var router = new OverlayControllerInputRouter(source, delivery.Callback);
        router.Start();

        source.Raise(Buttons(a: true));
        source.Raise(Buttons());
        source.Raise(Buttons(b: true));

        var actions = await delivery.WaitForAsync(2);
        Assert.Equal(new[] { OverlayNavigationAction.Accept, OverlayNavigationAction.Back }, actions);
    }

    [Fact]
    public async Task Release_edges_emit_no_navigation()
    {
        var source = new FakeSource();
        var delivery = new Delivery();
        using var router = new OverlayControllerInputRouter(source, delivery.Callback);
        router.Start();

        source.Raise(Buttons(a: true));
        await delivery.WaitForAsync(1);
        source.Raise(Buttons()); // release
        await Task.Delay(100);
        Assert.Single(delivery.Snapshot());
    }

    [Fact]
    public async Task Sticks_triggers_and_unmapped_buttons_emit_nothing()
    {
        var source = new FakeSource();
        var delivery = new Delivery();
        using var router = new OverlayControllerInputRouter(source, delivery.Callback);
        router.Start();

        // X/Y, Back/Start, stick clicks and LT/RT-full are all unmapped; LB/RB are excluded here
        // (they now map to PreviousTab/NextTab and are covered by dedicated bumper tests).
        var noisy = new GamepadButtons(false, false, X: true, Y: true, false, false, false, false, false, false, Back: true, Start: true, true, true, true, true);
        source.Raise(noisy);
        await Task.Delay(100);
        Assert.Empty(delivery.Snapshot());
    }

    [Fact]
    public async Task Bumper_rising_edges_map_to_previous_and_next_tab()
    {
        var source = new FakeSource();
        var delivery = new Delivery();
        using var router = new OverlayControllerInputRouter(source, delivery.Callback);
        router.Start();

        source.Raise(Buttons(lb: true));
        source.Raise(Buttons());
        source.Raise(Buttons(rb: true));

        var actions = await delivery.WaitForAsync(2);
        Assert.Equal(new[] { OverlayNavigationAction.PreviousTab, OverlayNavigationAction.NextTab }, actions);
    }

    [Fact]
    public async Task Held_bumper_emits_once_and_does_not_repeat()
    {
        var source = new FakeSource();
        var delivery = new Delivery();
        using var router = new OverlayControllerInputRouter(source, delivery.Callback);
        router.Start();

        source.Raise(Buttons(rb: true));
        source.Raise(Buttons(rb: true)); // still held
        source.Raise(Buttons(rb: true));
        await delivery.WaitForAsync(1);
        await Task.Delay(100);
        Assert.Equal(new[] { OverlayNavigationAction.NextTab }, delivery.Snapshot());
    }

    [Fact]
    public async Task Bumper_already_held_at_start_emits_nothing_until_released_and_pressed_again()
    {
        var source = new FakeSource { LatestState = State(Buttons(rb: true)) };
        var delivery = new Delivery();
        using var router = new OverlayControllerInputRouter(source, delivery.Callback);
        router.Start();

        source.Raise(Buttons(rb: true)); // same state, no edge
        await Task.Delay(100);
        Assert.Empty(delivery.Snapshot());

        source.Raise(Buttons());        // release
        source.Raise(Buttons(rb: true)); // fresh press
        var actions = await delivery.WaitForAsync(1);
        Assert.Equal(new[] { OverlayNavigationAction.NextTab }, actions);
    }

    [Fact]
    public async Task Release_waiter_waits_while_a_bumper_is_held_then_completes_on_release()
    {
        var source = new FakeSource();
        using var router = new OverlayControllerInputRouter(source, _ => Task.CompletedTask);
        router.Start();
        source.Raise(Buttons(lb: true)); // LB held

        var wait = router.WaitForConsumedControlsReleaseAsync(CancellationToken.None);
        Assert.False(wait.IsCompleted);

        source.Raise(Buttons()); // release
        Assert.Equal(OverlayConsumedControlsReleaseOutcome.ReleasedAfterWait, await wait.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task Stop_accepting_navigation_prevents_later_actions()
    {
        var source = new FakeSource();
        var delivery = new Delivery();
        using var router = new OverlayControllerInputRouter(source, delivery.Callback);
        router.Start();

        source.Raise(Buttons(a: true));
        await delivery.WaitForAsync(1);
        router.StopAcceptingNavigation();
        source.Raise(Buttons());
        source.Raise(Buttons(down: true));
        await Task.Delay(100);
        Assert.Single(delivery.Snapshot());
    }

    [Fact]
    public async Task Release_waiter_completes_immediately_when_no_consumed_control_is_held()
    {
        var source = new FakeSource();
        using var router = new OverlayControllerInputRouter(source, _ => Task.CompletedTask);
        router.Start();
        source.Raise(Buttons()); // neutral

        var outcome = await router.WaitForConsumedControlsReleaseAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(OverlayConsumedControlsReleaseOutcome.AlreadyReleased, outcome);
    }

    [Fact]
    public async Task Release_waiter_waits_while_a_consumed_control_is_held_then_completes_on_release()
    {
        var source = new FakeSource();
        using var router = new OverlayControllerInputRouter(source, _ => Task.CompletedTask);
        router.Start();
        source.Raise(Buttons(b: true)); // B held

        var wait = router.WaitForConsumedControlsReleaseAsync(CancellationToken.None);
        Assert.False(wait.IsCompleted);

        source.Raise(Buttons()); // release
        Assert.Equal(OverlayConsumedControlsReleaseOutcome.ReleasedAfterWait, await wait.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task Notify_source_unavailable_releases_the_waiter_as_unavailable()
    {
        var source = new FakeSource();
        using var router = new OverlayControllerInputRouter(source, _ => Task.CompletedTask);
        router.Start();
        source.Raise(Buttons(a: true)); // A held

        var wait = router.WaitForConsumedControlsReleaseAsync(CancellationToken.None);
        Assert.False(wait.IsCompleted);

        router.NotifySourceUnavailable();
        Assert.Equal(OverlayConsumedControlsReleaseOutcome.SourceUnavailable, await wait.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task Notify_source_unavailable_stops_further_navigation()
    {
        var source = new FakeSource();
        var delivery = new Delivery();
        using var router = new OverlayControllerInputRouter(source, delivery.Callback);
        router.Start();

        router.NotifySourceUnavailable();
        source.Raise(Buttons(a: true));
        await Task.Delay(100);
        Assert.Empty(delivery.Snapshot());
    }

    // ---- OQ5-UI-03: dual-stick directional navigation --------------------------------------------

    private const int Active = 20_000;

    [Fact]
    public async Task Left_stick_deflections_map_to_the_four_navigate_actions()
    {
        var source = new FakeSource();
        var delivery = new Delivery();
        using var router = new OverlayControllerInputRouter(source, delivery.Callback);
        router.Start();

        source.RaiseState(Sticks(left: Stick(0, Active)));   // up
        source.RaiseState(Sticks(left: Stick(0, 0)));
        source.RaiseState(Sticks(left: Stick(0, -Active)));  // down
        source.RaiseState(Sticks(left: Stick(0, 0)));
        source.RaiseState(Sticks(left: Stick(-Active, 0)));  // left
        source.RaiseState(Sticks(left: Stick(0, 0)));
        source.RaiseState(Sticks(left: Stick(Active, 0)));   // right

        var actions = await delivery.WaitForAsync(4);
        Assert.Equal(
            new[] { OverlayNavigationAction.NavigateUp, OverlayNavigationAction.NavigateDown, OverlayNavigationAction.NavigateLeft, OverlayNavigationAction.NavigateRight },
            actions);
    }

    [Fact]
    public async Task Right_stick_deflections_map_to_the_four_navigate_actions()
    {
        var source = new FakeSource();
        var delivery = new Delivery();
        using var router = new OverlayControllerInputRouter(source, delivery.Callback);
        router.Start();

        source.RaiseState(Sticks(right: Stick(0, Active)));
        source.RaiseState(Sticks(right: Stick(0, 0)));
        source.RaiseState(Sticks(right: Stick(0, -Active)));
        source.RaiseState(Sticks(right: Stick(0, 0)));
        source.RaiseState(Sticks(right: Stick(-Active, 0)));
        source.RaiseState(Sticks(right: Stick(0, 0)));
        source.RaiseState(Sticks(right: Stick(Active, 0)));

        var actions = await delivery.WaitForAsync(4);
        Assert.Equal(
            new[] { OverlayNavigationAction.NavigateUp, OverlayNavigationAction.NavigateDown, OverlayNavigationAction.NavigateLeft, OverlayNavigationAction.NavigateRight },
            actions);
    }

    [Fact]
    public async Task Held_stick_emits_once_then_re_arms_only_after_returning_to_neutral()
    {
        var source = new FakeSource();
        var delivery = new Delivery();
        using var router = new OverlayControllerInputRouter(source, delivery.Callback);
        router.Start();

        source.RaiseState(Sticks(left: Stick(Active, 0)));
        source.RaiseState(Sticks(left: Stick(Active, 0)));  // still held
        source.RaiseState(Sticks(left: Stick(30_000, 0)));  // farther, same direction
        await delivery.WaitForAsync(1);
        await Task.Delay(100);
        Assert.Equal(new[] { OverlayNavigationAction.NavigateRight }, delivery.Snapshot());

        source.RaiseState(Sticks(left: Stick(0, 0)));       // neutral -> re-arm
        source.RaiseState(Sticks(left: Stick(Active, 0)));  // fresh deflection
        var actions = await delivery.WaitForAsync(2);
        Assert.Equal(new[] { OverlayNavigationAction.NavigateRight, OverlayNavigationAction.NavigateRight }, actions);
    }

    [Fact]
    public async Task Stick_deflected_at_capture_start_emits_nothing_until_neutral_then_deflected()
    {
        var source = new FakeSource { LatestState = Sticks(left: Stick(0, Active)) };
        var delivery = new Delivery();
        using var router = new OverlayControllerInputRouter(source, delivery.Callback);
        router.Start();

        source.RaiseState(Sticks(left: Stick(0, Active)));  // same deflection, still disarmed
        await Task.Delay(100);
        Assert.Empty(delivery.Snapshot());

        source.RaiseState(Sticks(left: Stick(0, 0)));       // neutral -> arm
        source.RaiseState(Sticks(left: Stick(0, Active)));  // fresh
        var actions = await delivery.WaitForAsync(1);
        Assert.Equal(new[] { OverlayNavigationAction.NavigateUp }, actions);
    }

    [Fact]
    public async Task Right_stick_deflected_at_capture_start_is_independent_of_left_stick_arming()
    {
        var source = new FakeSource { LatestState = Sticks(right: Stick(Active, 0)) };
        var delivery = new Delivery();
        using var router = new OverlayControllerInputRouter(source, delivery.Callback);
        router.Start();

        // Left stick is armed from a neutral start and fires immediately; right stick started
        // deflected and stays silent until it returns to neutral.
        source.RaiseState(new(default, Stick(0, Active), Stick(Active, 0), default, default));
        var first = await delivery.WaitForAsync(1);
        Assert.Equal(new[] { OverlayNavigationAction.NavigateUp }, first);

        await Task.Delay(100);
        Assert.Single(delivery.Snapshot());

        source.RaiseState(Sticks(right: Stick(0, 0)));
        source.RaiseState(Sticks(right: Stick(Active, 0)));
        var actions = await delivery.WaitForAsync(2);
        Assert.Equal(new[] { OverlayNavigationAction.NavigateUp, OverlayNavigationAction.NavigateRight }, actions);
    }

    [Fact]
    public async Task Movement_into_the_hysteresis_band_does_not_re_arm_the_stick()
    {
        var source = new FakeSource();
        var delivery = new Delivery();
        using var router = new OverlayControllerInputRouter(source, delivery.Callback);
        router.Start();

        source.RaiseState(Sticks(left: Stick(Active, 0)));   // fire NavigateRight, disarm
        await delivery.WaitForAsync(1);
        source.RaiseState(Sticks(left: Stick(12_000, 0)));   // between neutral (8k) and activation (16k)
        source.RaiseState(Sticks(left: Stick(Active, 0)));   // back to active -- still disarmed
        await Task.Delay(100);
        Assert.Single(delivery.Snapshot());

        source.RaiseState(Sticks(left: Stick(0, 0)));        // real neutral -> re-arm
        source.RaiseState(Sticks(left: Stick(Active, 0)));
        var actions = await delivery.WaitForAsync(2);
        Assert.Equal(2, actions.Count);
    }

    [Fact]
    public async Task Diagonal_deflection_emits_only_the_dominant_axis_action()
    {
        var source = new FakeSource();
        var delivery = new Delivery();
        using var router = new OverlayControllerInputRouter(source, delivery.Callback);
        router.Start();

        source.RaiseState(Sticks(left: Stick(18_000, 24_000)));   // Y dominant -> Up only
        source.RaiseState(Sticks(left: Stick(0, 0)));
        source.RaiseState(Sticks(left: Stick(-24_000, 18_000))); // X dominant -> Left only
        source.RaiseState(Sticks(left: Stick(0, 0)));
        source.RaiseState(Sticks(left: Stick(20_000, 20_000)));  // tie -> vertical (Up)

        var actions = await delivery.WaitForAsync(3);
        Assert.Equal(
            new[] { OverlayNavigationAction.NavigateUp, OverlayNavigationAction.NavigateLeft, OverlayNavigationAction.NavigateUp },
            actions);
    }

    [Fact]
    public async Task Release_waiter_waits_while_left_stick_is_deflected_then_completes_on_neutral()
    {
        var source = new FakeSource();
        using var router = new OverlayControllerInputRouter(source, _ => Task.CompletedTask);
        router.Start();
        source.RaiseState(Sticks(left: Stick(0, Active)));

        var wait = router.WaitForConsumedControlsReleaseAsync(CancellationToken.None);
        Assert.False(wait.IsCompleted);

        source.RaiseState(Sticks(left: Stick(0, 0)));
        Assert.Equal(OverlayConsumedControlsReleaseOutcome.ReleasedAfterWait, await wait.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task Release_waiter_waits_while_right_stick_is_deflected_then_completes_on_neutral()
    {
        var source = new FakeSource();
        using var router = new OverlayControllerInputRouter(source, _ => Task.CompletedTask);
        router.Start();
        source.RaiseState(Sticks(right: Stick(Active, 0)));

        var wait = router.WaitForConsumedControlsReleaseAsync(CancellationToken.None);
        Assert.False(wait.IsCompleted);

        source.RaiseState(Sticks(right: Stick(0, 0)));
        Assert.Equal(OverlayConsumedControlsReleaseOutcome.ReleasedAfterWait, await wait.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task Release_waiter_waits_until_both_a_bumper_and_a_stick_are_released()
    {
        var source = new FakeSource();
        using var router = new OverlayControllerInputRouter(source, _ => Task.CompletedTask);
        router.Start();
        source.RaiseState(new(Buttons(rb: true), default, Stick(Active, 0), default, default));

        var wait = router.WaitForConsumedControlsReleaseAsync(CancellationToken.None);
        Assert.False(wait.IsCompleted);

        source.RaiseState(new(Buttons(), default, Stick(Active, 0), default, default)); // RB released, stick still held
        await Task.Delay(100);
        Assert.False(wait.IsCompleted);

        source.RaiseState(Sticks(right: Stick(0, 0)));
        Assert.Equal(OverlayConsumedControlsReleaseOutcome.ReleasedAfterWait, await wait.WaitAsync(TimeSpan.FromSeconds(5)));
    }
}
