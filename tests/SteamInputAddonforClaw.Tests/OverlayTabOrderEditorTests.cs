using SteamInputAddonforClaw.Contracts.Frontend;
using SteamInputAddonforClaw.Overlay;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

// OQ5-UI-10: the Setting-page tab-order editor is WinUI, but its non-visual contract is the pure
// composition of the shared tab-order product (intent + authoritative apply) and OverlayRowSelection
// (identity-preserving reselection). These cover that composition without a XAML host.
public sealed class AddonQuickSettingsTabOrderEditorTests
{
    private static OverlayRowCapabilities Selectable() => new(() => true);

    [Fact]
    public void LiveReorderKeepsTheSelectedEditorRowIdentityNotItsOldSlot()
    {
        var state = new OverlayTabState(); // Device, Profile, Controller, Shortcut, Setting
        var selection = new OverlayRowSelection();
        selection.SetRows([Selectable(), Selectable(), Selectable(), Selectable(), Selectable()]);

        // User selects the "Controller" editor row (index 2) and presses Left.
        selection.MoveNext();
        selection.MoveNext();
        var selectedId = state.Order[selection.SelectedIndex!.Value];
        Assert.Equal(AddonQuickSettingsTabId.Controller, selectedId);

        IReadOnlyList<AddonQuickSettingsTabId> proposed =
        [
            AddonQuickSettingsTabId.Device,
            AddonQuickSettingsTabId.Controller,
            AddonQuickSettingsTabId.Profile,
            AddonQuickSettingsTabId.Shortcut,
            AddonQuickSettingsTabId.Setting,
        ];

        // Runtime accepts and republishes; OverlayWindow.ApplyTabOrder re-points selection at the
        // same identity's new index.
        Assert.True(state.TryApplyOrder(proposed));
        var newIndex = IndexOf(state.Order, selectedId);
        selection.SetRows([Selectable(), Selectable(), Selectable(), Selectable(), Selectable()], preferredIndex: newIndex);

        Assert.Equal(1, newIndex);
        Assert.Equal(1, selection.SelectedIndex);
        Assert.Equal(AddonQuickSettingsTabId.Controller, state.Order[selection.SelectedIndex!.Value]); // still Controller, not Profile
    }

    [Fact]
    public void NextShowStillSelectsTheNewFirstAuthoritativeTab()
    {
        var state = new OverlayTabState();
        state.Select(AddonQuickSettingsTabId.Setting);
        IReadOnlyList<AddonQuickSettingsTabId> proposed =
        [
            AddonQuickSettingsTabId.Profile,
            AddonQuickSettingsTabId.Device,
            AddonQuickSettingsTabId.Controller,
            AddonQuickSettingsTabId.Shortcut,
            AddonQuickSettingsTabId.Setting,
        ];
        Assert.True(state.TryApplyOrder(proposed));

        Assert.Equal(AddonQuickSettingsTabId.Setting, state.SelectedTab); // preserved during live reorder
        state.ResetForShow();
        Assert.Equal(AddonQuickSettingsTabId.Profile, state.SelectedTab); // new first tab on the next Show
    }

    private static int IndexOf(IReadOnlyList<AddonQuickSettingsTabId> order, AddonQuickSettingsTabId tab)
    {
        for (var i = 0; i < order.Count; i++)
            if (order[i] == tab) return i;
        return -1;
    }
}
