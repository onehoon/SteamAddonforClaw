using System.Text.Json;
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
}
