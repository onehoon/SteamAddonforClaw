using SteamInputAddonforClaw.Lifecycle;
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

    [Fact]
    public void RuntimeShuttingDown_BlocksTermination()
    {
        var guard = new UserTerminationGuard(() => true);

        var decision = guard.Evaluate();

        Assert.False(decision.CanTerminate);
        Assert.Equal(UserTerminationBlockReason.RuntimeShuttingDown, decision.Reason);
    }

    [Theory]
    [InlineData(true, 0u)]
    [InlineData(false, 1u)]
    public void TerminationMenuFlagsUseStandardDisabledState(bool canTerminate, uint expected)
        => Assert.Equal(expected, SystemTrayIcon.TerminationMenuFlags(canTerminate));

    private static UserTerminationGuard Guard() => new(() => false);
}
