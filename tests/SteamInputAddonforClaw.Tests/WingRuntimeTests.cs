using SteamInputAddonforClaw.Contracts.Oem1;
using SteamInputAddonforClaw.GameBar;
using SteamInputAddonforClaw.Wing;
using SteamInputAddonforClaw.CenterM;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class WingRuntimeTests
{
    [Fact]
    public void Unsupported_contract_action_is_not_silently_converted_to_none()
    {
        var mapping = WingMapping.From(new SteamInputAddonforClaw.Contracts.Wing.WingMappingSettings
        {
            Single = new() { Action = (SteamInputAddonforClaw.Contracts.Wing.WingAction)99 }
        });

        Assert.Equal((WingAction)99, mapping.Single.Action);
    }
    [Fact]
    public async Task WinG_authority_is_active_only_between_successful_arm_and_rollback()
    {
        var stage = new WinGProtectionRoutingStage(() => true, () => { });
        Assert.False(stage.CaptureAuthority().Active);
        await stage.ExecuteMutationAsync(CancellationToken.None);
        var active = stage.CaptureAuthority();
        Assert.True(active.Active);
        await stage.RollbackMutationAsync(CancellationToken.None);
        var inactive = stage.CaptureAuthority();
        Assert.False(inactive.Active);
        Assert.NotEqual(active.Epoch, inactive.Epoch);
    }

    [Fact]
    public void Default_wing_mapping_dispatches_immediate_steam_pulse()
    {
        var requests = 0;
        var dispatcher = new WingActionDispatcher(() => WingMapping.Default, () => { requests++; return true; });
        dispatcher.Dispatch(WingGesture.Single);
        Assert.Equal(1, requests);
    }

    [Fact]
    public void Wing_hotkey_rejects_win_g_before_execution()
    {
        var executed = 0;
        var mapping = new WingMapping(new(WingAction.KeyboardHotkey,
            new Oem1HotkeyBinding(Oem1HotkeyModifiers.Windows, Oem1HotkeyKey.G)), new());
        var dispatcher = new WingActionDispatcher(() => mapping, () => false, _ => executed++);
        dispatcher.Dispatch(WingGesture.Single);
        Assert.Equal(0, executed);
    }

    [Fact]
    public async Task Late_second_press_is_not_classified_as_double_even_when_timeout_is_delayed()
    {
        var time = new TestTimeProvider();
        var delay = new HeldDelay();
        var gestures = new List<WingGesture>();
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var recognizer = new WingGestureRecognizer(() => true, delay, time);
        recognizer.GestureRecognized += delivery => { gestures.Add(delivery.Gesture); if (gestures.Count == 2) completed.SetResult(); };
        recognizer.OnPress(1);
        time.Advance(TimeSpan.FromMilliseconds(201));
        recognizer.OnPress(1);
        Assert.Equal([WingGesture.Single], gestures);
        delay.Complete();
        await completed.Task;
        Assert.Equal([WingGesture.Single, WingGesture.Single], gestures);
    }

    [Fact]
    public void Mapping_capture_failure_is_contained()
    {
        var dispatcher = new WingActionDispatcher(() => throw new InvalidOperationException("mapping"), () => true);
        var exception = Record.Exception(() => dispatcher.Dispatch(WingGesture.Single));
        Assert.Null(exception);
    }

    [Fact]
    public void Route_a_pending_gesture_cannot_execute_after_route_b_starts()
    {
        var authority = new WingRouteAuthoritySnapshot(true, 7);
        var actions = 0;
        using var source = new FakeSource();
        var delay = new HeldDelay();
        using var recognizer = new WingGestureRecognizer(() => true, delay);
        using var bridge = new WingEventGestureBridge(source, recognizer, () => authority,
            new WingActionDispatcher(() => WingMapping.Default, () => { actions++; return true; }));

        source.Emit(new(88, CenterMOemCode.Oem2));
        authority = new(true, 8);
        source.Emit(new(88, CenterMOemCode.Oem2));
        Assert.Equal(0, actions);

        source.Emit(new(41, CenterMOemCode.Oem1));
        Assert.Equal(0, actions);
    }

    [Fact]
    public void Recognition_policy_failure_stays_inside_event_path()
    {
        var actions = 0;
        using var source = new FakeSource();
        using var recognizer = new WingGestureRecognizer(() => throw new InvalidOperationException("policy"));
        using var bridge = new WingEventGestureBridge(source, recognizer,
            () => new(true, 1), new WingActionDispatcher(() => WingMapping.Default, () => { actions++; return true; }));

        var exception = Record.Exception(() => source.Emit(new(88, CenterMOemCode.Oem2)));
        Assert.Null(exception);
        Assert.Equal(0, actions);
    }

    [Fact]
    public void Disposed_bridge_rejects_later_event()
    {
        var actions = 0;
        var source = new FakeSource();
        using var recognizer = new WingGestureRecognizer(() => false);
        var bridge = new WingEventGestureBridge(source, recognizer,
            () => new(true, 1), new WingActionDispatcher(() => WingMapping.Default, () => { actions++; return true; }));
        bridge.Dispose();
        source.Emit(new(88, CenterMOemCode.Oem2));
        Assert.Equal(0, actions);
    }

    private sealed class HeldDelay : IOem1GestureDelay
    {
        private TaskCompletionSource _completion = NewCompletion();
        public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken) => _completion.Task.WaitAsync(cancellationToken);
        public void Complete() { _completion.TrySetResult(); }
        private static TaskCompletionSource NewCompletion() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private sealed class TestTimeProvider : TimeProvider
    {
        private long _timestamp;
        public void Advance(TimeSpan amount) => _timestamp += (long)(amount.TotalSeconds * global::System.Diagnostics.Stopwatch.Frequency);
        public override long GetTimestamp() => _timestamp;
    }

    private sealed class FakeSource : IMsiEventSource
    {
        public event Action<MsiOemEvent>? EventReceived;
        public bool Start() => true;
        public void Emit(MsiOemEvent value) => EventReceived?.Invoke(value);
        public void Dispose() { }
    }
}
