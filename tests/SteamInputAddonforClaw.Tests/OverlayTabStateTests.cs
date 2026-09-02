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
}
