using SteamInputAddonforClaw.Contracts.Frontend;
using SteamInputAddonforClaw.Views;
using Xunit;

namespace SteamInputAddonforClaw.UiTests;

public sealed class DeveloperBackButtonDiagnosticUiTests
{
    [Fact]
    public void Diagnostic_summary_stays_compact_for_a_very_large_transition_list()
    {
        var transitions = Enumerable.Range(0, 128)
            .Select(index => new FrontendRawDirectInputButtonIndexTransition(index, index + 1, index + 2))
            .ToArray();
        var result = DiagnosticResult(FrontendRawDirectInputButtonTransitionOutcome.Completed, transitions);

        var text = DeveloperPage.FormatMsiBackButtonDiagnosticResult(result);

        Assert.Contains("128 changed index(es)", text, StringComparison.Ordinal);
        Assert.Contains("0(1+/2-)", text, StringComparison.Ordinal);
        Assert.DoesNotContain("127(128+/129-)", text, StringComparison.Ordinal);
        Assert.True(text.Length < 700, $"Diagnostic summary was unexpectedly long: {text.Length} characters.");
    }

    [Theory]
    [InlineData(FrontendRawDirectInputButtonTransitionOutcome.Completed)]
    [InlineData(FrontendRawDirectInputButtonTransitionOutcome.NoRawButtonTransitionsObserved)]
    [InlineData(FrontendRawDirectInputButtonTransitionOutcome.Cancelled)]
    [InlineData(FrontendRawDirectInputButtonTransitionOutcome.Unavailable)]
    public void Diagnostic_summary_keeps_empty_cancelled_and_unavailable_results_legible(
        FrontendRawDirectInputButtonTransitionOutcome outcome)
    {
        var result = DiagnosticResult(outcome, []);

        var text = DeveloperPage.FormatMsiBackButtonDiagnosticResult(result);

        Assert.Contains("Model: msi.claw.a2vm.8", text, StringComparison.Ordinal);
        Assert.Contains("M1 0x00BA", text, StringComparison.Ordinal);
        Assert.Contains("M2 0x0163", text, StringComparison.Ordinal);
        Assert.Contains(outcome.ToString(), text, StringComparison.Ordinal);
        Assert.Contains("no changed indices", text, StringComparison.Ordinal);
        Assert.True(text.Length < 700);
    }

    [Fact]
    public void Manual_restore_summary_reports_each_write_sync_readback_and_raw_input_separately()
    {
        var result = FrontendMsiBackButtonManualRestoreResult.Unavailable("Manual action unavailable.") with
        {
            Outcome = FrontendMsiBackButtonManualRestoreOutcome.PartialVolatileWritePossible,
            RawInput = new(FrontendRawDirectInputButtonTransitionOutcome.Completed, "partial", 10_000, 10_000,
                128, 1, 1, [new(15, 1, 1)], true, false, true, false),
            M1 = new("M1", 0x00BA, FrontendMsiBackButtonProfileReadOutcome.ReadbackParsed,
                FrontendMsiBackButtonProfileClassification.ObservedLinuxRightBraceCandidate,
                "01040059FFFFFFFF", true, true, null, null, null),
        };

        var text = DeveloperPage.FormatMsiBackButtonManualRestoreResult(result);

        Assert.Contains("PartialVolatileWritePossible", text, StringComparison.Ordinal);
        Assert.Contains("M1 candidate=True; 01040059FFFFFFFF → n/a", text, StringComparison.Ordinal);
        Assert.Contains("M2 candidate=False; n/a → n/a", text, StringComparison.Ordinal);
        Assert.Contains("ROM sync issued/succeeded=False/False", text, StringComparison.Ordinal);
        Assert.Contains("readbacks=False, DirectInput headers verified=False", text, StringComparison.Ordinal);
        Assert.Contains("raw index15=True, index16=False, both=False", text, StringComparison.Ordinal);
    }

    private static FrontendMsiBackButtonDiagnosticResult DiagnosticResult(
        FrontendRawDirectInputButtonTransitionOutcome outcome,
        IReadOnlyList<FrontendRawDirectInputButtonIndexTransition> transitions)
    {
        var raw = new FrontendRawDirectInputButtonTransitionResult(
            outcome, "Diagnostic result", 10_000, 10_000, 128, transitions.Count, 1250,
            transitions, false, false, false, false);
        return new(
            "msi.claw.a2vm.8", 0x0230, "Diagnostic complete.",
            new("M1", 0x00BA, 0x01, 0x08, FrontendMsiBackButtonProfileReadOutcome.ReadbackParsed,
                FrontendMsiBackButtonProfileClassification.ObservedLinuxRightBraceCandidate, "ReadbackParsed"),
            new("M2", 0x0163, 0x01, 0x08, FrontendMsiBackButtonProfileReadOutcome.ReadbackParsed,
                FrontendMsiBackButtonProfileClassification.ObservedLinuxLeftBraceCandidate, "ReadbackParsed"),
            raw);
    }
}
