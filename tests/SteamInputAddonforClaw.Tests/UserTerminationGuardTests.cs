using SteamInputAddonforClaw.Contracts.Frontend;
using SteamInputAddonforClaw.Lifecycle;
using SteamInputAddonforClaw.Routing;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class UserTerminationGuardTests
{
    // ---- PR2.5: mandatory controller Runtime lifetime ----

    [Theory]
    [InlineData("Disabled", true)]
    [InlineData("Enabled", false)]
    [InlineData("Partial", false)]
    [InlineData("Unavailable", false)]
    public void MandatoryControllerRuntimePolicy_only_exact_disabled_is_addon_owned(string state, bool expected)
        => Assert.Equal(expected, MandatoryControllerRuntimePolicy.IsMandatory(Enum.Parse<FrontendCenterMStartupState>(state)));

    [Fact]
    public void Compose_blocks_ordinary_termination_when_center_m_is_disabled()
    {
        var decision = UserTerminationComposition.Compose(new(true, UserTerminationBlockReason.None), controllerRuntimeMandatory: true);

        Assert.False(decision.CanTerminate);
        Assert.Equal(UserTerminationBlockReason.ControllerAuthorityMandatory, decision.Reason);
    }

    [Fact]
    public void Compose_does_not_touch_a_permitted_decision_when_not_mandatory()
    {
        var inner = new UserTerminationDecision(true, UserTerminationBlockReason.None);
        Assert.Equal(inner, UserTerminationComposition.Compose(inner, controllerRuntimeMandatory: false));
    }

    [Fact]
    public void Compose_preserves_an_existing_lower_level_block_reason()
    {
        var inner = new UserTerminationDecision(false, UserTerminationBlockReason.RoutingTransition);

        var decision = UserTerminationComposition.Compose(inner, controllerRuntimeMandatory: true);

        Assert.False(decision.CanTerminate);
        Assert.Equal(UserTerminationBlockReason.RoutingTransition, decision.Reason);
    }

    [Fact]
    public void Compose_mandatory_decision_grays_the_tray_restart_and_exit_items()
    {
        var decision = UserTerminationComposition.Compose(new(true, UserTerminationBlockReason.None), controllerRuntimeMandatory: true);
        Assert.Equal(1u, SystemTrayIcon.TerminationMenuFlags(decision.CanTerminate)); // MF_STRING | MF_GRAYED
    }

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
