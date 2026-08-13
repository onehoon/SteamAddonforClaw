using SteamInputAddonforClaw.Steam;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class SteamSessionWatcherTests
{
    [Theory]
    [InlineData(0, false)]
    [InlineData(123, true)]
    public void Start_ReflectsCurrentRunningAppId(uint runningAppId, bool isActive)
    {
        var source = new FakeRunningAppIdSource(runningAppId);
        using var watcher = new SteamSessionWatcher(source);

        watcher.Start();

        Assert.Equal(isActive, watcher.State.IsActive);
        Assert.Equal(runningAppId, watcher.State.RunningAppId);
    }

    [Theory]
    [InlineData(0, 123)]
    [InlineData(123, 0)]
    [InlineData(123, 456)]
    public void SourceChange_WhenRunningAppIdChanges_RaisesStateChangedOnce(uint initialAppId, uint nextAppId)
    {
        var source = new FakeRunningAppIdSource(initialAppId);
        using var watcher = new SteamSessionWatcher(source);
        var notificationCount = 0;

        watcher.StateChanged += (_, _) => notificationCount++;
        watcher.Start();
        source.SetRunningAppId(nextAppId);

        Assert.Equal(1, notificationCount);
        Assert.Equal(nextAppId != 0, watcher.State.IsActive);
        Assert.Equal(nextAppId, watcher.State.RunningAppId);
    }

    [Fact]
    public void SourceChange_WhenRunningAppIdIsUnchanged_DoesNotRaiseStateChanged()
    {
        var source = new FakeRunningAppIdSource(123);
        using var watcher = new SteamSessionWatcher(source);
        var notificationCount = 0;

        watcher.StateChanged += (_, _) => notificationCount++;
        watcher.Start();
        source.NotifyChanged();

        Assert.Equal(0, notificationCount);
    }

    [Fact]
    public void Refresh_RereadsCurrentValueWithoutRegistryNotification()
    {
        var source = new FakeRunningAppIdSource(123);
        using var watcher = new SteamSessionWatcher(source);
        var notifications = 0;
        watcher.StateChanged += (_, _) => notifications++;
        watcher.Start();
        source.SetWithoutNotification(0);

        watcher.Refresh();

        Assert.Equal(0u, watcher.State.RunningAppId);
        Assert.False(watcher.State.IsActive);
        Assert.Equal(1, notifications);
    }

    [Fact]
    public void Refresh_BeforeStartAndAfterStop_IsNoOp()
    {
        var source = new FakeRunningAppIdSource(123);
        using var watcher = new SteamSessionWatcher(source);
        watcher.Refresh();
        Assert.Equal(0, source.ReadCount);
        watcher.Start();
        watcher.Stop();
        source.SetWithoutNotification(0);
        watcher.Refresh();
        Assert.Equal(1, source.ReadCount);
        Assert.Equal(123u, watcher.State.RunningAppId);
    }

    [Fact]
    public void Refresh_AfterDispose_Throws()
    {
        var watcher = new SteamSessionWatcher(new FakeRunningAppIdSource(0));
        watcher.Dispose();
        Assert.Throws<ObjectDisposedException>(watcher.Refresh);
    }

    [Fact]
    public void Start_WhenCalledMultipleTimes_DoesNotDuplicateSourceSubscriptions()
    {
        var source = new FakeRunningAppIdSource(0);
        using var watcher = new SteamSessionWatcher(source);
        var notificationCount = 0;

        watcher.StateChanged += (_, _) => notificationCount++;
        watcher.Start();
        watcher.Start();
        source.SetRunningAppId(123);

        Assert.Equal(1, notificationCount);
    }

    [Fact]
    public void Stop_PreventsFurtherStateChangedNotifications()
    {
        var source = new FakeRunningAppIdSource(0);
        using var watcher = new SteamSessionWatcher(source);
        var notificationCount = 0;

        watcher.StateChanged += (_, _) => notificationCount++;
        watcher.Start();
        watcher.Stop();
        source.SetRunningAppId(123);

        Assert.Equal(0, notificationCount);
    }

    [Fact]
    public void Dispose_PreventsFurtherStateChangedNotifications()
    {
        var source = new FakeRunningAppIdSource(0);
        var watcher = new SteamSessionWatcher(source);
        var notificationCount = 0;

        watcher.StateChanged += (_, _) => notificationCount++;
        watcher.Start();
        watcher.Dispose();
        source.SetRunningAppId(123);

        Assert.Equal(0, notificationCount);
    }

    [Theory]
    [InlineData(null, 0u)]
    [InlineData("123", 0u)]
    [InlineData(-1, uint.MaxValue)]
    [InlineData(123, 123u)]
    [InlineData(-1529805393, 2765161903u)]
    [InlineData(456L, 456u)]
    [InlineData(4294967296L, 0u)]
    public void ConvertRegistryValueToRunningAppId_HandlesSupportedAndUnsupportedRegistryRepresentations(object? value, uint expectedAppId)
    {
        var appId = SteamRunningAppIdRegistrySource.ConvertRegistryValueToRunningAppId(value);

        Assert.Equal(expectedAppId, appId);
    }

    private sealed class FakeRunningAppIdSource(uint runningAppId) : IRunningAppIdSource
    {
        private uint _runningAppId = runningAppId;

        public event EventHandler? Changed;

        public int ReadCount { get; private set; }
        public uint GetRunningAppId() { ReadCount++; return _runningAppId; }

        public void SetRunningAppId(uint runningAppId)
        {
            _runningAppId = runningAppId;
            NotifyChanged();
        }

        public void NotifyChanged() => Changed?.Invoke(this, EventArgs.Empty);
        public void SetWithoutNotification(uint runningAppId) => _runningAppId = runningAppId;
    }
}
