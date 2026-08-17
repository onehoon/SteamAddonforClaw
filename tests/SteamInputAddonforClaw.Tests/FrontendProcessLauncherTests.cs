using System.Diagnostics;
using SteamInputAddonforClaw.Lifecycle;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class FrontendProcessLauncherTests
{
    [Fact]
    public void Requests_before_ready_coalesce_and_release_once()
    {
        var starts = new List<ProcessStartInfo>();
        var launcher = new FrontendProcessLauncher("C:\\runtime", info => { starts.Add(info); return null; });
        launcher.RequestOpen(FrontendOpenReason.InitialManualLaunch);
        launcher.RequestOpen(FrontendOpenReason.Tray);
        Assert.Empty(starts);
        launcher.MarkRuntimeReady();
        Assert.Single(starts);
    }

    [Fact]
    public void Request_after_ready_starts_immediately()
    {
        var starts = new List<ProcessStartInfo>();
        var launcher = new FrontendProcessLauncher("C:\\runtime", info => { starts.Add(info); return null; });
        launcher.MarkRuntimeReady();
        launcher.RequestOpen(FrontendOpenReason.Tray);
        Assert.Single(starts);
    }

    [Fact]
    public void Stop_before_ready_drops_pending_and_future_requests()
    {
        var starts = new List<ProcessStartInfo>();
        var launcher = new FrontendProcessLauncher("C:\\runtime", info => { starts.Add(info); return null; });
        launcher.RequestOpen(FrontendOpenReason.Tray);
        launcher.StopAcceptingRequests();
        launcher.MarkRuntimeReady();
        launcher.RequestOpen(FrontendOpenReason.RuntimeActivation);
        Assert.Empty(starts);
    }

    [Fact]
    public void Failed_start_is_non_fatal_and_later_request_retries()
    {
        var attempts = 0;
        var launcher = new FrontendProcessLauncher("C:\\runtime", _ =>
        {
            attempts++;
            if (attempts == 1) throw new InvalidOperationException("test");
            return null;
        });
        launcher.MarkRuntimeReady();
        launcher.RequestOpen(FrontendOpenReason.Tray);
        launcher.RequestOpen(FrontendOpenReason.Tray);
        Assert.Equal(2, attempts);
    }

    [Fact]
    public void Uses_final_ui_relative_path()
    {
        var launcher = new FrontendProcessLauncher("C:\\runtime");
        Assert.Equal("C:\\runtime\\ui\\SteamInputAddonforClaw.UI.exe", launcher.ExecutablePath);
    }
}
