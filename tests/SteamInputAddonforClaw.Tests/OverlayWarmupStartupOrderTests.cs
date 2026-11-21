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
        Assert.Contains("if (!IsVerifiedMsiClawStartup(startupResult)\n            || startupResult.CenterMStartupState != FrontendCenterMStartupState.Disabled)\n            return;",
            controllerStartup, StringComparison.Ordinal);
    }

    [Fact]
    public void Steam_artwork_hints_replay_runtime_authority_without_joining_startup_or_visibility_paths()
    {
        var host = HostSource();
        var controller = File.ReadAllText(Path.Combine(RepositoryRoot(), "src/SteamInputAddonforClaw/Lifecycle/OverlayProcessController.cs"))
            .Replace("\r\n", "\n", StringComparison.Ordinal);
        var actualAppIdChanged = Method(host, "private void OnActualRunningAppIdChanged(uint appId)", "private void OnActiveXboxGameChanged(");
        var xboxChanged = Method(host, "private void OnActiveXboxGameChanged(ActiveXboxGame? activeGame)", "private void RefreshVisibleOverlayForActiveTargetChange()");
        var publish = Method(controller, "private async Task PublishCurrentSteamArtworkHintAsync()", "private void OnNoRunningGameProfileRecheckRequested(");
        var startCore = Method(controller, "private async Task<bool> StartCoreAsync(", "private async Task PublishCurrentSteamArtworkHintAsync()");
        var visibility = Method(controller, "private async Task<bool> SetVisibilityAsync(", "// OQ4 section 10:");

        Assert.Contains("BindSteamArtworkPrewarmAuthority(() => _runtimeHost?.ActualRunningAppId ?? 0)", host, StringComparison.Ordinal);
        Assert.Contains("_overlayController.NotifySteamArtworkTargetChanged();", actualAppIdChanged, StringComparison.Ordinal);
        Assert.Contains("_overlayController.NotifySteamArtworkTargetChanged();", xboxChanged, StringComparison.Ordinal);
        Assert.DoesNotContain("await _overlayController.NotifySteamArtworkTargetChanged()", host, StringComparison.Ordinal);
        Assert.Contains("NotifySteamArtworkTargetChanged();", startCore, StringComparison.Ordinal);
        Assert.Contains("if (_stopping) return;", publish, StringComparison.Ordinal);
        Assert.Contains("if (visible)", publish, StringComparison.Ordinal);
        Assert.Contains("server.State != OverlayState.Hidden", publish, StringComparison.Ordinal);
        Assert.Contains("captureAppId?.Invoke()", publish, StringComparison.Ordinal);
        Assert.Contains("SendSteamArtworkPrewarmAsync(appId)", publish, StringComparison.Ordinal);
        Assert.Contains("else\n                NotifySteamArtworkTargetChanged();", visibility, StringComparison.Ordinal);
        Assert.DoesNotContain("SteamGameArtworkResolver", controller, StringComparison.Ordinal);
    }

    private static string HostSource()
    {
        return File.ReadAllText(Path.Combine(RepositoryRoot(), "src/SteamInputAddonforClaw/Hosting/AddonProcessHost.cs"))
            .Replace("\r\n", "\n", StringComparison.Ordinal);
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "SteamInputAddonforClaw.slnx")))
            directory = directory.Parent;
        return directory!.FullName;
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
