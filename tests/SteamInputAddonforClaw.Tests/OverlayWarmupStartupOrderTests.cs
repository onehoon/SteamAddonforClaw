using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class OverlayWarmupStartupOrderTests
{
    [Fact]
    public void Initialize_runtime_binds_overlay_but_does_not_start_warmup()
    {
        var source = HostSource();
        var initialize = Method(source, "internal async Task InitializeRuntimeAsync()", "internal void StartRuntimeEventWatchers()");

        Assert.DoesNotContain("_overlayStartup = StartOverlayWarmupAsync();", initialize, StringComparison.Ordinal);
        Assert.Contains("_frontendControl.StateInvalidated += OnFrontendStateInvalidatedForOverlay;", initialize, StringComparison.Ordinal);
        Assert.Contains("_overlayController.BindQuickSettingsAuthority(", initialize, StringComparison.Ordinal);
    }

    [Fact]
    public void Deferred_start_starts_overlay_warmup_once_after_controller_attempt_and_shutdown_guard()
    {
        var source = HostSource();
        var deferred = Method(source, "internal void StartDeferredRuntimeStartup()", "private void StartClawHudStartup()");
        var controllerAttempt = deferred.IndexOf("await TryStartDisabledModeControllerAsync(", StringComparison.Ordinal);
        var controllerAttemptCompleted = deferred.IndexOf("Volatile.Write(ref _disabledControllerStartupPending, 0);", controllerAttempt, StringComparison.Ordinal);
        var shutdownGuard = deferred.IndexOf("if (cancellationToken.IsCancellationRequested", controllerAttemptCompleted, StringComparison.Ordinal);
        var overlayWarmup = deferred.IndexOf("_overlayStartup = StartOverlayWarmupAsync();", StringComparison.Ordinal);
        var clawHudStartup = deferred.IndexOf("StartClawHudStartup();", StringComparison.Ordinal);
        var powerObservation = deferred.IndexOf("StartPowerObservation();", StringComparison.Ordinal);

        Assert.True(controllerAttempt >= 0);
        Assert.True(controllerAttemptCompleted > controllerAttempt);
        Assert.True(shutdownGuard > controllerAttemptCompleted);
        Assert.True(overlayWarmup >= 0 && overlayWarmup > shutdownGuard);
        Assert.True(clawHudStartup >= 0 && clawHudStartup > overlayWarmup);
        Assert.True(powerObservation >= 0 && powerObservation > clawHudStartup);
        Assert.Equal(1, CountOccurrences(deferred[shutdownGuard..overlayWarmup], "if ("));
        Assert.Equal(1, CountOccurrences(deferred, "_overlayStartup = StartOverlayWarmupAsync();"));
        Assert.DoesNotContain("await _overlayStartup", deferred, StringComparison.Ordinal);
        Assert.Contains("catch (Exception exception)", deferred[..controllerAttemptCompleted], StringComparison.Ordinal);

        var controllerStartup = Method(source,
            "private async Task TryStartDisabledModeControllerAsync(",
            "private async Task RunInitialControllerAcquisitionAsync(");
        Assert.Contains("if (startupResult.CenterMStartupState != FrontendCenterMStartupState.Disabled)\n            return;",
            controllerStartup, StringComparison.Ordinal);
    }

    private static string HostSource()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "SteamInputAddonforClaw.slnx")))
            directory = directory.Parent;
        return File.ReadAllText(Path.Combine(directory!.FullName, "src/SteamInputAddonforClaw/Hosting/AddonProcessHost.cs"))
            .Replace("\r\n", "\n", StringComparison.Ordinal);
    }

    private static string Method(string source, string startSignature, string endSignature)
    {
        var start = source.IndexOf(startSignature, StringComparison.Ordinal);
        Assert.True(start >= 0, $"method not found: {startSignature}");
        var end = source.IndexOf(endSignature, start + startSignature.Length, StringComparison.Ordinal);
        Assert.True(end > start, $"method end not found: {endSignature}");
        return source[start..end];
    }

    private static int CountOccurrences(string source, string value)
    {
        var count = 0;
        var offset = 0;
        while ((offset = source.IndexOf(value, offset, StringComparison.Ordinal)) >= 0)
        {
            count++;
            offset += value.Length;
        }
        return count;
    }
}
