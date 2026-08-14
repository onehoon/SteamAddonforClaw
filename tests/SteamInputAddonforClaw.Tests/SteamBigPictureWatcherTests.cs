using SteamInputAddonforClaw.Steam;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class SteamBigPictureWatcherTests
{
    [Fact]
    public void WindowEnumerationFailure_IsUnreliable()
    {
        var result = new SteamBigPictureWindowProbe(_ => false).Capture();
        Assert.False(result.IsReliable);
        Assert.False(result.IsActive);
    }

    [Fact]
    public void UnreliableProbe_ClearsPreviouslyActiveState()
    {
        var probe = new MutableProbe(new(true, true, "Active"));
        var hook = new FakeHook();
        using var watcher = new SteamBigPictureWatcher(probe, hook);
        watcher.Start();
        Assert.True(watcher.IsActive);

        probe.Result = new(false, false, "ProbeFailed");
        hook.Raise();

        Assert.False(watcher.IsActive);
    }

    [Fact]
    public void HookRegistrationFailure_DoesNotAuthorizeBigPicture()
    {
        var probe = new MutableProbe(new(true, true, "Active"));
        using var watcher = new SteamBigPictureWatcher(probe, new FakeHook { StartResult = false });
        watcher.Start();
        Assert.False(watcher.IsActive);
    }

    [Fact]
    public void Dispose_PreventsLaterCallbackRefresh()
    {
        var probe = new MutableProbe(new(false, true, "Inactive"));
        var hook = new FakeHook();
        using var watcher = new SteamBigPictureWatcher(probe, hook);
        watcher.Start();
        watcher.Dispose();
        probe.Result = new(true, true, "Active");
        hook.Raise();
        Assert.False(watcher.IsActive);
    }

    private sealed class MutableProbe(SteamBigPictureProbeResult result) : ISteamBigPictureWindowProbe
    {
        public SteamBigPictureProbeResult Result { get; set; } = result;
        public SteamBigPictureProbeResult Capture() => Result;
    }

    private sealed class FakeHook : ISteamBigPictureEventHook
    {
        private Action? _callback;
        public bool StartResult { get; init; } = true;
        public bool Start(Action callback) { _callback = callback; return StartResult; }
        public void Raise() => _callback?.Invoke();
        public void Dispose() => _callback = null;
    }
}
