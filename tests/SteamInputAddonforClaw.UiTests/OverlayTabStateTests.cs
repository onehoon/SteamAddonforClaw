using SteamInputAddonforClaw.Contracts.Frontend;
using SteamInputAddonforClaw.Overlay;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class OverlayTabStateTests
{
    [Fact]
    public void DefaultOrderContainsEveryKnownTabExactlyOnce()
    {
        Assert.Equal(Enum.GetValues<AddonQuickSettingsTabId>().Order(), OverlayTabState.DefaultOrder.Order());
        Assert.Equal(OverlayTabState.DefaultOrder.Count, OverlayTabState.DefaultOrder.Distinct().Count());
    }
    [Fact]
    public void NewStateSelectsTheFirstConfiguredTab()
    {
        var state = new OverlayTabState();
        Assert.Equal(state.Order[0], state.SelectedTab);
    }
    [Fact]
    public void EachKnownTabCanBecomeSelected()
    {
        foreach (var tab in OverlayTabState.DefaultOrder)
        {
            var state = new OverlayTabState();

            state.Select(tab);

            Assert.Equal(tab, state.SelectedTab);
        }
    }

    [Theory]
    [InlineData(QuickOverlayYButtonTarget.Profile, AddonQuickSettingsTabId.Profile)]
    [InlineData(QuickOverlayYButtonTarget.Shortcut, AddonQuickSettingsTabId.Shortcut)]
    [InlineData(QuickOverlayYButtonTarget.Device, AddonQuickSettingsTabId.Device)]
    [InlineData(QuickOverlayYButtonTarget.Controller, AddonQuickSettingsTabId.Controller)]
    [InlineData(QuickOverlayYButtonTarget.Setting, AddonQuickSettingsTabId.Setting)]
    public void Quick_jump_resolves_target_by_tab_identity(QuickOverlayYButtonTarget target, AddonQuickSettingsTabId expected)
    {
        Assert.Equal(expected, OverlayWindow.ResolveQuickJumpTabId(target));
    }

    [Theory]
    [InlineData(QuickOverlayYButtonTarget.Off)]
    [InlineData((QuickOverlayYButtonTarget)99)]
    public void Quick_jump_off_or_unknown_target_fails_closed(QuickOverlayYButtonTarget target)
    {
        Assert.Null(OverlayWindow.ResolveQuickJumpTabId(target));
    }

    [Fact]
    public void Quick_jump_identity_does_not_change_configured_first_tab()
    {
        AddonQuickSettingsTabId[] order =
        [
            AddonQuickSettingsTabId.Device,
            AddonQuickSettingsTabId.Controller,
            AddonQuickSettingsTabId.Shortcut,
            AddonQuickSettingsTabId.Profile,
            AddonQuickSettingsTabId.Setting,
        ];
        var state = new OverlayTabState(order);
        var jumpTarget = OverlayWindow.ResolveQuickJumpTabId(QuickOverlayYButtonTarget.Profile);

        state.Select(jumpTarget!.Value);
        Assert.Equal(AddonQuickSettingsTabId.Profile, state.SelectedTab);
        Assert.Equal(order, state.Order);

        state.ResetForShow();
        Assert.Equal(AddonQuickSettingsTabId.Device, state.SelectedTab);
        Assert.Equal(order, state.Order);
    }

    [Fact]
    public void SelectingAnUnknownTabThrows()
    {
        var state = new OverlayTabState();

        Assert.Throws<ArgumentOutOfRangeException>(() => state.Select((AddonQuickSettingsTabId)42));
    }

    [Fact]
    public void ResetForShowReturnsToTheFirstConfiguredTab()
    {
        var state = new OverlayTabState();
        state.Select(state.Order[^1]);
        state.ResetForShow();
        Assert.Equal(state.Order[0], state.SelectedTab);
    }
    [Fact]
    public void ResetForShowReturnsToOrderZeroNotAHardCodedDevice()
    {
        var state = new OverlayTabState(
        [
            AddonQuickSettingsTabId.Controller,
            AddonQuickSettingsTabId.Device,
            AddonQuickSettingsTabId.Profile,
            AddonQuickSettingsTabId.Shortcut,
            AddonQuickSettingsTabId.Setting,
        ]);
        state.Select(AddonQuickSettingsTabId.Setting);

        state.ResetForShow();

        Assert.Equal(AddonQuickSettingsTabId.Controller, state.SelectedTab);
    }

    [Fact]
    public void ManuallySelectedProfileResetsToConfiguredFirstTabOnNextShow()
    {
        AddonQuickSettingsTabId[] configuredOrder =
        [
            AddonQuickSettingsTabId.Controller,
            AddonQuickSettingsTabId.Device,
            AddonQuickSettingsTabId.Profile,
            AddonQuickSettingsTabId.Shortcut,
            AddonQuickSettingsTabId.Setting,
        ];
        var state = new OverlayTabState(configuredOrder);

        state.ResetForShow();
        Assert.Equal(AddonQuickSettingsTabId.Controller, state.SelectedTab);

        // Selecting Profile is an explicit user tab change; it does not mutate the configured order.
        state.Select(AddonQuickSettingsTabId.Profile);

        Assert.Equal(AddonQuickSettingsTabId.Profile, state.SelectedTab);
        Assert.Equal(configuredOrder, state.Order);
        Assert.True(state.SelectNext());
        Assert.Equal(AddonQuickSettingsTabId.Shortcut, state.SelectedTab);
        Assert.True(state.SelectPrevious());
        Assert.Equal(AddonQuickSettingsTabId.Profile, state.SelectedTab);
        Assert.True(state.SelectPrevious());
        Assert.Equal(AddonQuickSettingsTabId.Device, state.SelectedTab);
        Assert.Equal(configuredOrder, state.Order);

        state.ResetForShow();
        Assert.Equal(AddonQuickSettingsTabId.Controller, state.SelectedTab);
    }

    [Fact]
    public void InvalidOrderFallsBackToTheKnownDefault()
    {
        AddonQuickSettingsTabId[][] invalidOrders =
        [[], [AddonQuickSettingsTabId.Device, AddonQuickSettingsTabId.Device, AddonQuickSettingsTabId.Profile,
            AddonQuickSettingsTabId.Controller, AddonQuickSettingsTabId.Shortcut],
            [AddonQuickSettingsTabId.Device, AddonQuickSettingsTabId.Profile, AddonQuickSettingsTabId.Controller]];

        foreach (var order in invalidOrders)
        {
            var state = new OverlayTabState(order);
            Assert.Equal(OverlayTabState.DefaultOrder, state.Order);
            Assert.Equal(state.Order[0], state.SelectedTab);
        }
    }
    [Fact]
    public void UnknownIdentityInOrderFallsBackToTheKnownDefault()
    {
        var state = new OverlayTabState(
        [AddonQuickSettingsTabId.Device, AddonQuickSettingsTabId.Profile, AddonQuickSettingsTabId.Controller,
            AddonQuickSettingsTabId.Shortcut, (AddonQuickSettingsTabId)99]);
        Assert.Equal(OverlayTabState.DefaultOrder, state.Order);
        Assert.Equal(state.Order[0], state.SelectedTab);
    }
    [Fact]
    public void NextAndPreviousFollowTheConfiguredOrder()
    {
        AddonQuickSettingsTabId[] configured =
        [AddonQuickSettingsTabId.Profile, AddonQuickSettingsTabId.Device, AddonQuickSettingsTabId.Shortcut,
            AddonQuickSettingsTabId.Controller, AddonQuickSettingsTabId.Setting];
        var state = new OverlayTabState(configured);
        state.Select(configured[0]);
        Assert.True(state.SelectNext());
        Assert.Equal(configured[1], state.SelectedTab);
        Assert.True(state.SelectPrevious());
        Assert.Equal(configured[0], state.SelectedTab);
    }
    [Fact]
    public void PreviousAtTheFirstConfiguredTabWrapsToTheLast()
    {
        var state = new OverlayTabState();
        state.Select(state.Order[0]);
        Assert.True(state.SelectPrevious());
        Assert.Equal(state.Order[^1], state.SelectedTab);
    }
    [Fact]
    public void NextAtTheLastConfiguredTabWrapsToTheFirst()
    {
        var state = new OverlayTabState();
        state.Select(state.Order[^1]);
        Assert.True(state.SelectNext());
        Assert.Equal(state.Order[0], state.SelectedTab);
    }
    [Fact]
    public void OverlayStateChangesOnlyWhenAuthoritativeOrderIsApplied()
    {
        var state = new OverlayTabState();
        state.Select(AddonQuickSettingsTabId.Setting);
        IReadOnlyList<AddonQuickSettingsTabId> proposed =
        [
            AddonQuickSettingsTabId.Device,
            AddonQuickSettingsTabId.Profile,
            AddonQuickSettingsTabId.Controller,
            AddonQuickSettingsTabId.Setting,
            AddonQuickSettingsTabId.Shortcut,
        ];

        Assert.Equal(OverlayTabState.DefaultOrder, state.Order); // unchanged -- proposal only
        Assert.Equal(AddonQuickSettingsTabId.Setting, state.SelectedTab);

        // Only an authoritative apply changes the current order.
        Assert.True(state.TryApplyOrder(proposed));
        Assert.Equal(AddonQuickSettingsTabId.Setting, state.Order[3]);
    }

    [Fact]
    public void TryApplyOrderReplacesTheOrderAndPreservesTheSelectedTab()
    {
        var state = new OverlayTabState();
        state.Select(AddonQuickSettingsTabId.Setting);

        Assert.True(state.TryApplyOrder(
        [
            AddonQuickSettingsTabId.Controller,
            AddonQuickSettingsTabId.Device,
            AddonQuickSettingsTabId.Profile,
            AddonQuickSettingsTabId.Shortcut,
            AddonQuickSettingsTabId.Setting,
        ]));

        Assert.Equal(AddonQuickSettingsTabId.Controller, state.Order[0]);
        Assert.Equal(AddonQuickSettingsTabId.Setting, state.SelectedTab); // preserved on a live reorder

        state.ResetForShow();
        Assert.Equal(AddonQuickSettingsTabId.Controller, state.SelectedTab); // new first tab on the next Show
    }

    [Fact]
    public void TryApplyOrderRejectsAnInvalidOrderWithoutCorruptingCurrentState()
    {
        var state = new OverlayTabState();
        state.Select(AddonQuickSettingsTabId.Profile);

        Assert.False(state.TryApplyOrder([AddonQuickSettingsTabId.Device, AddonQuickSettingsTabId.Device, AddonQuickSettingsTabId.Profile, AddonQuickSettingsTabId.Controller, AddonQuickSettingsTabId.Shortcut]));
        Assert.False(state.TryApplyOrder([AddonQuickSettingsTabId.Device, AddonQuickSettingsTabId.Profile]));

        Assert.Equal(OverlayTabState.DefaultOrder, state.Order);
        Assert.Equal(AddonQuickSettingsTabId.Profile, state.SelectedTab);
    }

    [Fact]
    public void TraversalFollowsTheCurrentOrderNotEnumDeclarationOrder()
    {
        var state = new OverlayTabState(
        [
            AddonQuickSettingsTabId.Controller,
            AddonQuickSettingsTabId.Device,
            AddonQuickSettingsTabId.Profile,
            AddonQuickSettingsTabId.Shortcut,
            AddonQuickSettingsTabId.Setting,
        ]);
        state.Select(AddonQuickSettingsTabId.Device);

        Assert.True(state.SelectPrevious());
        Assert.Equal(AddonQuickSettingsTabId.Controller, state.SelectedTab);

        state.Select(AddonQuickSettingsTabId.Device);
        Assert.True(state.SelectNext());
        Assert.Equal(AddonQuickSettingsTabId.Profile, state.SelectedTab);

        state.Select(AddonQuickSettingsTabId.Controller);
        Assert.True(state.SelectPrevious());
        Assert.Equal(AddonQuickSettingsTabId.Setting, state.SelectedTab);

        state.Select(AddonQuickSettingsTabId.Setting);
        Assert.True(state.SelectNext());
        Assert.Equal(AddonQuickSettingsTabId.Controller, state.SelectedTab);
    }
}
