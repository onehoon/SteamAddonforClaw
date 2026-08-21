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
}
