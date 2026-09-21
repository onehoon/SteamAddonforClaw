using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class ClawHudLifecycleContractTests
{
    [Fact]
    public void Optional_ClawHud_startup_is_after_controller_critical_boundary()
    {
        var source = HostSource();
        var deferred = source[source.IndexOf("internal void StartDeferredRuntimeStartup()", StringComparison.Ordinal)..];
        var controller = deferred.IndexOf("await TryStartDisabledModeControllerAsync(", StringComparison.Ordinal);
        var pendingCleared = deferred.IndexOf("Volatile.Write(ref _disabledControllerStartupPending, 0)", controller, StringComparison.Ordinal);
        var clawHud = deferred.IndexOf("StartClawHudStartup();", pendingCleared, StringComparison.Ordinal);
        var update = deferred.IndexOf("StartBackgroundUpdate();", pendingCleared, StringComparison.Ordinal);

        Assert.True(controller >= 0);
        Assert.True(pendingCleared > controller);
        Assert.True(clawHud > pendingCleared);
        Assert.True(update > clawHud);
    }

    [Fact]
    public void Disabled_ClawHud_setting_returns_before_acquisition_or_process_work()
    {
        var source = HostSource();
        var startup = Method(source, "private void StartClawHudStartup()");
        var reconcile = Method(source, "private async Task ReconcileClawHudAsync(");

        Assert.Contains("_runtimeStartupSettings?.ClawHudEnabled != true", startup, StringComparison.Ordinal);
        Assert.Contains("_runtimeStartupSettings?.ClawHudEnabled != true", reconcile, StringComparison.Ordinal);
        Assert.True(reconcile.IndexOf("EnsureClawHudController();", StringComparison.Ordinal)
            > reconcile.IndexOf("ClawHudEnabled != true", StringComparison.Ordinal));
    }

    [Fact]
    public void ClawHud_shutdown_is_observed_after_controller_runtime_teardown()
    {
        var source = HostSource();
        var dispose = Method(source, "public async ValueTask DisposeAsync()");
        var runtimeDispose = dispose.IndexOf("await _runtimeHost.DisposeAsync()", StringComparison.Ordinal);
        var clawHudObserve = dispose.IndexOf("await ObserveClawHudShutdownAsync()", runtimeDispose, StringComparison.Ordinal);

        Assert.True(runtimeDispose >= 0);
        Assert.True(clawHudObserve > runtimeDispose);
        Assert.Contains("_clawHudShutdown = StopClawHudForProcessShutdownAsync();", source, StringComparison.Ordinal);
    }

    private static string HostSource()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "SteamInputAddonforClaw.slnx")))
            directory = directory.Parent;
        return File.ReadAllText(Path.Combine(directory!.FullName, "src/SteamInputAddonforClaw/Hosting/AddonProcessHost.cs"));
    }

    private static string Method(string source, string signature)
    {
        var start = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start >= 0, $"method not found: {signature}");
        var next = source.IndexOf("\n    private ", start + signature.Length, StringComparison.Ordinal);
        return next < 0 ? source[start..] : source[start..next];
    }
}
