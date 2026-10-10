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
            "private async Task<InitialControllerCommitResult> CompleteInitialControllerOwnershipAsync(");
        var continuation = Method(host,
            "private async Task<InitialControllerCommitResult> CompleteInitialControllerOwnershipAsync(",
            "private async Task RunInitialControllerAcquisitionAsync(");
        var admissionGate = startup.IndexOf("if (startupResult.DisabledBootAdmission?.IsReady != true)", StringComparison.Ordinal);
        var viiperReadyGate = startup.IndexOf("if (presentation.ViiperState != VirtualOutput.Viiper.CanonicalViiperRuntimeState.Ready)", StringComparison.Ordinal);
        var acquisition = startup.IndexOf("var acquired = await owner.AcquireAsync(", StringComparison.Ordinal);
        var continuationCall = startup.IndexOf("CompleteInitialControllerOwnershipAsync(", acquisition, StringComparison.Ordinal);
        var liveSource = continuation.IndexOf("if (source is null || !source.IsRunning)", StringComparison.Ordinal);
        var led = continuation.IndexOf("ApplyOwnedControllerLedSettingsAsync(context.StartupSettings.ControllerLed", StringComparison.Ordinal);
        var vibration = continuation.IndexOf("ApplyOwnedControllerVibrationSettingsAsync(\n            context.StartupSettings.ControllerVibration", StringComparison.Ordinal);
        var presentation = continuation.IndexOf("context.Presentation.AttachInitialAsync", StringComparison.Ordinal);

        Assert.True(admissionGate >= 0 && viiperReadyGate > admissionGate && acquisition > viiperReadyGate && continuationCall > acquisition);
        Assert.Contains("return;", startup[viiperReadyGate..acquisition], StringComparison.Ordinal);
        Assert.True(liveSource >= 0 && led > liveSource && vibration > led && presentation > vibration);
        Assert.Contains("context.StartupSettings.ControllerVibration", continuation[vibration..], StringComparison.Ordinal);
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
            "private async Task<bool> QuiesceFull1902PresentationForSuspendAsync(");
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

    [Fact]
    public void Manual_restore_rejects_an_active_terminal_rumble_diagnostic_before_retiring_presentation()
    {
        var host = ReadHost();
        var recovery = Method(host,
            "private async Task<FrontendDeveloperRumbleRearmResult> RunDeveloperRumbleRearmCoreAsync(",
            "private string? GetDeveloperRumbleRearmAdmissionFailure(");
        var capture = recovery.IndexOf("CaptureXbox360RumbleLoopDiagnosticAsync(", StringComparison.Ordinal);
        var runningGuard = recovery.IndexOf("rumbleLoop.State == FrontendXbox360RumbleLoopState.Running", StringComparison.Ordinal);
        var retire = recovery.IndexOf("presentation.RunDeveloperRumbleRearmAsync(", StringComparison.Ordinal);

        Assert.True(capture >= 0 && runningGuard > capture && retire > runningGuard);
        Assert.Contains("Stop the Xbox360 terminal STOP diagnostic before restoring vibration.", recovery, StringComparison.Ordinal);
    }

    private static string ReadHost()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "SteamInputAddonforClaw.slnx")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        return File.ReadAllText(Path.Combine(directory!.FullName, "src", "SteamInputAddonforClaw", "Hosting", "AddonProcessHost.cs"))
            .Replace("\r\n", "\n", StringComparison.Ordinal);
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
