using SteamInputAddonforClaw.Lifecycle;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class NativeMessageLoopTests
{
    [Fact]
    public void Ready_callback_runs_from_the_message_loop_and_can_exit_cleanly()
    {
        var loop = new NativeMessageLoop();
        var readyCalls = 0;

        loop.Run(() =>
        {
            readyCalls++;
            loop.RequestExit();
        });

        Assert.Equal(1, readyCalls);
    }

    [Fact]
    public void Request_exit_before_run_returns_without_blocking()
    {
        var loop = new NativeMessageLoop();
        loop.RequestExit();
        loop.Run();
        loop.RequestExit();
    }

    [Fact]
    public async Task Request_exit_from_another_thread_unblocks_the_owner()
    {
        var ready = new TaskCompletionSource<NativeMessageLoop>(TaskCreationOptions.RunContinuationsAsynchronously);
        var run = Task.Run(() =>
        {
            var loop = new NativeMessageLoop();
            ready.SetResult(loop);
            loop.Run();
        });

        var ownerLoop = await ready.Task.WaitAsync(TimeSpan.FromSeconds(5));
        ownerLoop.RequestExit();
        await run.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void Failed_cross_thread_exit_post_can_be_retried()
    {
        var attempts = 0;
        var loop = new NativeMessageLoop(_ => ++attempts > 1, ownerThreadId: 1);

        Assert.ThrowsAny<Exception>(() => loop.RequestExit());
        loop.RequestExit();
        Assert.Equal(2, attempts);
    }
}
