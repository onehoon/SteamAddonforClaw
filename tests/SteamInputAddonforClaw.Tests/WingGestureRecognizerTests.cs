using SteamInputAddonforClaw.CenterM;
using SteamInputAddonforClaw.Wing;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

/// <summary>The low-level WING recognizer keeps dormant double-click support; production wires it
/// with double disabled so one press delivers immediately (App UI PR-C section 22.7).</summary>
public sealed class WingGestureRecognizerTests
{
    [Fact]
    public void Double_disabled_delivers_a_single_immediately()
    {
        var gestures = new List<WingGesture>();
        using var recognizer = new WingGestureRecognizer(() => false);
        recognizer.GestureRecognized += d => gestures.Add(d.Gesture);

        recognizer.OnPress(1);

        Assert.Equal([WingGesture.Single], gestures);
    }

    [Fact]
    public async Task Late_second_press_is_not_classified_as_double_even_when_the_timeout_is_delayed()
    {
        var time = new TestTimeProvider();
        var delay = new HeldDelay();
        var gestures = new List<WingGesture>();
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var recognizer = new WingGestureRecognizer(() => true, delay, time);
        recognizer.GestureRecognized += d => { gestures.Add(d.Gesture); if (gestures.Count == 2) completed.SetResult(); };

        recognizer.OnPress(1);
        time.Advance(TimeSpan.FromMilliseconds(201));
        recognizer.OnPress(1);
        Assert.Equal([WingGesture.Single], gestures);

        delay.Complete();
        await completed.Task;
        Assert.Equal([WingGesture.Single, WingGesture.Single], gestures);
    }

    private sealed class HeldDelay : IOem1GestureDelay
    {
        private readonly TaskCompletionSource _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken) => _completion.Task.WaitAsync(cancellationToken);
        public void Complete() => _completion.TrySetResult();
    }

    private sealed class TestTimeProvider : TimeProvider
    {
        private long _timestamp;
        public void Advance(TimeSpan amount) => _timestamp += (long)(amount.TotalSeconds * global::System.Diagnostics.Stopwatch.Frequency);
        public override long GetTimestamp() => _timestamp;
    }
}
