using SteamInputAddonforClaw.Contracts.Overlay;
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
                OverlayTabId.Device,
                OverlayTabId.Profile,
                OverlayTabId.Controller,
                OverlayTabId.Shortcut,
                OverlayTabId.Setting,
            },
            OverlayTabState.DefaultOrder);

        Assert.Equal(5, OverlayTabState.DefaultOrder.Count);
        Assert.Equal(5, OverlayTabState.DefaultOrder.Distinct().Count());
    }

    [Fact]
    public void NewStateSelectsTheFirstTab()
    {
        var state = new OverlayTabState();

        Assert.Equal(OverlayTabId.Device, state.SelectedTab);
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

        Assert.Throws<ArgumentOutOfRangeException>(() => state.Select((OverlayTabId)42));
    }

    [Fact]
    public void ResetForShowReturnsToTheFirstTabInDefaultOrder()
    {
        var state = new OverlayTabState();
        state.Select(OverlayTabId.Shortcut);

        state.ResetForShow();

        Assert.Equal(OverlayTabId.Device, state.SelectedTab);
    }

    [Fact]
    public void ResetForShowReturnsToOrderZeroNotAHardCodedDevice()
    {
        var state = new OverlayTabState(
        [
            OverlayTabId.Controller,
            OverlayTabId.Device,
            OverlayTabId.Profile,
            OverlayTabId.Shortcut,
            OverlayTabId.Setting,
        ]);
        state.Select(OverlayTabId.Setting);

        state.ResetForShow();

        Assert.Equal(OverlayTabId.Controller, state.SelectedTab);
    }

    [Fact]
    public void InvalidOrderFallsBackToTheFrozenDefault()
    {
        OverlayTabId[][] invalidOrders =
        [
            [],
            [OverlayTabId.Device, OverlayTabId.Device, OverlayTabId.Profile, OverlayTabId.Controller, OverlayTabId.Shortcut],
            [OverlayTabId.Device, OverlayTabId.Profile, OverlayTabId.Controller],
        ];

        foreach (var order in invalidOrders)
        {
            var state = new OverlayTabState(order);

            Assert.Equal(OverlayTabState.DefaultOrder, state.Order);
            Assert.Equal(OverlayTabId.Device, state.SelectedTab);
        }
    }

    [Fact]
    public void UnknownIdentityInOrderFallsBackToTheFrozenDefault()
    {
        var state = new OverlayTabState(
        [
            OverlayTabId.Device,
            OverlayTabId.Profile,
            OverlayTabId.Controller,
            OverlayTabId.Shortcut,
            (OverlayTabId)99,
        ]);

        Assert.Equal(OverlayTabState.DefaultOrder, state.Order);
    }

    [Fact]
    public void NextAndPreviousMoveOneTabInDefaultOrder()
    {
        var state = new OverlayTabState();

        Assert.True(state.SelectNext());
        Assert.Equal(OverlayTabId.Profile, state.SelectedTab);

        Assert.True(state.SelectPrevious());
        Assert.Equal(OverlayTabId.Device, state.SelectedTab);
    }

    [Fact]
    public void PreviousAtTheFirstTabIsANoOp()
    {
        var state = new OverlayTabState();

        Assert.False(state.SelectPrevious());
        Assert.Equal(OverlayTabId.Device, state.SelectedTab);
    }

    [Fact]
    public void NextAtTheLastTabIsANoOpAndDoesNotWrap()
    {
        var state = new OverlayTabState();
        state.Select(OverlayTabId.Setting);

        Assert.False(state.SelectNext());
        Assert.Equal(OverlayTabId.Setting, state.SelectedTab);
    }

    [Fact]
    public void TryCreateMovedOrderSwapsExactlyOneAdjacentPair()
    {
        var state = new OverlayTabState(); // Device, Profile, Controller, Shortcut, Setting

        Assert.True(state.TryCreateMovedOrder(OverlayTabId.Controller, -1, out var earlier));
        Assert.Equal(new[] { OverlayTabId.Device, OverlayTabId.Controller, OverlayTabId.Profile, OverlayTabId.Shortcut, OverlayTabId.Setting }, earlier);

        Assert.True(state.TryCreateMovedOrder(OverlayTabId.Controller, +1, out var later));
        Assert.Equal(new[] { OverlayTabId.Device, OverlayTabId.Profile, OverlayTabId.Shortcut, OverlayTabId.Controller, OverlayTabId.Setting }, later);

        Assert.Equal(5, earlier.Distinct().Count());
    }

    [Fact]
    public void TryCreateMovedOrderIsABoundedNoOpAtEitherEnd()
    {
        var state = new OverlayTabState();

        Assert.False(state.TryCreateMovedOrder(OverlayTabId.Device, -1, out _));   // first tab earlier
        Assert.False(state.TryCreateMovedOrder(OverlayTabId.Setting, +1, out _));  // last tab later
        Assert.False(state.TryCreateMovedOrder(OverlayTabId.Profile, 2, out _));   // delta must be +/-1
    }

    [Fact]
    public void TryCreateMovedOrderDoesNotMutateTheCurrentOrder()
    {
        var state = new OverlayTabState();
        state.Select(OverlayTabId.Setting);

        Assert.True(state.TryCreateMovedOrder(OverlayTabId.Setting, -1, out var proposed));

        Assert.Equal(OverlayTabState.DefaultOrder, state.Order); // unchanged -- proposal only
        Assert.Equal(OverlayTabId.Setting, state.SelectedTab);

        // Only an authoritative apply changes the current order.
        Assert.True(state.TryApplyOrder(proposed));
        Assert.Equal(OverlayTabId.Setting, state.Order[3]);
    }

    [Fact]
    public void TryApplyOrderReplacesTheOrderAndPreservesTheSelectedTab()
    {
        var state = new OverlayTabState();
        state.Select(OverlayTabId.Setting);

        Assert.True(state.TryApplyOrder(
        [
            OverlayTabId.Controller,
            OverlayTabId.Device,
            OverlayTabId.Profile,
            OverlayTabId.Shortcut,
            OverlayTabId.Setting,
        ]));

        Assert.Equal(OverlayTabId.Controller, state.Order[0]);
        Assert.Equal(OverlayTabId.Setting, state.SelectedTab); // preserved on a live reorder

        state.ResetForShow();
        Assert.Equal(OverlayTabId.Controller, state.SelectedTab); // new first tab on the next Show
    }

    [Fact]
    public void TryApplyOrderRejectsAnInvalidOrderWithoutCorruptingCurrentState()
    {
        var state = new OverlayTabState();
        state.Select(OverlayTabId.Profile);

        Assert.False(state.TryApplyOrder([OverlayTabId.Device, OverlayTabId.Device, OverlayTabId.Profile, OverlayTabId.Controller, OverlayTabId.Shortcut]));
        Assert.False(state.TryApplyOrder([OverlayTabId.Device, OverlayTabId.Profile]));

        Assert.Equal(OverlayTabState.DefaultOrder, state.Order);
        Assert.Equal(OverlayTabId.Profile, state.SelectedTab);
    }

    [Fact]
    public void TraversalFollowsTheCurrentOrderNotEnumDeclarationOrder()
    {
        var state = new OverlayTabState(
        [
            OverlayTabId.Controller,
            OverlayTabId.Device,
            OverlayTabId.Profile,
            OverlayTabId.Shortcut,
            OverlayTabId.Setting,
        ]);
        state.Select(OverlayTabId.Device);

        Assert.True(state.SelectPrevious());
        Assert.Equal(OverlayTabId.Controller, state.SelectedTab);

        state.Select(OverlayTabId.Device);
        Assert.True(state.SelectNext());
        Assert.Equal(OverlayTabId.Profile, state.SelectedTab);
    }
}
