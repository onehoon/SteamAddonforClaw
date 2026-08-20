using SteamInputAddonforClaw.GameBar;
using SteamInputAddonforClaw.Hosting;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class GameBarForegroundWatcherTests
{
    [Fact]
    public async Task PresentationDeliveryConvergesToLatestForegroundStateWithoutOverlap()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = new List<bool>();
        var concurrent = 0;
        var maximumConcurrent = 0;
        var delivery = new GameBarForegroundPresentationDelivery(async foreground =>
        {
            calls.Add(foreground);
            var active = Interlocked.Increment(ref concurrent);
            Volatile.Write(ref maximumConcurrent, Math.Max(maximumConcurrent, active));
            started.TrySetResult();
            await release.Task;
            Interlocked.Decrement(ref concurrent);
            return true;
        });

        delivery.Request(true);
        await started.Task;
        delivery.Request(false);
        delivery.Request(true);
        release.TrySetResult();
        await delivery.DrainAsync();

        Assert.Equal([true], calls);
        Assert.Equal(1, maximumConcurrent);
    }

    [Fact]
    public async Task PresentationDeliveryAppliesStateChangedDuringMutation()
    {
        var firstStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = new List<bool>();
        var delivery = new GameBarForegroundPresentationDelivery(async foreground =>
        {
            calls.Add(foreground);
            if (foreground)
            {
                firstStarted.TrySetResult();
                await releaseFirst.Task;
            }
            return true;
        });

        delivery.Request(true);
        await firstStarted.Task;
        delivery.Request(false);
        releaseFirst.TrySetResult();
        await delivery.DrainAsync();

        Assert.Equal([true, false], calls);
    }

    [Fact]
    public async Task PresentationDeliveryStopsNewRequestsAndDrainsCurrentDispatch()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = new List<bool>();
        var delivery = new GameBarForegroundPresentationDelivery(async foreground =>
        {
            calls.Add(foreground);
            started.TrySetResult();
            await release.Task;
            return true;
        });

        delivery.Request(true);
        await started.Task;
        delivery.StopAccepting();
        delivery.Request(false);
        var drain = delivery.DrainAsync();
        Assert.False(drain.IsCompleted);
        release.TrySetResult();
        await drain;

        Assert.Equal([true], calls);
    }

    [Fact]
    public async Task PresentationDeliveryCanBeRequestedAgainWhenRoutingCompletesWithoutAWatcherEvent()
    {
        var routeActive = false;
        var calls = new List<bool>();
        var delivery = new GameBarForegroundPresentationDelivery(foreground =>
        {
            calls.Add(foreground);
            return Task.FromResult(routeActive);
        });

        delivery.Request(true);
        await delivery.DrainAsync();
        Assert.Equal([true], calls);

        routeActive = true;
        delivery.Request(true);
        await delivery.DrainAsync();

        Assert.Equal([true, true], calls);
    }
    [Theory]
    [InlineData("Microsoft.XboxGamingOverlay_8wekyb3d8bbwe", true)]
    [InlineData("Microsoft.XboxGamingOverlay_wrong", false)]
    [InlineData(null, false)]
    public void PackageFamilyIdentity_IsExact(string? familyName, bool expected) =>
        Assert.Equal(expected, GameBarForegroundProbe.IsExpectedPackageFamily(familyName));

    [Fact]
    public void Start_InstallsHookAndInspectsStartupForeground()
    {
        var callback = default(GameBarForegroundWatcher.WinEventCallback);
        var probe = new FakeProbe([1]);
        using var watcher = Create(probe, (cb) => callback = cb, () => new(1));

        watcher.Start();
        watcher.Start();

        Assert.True(watcher.IsForeground);
        Assert.Single(probe.Inspected);
        Assert.NotNull(callback);
    }

    [Fact]
    public void Event_UsesCurrentForegroundAndPublishesOnlyOnBooleanChanges()
    {
        var current = new IntPtr(2);
        var callback = default(GameBarForegroundWatcher.WinEventCallback);
        var probe = new FakeProbe([2]);
        using var watcher = Create(probe, cb => callback = cb, () => current);
        var changes = 0;
        watcher.StateChanged += (_, _) => changes++;
        watcher.Start();

        current = new IntPtr(4);
        callback!(IntPtr.Zero, 3, new(2), 0, 0, 0, 0); // stale event: current HWND is 4
        Assert.False(watcher.IsForeground);

        current = new IntPtr(3);
        probe.GameBarWindows.Add(3);
        callback(IntPtr.Zero, 3, new(3), 0, 0, 0, 0);
        callback(IntPtr.Zero, 3, new(3), 0, 0, 0, 0);
        Assert.True(watcher.IsForeground);
        Assert.Equal(3, changes);

        current = new IntPtr(4);
        callback(IntPtr.Zero, 3, new(3), 0, 0, 0, 0);
        Assert.False(watcher.IsForeground);
        Assert.Equal(4, changes);
    }

    [Fact]
    public void HookFailure_IsContainedAndDisposeUnhooksOnceAndIgnoresEvents()
    {
        var callback = default(GameBarForegroundWatcher.WinEventCallback);
        var unhooks = 0;
        using var watcher = new GameBarForegroundWatcher(
            new FakeProbe([]),
            (_, _, cb, _, _, _, _) => { callback = cb; return IntPtr.Zero; },
            () => IntPtr.Zero,
            _ => unhooks++);

        watcher.Start();
        callback?.Invoke(IntPtr.Zero, 3, new(1), 0, 0, 0, 0);
        watcher.Dispose();
        watcher.Dispose();

        Assert.False(watcher.IsForeground);
        Assert.Equal(0, unhooks);
    }

    private static GameBarForegroundWatcher Create(FakeProbe probe, Action<GameBarForegroundWatcher.WinEventCallback> capture, Func<IntPtr> foreground) =>
        new(probe, (_, _, callback, _, _, _, _) => { capture(callback); return new(1); }, foreground, _ => { });

    private sealed class FakeProbe(HashSet<int> gameBarWindows) : IGameBarForegroundProbe
    {
        internal readonly HashSet<int> GameBarWindows = gameBarWindows;
        internal readonly List<IntPtr> Inspected = [];
        GameBarIdentityInspection IGameBarForegroundProbe.Inspect(IntPtr hwnd)
        {
            Inspected.Add(hwnd);
            return new(GameBarWindows.Contains(hwnd.ToInt32()));
        }
    }
}
