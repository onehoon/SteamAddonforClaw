using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class ControllerLedLifecycleContractTests
{
    [Fact]
    public void Startup_applies_persisted_led_after_owned_input_is_healthy_and_before_presentation()
    {
        var host = ReadHost();
        var startup = Method(host, "private async Task TryStartDisabledModeControllerAsync(", "private VirtualOutput.Viiper.CanonicalViiperRuntime? LoadAndInitializeCanonicalViiper()");
        var liveSourceCheck = startup.IndexOf("if (source is null || !source.IsRunning)", StringComparison.Ordinal);
        var apply = startup.IndexOf("ApplyOwnedControllerLedSettingsAsync(startupSettings.ControllerLed", StringComparison.Ordinal);
        var presentation = startup.IndexOf("presentation.AttachInitialAsync", StringComparison.Ordinal);

        Assert.True(liveSourceCheck >= 0 && apply > liveSourceCheck && presentation > apply);
        Assert.Contains("controllerLedAvailable: startupResult.HardwareSupported", host, StringComparison.Ordinal);
        Assert.Contains("startupResult.CenterMStartupState == FrontendCenterMStartupState.Disabled", host, StringComparison.Ordinal);
    }

    [Fact]
    public void Only_successful_real_physical_recovery_reapplies_and_authority_gate_fails_closed()
    {
        var host = ReadHost();
        var recovery = Method(host, "private async Task RecoverOwnedControllerPhysicalInputAsync(", "private async Task StartOverlayWarmupAsync()");
        var successGate = recovery.IndexOf("if (result.IsOwned && result.Reason != \"RecoveryNotNeeded\")", StringComparison.Ordinal);
        var apply = recovery.IndexOf("ApplyOwnedControllerLedSettingsAsync(", StringComparison.Ordinal);
        var reconcile = recovery.IndexOf("RequestControllerPresentationReconcile(\"PhysicalInputRecovered\")", StringComparison.Ordinal);
        var applyHelper = Method(host, "private async Task ApplyOwnedControllerLedSettingsAsync(", "private async Task StartOverlayWarmupAsync()");

        Assert.True(successGate >= 0 && apply > successGate && reconcile > apply);
        Assert.Contains("_centerMStartupControl?.Capture().State != FrontendCenterMStartupState.Disabled", applyHelper, StringComparison.Ordinal);
        Assert.Contains("physical?.LiveInputSource is not { IsRunning: true }", applyHelper, StringComparison.Ordinal);
        Assert.Contains("physical.OwnedPhysicalIdentity is not { } identity", applyHelper, StringComparison.Ordinal);
        Assert.Contains("new SteamInputAddonforClaw.Devices.MSI.Claw.MsiClawControlHidResolver()", applyHelper, StringComparison.Ordinal);
    }

    [Fact]
    public void Presentation_and_resume_reconcile_paths_do_not_write_leds()
    {
        var host = ReadHost();
        var presentation = Method(host, "private async Task ReconcileControllerPresentationAsync(", "private Task<bool> QuiesceFull1902PresentationForSuspendAsync(");
        var resume = Method(host, "private void OnPowerResumeObserved()", "internal static async Task ReconcilePerformanceAfterResumeAsync(");

        Assert.DoesNotContain("ApplyOwnedControllerLedSettingsAsync", presentation, StringComparison.Ordinal);
        Assert.DoesNotContain("ApplyOwnedControllerLedSettingsAsync", resume, StringComparison.Ordinal);
        Assert.Contains("RecoverLostInputAsync", host, StringComparison.Ordinal);
        Assert.Contains("ApplyOwnedControllerLedSettingsAsync(", host, StringComparison.Ordinal);
    }

    private static string ReadHost()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "SteamInputAddonforClaw.slnx")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        return File.ReadAllText(Path.Combine(directory!.FullName, "src", "SteamInputAddonforClaw", "Hosting", "AddonProcessHost.cs"));
    }

    private static string Method(string source, string startToken, string endToken)
    {
        var start = source.IndexOf(startToken, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Could not find method start: {startToken}");
        var end = source.IndexOf(endToken, start + startToken.Length, StringComparison.Ordinal);
        Assert.True(end > start, $"Could not find method end: {endToken}");
        return source[start..end];
    }
}
