using SteamInputAddonforClaw.Hosting;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class RuntimeProcessApplicationShutdownTests
{
    [Fact]
    public async Task Shutdown_requests_message_loop_exit_only_after_runtime_shutdown()
    {
        var events = new List<string>();
        await RuntimeProcessApplication.RunShutdownBeforeMessageLoopExitAsync(
            async () => { events.Add("ShutdownStarted"); await Task.Yield(); events.Add("ShutdownCompleted"); },
            () => events.Add("LoopExitRequested"));

        Assert.Equal(["ShutdownStarted", "ShutdownCompleted", "LoopExitRequested"], events);
    }

    [Fact]
    public void Failed_message_loop_exit_request_can_be_retried()
    {
        var attempts = 0;
        Assert.False(RuntimeProcessApplication.TryRequestMessageLoopExit(() =>
        {
            attempts++;
            if (attempts == 1) throw new InvalidOperationException("simulated post failure");
        }));
        Assert.True(RuntimeProcessApplication.TryRequestMessageLoopExit(() => attempts++));
        Assert.Equal(2, attempts);
    }
}
