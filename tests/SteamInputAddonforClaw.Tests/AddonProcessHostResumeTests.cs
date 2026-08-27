using SteamInputAddonforClaw.Hosting;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class AddonProcessHostResumeTests
{
    [Fact]
    public async Task Resume_reconcile_waits_then_uses_latest_app_id_for_both_runtimes()
    {
        var delayStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseDelay = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var appId = 111u;
        var calls = new List<(string Runtime, uint AppId)>();

        var reconcile = AddonProcessHost.ReconcilePerformanceAfterResumeAsync(
            CancellationToken.None,
            async (delay, _) =>
            {
                Assert.Equal(TimeSpan.FromMilliseconds(2500), delay);
                delayStarted.SetResult();
                await releaseDelay.Task;
            },
            () => appId,
            currentAppId => calls.Add(("CPU", currentAppId)),
            currentAppId => calls.Add(("Power", currentAppId)));

        await delayStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));
        Assert.Empty(calls);
        appId = 222;
        releaseDelay.SetResult();
        await reconcile;

        Assert.Equal(new[] { ("CPU", 222u), ("Power", 222u) }, calls);
    }

    [Fact]
    public async Task Resume_reconcile_keeps_power_mode_independent_when_cpu_boost_fails()
    {
        var calls = new List<string>();

        await AddonProcessHost.ReconcilePerformanceAfterResumeAsync(
            CancellationToken.None,
            static (_, _) => Task.CompletedTask,
            static () => 123u,
            _ =>
            {
                calls.Add("CPU");
                throw new InvalidOperationException("CPU failure");
            },
            _ => calls.Add("Power"));

        Assert.Equal(new[] { "CPU", "Power" }, calls);
    }

    [Fact]
    public async Task Resume_reconcile_keeps_cpu_boost_independent_when_power_mode_fails()
    {
        var calls = new List<string>();

        await AddonProcessHost.ReconcilePerformanceAfterResumeAsync(
            CancellationToken.None,
            static (_, _) => Task.CompletedTask,
            static () => 123u,
            _ => calls.Add("CPU"),
            _ =>
            {
                calls.Add("Power");
                throw new InvalidOperationException("Power failure");
            });

        Assert.Equal(new[] { "CPU", "Power" }, calls);
    }

    [Fact]
    public async Task Resume_reconcile_cancellation_during_settle_skips_both_runtimes()
    {
        using var cancellation = new CancellationTokenSource();
        var delayStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;

        var reconcile = AddonProcessHost.ReconcilePerformanceAfterResumeAsync(
            cancellation.Token,
            async (_, token) =>
            {
                delayStarted.SetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
            },
            static () => 123u,
            _ => calls++,
            _ => calls++);

        await delayStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));
        cancellation.Cancel();
        await reconcile;

        Assert.Equal(0, calls);
    }
}
