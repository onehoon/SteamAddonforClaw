using System.Text.Json;
using SteamInputAddonforClaw.Contracts.Frontend;
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
}
