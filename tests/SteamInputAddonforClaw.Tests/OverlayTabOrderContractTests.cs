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
}
