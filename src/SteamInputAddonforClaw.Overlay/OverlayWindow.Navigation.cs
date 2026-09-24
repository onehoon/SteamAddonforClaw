using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
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

    // The Runtime-owned Shortcut projection uses one transient 2D selection model.
    private readonly OverlayShortcutSelection _shortcutSelection = new();

    // OQ5-UI-04 s.11: NavigateUp/Down move logical row selection; Left/Right and Accept dispatch to
    // the selected row only when it registered that capability. All row/selection state stays private
    // to OverlayWindow -- App only forwards the semantic action. Shortcut remains the one 2D page,
    // with its current Runtime-projected tile count and two-column layout.
    internal void NavigateUp()
    {
        if (OnProfileCatalogPage()) { NavigateProfileCatalogUp(); return; }
        if (OnShortcutPage()) { if (_shortcutSelection.MoveUp()) ApplyShortcutSelectionVisual(); return; }
        MoveRowSelection(up: true);
    }

    internal void NavigateDown()
    {
        if (OnProfileCatalogPage()) { NavigateProfileCatalogDown(); return; }
        if (OnShortcutPage()) { if (_shortcutSelection.MoveDown()) ApplyShortcutSelectionVisual(); return; }
        MoveRowSelection(up: false);
    }

    // If the selected row became unselectable, the selection method normalizes to another row and
    // reports it; on that same press we only refresh the highlight and skip the adjust/activate so
    // the fallback row is never mutated under a stale highlight.
    internal void AdjustSelectedRow(int delta)
    {
        if (OnProfileCatalogPage())
        {
            if (delta < 0) NavigateProfileCatalogLeft(); else NavigateProfileCatalogRight();
            return;
        }
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
        if (OnProfileCatalogPage()) { ActivateProfileCatalogSelection(); return; }
        if (OnShortcutPage())
        {
            RequestSelectedShortcutExecution();
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

    private void SelectShortcutTile(Guid tileId, string source)
    {
        var tiles = _shortcutSnapshot.Tiles;
        for (var index = 0; index < tiles.Count; index++)
        {
            if (tiles[index].TileId != tileId || !_shortcutSelection.Select(index)) continue;
            OverlayLog.Debug("Shortcut", "Shortcut tile selected.", ("TileId", tileId), ("Source", source));
            ApplyShortcutSelectionVisual();
            return;
        }
    }

    private void ApplyShortcutSelectionVisual()
    {
        var selectedTile = GetSelectedShortcutTile();
        foreach (var (tileId, tile) in _shortcutTiles)
            tile.BorderBrush = selectedTile?.TileId == tileId ? _rowSelectedBrush : RowUnselectedBrush;
        if (selectedTile is not null && _shortcutTiles.TryGetValue(selectedTile.TileId, out var selectedBorder))
        {
            try { selectedBorder.StartBringIntoView(new BringIntoViewOptions { AnimationDesired = false }); }
            catch (Exception exception) { OverlayLog.Warn("Shortcut", "Could not bring the selected tile into view.", exception); }
        }
    }

    private FrontendShortcutTile? GetSelectedShortcutTile()
    {
        if (_shortcutSelection.SelectedIndex is not { } index
            || index < 0
            || index >= _shortcutSnapshot.Tiles.Count)
            return null;
        return _shortcutSnapshot.Tiles[index];
    }

    private void ResetShortcutSelection()
    {
        var count = _shortcutSnapshot.Available ? _shortcutSnapshot.Tiles.Count : 0;
        var columns = count == 0 ? 0 : Math.Min(2, count);
        _shortcutSelection.Configure(count, columns, preferredIndex: 0);
    }

    private void RefreshShortcutSelection(FrontendShortcutDashboardSnapshot snapshot)
    {
        Guid? selectedTileId = GetSelectedShortcutTile()?.TileId;
        _shortcutSnapshot = snapshot;
        var columns = snapshot.Available && snapshot.Tiles.Count > 0 ? Math.Min(2, snapshot.Tiles.Count) : 0;
        var preferredIndex = selectedTileId is { } id
            ? snapshot.Tiles.ToList().FindIndex(tile => tile.TileId == id)
            : -1;
        _shortcutSelection.Configure(snapshot.Available ? snapshot.Tiles.Count : 0, columns,
            preferredIndex >= 0 ? preferredIndex : 0);
        RenderShortcutSnapshot();
        ApplyShortcutSelectionVisual();
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
        container.AddHandler(
            UIElement.PointerPressedEvent,
            new PointerEventHandler((_, _) => SelectRenderedRow(container)),
            handledEventsToo: true);
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
