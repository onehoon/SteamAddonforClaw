using SteamInputAddonforClaw.Contracts.Overlay;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class OverlayTabOrderContractTests
{
    [Fact]
    public void DefaultOrderIsTheFrozenFiveTabsInOrder()
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
            OverlayTabOrderContract.DefaultOrder);

        Assert.Equal(5, OverlayTabOrderContract.DefaultOrder.Distinct().Count());
    }

    [Fact]
    public void DefaultOrderCannotBeMutatedByCallers()
    {
        var first = OverlayTabOrderContract.DefaultOrder;
        ((OverlayTabId[])first)[0] = OverlayTabId.Setting;

        Assert.Equal(OverlayTabId.Device, OverlayTabOrderContract.DefaultOrder[0]);
    }

    [Fact]
    public void TryNormalizeAcceptsAnyCompleteOrderAndReturnsAnIndependentCopy()
    {
        var requested = new[]
        {
            OverlayTabId.Controller,
            OverlayTabId.Device,
            OverlayTabId.Profile,
            OverlayTabId.Shortcut,
            OverlayTabId.Setting,
        };

        Assert.True(OverlayTabOrderContract.TryNormalize(requested, out var normalized));
        Assert.Equal(requested, normalized);
        Assert.NotSame(requested, normalized);
    }

    [Fact]
    public void TryNormalizeRejectsMalformedOrders()
    {
        Assert.False(OverlayTabOrderContract.TryNormalize(null, out _));
        Assert.False(OverlayTabOrderContract.TryNormalize([], out _));
        Assert.False(OverlayTabOrderContract.TryNormalize(
            [OverlayTabId.Device, OverlayTabId.Profile, OverlayTabId.Controller], out _)); // missing
        Assert.False(OverlayTabOrderContract.TryNormalize(
            [OverlayTabId.Device, OverlayTabId.Device, OverlayTabId.Profile, OverlayTabId.Controller, OverlayTabId.Shortcut], out _)); // duplicate
        Assert.False(OverlayTabOrderContract.TryNormalize(
            [OverlayTabId.Device, OverlayTabId.Profile, OverlayTabId.Controller, OverlayTabId.Shortcut, (OverlayTabId)99], out _)); // unknown
    }

    [Fact]
    public void TryNormalizeOnFailureYieldsTheDefaultOrder()
    {
        OverlayTabOrderContract.TryNormalize([], out var normalized);
        Assert.Equal(OverlayTabOrderContract.DefaultOrder, normalized);
    }

    [Fact]
    public void NormalizeOrDefaultFallsBackForMalformedInputAndPassesValidThrough()
    {
        Assert.Equal(OverlayTabOrderContract.DefaultOrder, OverlayTabOrderContract.NormalizeOrDefault(null));

        var custom = new[]
        {
            OverlayTabId.Setting,
            OverlayTabId.Shortcut,
            OverlayTabId.Controller,
            OverlayTabId.Profile,
            OverlayTabId.Device,
        };
        Assert.Equal(custom, OverlayTabOrderContract.NormalizeOrDefault(custom));
    }
}
