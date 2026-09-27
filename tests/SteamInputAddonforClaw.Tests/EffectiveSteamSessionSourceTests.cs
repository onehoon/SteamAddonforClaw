using SteamInputAddonforClaw.Steam;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class EffectiveSteamSessionSourceTests
{
    [Fact]
    public async Task ActualTransition_PublishesRoutingBeforeLaterProfileSubscriberRuns()
    {
        var actual = new FakeRunningAppIdSource(0);
        using var watcher = new SteamSessionWatcher(actual);
        using var bigPicture = new SteamBigPictureWatcher(new FakeBigPictureProbe(false), new FakeBigPictureEventHook());
        using var effective = new EffectiveSteamSessionSource(watcher, bigPicture);
        watcher.Start();
        bigPicture.Start();
        using var routingPublished = new ManualResetEventSlim();
        using var releaseProfile = new ManualResetEventSlim();
        var profileSawRoutingPublished = false;
        effective.StateChanged += (_, _) => routingPublished.Set();
        watcher.StateChanged += (_, _) =>
        {
            profileSawRoutingPublished = routingPublished.IsSet;
            releaseProfile.Wait();
        };

        var transition = Task.Run(() => actual.SetRunningAppId(123));
        Assert.True(routingPublished.Wait(TimeSpan.FromSeconds(5)));
        releaseProfile.Set();
        await transition;
        Assert.True(profileSawRoutingPublished);
    }

    [Fact]
    public void WithActualGame_ReportsActualSession()
    {
        var actual = new FakeRunningAppIdSource(123);
        using var watcher = new SteamSessionWatcher(actual);
        using var bigPicture = new SteamBigPictureWatcher(new FakeBigPictureProbe(false), new FakeBigPictureEventHook());
        using var effective = new EffectiveSteamSessionSource(watcher, bigPicture);
        watcher.Start(); bigPicture.Start(); effective.Refresh();

        Assert.True(effective.State.IsActive);
        Assert.Equal(SteamSessionSource.Actual, effective.State.Source);
    }

    [Fact]
    public void WithBigPictureOnly_ReportsBigPictureSession()
    {
        var actual = new FakeRunningAppIdSource(0);
        using var watcher = new SteamSessionWatcher(actual);
        using var bigPicture = new SteamBigPictureWatcher(new FakeBigPictureProbe(true), new FakeBigPictureEventHook());
        using var effective = new EffectiveSteamSessionSource(watcher, bigPicture);
        watcher.Start(); bigPicture.Start();

        Assert.True(effective.State.IsActive);
        Assert.Equal(SteamSessionSource.BigPicture, effective.State.Source);
    }

    [Fact]
    public void WithNeitherGameNorBigPicture_ReportsInactive()
    {
        var actual = new FakeRunningAppIdSource(0);
        using var watcher = new SteamSessionWatcher(actual);
        using var bigPicture = new SteamBigPictureWatcher(new FakeBigPictureProbe(false), new FakeBigPictureEventHook());
        using var effective = new EffectiveSteamSessionSource(watcher, bigPicture);
        watcher.Start(); bigPicture.Start();

        Assert.False(effective.State.IsActive);
    }

    [Fact]
    public void ActualGame_HasPriorityOverBigPicture()
    {
        var actual = new FakeRunningAppIdSource(123);
        using var watcher = new SteamSessionWatcher(actual);
        using var bigPicture = new SteamBigPictureWatcher(new FakeBigPictureProbe(true), new FakeBigPictureEventHook());
        using var effective = new EffectiveSteamSessionSource(watcher, bigPicture);
        watcher.Start(); bigPicture.Start(); effective.Refresh();

        Assert.Equal(SteamSessionSource.Actual, effective.State.Source);
        Assert.Equal(123u, effective.State.RunningAppId);
    }

    [Fact]
    public void BigPictureToActualAndBack_PublishesActiveSourcesWithoutInactiveGap()
    {
        var actual = new FakeRunningAppIdSource(0);
        using var watcher = new SteamSessionWatcher(actual);
        var probe = new FakeBigPictureProbe(true);
        using var bigPicture = new SteamBigPictureWatcher(probe, new FakeBigPictureEventHook());
        using var effective = new EffectiveSteamSessionSource(watcher, bigPicture);
        watcher.Start(); bigPicture.Start(); effective.Refresh();
        var sources = new List<SteamSessionSource>();
        effective.StateChanged += (_, args) => sources.Add(args.Current.Source);

        actual.SetRunningAppId(123);
        actual.SetRunningAppId(0);
        bigPicture.Refresh();

        Assert.Equal([SteamSessionSource.Actual, SteamSessionSource.BigPicture], sources);
        Assert.Equal(SteamSessionSource.BigPicture, effective.State.Source);
    }

    [Fact]
    public void Refresh_ReflectsActualSessionReadWhenWatcherStarts()
    {
        var actual = new FakeRunningAppIdSource(123);
        using var watcher = new SteamSessionWatcher(actual);
        using var effective = new EffectiveSteamSessionSource(watcher);
        watcher.Start();

        Assert.False(effective.State.IsActive);
        effective.Refresh();

        Assert.Equal(SteamSessionSource.Actual, effective.State.Source);
        Assert.Equal(123u, effective.State.RunningAppId);
    }

    [Fact]
    public void ReentrantChanges_ArePublishedInCommitOrder()
    {
        var actual = new FakeRunningAppIdSource(0);
        using var watcher = new SteamSessionWatcher(actual);
        using var effective = new EffectiveSteamSessionSource(watcher);
        watcher.Start();
        var published = new List<SteamSessionState>();
        effective.StateChanged += (_, args) =>
        {
            published.Add(args.Current);
            if (args.Current.IsActive) actual.SetRunningAppId(0);
        };

        actual.SetRunningAppId(123);

        Assert.Equal(2, published.Count);
        Assert.Equal(123u, published[0].RunningAppId);
        Assert.Equal(0u, published[1].RunningAppId);
        Assert.False(effective.State.IsActive);
    }

    [Fact]
    public void SubscriberException_DoesNotBlockOtherSubscribers()
    {
        var actual = new FakeRunningAppIdSource(0);
        using var watcher = new SteamSessionWatcher(actual);
        using var effective = new EffectiveSteamSessionSource(watcher);
        watcher.Start();
        var called = 0;
        effective.StateChanged += (_, _) => throw new InvalidOperationException("test");
        effective.StateChanged += (_, _) => called++;

        actual.SetRunningAppId(123);

        Assert.Equal(1, called);
    }

    [Fact]
    public async Task Dispose_WaitsForInFlightPublicationAndPreventsLaterPublication()
    {
        var actual = new FakeRunningAppIdSource(0);
        using var watcher = new SteamSessionWatcher(actual);
        var effective = new EffectiveSteamSessionSource(watcher);
        watcher.Start();
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var notifications = 0;
        effective.StateChanged += (_, _) =>
        {
            Interlocked.Increment(ref notifications);
            entered.Set();
            release.Wait();
        };

        var enable = Task.Run(() => actual.SetRunningAppId(123));
        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(10)));
            var dispose = Task.Run(effective.Dispose);
            Assert.NotSame(dispose, await Task.WhenAny(dispose, Task.Delay(100)));
            release.Set();
            await Task.WhenAll(enable, dispose);

            actual.SetRunningAppId(0);
            Assert.Equal(1, notifications);
        }
        finally
        {
            // If an assertion above throws, the StateChanged handler may still be sitting in
            // release.Wait() (or about to enter it) with `enable` never observed/awaited -- without this,
            // that leaves a background thread-pool work item permanently blocked for the rest of the
            // test process, which can then starve unrelated later tests. Always release it and let
            // `enable` finish so nothing outlives this test.
            release.Set();
            try { await enable.WaitAsync(TimeSpan.FromSeconds(5)); } catch { }
            effective.Dispose();
        }
    }

    [Fact]
    public void ReentrantDispose_StopsRemainingSubscribers()
    {
        var actual = new FakeRunningAppIdSource(0);
        using var watcher = new SteamSessionWatcher(actual);
        var effective = new EffectiveSteamSessionSource(watcher);
        watcher.Start();
        var first = 0;
        var second = 0;

        effective.StateChanged += (_, _) =>
        {
            first++;
            effective.Dispose();
        };
        effective.StateChanged += (_, _) => second++;

        actual.SetRunningAppId(123);

        Assert.Equal(1, first);
        Assert.Equal(0, second);

        actual.SetRunningAppId(0);
        Assert.Equal(0, second);
    }

    private sealed class FakeRunningAppIdSource(uint appId) : IRunningAppIdSource
    {
        private uint _appId = appId;
        public event EventHandler? Changed;
        public uint GetRunningAppId() => _appId;
        public void SetRunningAppId(uint appId) { _appId = appId; Changed?.Invoke(this, EventArgs.Empty); }
    }

    private static readonly IntPtr FakeBigPictureHwnd = new(0x1234);

    private sealed class FakeBigPictureProbe(bool active) : ISteamBigPictureWindowProbe
    {
        private bool _active = active;
        public BigPictureCandidateInspection InspectCandidate(IntPtr window)
            => window == FakeBigPictureHwnd && _active ? new(true, true, 111u) : new(false, true, 0);
        public BigPictureScanResult ScanForCandidate(IntPtr preferredHwnd)
            => _active ? new(true, FakeBigPictureHwnd, 111u, true) : new(false, IntPtr.Zero, 0, true);
        public BigPictureTrackedWindowInspection InspectTrackedWindow(IntPtr window, uint expectedProcessId)
            => window == FakeBigPictureHwnd && _active ? new(true, true) : new(false, true);
        public void SetActive(bool active) => _active = active;
    }

    private sealed class FakeBigPictureEventHook : ISteamBigPictureEventHook
    {
        private Action<BigPictureWinEvent>? _callback;
        public bool Start(Action<BigPictureWinEvent> callback) { _callback = callback; return true; }
        public void Raise() => _callback?.Invoke(new BigPictureWinEvent(BigPictureWinEventType.Create, FakeBigPictureHwnd, 0, 0));
        public void Dispose() => _callback = null;
    }
}
