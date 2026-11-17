using System.Text.Json;
using System.Xml.Linq;
using SteamInputAddonforClaw.Contracts.Frontend;
using SteamInputAddonforClaw.Views;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class TdpFrontendContractTests
{
    [Fact]
    public void Snapshot_round_trips_and_keeps_independent_limits()
    {
        var value = new FrontendTdpSnapshot(true, true,
            new FrontendTdpConfiguration(true, new(30, 8), new(35, 8)),
            new(8, 35, 8, 45));

        var restored = JsonSerializer.Deserialize<FrontendTdpSnapshot>(JsonSerializer.Serialize(value));

        Assert.Equivalent(value, restored, strict: true);
        Assert.Equal(30, restored!.Configuration!.Ac.Pl1Watts);
        Assert.Equal(8, restored.Configuration.Ac.Pl2Watts);
    }

    [Fact]
    public void Unavailable_snapshot_has_no_invented_configuration()
    {
        var snapshot = FrontendTdpSnapshot.Unavailable;

        Assert.False(snapshot.Available);
        Assert.False(snapshot.PersistenceWritable);
        Assert.False(snapshot.Initialized);
        Assert.Null(snapshot.Configuration);
        Assert.Null(snapshot.Limits);
    }

    [Fact]
    public void Mutation_result_preserves_non_boolean_outcomes()
    {
        var result = new FrontendTdpMutationResult(
            FrontendTdpMutationOutcome.InvalidTarget,
            "outside model ranges",
            new(true, true, null, new(8, 30, 8, 37)));

        Assert.False(result.Succeeded);
        Assert.Equal(FrontendTdpMutationOutcome.InvalidTarget, result.Outcome);
        Assert.Equal("outside model ranges", result.FailureMessage);
    }

    [Fact]
    public void Retired_debounce_generation_cannot_submit_an_enabled_edit()
    {
        Assert.False(DevicePage.TdpDraftPolicy.CanSubmitDebouncedEdit(4, 5, true));
        Assert.False(DevicePage.TdpDraftPolicy.CanSubmitDebouncedEdit(5, 5, false));
        Assert.True(DevicePage.TdpDraftPolicy.CanSubmitDebouncedEdit(5, 5, true));
    }

    [Fact]
    public void Disable_falls_back_to_saved_pairs_when_a_number_box_is_temporarily_blank()
    {
        var saved = new FrontendTdpConfiguration(true, new(20, 30), new(10, 20));
        var result = DevicePage.TdpDraftPolicy.TryBuildToggleConfiguration(false, null, 30, 10, 20, saved);

        Assert.NotNull(result);
        Assert.False(result!.Enabled);
        Assert.Equal(new FrontendTdpPowerPair(20, 30), result.Ac);
        Assert.Equal(new FrontendTdpPowerPair(10, 20), result.Dc);
    }

    [Fact]
    public void Debounced_edit_does_not_fall_back_to_saved_value_when_draft_is_invalid()
    {
        var result = DevicePage.TdpDraftPolicy.TryBuildCompleteConfiguration(true, null, 30, 10, 20);

        Assert.Null(result);
    }

    [Fact]
    public void Older_snapshot_cannot_replace_a_newer_dirty_edit()
    {
        Assert.True(DevicePage.TdpDraftPolicy.ShouldPreserveDirtyDraft(true, 7, 8));
        Assert.False(DevicePage.TdpDraftPolicy.ShouldPreserveDirtyDraft(true, 8, 8));
        Assert.False(DevicePage.TdpDraftPolicy.ShouldPreserveDirtyDraft(false, 7, 8));
    }

    [Theory]
    [InlineData(30, 30, 32)]
    [InlineData(25, 25, 30)]
    [InlineData(44, 43, 45)]
    public void Ex_pl1_edit_preserves_priority_and_bounds(int editedPl1, int expectedPl1, int expectedPl2)
    {
        var beforePl2 = expectedPl2 == 45 ? 45 : 30;
        var result = DevicePage.TdpDraftPolicy.AdjustAfterEdit(true, editedPl1, beforePl2, new(8, 35, 8, 45));
        Assert.Equal(new(expectedPl1, expectedPl2), result);
    }

    [Fact]
    public void Ex_pl2_edit_pulls_pl1_down_and_respects_lower_boundary()
    {
        var limits = new FrontendTdpLimits(8, 35, 8, 45);
        Assert.Equal(new(18, 20), DevicePage.TdpDraftPolicy.AdjustAfterEdit(false, 20, 20, limits));
        Assert.Equal(new(8, 10), DevicePage.TdpDraftPolicy.AdjustAfterEdit(false, 8, 9, limits));
        Assert.Equal(new(20, 25), DevicePage.TdpDraftPolicy.AdjustAfterEdit(false, 20, 25, limits));
    }

    [Fact]
    public void A2vm_uses_one_watt_gap_and_blank_companion_stays_blank()
    {
        var limits = new FrontendTdpLimits(8, 30, 8, 37);
        Assert.Equal(new(19, 20), DevicePage.TdpDraftPolicy.AdjustAfterEdit(false, 20, 20, limits));
        Assert.Equal(new(20, 21), DevicePage.TdpDraftPolicy.AdjustAfterEdit(true, 20, 20, limits));
        Assert.Equal(new(20, null), DevicePage.TdpDraftPolicy.AdjustAfterEdit(true, 20, null, limits));
    }

    [Theory]
    [InlineData(30, 30, 31)]
    [InlineData(35, 30, 31)]
    [InlineData(100, 30, 31)]
    public void Simplified_a2vm_pl1_edit_sets_exact_one_watt_gap_and_respects_pl1_maximum(int editedPl1, int expectedPl1, int expectedPl2)
    {
        var result = DevicePage.TdpDraftPolicy.AdjustAfterEdit(true, editedPl1, 36, new(8, 30, 8, 37), exactGap: true);
        Assert.Equal(new(expectedPl1, expectedPl2), result);
    }

    [Theory]
    [InlineData(35, 35, 37)]
    [InlineData(100, 35, 37)]
    public void Simplified_cg3em_pl1_edit_sets_exact_two_watt_gap_and_respects_pl1_maximum(int editedPl1, int expectedPl1, int expectedPl2)
    {
        var result = DevicePage.TdpDraftPolicy.AdjustAfterEdit(true, editedPl1, 44, new(8, 35, 8, 45), exactGap: true);
        Assert.Equal(new(expectedPl1, expectedPl2), result);
    }

    [Fact]
    public void Simplified_mode_does_not_change_direct_pl2_edits_or_unknown_model_policy()
    {
        var known = new FrontendTdpLimits(8, 30, 8, 37);
        Assert.Equal(new(20, 31), DevicePage.TdpDraftPolicy.AdjustAfterEdit(false, 20, 31, known, exactGap: true));

        var unknown = new FrontendTdpLimits(8, 32, 8, 40);
        Assert.Equal(new(20, 35), DevicePage.TdpDraftPolicy.AdjustAfterEdit(true, 20, 35, unknown, exactGap: true));
    }

    [Fact]
    public void Simplified_mode_visibility_targets_whole_pl2_rows_in_all_three_main_pages()
    {
        var root = Path.Combine(FindRepositoryRoot(), "src", "SteamInputAddonforClaw.UI", "Views");
        foreach (var file in new[] { "DevicePage.xaml", "ProfilePage.xaml", "XboxPage.xaml" })
        {
            var xaml = XDocument.Load(Path.Combine(root, file));
            Assert.Contains(xaml.Descendants().Where(element => element.Name.LocalName == "Grid"),
                element => ((string?)element.Attribute(XName.Get("Name", "http://schemas.microsoft.com/winfx/2006/xaml"))) == "AcPl2Row" || ((string?)element.Attribute(XName.Get("Name", "http://schemas.microsoft.com/winfx/2006/xaml"))) == "TdpAcPl2Row");
            Assert.Contains(xaml.Descendants().Where(element => element.Name.LocalName == "Grid"),
                element => ((string?)element.Attribute(XName.Get("Name", "http://schemas.microsoft.com/winfx/2006/xaml"))) == "DcPl2Row" || ((string?)element.Attribute(XName.Get("Name", "http://schemas.microsoft.com/winfx/2006/xaml"))) == "TdpDcPl2Row");
        }
    }

    [Fact]
    public void Existing_independent_pair_is_unchanged_until_a_user_edit()
    {
        var loaded = new FrontendTdpConfiguration(true, new(30, 8), new(30, 8));
        Assert.Equal(30, loaded.Ac.Pl1Watts);
        Assert.Equal(8, loaded.Ac.Pl2Watts);
        Assert.Equal(new(18, 20), DevicePage.TdpDraftPolicy.AdjustAfterEdit(false, 30, 20, new(8, 35, 8, 45)));
    }

    [Fact]
    public void Inactive_rail_edit_does_not_show_active_rail_hardware_result()
    {
        var hardware = new FrontendTdpHardwareApplyResult(FrontendTdpPowerSource.AC, 22, 24, true, true);

        Assert.False(DevicePage.TdpDraftPolicy.ShouldShowHardwareResult(editedAc: false, hardware));
        Assert.True(DevicePage.TdpDraftPolicy.ShouldShowHardwareResult(editedAc: true, hardware));
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "SteamInputAddonforClaw.slnx")))
                return directory.FullName;
        }

        throw new DirectoryNotFoundException("Repository root was not found.");
    }
}
