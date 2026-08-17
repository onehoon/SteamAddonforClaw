using SteamInputAddonforClaw.Lifecycle;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class NativeMessageLoopTests
{
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
}
