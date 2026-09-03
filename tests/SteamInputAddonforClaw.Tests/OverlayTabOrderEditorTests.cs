using SteamInputAddonforClaw.Contracts.Overlay;
using SteamInputAddonforClaw.Overlay;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

// OQ5-UI-10: the Setting-page tab-order editor is WinUI, but its non-visual contract is the pure
// composition of OverlayTabState (proposal + authoritative apply) and OverlayRowSelection
// (identity-preserving reselection). These cover that composition without a XAML host.
public sealed class OverlayTabOrderEditorTests
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
        Assert.Equal(OverlayTabId.Controller, selectedId);

        Assert.True(state.TryCreateMovedOrder(selectedId, -1, out var proposed));

        // Runtime accepts and republishes; OverlayWindow.ApplyTabOrder re-points selection at the
        // same identity's new index.
        Assert.True(state.TryApplyOrder(proposed));
        var newIndex = IndexOf(state.Order, selectedId);
        selection.SetRows([Selectable(), Selectable(), Selectable(), Selectable(), Selectable()], preferredIndex: newIndex);

        Assert.Equal(1, newIndex);
        Assert.Equal(1, selection.SelectedIndex);
        Assert.Equal(OverlayTabId.Controller, state.Order[selection.SelectedIndex!.Value]); // still Controller, not Profile
    }

    [Fact]
    public void BoundaryMoveProducesNoProposalSoNoRequestIsSent()
    {
        var state = new OverlayTabState();

        Assert.False(state.TryCreateMovedOrder(state.Order[0], -1, out _));
        Assert.False(state.TryCreateMovedOrder(state.Order[^1], +1, out _));
        Assert.Equal(OverlayTabState.DefaultOrder, state.Order);
    }

    [Fact]
    public void NextShowStillSelectsTheNewFirstAuthoritativeTab()
    {
        var state = new OverlayTabState();
        state.Select(OverlayTabId.Setting);

        Assert.True(state.TryCreateMovedOrder(OverlayTabId.Profile, -1, out var proposed)); // Profile -> position 0
        Assert.True(state.TryApplyOrder(proposed));

        Assert.Equal(OverlayTabId.Setting, state.SelectedTab); // preserved during live reorder
        state.ResetForShow();
        Assert.Equal(OverlayTabId.Profile, state.SelectedTab); // new first tab on the next Show
    }

    private static int IndexOf(IReadOnlyList<OverlayTabId> order, OverlayTabId tab)
    {
        for (var i = 0; i < order.Count; i++)
            if (order[i] == tab) return i;
        return -1;
    }
}
