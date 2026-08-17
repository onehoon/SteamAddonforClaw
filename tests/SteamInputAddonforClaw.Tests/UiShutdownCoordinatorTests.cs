using SteamInputAddonforClaw.UI.Lifecycle;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class UiShutdownCoordinatorTests
{
    [Fact]
    public async Task Cleanup_exception_still_requests_exit()
    {
        var exits = 0;
        var coordinator = new UiShutdownCoordinator(
            () => Task.FromException(new InvalidOperationException("dispose failed")),
            () => Interlocked.Increment(ref exits),
            TimeSpan.FromSeconds(1));

        await coordinator.ShutdownAsync();

        Assert.Equal(1, exits);
    }

    [Fact]
    public async Task Stalled_cleanup_is_bounded_and_still_requests_exit()
    {
        var neverCompletes = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var exits = 0;
        var coordinator = new UiShutdownCoordinator(
            () => neverCompletes.Task,
            () => Interlocked.Increment(ref exits),
            TimeSpan.FromMilliseconds(25));

        await coordinator.ShutdownAsync();

        Assert.Equal(1, exits);
    }

    [Fact]
    public async Task Competing_shutdown_requests_cleanup_and_exit_only_once()
    {
        var cleanupCalls = 0;
        var exits = 0;
        var coordinator = new UiShutdownCoordinator(
            () => { Interlocked.Increment(ref cleanupCalls); return Task.CompletedTask; },
            () => Interlocked.Increment(ref exits),
            TimeSpan.FromSeconds(1));

        await Task.WhenAll(coordinator.ShutdownAsync(), coordinator.ShutdownAsync(), coordinator.ShutdownAsync());

        Assert.Equal(1, cleanupCalls);
        Assert.Equal(1, exits);
    }
}
