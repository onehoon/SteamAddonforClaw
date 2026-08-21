using SteamInputAddonforClaw.Contracts.Oem1;
using SteamInputAddonforClaw.GameBar;
using SteamInputAddonforClaw.Wing;
using SteamInputAddonforClaw.CenterM;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class WingRuntimeTests
{
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
}
