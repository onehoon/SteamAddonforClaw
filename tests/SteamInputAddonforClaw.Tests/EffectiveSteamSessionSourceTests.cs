using SteamInputAddonforClaw.Developer;
using SteamInputAddonforClaw.Steam;
using SteamInputAddonforClaw.Settings;
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
    public void RoutingEnabled_WithActualGame_ReportsActualSession()
    {
        var actual = new FakeRunningAppIdSource(123);
        using var watcher = new SteamSessionWatcher(actual);
        using var bigPicture = new SteamBigPictureWatcher(new FakeBigPictureProbe(false), new FakeBigPictureEventHook());
        using var effective = new EffectiveSteamSessionSource(watcher, bigPicture, new DeveloperTestModeState(), new FakeSteamInputRoutingPreference(true));
        watcher.Start(); bigPicture.Start(); effective.Refresh();

        Assert.True(effective.State.IsActive);
        Assert.Equal(SteamSessionSource.Actual, effective.State.Source);
    }

    [Fact]
    public void RoutingEnabled_WithBigPictureOnly_ReportsBigPictureSession()
    {
        var actual = new FakeRunningAppIdSource(0);
        using var watcher = new SteamSessionWatcher(actual);
        using var bigPicture = new SteamBigPictureWatcher(new FakeBigPictureProbe(true), new FakeBigPictureEventHook());
        using var effective = new EffectiveSteamSessionSource(watcher, bigPicture, new DeveloperTestModeState(), new FakeSteamInputRoutingPreference(true));
        watcher.Start(); bigPicture.Start();

        Assert.True(effective.State.IsActive);
        Assert.Equal(SteamSessionSource.BigPicture, effective.State.Source);
    }

    [Fact]
    public void RoutingEnabled_WithNeitherGameNorBigPicture_ReportsInactive()
    {
        var actual = new FakeRunningAppIdSource(0);
        using var watcher = new SteamSessionWatcher(actual);
        using var bigPicture = new SteamBigPictureWatcher(new FakeBigPictureProbe(false), new FakeBigPictureEventHook());
        using var effective = new EffectiveSteamSessionSource(watcher, bigPicture, new DeveloperTestModeState(), new FakeSteamInputRoutingPreference(true));
        watcher.Start(); bigPicture.Start();

        Assert.False(effective.State.IsActive);
    }

    [Fact]
    public void RoutingDisabled_WithActualGame_ReportsInactive()
    {
        var actual = new FakeRunningAppIdSource(123);
        using var watcher = new SteamSessionWatcher(actual);
        using var bigPicture = new SteamBigPictureWatcher(new FakeBigPictureProbe(false), new FakeBigPictureEventHook());
        using var effective = new EffectiveSteamSessionSource(watcher, bigPicture, new DeveloperTestModeState(), new FakeSteamInputRoutingPreference(false));
        watcher.Start(); bigPicture.Start(); effective.Refresh();

        Assert.False(effective.State.IsActive);
        Assert.Equal(0u, effective.State.RunningAppId);
    }

    [Fact]
    public void RoutingDisabled_WithBigPicture_ReportsInactive()
    {
        var actual = new FakeRunningAppIdSource(0);
        using var watcher = new SteamSessionWatcher(actual);
        using var bigPicture = new SteamBigPictureWatcher(new FakeBigPictureProbe(true), new FakeBigPictureEventHook());
        using var effective = new EffectiveSteamSessionSource(watcher, bigPicture, new DeveloperTestModeState(), new FakeSteamInputRoutingPreference(false));
        watcher.Start(); bigPicture.Start();

        Assert.False(effective.State.IsActive);
    }

    [Fact]
    public void RoutingDisabled_WithDeveloperTestMode_ReportsInactive()
    {
        var actual = new FakeRunningAppIdSource(0);
        using var watcher = new SteamSessionWatcher(actual);
        using var bigPicture = new SteamBigPictureWatcher(new FakeBigPictureProbe(false), new FakeBigPictureEventHook());
        var testMode = new DeveloperTestModeState();
        using var effective = new EffectiveSteamSessionSource(watcher, bigPicture, testMode, new FakeSteamInputRoutingPreference(false));
        watcher.Start(); bigPicture.Start();

        testMode.SetEnabled(true);

        Assert.False(effective.State.IsActive);
    }

    [Fact]
    public void ActualSessionActive_RoutingTurnedOff_PublishesActiveToInactiveTransition()
    {
        var actual = new FakeRunningAppIdSource(123);
        using var watcher = new SteamSessionWatcher(actual);
        using var bigPicture = new SteamBigPictureWatcher(new FakeBigPictureProbe(false), new FakeBigPictureEventHook());
        var preference = new FakeSteamInputRoutingPreference(true);
        using var effective = new EffectiveSteamSessionSource(watcher, bigPicture, new DeveloperTestModeState(), preference);
        watcher.Start(); bigPicture.Start(); effective.Refresh();
        var transitions = new List<SteamSessionStateChangedEventArgs>();
        effective.StateChanged += (_, args) => transitions.Add(args);

        preference.Set(false);

        var transition = Assert.Single(transitions);
        Assert.True(transition.Previous.IsActive);
        Assert.Equal(SteamSessionSource.Actual, transition.Previous.Source);
        Assert.False(transition.Current.IsActive);
    }

    [Fact]
    public void BigPictureSessionActive_RoutingTurnedOff_PublishesActiveToInactiveTransition()
    {
        var actual = new FakeRunningAppIdSource(0);
        using var watcher = new SteamSessionWatcher(actual);
        using var bigPicture = new SteamBigPictureWatcher(new FakeBigPictureProbe(true), new FakeBigPictureEventHook());
        var preference = new FakeSteamInputRoutingPreference(true);
        using var effective = new EffectiveSteamSessionSource(watcher, bigPicture, new DeveloperTestModeState(), preference);
        watcher.Start(); bigPicture.Start();
        var transitions = new List<SteamSessionStateChangedEventArgs>();
        effective.StateChanged += (_, args) => transitions.Add(args);

        preference.Set(false);

        var transition = Assert.Single(transitions);
        Assert.Equal(SteamSessionSource.BigPicture, transition.Previous.Source);
        Assert.False(transition.Current.IsActive);
    }

    [Fact]
    public void ActualGameAlreadyRunning_RoutingTurnedOn_PublishesInactiveToActualTransition()
    {
        var actual = new FakeRunningAppIdSource(123);
        using var watcher = new SteamSessionWatcher(actual);
        using var bigPicture = new SteamBigPictureWatcher(new FakeBigPictureProbe(false), new FakeBigPictureEventHook());
        var preference = new FakeSteamInputRoutingPreference(false);
        using var effective = new EffectiveSteamSessionSource(watcher, bigPicture, new DeveloperTestModeState(), preference);
        watcher.Start(); bigPicture.Start();
        var transitions = new List<SteamSessionStateChangedEventArgs>();
        effective.StateChanged += (_, args) => transitions.Add(args);

        preference.Set(true);

        var transition = Assert.Single(transitions);
        Assert.False(transition.Previous.IsActive);
        Assert.True(transition.Current.IsActive);
        Assert.Equal(SteamSessionSource.Actual, transition.Current.Source);
        Assert.Equal(123u, transition.Current.RunningAppId);
    }

    [Fact]
    public void BigPictureAlreadyActive_RoutingTurnedOn_PublishesInactiveToBigPictureTransition()
    {
        var actual = new FakeRunningAppIdSource(0);
        using var watcher = new SteamSessionWatcher(actual);
        using var bigPicture = new SteamBigPictureWatcher(new FakeBigPictureProbe(true), new FakeBigPictureEventHook());
        var preference = new FakeSteamInputRoutingPreference(false);
        using var effective = new EffectiveSteamSessionSource(watcher, bigPicture, new DeveloperTestModeState(), preference);
        watcher.Start(); bigPicture.Start();
        var transitions = new List<SteamSessionStateChangedEventArgs>();
        effective.StateChanged += (_, args) => transitions.Add(args);

        preference.Set(true);

        var transition = Assert.Single(transitions);
        Assert.False(transition.Previous.IsActive);
        Assert.Equal(SteamSessionSource.BigPicture, transition.Current.Source);
    }

    [Fact]
    public void RoutingPreference_EnablesAndDisablesTheEffectiveSessionImmediately()
    {
        var actual = new FakeRunningAppIdSource(0);
        using var watcher = new SteamSessionWatcher(actual);
        var bigPictureProbe = new FakeBigPictureProbe(false);
        using var bigPicture = new SteamBigPictureWatcher(bigPictureProbe, new FakeBigPictureEventHook());
        var preference = new FakeSteamInputRoutingPreference();
        var testMode = new DeveloperTestModeState();
        using var effective = new EffectiveSteamSessionSource(watcher, bigPicture, testMode, preference);
        watcher.Start(); bigPicture.Start();
        var states = new List<SteamSessionState>();
        effective.StateChanged += (_, args) => states.Add(args.Current);

        bigPictureProbe.SetActive(true);
        bigPicture.Refresh();
        preference.Set(true);
        preference.Set(false);

        Assert.Equal([SteamSessionSource.BigPicture, SteamSessionSource.Actual], states.Select(state => state.Source));
        Assert.False(effective.State.IsActive);
    }

    [Fact]
    public void ActualGame_HasPriorityOverBigPicture()
    {
        var actual = new FakeRunningAppIdSource(123);
        using var watcher = new SteamSessionWatcher(actual);
        using var bigPicture = new SteamBigPictureWatcher(new FakeBigPictureProbe(true), new FakeBigPictureEventHook());
        var preference = new FakeSteamInputRoutingPreference();
        using var effective = new EffectiveSteamSessionSource(watcher, bigPicture, new DeveloperTestModeState(), preference);
        watcher.Start(); bigPicture.Start(); preference.Set(true);

        Assert.Equal(SteamSessionSource.Actual, effective.State.Source);
        Assert.Equal(123u, effective.State.RunningAppId);
    }

    [Fact]
    public void SameRoutingPreference_DoesNotPublishDuplicateTransition()
    {
        var actual = new FakeRunningAppIdSource(0);
        using var watcher = new SteamSessionWatcher(actual);
        var probe = new FakeBigPictureProbe(true);
        using var bigPicture = new SteamBigPictureWatcher(probe, new FakeBigPictureEventHook());
        var preference = new FakeSteamInputRoutingPreference(true);
        using var effective = new EffectiveSteamSessionSource(watcher, bigPicture, new DeveloperTestModeState(), preference);
        watcher.Start(); bigPicture.Start();
        var publications = 0;
        effective.StateChanged += (_, _) => publications++;

        preference.Set(false);
        preference.Set(false);

        Assert.Equal(1, publications);
    }

    [Fact]
    public void BigPictureToActualAndBack_PublishesActiveSourcesWithoutInactiveGap()
    {
        var actual = new FakeRunningAppIdSource(0);
        using var watcher = new SteamSessionWatcher(actual);
        var probe = new FakeBigPictureProbe(true);
        using var bigPicture = new SteamBigPictureWatcher(probe, new FakeBigPictureEventHook());
        var preference = new FakeSteamInputRoutingPreference();
        using var effective = new EffectiveSteamSessionSource(watcher, bigPicture, new DeveloperTestModeState(), preference);
        watcher.Start(); bigPicture.Start(); preference.Set(true);
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
        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(10)));
            var dispose = Task.Run(effective.Dispose);
            Assert.NotSame(dispose, await Task.WhenAny(dispose, Task.Delay(100)));
            release.Set();
            await Task.WhenAll(enable, dispose);

            testMode.SetEnabled(false);
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

    private sealed class FakeSteamInputRoutingPreference(bool enabled = true) : ISteamInputRoutingPreference
    {
        public bool SteamInputRoutingEnabled { get; private set; } = enabled;
        public event EventHandler? SteamInputRoutingEnabledChanged;
        public void Set(bool value)
        {
            if (SteamInputRoutingEnabled == value) return;
            SteamInputRoutingEnabled = value;
            SteamInputRoutingEnabledChanged?.Invoke(this, EventArgs.Empty);
        }
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
