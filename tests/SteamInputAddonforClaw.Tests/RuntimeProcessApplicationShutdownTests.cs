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
        // Tray restart/overlay cleanup work order section 6: ordinary tray Exit is removed -- the only
        // paths that begin Runtime shutdown + loop exit are tray Restart and the explicit uninstall
        // request -- there is no frontend-disconnect path.
        var source = File.ReadAllText(Path.Combine(RepositoryRoot(), "src/SteamInputAddonforClaw/Hosting/RuntimeProcessApplication.cs"));

        // One definition + exactly two call sites: RequestRestart, RequestExitForUninstall -- the
        // only remaining user/uninstall lifecycle paths.
        Assert.Equal(3, Regex.Matches(source, "BeginShutdownAndRequestLoopExit").Count);
        foreach (var method in new[] { "RequestRestart", "RequestExitForUninstall" })
            Assert.Contains($"void {method}()", source, StringComparison.Ordinal);
        Assert.DoesNotContain("void RequestExit()", source, StringComparison.Ordinal);
        // No other private method exists that could reach shutdown.
        Assert.Equal(2, Regex.Matches(source, @"private (?:async )?void Request\w+\(\)").Count);

        // No frontend/pipe-disconnect signal reaches this class at all.
        Assert.DoesNotContain("Disconnected", source, StringComparison.Ordinal);
        Assert.DoesNotContain("_frontendServer", source, StringComparison.Ordinal);
        Assert.DoesNotContain("frontendClient", source, StringComparison.Ordinal);
    }

    [Fact] // Tray restart/overlay cleanup work order section 7/9: Restart uses the restart-specific
           // safety evaluation and re-enters the ordinary startup path -- it launches the normal
           // executable with --restart rather than calling any update API directly.
    public void Restart_uses_restart_specific_safety_and_launches_the_normal_executable_with_restart_flag()
    {
        var source = File.ReadAllText(Path.Combine(RepositoryRoot(), "src/SteamInputAddonforClaw/Hosting/RuntimeProcessApplication.cs"));
        var method = source[source.IndexOf("private void RequestRestart()", StringComparison.Ordinal)..];
        method = method[..method.IndexOf("\n    private ", StringComparison.Ordinal)];

        Assert.Contains("_processHost?.EvaluateUserRestart()", method, StringComparison.Ordinal);
        Assert.DoesNotContain("EvaluateUserTermination", method, StringComparison.Ordinal);
        Assert.Contains("new ProcessStartInfo(executablePath)", method, StringComparison.Ordinal);
        Assert.Contains("restartInfo.ArgumentList.Add(\"--restart\")", method, StringComparison.Ordinal);
        // The existing launch arguments are kept, with any pre-existing --restart de-duplicated first.
        Assert.Contains("Environment.GetCommandLineArgs().Skip(1)", method, StringComparison.Ordinal);
        Assert.Contains("!string.Equals(argument, \"--restart\", StringComparison.OrdinalIgnoreCase)", method, StringComparison.Ordinal);
        // No update-checker/downloader API is invoked directly here -- Restart only re-enters the
        // ordinary startup path, which already runs SilentUpdateGate.
        foreach (var forbidden in new[] { "IUpdateClient", "IUpdateGate", "SilentUpdateGate", "WaitExitThenApplyUpdates" })
            Assert.DoesNotContain(forbidden, method, StringComparison.Ordinal);
    }

    [Fact] // PR12 review [P1]: a stock preparation that does not succeed (or throws) must NOT shut
           // down the only controller Runtime.
    public void Uninstall_shutdown_is_gated_on_a_successful_stock_preparation()
    {
        var source = File.ReadAllText(Path.Combine(RepositoryRoot(), "src/SteamInputAddonforClaw/Hosting/RuntimeProcessApplication.cs"));
        var method = source[source.IndexOf("private void RequestExitForUninstall()", StringComparison.Ordinal)..];
        method = method[..method.IndexOf("\n    private ", StringComparison.Ordinal)];

        // The success gate and both early returns come before the single shutdown call.
        var successGate = method.IndexOf("Succeeded: true", StringComparison.Ordinal);
        var shutdown = method.IndexOf("BeginShutdownAndRequestLoopExit()", StringComparison.Ordinal);
        Assert.True(successGate > 0 && successGate < shutdown);
        Assert.Equal(2, Regex.Matches(method, @"\breturn;").Count); // throw path + failed-result path
        Assert.Contains("remain active", method, StringComparison.Ordinal);
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
