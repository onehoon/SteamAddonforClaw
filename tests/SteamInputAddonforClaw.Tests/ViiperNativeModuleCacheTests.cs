using SteamInputAddonforClaw.VirtualOutput.Viiper;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class ViiperNativeModuleCacheTests
{
    private static string UniquePath() => Path.GetFullPath(Path.Combine(Path.GetTempPath(), $"libVIIPER-{Guid.NewGuid():N}.dll"));

    [Fact]
    public void GetOrLoadInvokesTheLoaderExactlyOnceForTheSamePath()
    {
        var path = UniquePath();
        var loadCount = 0;
        nint Load(string p) { loadCount++; return new nint(1); }

        var first = ViiperNativeModuleCache.GetOrLoad(path, Load);
        var second = ViiperNativeModuleCache.GetOrLoad(path, Load);

        Assert.Equal(1, loadCount);
        Assert.Equal(first, second);
    }

    [Fact]
    public void GetOrLoadTreatsEquivalentWindowsPathCasingAsTheSamePath()
    {
        var path = UniquePath();
        var loadCount = 0;
        nint Load(string p) { loadCount++; return new nint(1); }

        ViiperNativeModuleCache.GetOrLoad(path, Load);
        ViiperNativeModuleCache.GetOrLoad(path.ToUpperInvariant(), Load);

        Assert.Equal(1, loadCount);
    }

    [Fact]
    public async Task ConcurrentRequestsForTheSamePathLoadExactlyOnce()
    {
        const int concurrency = 16;
        var path = UniquePath();
        var loadCount = 0;
        var ready = new CountdownEvent(concurrency);
        var start = new ManualResetEventSlim(false);
        var loaderEntered = new ManualResetEventSlim(false);
        var loaderGate = new ManualResetEventSlim(false);
        nint Load(string p)
        {
            Interlocked.Increment(ref loadCount);
            loaderEntered.Set();
            loaderGate.Wait();
            return new nint(1);
        }

        var tasks = new Task<nint>[concurrency];
        for (var i = 0; i < tasks.Length; i++)
        {
            tasks[i] = Task.Run(() =>
            {
                ready.Signal();
                start.Wait();
                return ViiperNativeModuleCache.GetOrLoad(path, Load);
            });
        }

        // Wait for every task to reach the starting line before releasing them together,
        // so the loader is guaranteed to observe genuinely concurrent GetOrLoad entrants.
        ready.Wait();
        start.Set();
        loaderEntered.Wait();
        loaderGate.Set();
        await Task.WhenAll(tasks);

        Assert.Equal(1, loadCount);
        Assert.True(tasks.All(t => t.Result == tasks[0].Result));
    }

    [Fact]
    public void FailedLoadIsNotCachedAndCanBeRetried()
    {
        var path = UniquePath();
        var attempt = 0;
        nint Load(string p)
        {
            attempt++;
            if (attempt == 1) throw new InvalidOperationException("simulated load failure");
            return new nint(1);
        }

        Assert.Throws<InvalidOperationException>(() => ViiperNativeModuleCache.GetOrLoad(path, Load));
        var handle = ViiperNativeModuleCache.GetOrLoad(path, Load);

        Assert.Equal(2, attempt);
        Assert.Equal(new nint(1), handle);
    }
}
