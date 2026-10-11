using SteamInputAddonforClaw.Contracts.Frontend;
using SteamInputAddonforClaw.Hosting;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class MsiClawManualRestoreHostContractTests
{
    [Fact]
    public void Manual_restore_reuses_the_owned_input_poll_worker_without_acquiring_or_switching_mode()
    {
        var host = ReadSource("src", "SteamInputAddonforClaw", "Hosting", "AddonProcessHost.cs");
        var method = Method(host, "private async Task<FrontendMsiBackButtonManualRestoreResult> RunMsiBackButtonManualRestoreAsync(");

        Assert.Contains("var physical = _physicalOwnership;", method, StringComparison.Ordinal);
        Assert.Contains("var ownedInput = physical?.LiveInputSource;", method, StringComparison.Ordinal);
        Assert.Contains("ownedInput.RunRawDirectInputButtonTransitionDiagnosticAsync(cancellationToken)", method, StringComparison.Ordinal);
        Assert.DoesNotContain("new MsiClawInputSource", method, StringComparison.Ordinal);
        Assert.DoesNotContain("ReadState(", method, StringComparison.Ordinal);
        Assert.DoesNotContain("AcquireAsync(", method, StringComparison.Ordinal);
        Assert.DoesNotContain("SwitchModeAsync(", method, StringComparison.Ordinal);
        Assert.DoesNotContain("0x24", method, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false, false, FrontendRawDirectInputButtonTransitionOutcome.NoRawButtonTransitionsObserved, false)]
    [InlineData(true, false, FrontendRawDirectInputButtonTransitionOutcome.Completed, false)]
    [InlineData(false, true, FrontendRawDirectInputButtonTransitionOutcome.Completed, false)]
    [InlineData(true, true, FrontendRawDirectInputButtonTransitionOutcome.Completed, true)]
    public void Manual_restore_requires_both_independent_raw_button_transitions(
        bool button15Observed,
        bool button16Observed,
        FrontendRawDirectInputButtonTransitionOutcome rawOutcome,
        bool expectedRestored)
    {
        IReadOnlyList<FrontendRawDirectInputButtonIndexTransition> transitions = [];
        if (button15Observed)
            transitions = [new(15, 1, 1)];
        if (button16Observed)
            transitions = [.. transitions, new(16, 1, 1)];
        var raw = new FrontendRawDirectInputButtonTransitionResult(
            rawOutcome, "capture", 10_000, 10_000, 128, transitions.Count, 1,
            transitions, button15Observed, button16Observed, button15Observed, button16Observed);
        var input = VerifiedProfileResult(raw);

        var result = AddonProcessHost.ApplyManualRestoreRawInputEvidence(input);

        Assert.Equal(button15Observed && button16Observed, result.RawInput15_16Observed);
        Assert.Equal(expectedRestored
            ? FrontendMsiBackButtonManualRestoreOutcome.Restored
            : FrontendMsiBackButtonManualRestoreOutcome.Unverified, result.Outcome);
        Assert.Equal(button15Observed, result.RawInput.Button15PressedAndReleasedObserved);
        Assert.Equal(button16Observed, result.RawInput.Button16PressedAndReleasedObserved);
    }

    [Fact]
    public void Manual_restore_does_not_promote_when_profile_readback_does_not_match_directinput()
    {
        var raw = new FrontendRawDirectInputButtonTransitionResult(
            FrontendRawDirectInputButtonTransitionOutcome.Completed, "capture", 10_000, 10_000, 128, 2, 1,
            [new(15, 1, 1), new(16, 1, 1)], true, true, true, true);
        var input = VerifiedProfileResult(raw) with
        {
            M2 = new("M2", 0x0163, FrontendMsiBackButtonProfileReadOutcome.ReadbackParsed,
                FrontendMsiBackButtonProfileClassification.OtherFormat, "01040058FFFFFFFF", true, true,
                FrontendMsiBackButtonProfileReadOutcome.ReadbackParsed,
                FrontendMsiBackButtonProfileClassification.OtherFormat, "01040058FFFFFFFF"),
        };

        var result = AddonProcessHost.ApplyManualRestoreRawInputEvidence(input);

        Assert.False(result.ProfileReadbackVerified);
        Assert.Equal(FrontendMsiBackButtonManualRestoreOutcome.Unverified, result.Outcome);
    }

    private static FrontendMsiBackButtonManualRestoreResult VerifiedProfileResult(
        FrontendRawDirectInputButtonTransitionResult raw) => new(
            FrontendMsiBackButtonManualRestoreOutcome.Unverified, "msi.claw.a2vm.8", 0x0230,
            "Profile readback is not physical input verification.",
            new("M1", 0x00BA, FrontendMsiBackButtonProfileReadOutcome.ReadbackParsed,
                FrontendMsiBackButtonProfileClassification.ObservedLinuxRightBraceCandidate, "01040059FFFFFFFF", true, true,
                FrontendMsiBackButtonProfileReadOutcome.ReadbackParsed,
                FrontendMsiBackButtonProfileClassification.OtherFormat, "01000059FFFFFFFF"),
            new("M2", 0x0163, FrontendMsiBackButtonProfileReadOutcome.ReadbackParsed,
                FrontendMsiBackButtonProfileClassification.ObservedLinuxLeftBraceCandidate, "01040058FFFFFFFF", true, true,
                FrontendMsiBackButtonProfileReadOutcome.ReadbackParsed,
                FrontendMsiBackButtonProfileClassification.OtherFormat, "01000058FFFFFFFF"),
            true, true, true, raw);

    private static string ReadSource(params string[] parts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "SteamInputAddonforClaw.slnx")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        return File.ReadAllText(Path.Combine([directory!.FullName, .. parts])).Replace("\r\n", "\n", StringComparison.Ordinal);
    }

    private static string Method(string source, string marker)
    {
        var start = source.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Could not find source marker: {marker}");
        var end = source.IndexOf("\n    private ", start + marker.Length, StringComparison.Ordinal);
        var internalEnd = source.IndexOf("\n    internal static ", start + marker.Length, StringComparison.Ordinal);
        if (end < 0 || internalEnd >= 0 && internalEnd < end) end = internalEnd;
        if (end < 0) end = source.Length;
        return source[start..end];
    }
}
