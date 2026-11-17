using SteamInputAddonforClaw.Contracts.Frontend;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class AddonQuickSettingsTabOrderContractTests
{
    [Fact]
    public void DefaultOrderIsACompleteUniqueSetOfKnownTabs()
    {
        var order = AddonQuickSettingsTabOrderContract.DefaultOrder;
        Assert.Equal(Enum.GetValues<AddonQuickSettingsTabId>().Order(), order.Order());
        Assert.Equal(order.Count, order.Distinct().Count());
    }
    [Fact]
    public void DefaultOrderCannotBeMutatedByCallers()
    {
        var exposed = (AddonQuickSettingsTabId[])AddonQuickSettingsTabOrderContract.DefaultOrder;
        var original = exposed.ToArray();
        exposed[0] = exposed[0] == AddonQuickSettingsTabId.Setting
            ? AddonQuickSettingsTabId.Device
            : AddonQuickSettingsTabId.Setting;

        Assert.Equal(original, AddonQuickSettingsTabOrderContract.DefaultOrder);
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
    public void Shell_snapshot_preserves_tab_identity_and_authoritative_order_with_nonblank_labels()
    {
        AddonQuickSettingsTabId[] requested =
        [AddonQuickSettingsTabId.Setting, AddonQuickSettingsTabId.Device, AddonQuickSettingsTabId.Profile,
            AddonQuickSettingsTabId.Controller, AddonQuickSettingsTabId.Shortcut];
        var snapshot = AddonQuickSettingsShellContract.Create(requested);

        Assert.True(snapshot.Available);
        Assert.Equal(requested, snapshot.Tabs.Select(tab => tab.TabId));
        Assert.All(snapshot.Tabs, tab => Assert.False(string.IsNullOrWhiteSpace(tab.Label)));
        Assert.False(AddonQuickSettingsShellSnapshot.Unavailable().Available);
        Assert.Empty(AddonQuickSettingsShellSnapshot.Unavailable().Tabs);
    }
    [Fact]
    public void Product_projection_preserves_order_and_move_boundaries()
    {
        AddonQuickSettingsTabId[] requested =
        [AddonQuickSettingsTabId.Setting, AddonQuickSettingsTabId.Device, AddonQuickSettingsTabId.Profile,
            AddonQuickSettingsTabId.Controller, AddonQuickSettingsTabId.Shortcut];
        var snapshot = AddonQuickSettingsTabOrderProduct.Create(requested);

        Assert.Equal(requested, snapshot.Rows.Select(row => row.TabId));
        Assert.All(snapshot.Rows, row => Assert.False(string.IsNullOrWhiteSpace(row.Label)));
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
    public void Product_move_swaps_only_the_requested_adjacent_pair(int delta)
    {
        AddonQuickSettingsTabId[] current =
        [AddonQuickSettingsTabId.Profile, AddonQuickSettingsTabId.Device, AddonQuickSettingsTabId.Shortcut,
            AddonQuickSettingsTabId.Controller, AddonQuickSettingsTabId.Setting];
        var sourceIndex = Array.IndexOf(current, AddonQuickSettingsTabId.Shortcut);
        var targetIndex = sourceIndex + delta;
        var expected = current.ToArray();
        (expected[sourceIndex], expected[targetIndex]) = (expected[targetIndex], expected[sourceIndex]);
        var intent = new AddonQuickSettingsTabOrderMoveIntent(AddonQuickSettingsTabId.Shortcut, delta);

        Assert.True(AddonQuickSettingsTabOrderProduct.TryCreateMovedOrder(current, intent, out var proposed));
        Assert.Equal(expected, proposed);
    }
    [Fact]
    public void Product_rejects_invalid_or_boundary_moves()
    {
        var order = AddonQuickSettingsTabOrderContract.DefaultOrder;
        Assert.False(AddonQuickSettingsTabOrderProduct.TryCreateMovedOrder(order,
            new((AddonQuickSettingsTabId)99, 1), out _));
        Assert.False(AddonQuickSettingsTabOrderProduct.TryCreateMovedOrder(order,
            new(order[0], -1), out _));
        Assert.False(AddonQuickSettingsTabOrderProduct.TryCreateMovedOrder(order,
            new(order[^1], 1), out _));
        Assert.False(AddonQuickSettingsTabOrderProduct.TryCreateMovedOrder(order,
            new(order[0], 0), out _));
        Assert.False(AddonQuickSettingsTabOrderProduct.TryCreateMovedOrder(order,
            new(order[0], 2), out _));
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
