using SteamInputAddonforClaw.Diagnostics.GordonDPad;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class GordonHidCandidateSelectorTests
{
    private static GordonHidCandidate Candidate(string path, string? instanceId, uint devInst = 0) =>
        new(path, instanceId, devInst, 0x28DE, 0x1102, 0xFF00, 0x01, 64);

    [Fact]
    public void Select_NoCandidates_ReturnsNoneFound()
    {
        var result = GordonHidCandidateSelector.Select([], new HashSet<string>(), _ => []);

        Assert.Equal(GordonHidSelectionStatus.NoneFound, result.Status);
        Assert.Null(result.Selected);
    }

    [Fact]
    public void Select_ExactOwnedInstanceIdMatch_IsPreferred()
    {
        var owned = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { @"HID\VID_28DE&PID_1102\OWNED" };
        var candidates = new[]
        {
            Candidate(@"\\?\hid#stale", @"HID\VID_28DE&PID_1102\STALE"),
            Candidate(@"\\?\hid#owned", @"HID\VID_28DE&PID_1102\OWNED"),
        };

        var result = GordonHidCandidateSelector.Select(candidates, owned, _ => []);

        Assert.Equal(GordonHidSelectionStatus.Selected, result.Status);
        Assert.True(result.OwnershipConfirmed);
        Assert.Equal(@"\\?\hid#owned", result.Selected!.Value.DevicePath);
    }

    [Fact]
    public void Select_AncestorMatch_IsPreferredOverAnUnrelatedSingleCandidateHeuristic()
    {
        var owned = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { @"USB\VID_28DE&PID_1102\PARENT" };
        var candidate = Candidate(@"\\?\hid#child", @"HID\VID_28DE&PID_1102&COL01\CHILD", devInst: 7);

        var result = GordonHidCandidateSelector.Select([candidate], owned, c => c.DevInst == 7 ? [@"USB\VID_28DE&PID_1102\PARENT"] : []);

        Assert.Equal(GordonHidSelectionStatus.Selected, result.Status);
        Assert.True(result.OwnershipConfirmed);
    }

    [Fact]
    public void Select_SingleCandidateWithNoCorrelation_IsBestEffortSelectedButUnconfirmed()
    {
        var owned = new HashSet<string>(StringComparer.OrdinalIgnoreCase); // nothing owned yet (e.g. Gordon just created)
        var candidate = Candidate(@"\\?\hid#only", @"HID\VID_28DE&PID_1102\ONLY");

        var result = GordonHidCandidateSelector.Select([candidate], owned, _ => []);

        Assert.Equal(GordonHidSelectionStatus.Selected, result.Status);
        Assert.False(result.OwnershipConfirmed);
        Assert.Equal(candidate, result.Selected);
    }

    [Fact]
    public void Select_MultipleCandidatesNoneCorrelated_IsAmbiguous()
    {
        // e.g. a real Steam Controller (28DE:1102) plugged in alongside the Addon's own Gordon, and the
        // owned instance ID doesn't match either candidate or its ancestors.
        var owned = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { @"USB\VID_28DE&PID_1102\SOMETHING_ELSE" };
        var candidates = new[]
        {
            Candidate(@"\\?\hid#a", @"HID\VID_28DE&PID_1102\A"),
            Candidate(@"\\?\hid#b", @"HID\VID_28DE&PID_1102\B"),
        };

        var result = GordonHidCandidateSelector.Select(candidates, owned, _ => []);

        Assert.Equal(GordonHidSelectionStatus.Ambiguous, result.Status);
        Assert.Null(result.Selected);
        Assert.Equal(2, result.AllCandidates.Count);
    }

    [Fact]
    public void Select_StaleUnownedSingleMatchIsStillBestEffortSelectedWhenNoOwnedIdsAreKnown()
    {
        // With no owned instance IDs at all (e.g. diagnostic started before Gordon creation finished),
        // a lone VID/PID/usage match is the best-effort choice -- this is documented as unconfirmed, not
        // silently treated as certain.
        var candidate = Candidate(@"\\?\hid#lone", null);
        var result = GordonHidCandidateSelector.Select([candidate], new HashSet<string>(), _ => []);

        Assert.Equal(GordonHidSelectionStatus.Selected, result.Status);
        Assert.False(result.OwnershipConfirmed);
    }
}
