using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SteamInputAddonforClaw.Contracts.Frontend;
using SteamInputAddonforClaw.Overlay.Diagnostics;

namespace SteamInputAddonforClaw.Overlay;

public sealed partial class OverlayWindow
{
    // The shared Quick Settings identity travels with the rendered row so selection can be re-found
    // by RowId across an authoritative page rebuild instead of by numeric index.
    private sealed record OverlayRow(Border Container, OverlayRowCapabilities Capabilities, QuickSettingsRowId? QuickSettingsRowId = null);

    private readonly Dictionary<AddonQuickSettingsTabId, IReadOnlyList<OverlayRow>> _pageRows = new();
    private readonly OverlayRowSelection _rowSelection = new();
    private readonly Brush _rowSelectedFillBrush;
    private static readonly Brush RowUnselectedBrush = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
    private static readonly Brush RowUnselectedFillBrush = new SolidColorBrush(Microsoft.UI.Colors.Transparent);

    // OQ5-UI-11: the fixed 2x2 Shortcut grid -- one pure selection model + the four tile borders.
    private readonly OverlayShortcutSelection _shortcutSelection = new();
    private readonly Dictionary<AddonQuickSettingsShortcutSlotId, Border> _shortcutTiles = new();

    // OQ5-UI-04 s.11: NavigateUp/Down move logical row selection; Left/Right and Accept dispatch to
    // the selected row only when it registered that capability. All row/selection state stays private
    // to OverlayWindow -- App only forwards the semantic action.
    // OQ5-UI-11 s.7.5: the Shortcut page is the one 2D exception -- the same semantic actions drive
    // the fixed 2x2 grid instead of the linear row model while that page is active.
    internal void NavigateUp()
    {
        if (OnShortcutPage()) { if (_shortcutSelection.MoveUp()) ApplyShortcutSelectionVisual(); return; }
        MoveRowSelection(up: true);
    }

    internal void NavigateDown()
    {
        if (OnShortcutPage()) { if (_shortcutSelection.MoveDown()) ApplyShortcutSelectionVisual(); return; }
        MoveRowSelection(up: false);
    }

    // If the selected row became unselectable, the selection method normalizes to another row and
    // reports it; on that same press we only refresh the highlight and skip the adjust/activate so
    // the fallback row is never mutated under a stale highlight.
    internal void AdjustSelectedRow(int delta)
    {
        if (OnShortcutPage())
        {
            if (delta < 0 ? _shortcutSelection.MoveLeft() : _shortcutSelection.MoveRight())
                ApplyShortcutSelectionVisual();
            return;
        }
        if (_rowSelection.AdjustSelected(delta)) RefreshRowSelectionAfterMove();
    }

    internal void ActivateSelectedRow()
    {
        if (OnShortcutPage())
        {
            // Every PR11 slot is Unassigned: A performs no product action.
            OverlayLog.Debug("Shortcut", "Accept on an unassigned Shortcut slot; no action.", ("Slot", _shortcutSelection.SelectedSlot));
            return;
        }
        if (_rowSelection.ActivateSelected()) RefreshRowSelectionAfterMove();
    }

    private bool OnShortcutPage() => _tabState.SelectedTab == AddonQuickSettingsTabId.Shortcut;

    private void MoveRowSelection(bool up)
    {
        if (up ? _rowSelection.MovePrevious() : _rowSelection.MoveNext())
            RefreshRowSelectionAfterMove();
    }

    private void RefreshRowSelectionAfterMove()
    {
        ApplyRowSelectionVisual();
        BringSelectedRowIntoView();
    }

    private void SelectShortcutSlot(AddonQuickSettingsShortcutSlotId slot, string source)
    {
        if (!_shortcutSelection.Select(slot)) return;
        OverlayLog.Debug("Shortcut", "Shortcut tile selected.", ("Slot", slot), ("Source", source));
        ApplyShortcutSelectionVisual();
    }

    private void ApplyShortcutSelectionVisual()
    {
        foreach (var (id, tile) in _shortcutTiles)
            tile.BorderBrush = id == _shortcutSelection.SelectedSlot ? _rowSelectedBrush : RowUnselectedBrush;
    }

    private IReadOnlyList<OverlayRowCapabilities> CapabilitiesFor(AddonQuickSettingsTabId tab) =>
        _pageRows.TryGetValue(tab, out var rows)
            ? rows.Select(row => row.Capabilities).ToArray()
            : [];

    private void ApplyRowSelectionVisual()
    {
        if (!_pageRows.TryGetValue(_tabState.SelectedTab, out var rows)) return;
        var selectedIndex = _rowSelection.SelectedIndex;
        for (var i = 0; i < rows.Count; i++)
        {
            var selected = i == selectedIndex;
            rows[i].Container.Background = selected ? _rowSelectedFillBrush : RowUnselectedFillBrush;
            rows[i].Container.BorderBrush = selected ? _rowSelectedBrush : RowUnselectedBrush;
        }
    }

    // Resolve against the current page order at interaction time. Setting rows can be reordered
    // authoritatively while their Border instances remain alive, so a cached numeric index would
    // select the wrong logical row after a reorder.
    private void RegisterRowPointerSelection(Border container)
    {
        container.PointerPressed += (_, _) => SelectRenderedRow(container);
        container.Tapped += (_, _) => SelectRenderedRow(container);
    }

    private void SelectRenderedRow(Border container)
    {
        if (!_pageRows.TryGetValue(_tabState.SelectedTab, out var rows)) return;
        for (var index = 0; index < rows.Count; index++)
        {
            if (!ReferenceEquals(rows[index].Container, container)) continue;
            if (_rowSelection.TrySelect(index)) ApplyRowSelectionVisual();
            return;
        }
    }

    private void BringSelectedRowIntoView()
    {
        if (!_pageRows.TryGetValue(_tabState.SelectedTab, out var rows)) return;
        if (_rowSelection.SelectedIndex is not { } index || index < 0 || index >= rows.Count) return;
        try
        {
            rows[index].Container.StartBringIntoView(new BringIntoViewOptions { AnimationDesired = false });
        }
        catch (Exception exception)
        {
            OverlayLog.Warn("Shell", "Could not bring the selected row into view.", exception);
        }
    }
}
