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
    public void AuthoritativeRefreshKeepsTheSelectedClawHudRow()
    {
        var state = new OverlayTabState();
        const int clawHudRowCount = 3;
        const int alignmentRowIndex = 2; // ClawHUD card header precedes its detail rows.
        var rows = Enumerable.Range(0, clawHudRowCount + state.Order.Count)
            .Select(_ => Selectable())
            .ToArray();
        var selection = new OverlayRowSelection();
        selection.SetRows(rows, preferredIndex: alignmentRowIndex);

        var previousOrder = state.Order.ToArray();
        IReadOnlyList<AddonQuickSettingsTabId> proposed =
        [
            AddonQuickSettingsTabId.Profile,
            AddonQuickSettingsTabId.Device,
            AddonQuickSettingsTabId.Controller,
            AddonQuickSettingsTabId.Shortcut,
            AddonQuickSettingsTabId.Setting,
        ];

        Assert.True(state.TryApplyOrder(proposed));

        var preferredIndex = OverlayWindow.ResolvePreferredSettingRowIndex(
            selection.SelectedIndex,
            clawHudRowCount + 4,
            previousOrder,
            state.Order);
        selection.SetRows(rows, preferredIndex);

        Assert.Equal(alignmentRowIndex, selection.SelectedIndex);
    }

    [Fact]
    public void AuthoritativeRefreshKeepsTheSelectedTabOrderRowIdentityAfterCardHeaders()
    {
        var state = new OverlayTabState();
        const int clawHudRowCount = 3;
        var previousOrder = state.Order.ToArray();
        var editorRowStart = clawHudRowCount + 4; // Three Setting card headers and the Quick Settings detail row.
        var selectedIndex = editorRowStart + 2; // Controller editor row.
        IReadOnlyList<AddonQuickSettingsTabId> appliedOrder =
        [
            AddonQuickSettingsTabId.Device,
            AddonQuickSettingsTabId.Shortcut,
            AddonQuickSettingsTabId.Profile,
            AddonQuickSettingsTabId.Controller,
            AddonQuickSettingsTabId.Setting,
        ];

        var preferredIndex = OverlayWindow.ResolvePreferredSettingRowIndex(
            selectedIndex,
            editorRowStart,
            previousOrder,
            appliedOrder);

        Assert.Equal(editorRowStart + 3, preferredIndex);
        Assert.Equal(AddonQuickSettingsTabId.Controller, appliedOrder[preferredIndex!.Value - editorRowStart]);
    }

    [Fact]
    public void Collapsed_setting_card_skips_its_hidden_detail_rows()
    {
        var expanded = false;
        var rows = new OverlayRowSelection();
        rows.SetRows(
        [
            Selectable(),
            new OverlayRowCapabilities(() => expanded),
            Selectable(),
        ]);

        Assert.True(rows.MoveNext());
        Assert.Equal(2, rows.SelectedIndex);

        expanded = true;
        rows.SetRows(
        [
            Selectable(),
            new OverlayRowCapabilities(() => expanded),
            Selectable(),
        ], preferredIndex: 0);
        Assert.True(rows.MoveNext());
        Assert.Equal(1, rows.SelectedIndex);

        expanded = false;
        Assert.True(rows.MoveNext());
        Assert.Equal(2, rows.SelectedIndex);
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
