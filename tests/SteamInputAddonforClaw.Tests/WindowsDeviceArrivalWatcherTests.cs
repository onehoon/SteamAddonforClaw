using SteamInputAddonforClaw.Controllers.Detection;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

/// <summary>Work order PR10 section 20.10 / 20.11: the one Runtime-owned Windows Device Arrival
/// observer. It is only a wake-up trigger for the existing owned-controller recovery -- it carries
/// no device identity and has no polling/timer fallback.</summary>
[Collection("AppLog")]
public sealed class WindowsDeviceArrivalWatcherTests
{
    private sealed class FakeAdapter : IDeviceArrivalWatcherAdapter
    {
        public bool StartAttempted;
        public bool Disposed;
        public Exception? StartError;

        public event Action? DeviceArrived;

        public bool TryStart(out Exception? error)
        {
            StartAttempted = true;
            error = StartError;
            return StartError is null;
        }

        public void RaiseArrival() => DeviceArrived?.Invoke();

        public void Dispose() => Disposed = true;
    }

    [Fact] // 20.10
    public void Start_subscribes_once_and_forwards_every_arrival()
    {
        var adapter = new FakeAdapter();
        using var watcher = new WindowsDeviceArrivalWatcher(adapter);
        var count = 0;
        watcher.DeviceArrived += () => count++;

        Assert.True(watcher.Start());
        Assert.False(watcher.Start()); // one-shot
        adapter.RaiseArrival();
        adapter.RaiseArrival();

        Assert.True(adapter.StartAttempted);
        Assert.Equal(2, count);
    }

    [Fact] // 20.10
    public void Dispose_stops_the_adapter_and_no_later_arrival_escapes()
    {
        var adapter = new FakeAdapter();
        var watcher = new WindowsDeviceArrivalWatcher(adapter);
        var count = 0;
        watcher.DeviceArrived += () => count++;
        watcher.Start();

        watcher.Dispose();
        watcher.Dispose(); // idempotent
        adapter.RaiseArrival();

        Assert.True(adapter.Disposed);
        Assert.Equal(0, count);
        Assert.False(watcher.Start()); // refused after dispose
    }

    [Fact] // 20.11 -- a start failure is swallowed; the Runtime keeps running with no trigger
    public void Start_failure_leaves_the_watcher_unstarted_and_silent()
    {
        var adapter = new FakeAdapter { StartError = new InvalidOperationException("WMI unavailable") };
        using var watcher = new WindowsDeviceArrivalWatcher(adapter);
        var count = 0;
        watcher.DeviceArrived += () => count++;

        Assert.False(watcher.Start());
        adapter.RaiseArrival(); // adapter was unsubscribed again on failure
        Assert.Equal(0, count);
    }

    [Fact] // 20.11 -- no polling / timer fallback anywhere in the watcher
    public void Watcher_source_has_no_timer_or_polling_fallback()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "SteamInputAddonforClaw.slnx"))) dir = dir.Parent;
        var source = File.ReadAllText(Path.Combine(dir!.FullName,
            "src/SteamInputAddonforClaw/Controllers/Detection/WindowsDeviceArrivalWatcher.cs"));

        foreach (var forbidden in new[] { "PeriodicTimer", "System.Timers", "Thread.Sleep", "Task.Delay", "while (true)", "while(true)" })
            Assert.DoesNotContain(forbidden, source, StringComparison.Ordinal);
    }
}
