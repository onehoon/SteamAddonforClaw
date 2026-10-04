using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class ControllerVibrationLifecycleContractTests
{
    [Fact]
    public void Startup_applies_vibration_after_healthy_owned_input_and_before_first_presentation()
    {
        var host = ReadHost();
        var startup = Method(host,
            "private async Task TryStartDisabledModeControllerAsync(",
            "private VirtualOutput.Viiper.CanonicalViiperRuntime? LoadAndInitializeCanonicalViiper()");
        var liveSource = startup.IndexOf("if (source is null || !source.IsRunning)", StringComparison.Ordinal);
        var led = startup.IndexOf("ApplyOwnedControllerLedSettingsAsync(startupSettings.ControllerLed", StringComparison.Ordinal);
        var vibration = startup.IndexOf("ApplyOwnedControllerVibrationSettingsAsync(\n                startupSettings.ControllerVibration, \"Startup\"", StringComparison.Ordinal);
        var presentation = startup.IndexOf("presentation.AttachInitialAsync", StringComparison.Ordinal);

        Assert.True(liveSource >= 0 && led > liveSource && vibration > led && presentation > vibration);
        Assert.Contains("startupSettings.ControllerVibration", vibration >= 0 ? startup[vibration..] : string.Empty, StringComparison.Ordinal);
    }

    [Fact]
    public void Only_successful_real_physical_recovery_reapplies_vibration()
    {
        var host = ReadHost();
        var recovery = Method(host,
            "private async Task RecoverOwnedControllerPhysicalInputAsync(",
            "private async Task StartOverlayWarmupAsync()");
        var successGate = recovery.IndexOf("if (result.IsOwned && result.Reason != \"RecoveryNotNeeded\")", StringComparison.Ordinal);
        var led = recovery.IndexOf("ApplyOwnedControllerLedSettingsAsync(", StringComparison.Ordinal);
        var vibration = recovery.IndexOf("ApplyOwnedControllerVibrationSettingsAsync(", StringComparison.Ordinal);
        var presentation = recovery.IndexOf("RequestControllerPresentationReconcile(\"PhysicalInputRecovered\")", StringComparison.Ordinal);

        Assert.True(successGate >= 0 && led > successGate && vibration > led && presentation > vibration);
        Assert.Contains("_runtimeStartupSettings?.ControllerVibration ?? ControllerVibrationSettings.Default", recovery, StringComparison.Ordinal);
    }

    [Fact]
    public void Resume_keeps_immediate_presentation_reconcile_and_reapplies_both_physical_settings_after_one_settle()
    {
        var host = ReadHost();
        var resume = Method(host, "private void OnPowerResumeObserved()", "internal static async Task ReconcilePerformanceAfterResumeAsync(");
        var immediateReconcile = resume.IndexOf("RequestControllerPresentationReconcile(\"PowerResume\")", StringComparison.Ordinal);
        var delayedApply = resume.IndexOf("ReapplyOwnedControllerHardwareSettingsAfterResumeAsync(", StringComparison.Ordinal);
        var reapply = Method(host,
            "private async Task ReapplyOwnedControllerHardwareSettingsAfterResumeAsync(",
            "private void OnAcDcPowerSourceChanged()");

        Assert.True(immediateReconcile >= 0 && delayedApply > immediateReconcile);
        Assert.Equal(1, resume.Split("ReapplyOwnedControllerHardwareSettingsAfterResumeAsync(", StringSplitOptions.None).Length - 1);
        Assert.Contains("Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken)", reapply, StringComparison.Ordinal);
        Assert.Equal(1, reapply.Split("ApplyOwnedControllerLedSettingsAsync(", StringSplitOptions.None).Length - 1);
        Assert.Equal(1, reapply.Split("ApplyOwnedControllerVibrationSettingsAsync(", StringSplitOptions.None).Length - 1);
        Assert.Contains("_runtimeStartupSettings?.ControllerVibration ?? ControllerVibrationSettings.Default", reapply, StringComparison.Ordinal);
    }

    [Fact]
    public void Presentation_reconcile_has_no_vibration_strength_apply_and_host_gate_is_exact()
    {
        var host = ReadHost();
        var presentation = Method(host,
            "private async Task ReconcileControllerPresentationAsync(",
            "private Task<bool> QuiesceFull1902PresentationForSuspendAsync(");
        var apply = Method(host,
            "private async Task<bool> ApplyOwnedControllerVibrationSettingsAsync(",
            "private async Task StartOverlayWarmupAsync()");

        Assert.DoesNotContain("ApplyOwnedControllerVibrationSettingsAsync", presentation, StringComparison.Ordinal);
        Assert.Contains("_centerMStartupControl?.Capture().State != FrontendCenterMStartupState.Disabled", apply, StringComparison.Ordinal);
        Assert.Contains("LiveInputSource is not { IsRunning: true }", apply, StringComparison.Ordinal);
        Assert.Contains("identity.Confidence != MsiClawIdentityConfidence.Strong", apply, StringComparison.Ordinal);
        Assert.Contains("client.IsProductionPairWriteVerified", apply, StringComparison.Ordinal);
        Assert.Contains("client.ApplyAsync(settings, identity, cancellationToken)", apply, StringComparison.Ordinal);
        Assert.Contains("ControllerVibrationSettingsApplied", apply, StringComparison.Ordinal);
        Assert.Contains("ControllerVibrationSettingsApplyFailed", apply, StringComparison.Ordinal);
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
