using SteamInputAddonforClaw.Contracts.Frontend;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class AddonQuickSettingsTabOrderContractTests
{
    [Fact]
    public void DefaultOrderIsTheFrozenFiveTabsInOrder()
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
            AddonQuickSettingsTabOrderContract.DefaultOrder);

        Assert.Equal(5, AddonQuickSettingsTabOrderContract.DefaultOrder.Distinct().Count());
    }

    [Fact]
    public void DefaultOrderCannotBeMutatedByCallers()
    {
        var first = AddonQuickSettingsTabOrderContract.DefaultOrder;
        ((AddonQuickSettingsTabId[])first)[0] = AddonQuickSettingsTabId.Setting;

        Assert.Equal(AddonQuickSettingsTabId.Device, AddonQuickSettingsTabOrderContract.DefaultOrder[0]);
    }

    [Fact]
    public void TryNormalizeAcceptsAnyCompleteOrderAndReturnsAnIndependentCopy()
    {
        var requested = new[]
        {
            AddonQuickSettingsTabId.Controller,
            AddonQuickSettingsTabId.Device,
            AddonQuickSettingsTabId.Profile,
            AddonQuickSettingsTabId.Shortcut,
            AddonQuickSettingsTabId.Setting,
        };

        Assert.True(AddonQuickSettingsTabOrderContract.TryNormalize(requested, out var normalized));
        Assert.Equal(requested, normalized);
        Assert.NotSame(requested, normalized);
    }

    [Fact]
    public void TryNormalizeRejectsMalformedOrders()
    {
        Assert.False(AddonQuickSettingsTabOrderContract.TryNormalize(null, out _));
        Assert.False(AddonQuickSettingsTabOrderContract.TryNormalize([], out _));
        Assert.False(AddonQuickSettingsTabOrderContract.TryNormalize(
            [AddonQuickSettingsTabId.Device, AddonQuickSettingsTabId.Profile, AddonQuickSettingsTabId.Controller], out _)); // missing
        Assert.False(AddonQuickSettingsTabOrderContract.TryNormalize(
            [AddonQuickSettingsTabId.Device, AddonQuickSettingsTabId.Device, AddonQuickSettingsTabId.Profile, AddonQuickSettingsTabId.Controller, AddonQuickSettingsTabId.Shortcut], out _)); // duplicate
        Assert.False(AddonQuickSettingsTabOrderContract.TryNormalize(
            [AddonQuickSettingsTabId.Device, AddonQuickSettingsTabId.Profile, AddonQuickSettingsTabId.Controller, AddonQuickSettingsTabId.Shortcut, (AddonQuickSettingsTabId)99], out _)); // unknown
    }

    [Fact]
    public void TryNormalizeOnFailureYieldsTheDefaultOrder()
    {
        AddonQuickSettingsTabOrderContract.TryNormalize([], out var normalized);
        Assert.Equal(AddonQuickSettingsTabOrderContract.DefaultOrder, normalized);
    }

    [Fact]
    public void NormalizeOrDefaultFallsBackForMalformedInputAndPassesValidThrough()
    {
        Assert.Equal(AddonQuickSettingsTabOrderContract.DefaultOrder, AddonQuickSettingsTabOrderContract.NormalizeOrDefault(null));

        var custom = new[]
        {
            AddonQuickSettingsTabId.Setting,
            AddonQuickSettingsTabId.Shortcut,
            AddonQuickSettingsTabId.Controller,
            AddonQuickSettingsTabId.Profile,
            AddonQuickSettingsTabId.Device,
        };
        Assert.Equal(custom, AddonQuickSettingsTabOrderContract.NormalizeOrDefault(custom));
    }

    [Fact]
    public void LabelsAndShellSnapshotUseTheCanonicalProductIdentity()
    {
        Assert.Equal("Device", AddonQuickSettingsShellContract.LabelFor(AddonQuickSettingsTabId.Device));
        Assert.Equal("Profile", AddonQuickSettingsShellContract.LabelFor(AddonQuickSettingsTabId.Profile));
        Assert.Equal("Controller", AddonQuickSettingsShellContract.LabelFor(AddonQuickSettingsTabId.Controller));
        Assert.Equal("Shortcut", AddonQuickSettingsShellContract.LabelFor(AddonQuickSettingsTabId.Shortcut));
        Assert.Equal("Setting", AddonQuickSettingsShellContract.LabelFor(AddonQuickSettingsTabId.Setting));

        var snapshot = AddonQuickSettingsShellContract.Create([
            AddonQuickSettingsTabId.Setting,
            AddonQuickSettingsTabId.Device,
            AddonQuickSettingsTabId.Profile,
            AddonQuickSettingsTabId.Controller,
            AddonQuickSettingsTabId.Shortcut]);

        Assert.True(snapshot.Available);
        Assert.Equal([AddonQuickSettingsTabId.Setting, AddonQuickSettingsTabId.Device, AddonQuickSettingsTabId.Profile, AddonQuickSettingsTabId.Controller, AddonQuickSettingsTabId.Shortcut], snapshot.Tabs.Select(x => x.TabId));
        Assert.Equal(["Setting", "Device", "Profile", "Controller", "Shortcut"], snapshot.Tabs.Select(x => x.Label));
        Assert.False(AddonQuickSettingsShellSnapshot.Unavailable().Available);
        Assert.Empty(AddonQuickSettingsShellSnapshot.Unavailable().Tabs);
    }

    [Fact]
    public void Product_projection_uses_authoritative_order_labels_and_boundaries()
    {
        var snapshot = AddonQuickSettingsTabOrderProduct.Create([
            AddonQuickSettingsTabId.Setting,
            AddonQuickSettingsTabId.Device,
            AddonQuickSettingsTabId.Profile,
            AddonQuickSettingsTabId.Controller,
            AddonQuickSettingsTabId.Shortcut]);

        Assert.Equal([
            AddonQuickSettingsTabId.Setting,
            AddonQuickSettingsTabId.Device,
            AddonQuickSettingsTabId.Profile,
            AddonQuickSettingsTabId.Controller,
            AddonQuickSettingsTabId.Shortcut], snapshot.Rows.Select(row => row.TabId));
        Assert.Equal(["Setting", "Device", "Profile", "Controller", "Shortcut"], snapshot.Rows.Select(row => row.Label));
        Assert.False(snapshot.Rows[0].CanMoveEarlier);
        Assert.True(snapshot.Rows[0].CanMoveLater);
        Assert.All(snapshot.Rows.Skip(1).SkipLast(1), row =>
        {
            Assert.True(row.CanMoveEarlier);
            Assert.True(row.CanMoveLater);
        });
        Assert.True(snapshot.Rows[^1].CanMoveEarlier);
        Assert.False(snapshot.Rows[^1].CanMoveLater);
    }

    [Fact]
    public void Shared_projection_matches_the_shell_identity_consumed_by_both_surfaces()
    {
        var order = new[]
        {
            AddonQuickSettingsTabId.Controller,
            AddonQuickSettingsTabId.Device,
            AddonQuickSettingsTabId.Setting,
            AddonQuickSettingsTabId.Profile,
            AddonQuickSettingsTabId.Shortcut,
        };
        var shell = AddonQuickSettingsShellContract.Create(order);
        var setting = AddonQuickSettingsTabOrderProduct.Create(order);

        Assert.Equal(shell.Tabs.Select(tab => tab.TabId), setting.Rows.Select(row => row.TabId));
        Assert.Equal(shell.Tabs.Select(tab => tab.Label), setting.Rows.Select(row => row.Label));
        Assert.Equal(
            setting.Rows.Select(row => (row.TabId, row.CanMoveEarlier, row.CanMoveLater)),
            shell.Tabs.Select((tab, index) => (tab.TabId, index > 0, index < shell.Tabs.Count - 1)));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(1)]
    public void Product_move_swaps_exactly_one_adjacent_pair(int delta)
    {
        var current = AddonQuickSettingsTabOrderContract.DefaultOrder;
        var intent = new AddonQuickSettingsTabOrderMoveIntent(AddonQuickSettingsTabId.Profile, delta);

        Assert.True(AddonQuickSettingsTabOrderProduct.TryCreateMovedOrder(current, intent, out var proposed));
        Assert.Equal(
            delta < 0
                ? [AddonQuickSettingsTabId.Profile, AddonQuickSettingsTabId.Device, AddonQuickSettingsTabId.Controller, AddonQuickSettingsTabId.Shortcut, AddonQuickSettingsTabId.Setting]
                : [AddonQuickSettingsTabId.Device, AddonQuickSettingsTabId.Controller, AddonQuickSettingsTabId.Profile, AddonQuickSettingsTabId.Shortcut, AddonQuickSettingsTabId.Setting],
            proposed);
    }

    [Theory]
    [InlineData((AddonQuickSettingsTabId)99, 1)]
    [InlineData(AddonQuickSettingsTabId.Device, -1)]
    [InlineData(AddonQuickSettingsTabId.Setting, 1)]
    [InlineData(AddonQuickSettingsTabId.Profile, 0)]
    [InlineData(AddonQuickSettingsTabId.Profile, 2)]
    public void Product_rejects_invalid_or_boundary_moves(AddonQuickSettingsTabId tabId, int delta)
    {
        Assert.False(AddonQuickSettingsTabOrderProduct.TryCreateMovedOrder(
            AddonQuickSettingsTabOrderContract.DefaultOrder,
            new(tabId, delta), out _));
    }

    [Fact]
    public void Product_rejects_malformed_current_order_and_returns_no_proposal()
    {
        Assert.False(AddonQuickSettingsTabOrderProduct.TryCreateMovedOrder(
            [AddonQuickSettingsTabId.Device, AddonQuickSettingsTabId.Device],
            new(AddonQuickSettingsTabId.Device, 1), out var proposed));
        Assert.Empty(proposed);
    }
}
