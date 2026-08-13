using SteamInputAddonforClaw.Developer;
using SteamInputAddonforClaw.Steam;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class EffectiveSteamSessionSourceTests
{
    [Fact]
    public void TestMode_UsesDeveloperSourceWithoutChangingActualState()
    {
        var actual = new FakeRunningAppIdSource(0);
        using var watcher = new SteamSessionWatcher(actual);
        var testMode = new DeveloperTestModeState();
        using var effective = new EffectiveSteamSessionSource(watcher, testMode);
        watcher.Start();

        testMode.SetEnabled(true);

        Assert.Equal(SteamSessionSource.DeveloperTest, effective.State.Source);
        Assert.Equal(uint.MaxValue, effective.State.RunningAppId);
        Assert.Equal(0u, actual.GetRunningAppId());
    }

    [Fact]
    public void Refresh_ReflectsActualSessionReadWhenWatcherStarts()
    {
        var actual = new FakeRunningAppIdSource(123);
        using var watcher = new SteamSessionWatcher(actual);
        var testMode = new DeveloperTestModeState();
        using var effective = new EffectiveSteamSessionSource(watcher, testMode);
        watcher.Start();

        Assert.False(effective.State.IsActive);
        effective.Refresh();

        Assert.Equal(SteamSessionSource.Actual, effective.State.Source);
        Assert.Equal(123u, effective.State.RunningAppId);
    }

    [Fact]
    public void ActualSession_HasPriorityOverTestMode()
    {
        var actual = new FakeRunningAppIdSource(0);
        using var watcher = new SteamSessionWatcher(actual);
        var testMode = new DeveloperTestModeState();
        using var effective = new EffectiveSteamSessionSource(watcher, testMode);
        watcher.Start();
        testMode.SetEnabled(true);

        actual.SetRunningAppId(123);
        Assert.Equal(SteamSessionSource.Actual, effective.State.Source);
        Assert.Equal(123u, effective.State.RunningAppId);

        actual.SetRunningAppId(0);
        Assert.Equal(SteamSessionSource.DeveloperTest, effective.State.Source);
    }

    [Fact]
    public void TestModeOnAndOffPublishesTheSyntheticSessionBoundary()
    {
        var actual = new FakeRunningAppIdSource(0);
        using var watcher = new SteamSessionWatcher(actual);
        var testMode = new DeveloperTestModeState();
        using var effective = new EffectiveSteamSessionSource(watcher, testMode);
        watcher.Start();
        var states = new List<SteamSessionState>();
        effective.StateChanged += (_, args) => states.Add(args.Current);

        testMode.SetEnabled(true);
        testMode.SetEnabled(false);

        Assert.Equal(2, states.Count);
        Assert.True(states[0].IsActive);
        Assert.Equal(SteamSessionSource.DeveloperTest, states[0].Source);
        Assert.False(states[1].IsActive);
        Assert.Equal(SteamSessionSource.Actual, states[1].Source);
    }

    [Fact]
    public void ReentrantChanges_ArePublishedInCommitOrder()
    {
        var actual = new FakeRunningAppIdSource(0);
        using var watcher = new SteamSessionWatcher(actual);
        var testMode = new DeveloperTestModeState();
        using var effective = new EffectiveSteamSessionSource(watcher, testMode);
        watcher.Start();
        var published = new List<SteamSessionState>();
        effective.StateChanged += (_, args) =>
        {
            published.Add(args.Current);
            if (args.Current.Source == SteamSessionSource.DeveloperTest) testMode.SetEnabled(false);
        };

        testMode.SetEnabled(true);

        Assert.Equal(2, published.Count);
        Assert.Equal(SteamSessionSource.DeveloperTest, published[0].Source);
        Assert.Equal(SteamSessionSource.Actual, published[1].Source);
        Assert.False(effective.State.IsActive);
    }

    [Fact]
    public void SubscriberException_DoesNotBlockOtherSubscribers()
    {
        var actual = new FakeRunningAppIdSource(0);
        using var watcher = new SteamSessionWatcher(actual);
        var testMode = new DeveloperTestModeState();
        using var effective = new EffectiveSteamSessionSource(watcher, testMode);
        watcher.Start();
        var called = 0;
        effective.StateChanged += (_, _) => throw new InvalidOperationException("test");
        effective.StateChanged += (_, _) => called++;

        testMode.SetEnabled(true);

        Assert.Equal(1, called);
    }

    [Fact]
    public async Task Dispose_WaitsForInFlightPublicationAndPreventsLaterPublication()
    {
        var actual = new FakeRunningAppIdSource(0);
        using var watcher = new SteamSessionWatcher(actual);
        var testMode = new DeveloperTestModeState();
        var effective = new EffectiveSteamSessionSource(watcher, testMode);
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

        var enable = Task.Run(() => testMode.SetEnabled(true));
        Assert.True(entered.Wait(TimeSpan.FromSeconds(10)));
        var dispose = Task.Run(effective.Dispose);
        Assert.NotSame(dispose, await Task.WhenAny(dispose, Task.Delay(100)));
        release.Set();
        await Task.WhenAll(enable, dispose);

        testMode.SetEnabled(false);
        Assert.Equal(1, notifications);
    }

    [Fact]
    public void DeveloperTestModeState_SubscriberExceptionDoesNotBlockRemainingSubscribers()
    {
        var state = new DeveloperTestModeState();
        var called = 0;
        state.Changed += (_, _) => throw new InvalidOperationException("test");
        state.Changed += (_, _) => called++;

        state.SetEnabled(true);

        Assert.True(state.IsEnabled);
        Assert.Equal(1, called);
    }

    [Fact]
    public void ReentrantDispose_StopsRemainingSubscribers()
    {
        var actual = new FakeRunningAppIdSource(0);
        using var watcher = new SteamSessionWatcher(actual);
        var testMode = new DeveloperTestModeState();
        var effective = new EffectiveSteamSessionSource(watcher, testMode);
        watcher.Start();
        var first = 0;
        var second = 0;

        effective.StateChanged += (_, _) =>
        {
            first++;
            effective.Dispose();
        };
        effective.StateChanged += (_, _) => second++;

        testMode.SetEnabled(true);

        Assert.Equal(1, first);
        Assert.Equal(0, second);

        testMode.SetEnabled(false);
        Assert.Equal(0, second);
    }

    private sealed class FakeRunningAppIdSource(uint appId) : IRunningAppIdSource
    {
        private uint _appId = appId;
        public event EventHandler? Changed;
        public uint GetRunningAppId() => _appId;
        public void SetRunningAppId(uint appId) { _appId = appId; Changed?.Invoke(this, EventArgs.Empty); }
    }
}
