using System.Text.RegularExpressions;
using SteamInputAddonforClaw.Hosting;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class RuntimeProcessApplicationShutdownTests
{
    [Fact]
    public void Runtime_message_loop_exit_is_only_triggered_by_user_or_uninstall_lifecycle_actions()
    {
        // PR2.5 section 4.4 / 16.5: closing the frontend window must not stop the Runtime process.
        // The only paths that begin Runtime shutdown + loop exit are the tray/user Exit, tray
        // Restart, and the explicit uninstall request -- there is no frontend-disconnect path.
        var source = File.ReadAllText(Path.Combine(RepositoryRoot(), "src/SteamInputAddonforClaw/Hosting/RuntimeProcessApplication.cs"));

        // One definition + exactly three call sites: RequestExit, RequestRestart,
        // RequestExitForUninstall -- the only user/uninstall lifecycle paths.
        Assert.Equal(4, Regex.Matches(source, "BeginShutdownAndRequestLoopExit").Count);
        foreach (var method in new[] { "RequestExit", "RequestRestart", "RequestExitForUninstall" })
            Assert.Contains($"void {method}()", source, StringComparison.Ordinal);
        // No other private method exists that could reach shutdown.
        Assert.Equal(3, Regex.Matches(source, @"private (?:async )?void Request\w+\(\)").Count);

        // No frontend/pipe-disconnect signal reaches this class at all.
        Assert.DoesNotContain("Disconnected", source, StringComparison.Ordinal);
        Assert.DoesNotContain("_frontendServer", source, StringComparison.Ordinal);
        Assert.DoesNotContain("frontendClient", source, StringComparison.Ordinal);
    }

    private static string RepositoryRoot()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent)
            if (File.Exists(Path.Combine(d.FullName, "SteamInputAddonforClaw.slnx"))) return d.FullName;
        throw new DirectoryNotFoundException("Repository root was not found.");
    }

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
