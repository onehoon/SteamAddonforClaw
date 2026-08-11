using SteamInputAddonforClaw.Lifecycle;
using SteamInputAddonforClaw.Routing;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class UserTerminationGuardTests
{
    [Fact]
    public void Passive_AllowsTermination()
    {
        var decision = Guard().Evaluate();

        Assert.True(decision.CanTerminate);
        Assert.Equal(UserTerminationBlockReason.None, decision.Reason);
    }

    [Theory]
    [InlineData(true, false, false, false, 1)]
    [InlineData(false, true, false, false, 2)]
    [InlineData(false, false, true, false, 3)]
    [InlineData(false, false, false, true, 4)]
    public void LiveRoutingOwnership_BlocksTermination(
        bool transition,
        bool pendingCleanup,
        bool nativeActive,
        bool nativeRecoveryOwned,
        int expectedReason)
    {
        var guard = new UserTerminationGuard(
            () => new(transition, pendingCleanup, false),
            () => nativeActive,
            () => nativeRecoveryOwned,
            () => false);

        var decision = guard.Evaluate();

        Assert.False(decision.CanTerminate);
        Assert.Equal((UserTerminationBlockReason)expectedReason, decision.Reason);
    }

    [Fact]
    public void LiveRecoveryMutation_BlocksTermination()
    {
        var guard = new UserTerminationGuard(() => default, () => false, () => false, () => true);

        Assert.Equal(UserTerminationBlockReason.RecoveryMutationOwned, guard.Evaluate().Reason);
    }

    [Fact]
    public void RecoveryUnsafeStaleJournal_DoesNotBlock()
    {
        var guard = new UserTerminationGuard(() => default, () => false, () => false, () => false);

        Assert.True(guard.Evaluate().CanTerminate);
    }

    [Fact]
    public void RuntimeShuttingDown_BlocksTermination()
    {
        var guard = new UserTerminationGuard(() => new(false, false, true), () => false, () => false, () => false);

        var decision = guard.Evaluate();

        Assert.False(decision.CanTerminate);
        Assert.Equal(UserTerminationBlockReason.RuntimeShuttingDown, decision.Reason);
    }

    [Theory]
    [InlineData(true, 0u)]
    [InlineData(false, 1u)]
    public void TerminationMenuFlagsUseStandardDisabledState(bool canTerminate, uint expected)
        => Assert.Equal(expected, SystemTrayIcon.TerminationMenuFlags(canTerminate));

    private static UserTerminationGuard Guard() =>
        new(() => default, () => false, () => false, () => false);
}
