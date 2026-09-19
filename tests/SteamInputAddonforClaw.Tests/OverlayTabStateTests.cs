using SteamInputAddonforClaw.Contracts.Frontend;
using SteamInputAddonforClaw.Overlay;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class OverlayTabStateTests
{
    [Fact]
    public void DefaultOrderIsTheFrozenFiveTabs()
    {
        Assert.Equal(
            new[]
            {
                AddonQuickSettingsTabId.Device,
                AddonQuickSettingsTabId.Profile,
                AddonQuickSettingsTabId.Controller,
                AddonQuickSettingsTabId.Shortcut,
                AddonQuickSettingsTabId.Setting,
            },
            OverlayTabState.DefaultOrder);

        Assert.Equal(5, OverlayTabState.DefaultOrder.Count);
        Assert.Equal(5, OverlayTabState.DefaultOrder.Distinct().Count());
    }

    [Fact]
    public void NewStateSelectsTheFirstTab()
    {
        var state = new OverlayTabState();

        Assert.Equal(AddonQuickSettingsTabId.Device, state.SelectedTab);
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

    [Fact]
    public void SelectingAnUnknownTabThrows()
    {
        var state = new OverlayTabState();

        Assert.Throws<ArgumentOutOfRangeException>(() => state.Select((AddonQuickSettingsTabId)42));
    }

    [Fact]
    public void ResetForShowReturnsToTheFirstTabInDefaultOrder()
    {
        var state = new OverlayTabState();
        state.Select(AddonQuickSettingsTabId.Shortcut);

        state.ResetForShow();

        Assert.Equal(AddonQuickSettingsTabId.Device, state.SelectedTab);
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
    public void InvalidOrderFallsBackToTheFrozenDefault()
    {
        AddonQuickSettingsTabId[][] invalidOrders =
        [
            [],
            [AddonQuickSettingsTabId.Device, AddonQuickSettingsTabId.Device, AddonQuickSettingsTabId.Profile, AddonQuickSettingsTabId.Controller, AddonQuickSettingsTabId.Shortcut],
            [AddonQuickSettingsTabId.Device, AddonQuickSettingsTabId.Profile, AddonQuickSettingsTabId.Controller],
        ];

        foreach (var order in invalidOrders)
        {
            var state = new OverlayTabState(order);

            Assert.Equal(OverlayTabState.DefaultOrder, state.Order);
            Assert.Equal(AddonQuickSettingsTabId.Device, state.SelectedTab);
        }
    }

    [Fact]
    public void UnknownIdentityInOrderFallsBackToTheFrozenDefault()
    {
        var state = new OverlayTabState(
        [
            AddonQuickSettingsTabId.Device,
            AddonQuickSettingsTabId.Profile,
            AddonQuickSettingsTabId.Controller,
            AddonQuickSettingsTabId.Shortcut,
            (AddonQuickSettingsTabId)99,
        ]);

        Assert.Equal(OverlayTabState.DefaultOrder, state.Order);
    }

    [Fact]
    public void NextAndPreviousMoveOneTabInDefaultOrder()
    {
        var state = new OverlayTabState();

        Assert.True(state.SelectNext());
        Assert.Equal(AddonQuickSettingsTabId.Profile, state.SelectedTab);

        Assert.True(state.SelectPrevious());
        Assert.Equal(AddonQuickSettingsTabId.Device, state.SelectedTab);
    }

    [Fact]
    public void PreviousAtTheFirstTabIsANoOp()
    {
        var state = new OverlayTabState();

        Assert.False(state.SelectPrevious());
        Assert.Equal(AddonQuickSettingsTabId.Device, state.SelectedTab);
    }

    [Fact]
    public void NextAtTheLastTabIsANoOpAndDoesNotWrap()
    {
        var state = new OverlayTabState();
        state.Select(AddonQuickSettingsTabId.Setting);

        Assert.False(state.SelectNext());
        Assert.Equal(AddonQuickSettingsTabId.Setting, state.SelectedTab);
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
    }
}
