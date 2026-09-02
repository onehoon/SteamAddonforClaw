using SteamInputAddonforClaw.Devices.MSI.Claw;
using SteamInputAddonforClaw.FrontendTransport;
using SteamInputAddonforClaw.Input;
using SteamInputAddonforClaw.Input.DirectInput;
using SteamInputAddonforClaw.Lifecycle;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class OverlayControllerInputRouterTests
{
    private static GamepadButtons Buttons(bool up = false, bool down = false, bool left = false, bool right = false, bool a = false, bool b = false) =>
        new(a, b, false, false, up, right, down, left, false, false, false, false, false, false, false, false);

    private static ControllerState State(GamepadButtons buttons) => new(buttons, default, default, default, default);

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
        public async Task<IReadOnlyList<OverlayNavigationAction>> WaitForAsync(int count)
        {
            for (var i = 0; i < count; i++)
                Assert.True(await _signal.WaitAsync(TimeSpan.FromSeconds(5)));
            lock (_actions) return _actions.ToArray();
        }
        public IReadOnlyList<OverlayNavigationAction> Snapshot() { lock (_actions) return _actions.ToArray(); }
    }

    private sealed class FakeSource : IMsiClawPreparedInputSource
    {
        public ControllerState LatestState { get; set; }
        public bool IsRunning => true;
        public event EventHandler<ControllerState>? StateChanged;
        public void Raise(GamepadButtons buttons) { LatestState = State(buttons); StateChanged?.Invoke(this, LatestState); }
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

        var noisy = new GamepadButtons(false, false, X: true, Y: true, false, false, false, false, LeftBumper: true, RightBumper: true, Back: true, Start: true, true, true, true, true);
        source.Raise(noisy);
        await Task.Delay(100);
        Assert.Empty(delivery.Snapshot());
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
}
