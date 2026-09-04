using System.Text.Json;
using SteamInputAddonforClaw.Contracts.Frontend;
using SteamInputAddonforClaw.FrontendTransport;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class CenterMStartupContractTests
{
    [Fact]
    public void Center_m_rpcs_have_named_wire_methods_and_bump_the_protocol()
    {
        Assert.Equal("CaptureCenterMStartup", FrontendRpcMethod.CaptureCenterMStartup.ToString());
        // PR3: the PR1 SetCenterMStartupEnabled RPC is renamed to the reboot-bound authority transition.
        Assert.Equal("RequestCenterMAuthorityTransition", FrontendRpcMethod.RequestCenterMAuthorityTransition.ToString());
        Assert.DoesNotContain("SetCenterMStartupEnabled", Enum.GetNames<FrontendRpcMethod>());
        // Bumped to 25 by App UI PR-C; 24 by App UI PR-B (obsolete LaunchAtWindowsStartup user-preference contract removed).
        Assert.Equal(25, FrontendTransportProtocol.CurrentVersion);
    }

    [Fact]
    public void Snapshot_round_trips_and_preserves_each_root()
    {
        var value = new FrontendCenterMStartupSnapshot(FrontendCenterMStartupState.Partial, true, false, true, "inconsistent");

        var restored = JsonSerializer.Deserialize<FrontendCenterMStartupSnapshot>(JsonSerializer.Serialize(value));

        Assert.Equivalent(value, restored, strict: true);
        Assert.True(restored!.ServerTaskEnabled);
        Assert.False(restored.UpdaterTaskEnabled);
        Assert.True(restored.FoundationServiceEnabled);
    }

    [Fact]
    public void Unavailable_snapshot_invents_no_state()
    {
        var snapshot = FrontendCenterMStartupSnapshot.Unavailable;
        Assert.Equal(FrontendCenterMStartupState.Unavailable, snapshot.State);
        Assert.False(snapshot.ServerTaskEnabled);
        Assert.False(snapshot.UpdaterTaskEnabled);
        Assert.False(snapshot.FoundationServiceEnabled);
        Assert.Null(snapshot.FailureMessage);
    }

    [Theory]
    [InlineData(FrontendCenterMStartupMutationOutcome.Succeeded, true)]
    [InlineData(FrontendCenterMStartupMutationOutcome.Cancelled, false)]
    [InlineData(FrontendCenterMStartupMutationOutcome.Failed, false)]
    [InlineData(FrontendCenterMStartupMutationOutcome.Unavailable, false)]
    public void Mutation_result_keeps_the_non_boolean_outcome(FrontendCenterMStartupMutationOutcome outcome, bool succeeded)
    {
        var result = new FrontendCenterMStartupMutationResult(outcome, FrontendCenterMStartupSnapshot.Unavailable, "why");
        Assert.Equal(succeeded, result.Succeeded);
        Assert.Equal(outcome, result.Outcome);
    }

    [Fact]
    public void Cancelled_is_distinct_from_failed_and_unavailable()
    {
        var outcomes = Enum.GetValues<FrontendCenterMStartupMutationOutcome>();
        Assert.Contains(FrontendCenterMStartupMutationOutcome.Cancelled, outcomes);
        Assert.Equal(4, outcomes.Length);
    }
}
