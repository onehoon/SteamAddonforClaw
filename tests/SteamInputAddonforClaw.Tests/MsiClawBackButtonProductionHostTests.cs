using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class MsiClawBackButtonProductionHostTests
{
    [Fact]
    public void Initial_and_deferred_owned_sessions_prepare_profiles_after_presentation_before_LED_and_vibration()
    {
        var host = ReadHost();
        var method = Method(host,
            "private async Task<InitialControllerCommitResult> CompleteInitialControllerOwnershipAsync(",
            "private async Task RunInitialControllerAcquisitionAsync(");

        var attach = method.IndexOf("context.Presentation.AttachInitialAsync(", StringComparison.Ordinal);
        var gate = method.IndexOf("if (presentationResult.Succeeded)", StringComparison.Ordinal);
        var prepare = method.IndexOf("PrepareOwnedMsiBackButtonProfileAsync(", StringComparison.Ordinal);
        var led = method.IndexOf("ApplyOwnedControllerLedSettingsAsync(", prepare, StringComparison.Ordinal);
        var vibration = method.IndexOf("ApplyOwnedControllerVibrationSettingsAsync(", led, StringComparison.Ordinal);

        Assert.True(attach >= 0 && attach < gate && gate < prepare && prepare < led && led < vibration,
            "the optional profile prepare must follow the initial presentation and precede existing LED/vibration applies");
        Assert.Equal(1, CountOccurrences(method, "PrepareOwnedMsiBackButtonProfileAsync("));
        Assert.Contains("? \"DeferredInitialAcquisition\"", method, StringComparison.Ordinal);
        Assert.Contains(": \"Startup\"", method, StringComparison.Ordinal);
        Assert.Contains("InitialPresentationNotEstablished", method, StringComparison.Ordinal);
    }

    [Fact]
    public void Physical_reacquisition_prepares_once_only_for_a_newly_owned_recovery_before_LED_and_vibration()
    {
        var host = ReadHost();
        var method = Method(host,
            "private async Task RecoverOwnedControllerPhysicalInputAsync(",
            "private async Task PrepareOwnedMsiBackButtonProfileAsync(");

        var admission = method.IndexOf("if (result.IsOwned && result.Reason != \"RecoveryNotNeeded\")", StringComparison.Ordinal);
        var prepare = method.IndexOf("PrepareOwnedMsiBackButtonProfileAsync(", admission, StringComparison.Ordinal);
        var led = method.IndexOf("ApplyOwnedControllerLedSettingsAsync(", prepare, StringComparison.Ordinal);
        var vibration = method.IndexOf("ApplyOwnedControllerVibrationSettingsAsync(", led, StringComparison.Ordinal);

        Assert.True(admission >= 0 && admission < prepare && prepare < led && led < vibration,
            "profile preparation must stay inside newly-owned physical recovery and precede the existing hardware applies");
        Assert.Equal(1, CountOccurrences(method, "PrepareOwnedMsiBackButtonProfileAsync("));
        Assert.True(CountOccurrences(host, "PrepareOwnedMsiBackButtonProfileAsync(") == 3,
            "Only initial ownership, owned physical recovery, and the helper declaration may reference production preparation.");
        Assert.DoesNotContain("PrepareOwnedMsiBackButtonProfileAsync(", Method(host,
            "private async Task ReapplyOwnedControllerHardwareSettingsAfterResumeAsync(",
            "private void OnAcDcPowerSourceChanged("), StringComparison.Ordinal);
    }

    [Fact]
    public void Production_prepare_reuses_strong_current_owner_and_centerM_disabled_gates_without_input_reacquisition()
    {
        var host = ReadHost();
        var method = Method(host,
            "private async Task PrepareOwnedMsiBackButtonProfileAsync(",
            "private async Task ApplyOwnedControllerLedSettingsAsync(");

        Assert.Contains("_processShutdownStarted", method, StringComparison.Ordinal);
        Assert.Contains("_controllerOwnershipReleaseStarted", method, StringComparison.Ordinal);
        Assert.Contains("_ownedControllerRecoveryBlockedByCleanup", method, StringComparison.Ordinal);
        Assert.Contains("_presentationOwnership?.IsSuspendPaused != true", method, StringComparison.Ordinal);
        Assert.Contains("FrontendCenterMStartupState.Disabled", method, StringComparison.Ordinal);
        Assert.Contains("MsiClawIdentityConfidence.Strong", method, StringComparison.Ordinal);
        Assert.Contains("MsiClawHardware.DirectInputProductId", method, StringComparison.Ordinal);
        Assert.Contains("physical.LiveInputSource", method, StringComparison.Ordinal);
        Assert.Contains("PrepareDirectInputProfileAsync(", method, StringComparison.Ordinal);
        Assert.DoesNotContain("AcquireAsync(", method, StringComparison.Ordinal);
        Assert.DoesNotContain("ReadState(", method, StringComparison.Ordinal);
        Assert.DoesNotContain("0x24", method, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Removed_developer_M1_M2_entrypoints_and_raw_capture_are_not_present_in_runtime_sources()
    {
        var root = FindRepositoryRoot();
        var sourceFiles = Directory.EnumerateFiles(Path.Combine(root, "src"), "*.cs", SearchOption.AllDirectories);
        var sources = string.Join("\n", sourceFiles.Select(File.ReadAllText));

        Assert.DoesNotContain("RunMsiBackButtonProfileAndRawInputDiagnosticAsync", sources, StringComparison.Ordinal);
        Assert.DoesNotContain("RunMsiBackButtonManualRestoreAsync", sources, StringComparison.Ordinal);
        Assert.DoesNotContain("RunRawDirectInputButtonTransitionDiagnosticAsync", sources, StringComparison.Ordinal);
        Assert.DoesNotContain("MsiClawRawButtonTransitionCollector", sources, StringComparison.Ordinal);
    }

    private static string ReadHost() => File.ReadAllText(Path.Combine(FindRepositoryRoot(),
        "src/SteamInputAddonforClaw/Hosting/AddonProcessHost.cs"));

    private static string Method(string source, string signature, string nextSignature)
    {
        var start = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Expected method was not found: {signature}");
        var end = source.IndexOf(nextSignature, start + signature.Length, StringComparison.Ordinal);
        Assert.True(end > start, $"Expected method boundary was not found after: {signature}");
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

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "SteamInputAddonforClaw.slnx")))
            directory = directory.Parent;

        Assert.NotNull(directory);
        return directory!.FullName;
    }
}
