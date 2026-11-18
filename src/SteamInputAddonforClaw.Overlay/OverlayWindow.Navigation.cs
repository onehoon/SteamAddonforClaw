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
    private readonly OverlayNumericSliderRepeatTarget _numericSliderRepeatTarget = new();
    private readonly Brush _rowSelectedFillBrush;
    private static readonly Brush RowUnselectedFillBrush = OverlayQamResources.Brush("QamSectionBrush");

    // The Runtime-owned Shortcut projection uses one transient 2D selection model.
    private readonly OverlayShortcutSelection _shortcutSelection = new();
    private bool _shortcutSettingsSelected;
    private bool _shortcutUserNavigatedThisEntry;

    // OQ5-UI-04 s.11: NavigateUp/Down move logical row selection; Left/Right and Accept dispatch to
    // the selected row only when it registered that capability. All row/selection state stays private
    // to OverlayWindow -- App only forwards the semantic action. Shortcut remains the one 2D page,
    // with its current Runtime-projected tile count and three-column layout.
    internal void NavigateUp()
    {
        MarkShortcutUserNavigatedThisEntry();
        if (OnShortcutPage())
        {
            if (_shortcutSettingsSelected)
            {
                MoveShortcutSettingsRow(up: true);
                return;
            }

            if (_shortcutSelection.SelectedIndex is { } selectedTile &&
                selectedTile < _shortcutSelection.ColumnCount &&
                SelectLastShortcutSettingsRow())
                return;

            if (_shortcutSelection.MoveUp()) ApplyShortcutSelectionVisual(bringIntoView: true);
            return;
        }
        MoveRowSelection(up: true);
    }

    internal void NavigateDown()
    {
        MarkShortcutUserNavigatedThisEntry();
        if (OnShortcutPage())
        {
            if (_shortcutSettingsSelected)
            {
                var lastSelectable = LastSelectableShortcutSettingsRow();
                if (lastSelectable is { } last && _rowSelection.SelectedIndex == last && HasSelectableShortcutTile())
                {
                    SelectFirstShortcutTile();
                    return;
                }

                MoveShortcutSettingsRow(up: false);
                return;
            }

            if (_shortcutSelection.MoveDown()) ApplyShortcutSelectionVisual(bringIntoView: true);
            return;
        }
        MoveRowSelection(up: false);
    }

    // If the selected row became unselectable, the selection method normalizes to another row and
    // reports it; on that same press we only refresh the highlight and skip the adjust/activate so
    // the fallback row is never mutated under a stale highlight.
    internal void AdjustSelectedRow(int delta)
    {
        MarkShortcutUserNavigatedThisEntry();
        _numericSliderRepeatTarget.Clear();
        if (OnShortcutPage())
        {
            if (_shortcutSettingsSelected)
            {
                if (_rowSelection.AdjustSelected(delta))
                {
                    RefreshRowSelectionAfterMove();
                    return;
                }

                _numericSliderRepeatTarget.Arm(_tabState.SelectedTab, _rowSelection.SelectedCapabilities, delta);
                return;
            }

            if (delta < 0 ? _shortcutSelection.MoveLeft() : _shortcutSelection.MoveRight())
                ApplyShortcutSelectionVisual(bringIntoView: true);
            return;
        }
        if (_rowSelection.AdjustSelected(delta))
        {
            RefreshRowSelectionAfterMove();
            return;
        }

        _numericSliderRepeatTarget.Arm(_tabState.SelectedTab, _rowSelection.SelectedCapabilities, delta);
    }

    internal void RepeatAdjustSelectedNumericSlider(int delta)
    {
        if (OnShortcutPage() && !_shortcutSettingsSelected) return;
        _numericSliderRepeatTarget.TryAdjust(_tabState.SelectedTab, _rowSelection.SelectedCapabilities, delta);
    }

    internal void CancelNumericSliderHoldRepeat() => _numericSliderRepeatTarget.Clear();

    internal void ActivateSelectedRow()
    {
        MarkShortcutUserNavigatedThisEntry();
        if (OnShortcutPage())
        {
            if (_shortcutSettingsSelected)
            {
                if (_rowSelection.ActivateSelected()) RefreshRowSelectionAfterMove();
            }
            else
                RequestSelectedShortcutExecution();
            return;
        }
        if (_rowSelection.ActivateSelected()) RefreshRowSelectionAfterMove();
    }

    private bool OnShortcutPage() => _tabState.SelectedTab == AddonQuickSettingsTabId.Shortcut;

    private void MarkShortcutUserNavigatedThisEntry()
    {
        if (OnShortcutPage())
            _shortcutUserNavigatedThisEntry = true;
    }

    private void MoveRowSelection(bool up)
    {
        if (up ? _rowSelection.MovePrevious() : _rowSelection.MoveNext())
        {
            _numericSliderRepeatTarget.Clear();
            RefreshRowSelectionAfterMove();
        }
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
            if (tiles[index].TileId != tileId) continue;
            MarkShortcutUserNavigatedThisEntry();
            _shortcutSettingsSelected = false;
            _numericSliderRepeatTarget.Clear();
            _shortcutSelection.Select(index);
            OverlayLog.Debug("Shortcut", "Shortcut tile selected.", ("TileId", tileId), ("Source", source));
            ApplyRowSelectionVisual();
            ApplyShortcutSelectionVisual();
            return;
        }
    }

    private void ApplyShortcutSelectionVisual(bool bringIntoView = false)
    {
        var selectedTile = _shortcutSettingsSelected ? null : GetSelectedShortcutTile();
        var selectedTileId = selectedTile?.TileId;
        if (_renderedShortcutSelectedTileId != selectedTileId)
        {
            if (_renderedShortcutSelectedTileId is { } previousId
                && _shortcutTiles.TryGetValue(previousId, out var previousControls))
                ApplyShortcutTileSelectionVisual(previousControls, selected: false);
            if (selectedTileId is { } nextId
                && _shortcutTiles.TryGetValue(nextId, out var nextControls))
                ApplyShortcutTileSelectionVisual(nextControls, selected: true);
            _renderedShortcutSelectedTileId = selectedTileId;
        }
        if (bringIntoView && OnShortcutPage()
            && selectedTile is not null
            && _shortcutTiles.TryGetValue(selectedTile.TileId, out var selectedControls))
        {
            try { selectedControls.Border.StartBringIntoView(new BringIntoViewOptions { AnimationDesired = false }); }
            catch (Exception exception) { OverlayLog.Warn("Shortcut", "Could not bring the selected tile into view.", exception); }
        }
    }

    private void ApplyShortcutTileSelectionVisual(ShortcutTileControls tile, bool selected)
    {
        if (tile.IsSelected == selected) return;
        tile.Border.BorderBrush = _rowSelectedBrush;
        tile.Border.Background = OverlayQamResources.Brush(selected ? "QamTileSelectedBrush" : "QamTileBrush");
        tile.IsSelected = selected;
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
        var columns = count == 0 ? 0 : Math.Min(ShortcutVisualColumnCount, count);
        var initialIndex = count == 0 ? 0 : FindInitialShortcutTileIndex(_shortcutSnapshot.Tiles);
        _shortcutSelection.Configure(count, columns, preferredIndex: initialIndex);
    }

    private void MoveShortcutSettingsRow(bool up)
    {
        if (up ? _rowSelection.MovePrevious() : _rowSelection.MoveNext())
        {
            _numericSliderRepeatTarget.Clear();
            RefreshRowSelectionAfterMove();
        }
    }

    private int? LastSelectableShortcutSettingsRow()
    {
        var capabilities = CapabilitiesFor(AddonQuickSettingsTabId.Shortcut);
        for (var index = capabilities.Count - 1; index >= 0; index--)
            if (capabilities[index].IsSelectable())
                return index;
        return null;
    }

    private bool SelectLastShortcutSettingsRow()
    {
        if (LastSelectableShortcutSettingsRow() is not { } index) return false;
        SelectShortcutSettingsRow(index, bringIntoView: true);
        return true;
    }

    private bool HasSelectableShortcutTile() =>
        _shortcutSnapshot.Available && _shortcutSnapshot.Tiles.Count > 0 && _shortcutSelection.SelectedIndex is not null;

    private void SelectFirstShortcutTile()
    {
        if (!HasSelectableShortcutTile()) return;
        _shortcutSettingsSelected = false;
        _numericSliderRepeatTarget.Clear();
        var initialIndex = FindInitialShortcutTileIndex(_shortcutSnapshot.Tiles);
        _shortcutSelection.Select(initialIndex);
        if (_shortcutSelection.SelectedIndex != initialIndex)
        {
            var count = _shortcutSelection.TileCount;
            var columns = _shortcutSelection.ColumnCount;
            _shortcutSelection.Configure(count, columns, preferredIndex: initialIndex);
        }
        ApplyRowSelectionVisual();
        ApplyShortcutSelectionVisual(bringIntoView: true);
    }

    private static int FindInitialShortcutTileIndex(IReadOnlyList<FrontendShortcutTile> tiles)
    {
        for (var index = 0; index < tiles.Count; index++)
            if (tiles[index].Enabled)
                return index;
        return 0;
    }

    private void SelectShortcutSettingsRow(int index, bool bringIntoView)
    {
        _shortcutSettingsSelected = true;
        _numericSliderRepeatTarget.Clear();
        _rowSelection.SetRows(CapabilitiesFor(AddonQuickSettingsTabId.Shortcut), index);
        ApplyShortcutSelectionVisual();
        ApplyRowSelectionVisual();
        if (bringIntoView) BringSelectedRowIntoView();
    }

    private void RefreshShortcutSelection(FrontendShortcutDashboardSnapshot snapshot)
    {
        Guid? selectedTileId = GetSelectedShortcutTile()?.TileId;
        _shortcutSnapshot = snapshot;
        var columns = snapshot.Available && snapshot.Tiles.Count > 0
            ? Math.Min(ShortcutVisualColumnCount, snapshot.Tiles.Count)
            : 0;
        var preferredIndex = OverlayShortcutSelection.FindTileIndex(selectedTileId, snapshot.Tiles);
        var fallbackIndex = snapshot.Available && snapshot.Tiles.Count > 0
            ? FindInitialShortcutTileIndex(snapshot.Tiles)
            : 0;
        _shortcutSelection.Configure(snapshot.Available ? snapshot.Tiles.Count : 0, columns,
            preferredIndex >= 0 ? preferredIndex : fallbackIndex);
        RenderShortcutSnapshot();
    }

    private IReadOnlyList<OverlayRowCapabilities> CapabilitiesFor(AddonQuickSettingsTabId tab)
    {
        var capabilities = _pageRows.TryGetValue(tab, out var rows)
            ? rows.Select(row => row.Capabilities).ToArray()
            : [];
        return OverlayShortcutQuickSettingsProjection.FilterInputCapabilitiesForCurrentShow(
            tab, capabilities, _devicePageReceivedThisShow, _profilePageReceivedThisShow);
    }

    private Border? SelectedRowContainer(AddonQuickSettingsTabId tab)
    {
        if (!_pageRows.TryGetValue(tab, out var rows) ||
            _rowSelection.SelectedIndex is not { } index || index < 0 || index >= rows.Count)
            return null;

        return rows[index].Container;
    }

    private void ReconcileRowSelectionIfNeeded(AddonQuickSettingsTabId tab, Border? preferredContainer = null)
    {
        if (_tabState.SelectedTab != tab || (tab == AddonQuickSettingsTabId.Shortcut && !_shortcutSettingsSelected) ||
            !_pageRows.TryGetValue(tab, out var rows))
            return;

        var selectedIndex = _rowSelection.SelectedIndex;
        var selectedContainer = SelectedRowContainer(tab);
        if (selectedIndex is { } index && index >= 0 && index < rows.Count &&
            rows[index].Capabilities.IsSelectable() &&
            (preferredContainer is null || ReferenceEquals(selectedContainer, preferredContainer)))
            return;
        if (selectedIndex is null && !rows.Any(row => row.Capabilities.IsSelectable()))
            return;

        ReconcileRowSelection(
            tab,
            preferredContainer ?? selectedContainer,
            selectedIndex,
            bringIntoViewOnChange: true);
    }

    private void ReconcileRowSelection(
        AddonQuickSettingsTabId tab,
        Border? preferredContainer,
        int? preferredIndex,
        bool bringIntoViewOnChange)
    {
        if (_tabState.SelectedTab != tab || (tab == AddonQuickSettingsTabId.Shortcut && !_shortcutSettingsSelected) ||
            !_pageRows.TryGetValue(tab, out var rows))
            return;

        var previousContainer = preferredContainer ?? SelectedRowContainer(tab);
        var resolvedIndex = preferredIndex;
        if (preferredContainer is not null)
        {
            for (var index = 0; index < rows.Count; index++)
            {
                if (!ReferenceEquals(rows[index].Container, preferredContainer)) continue;
                resolvedIndex = index;
                break;
            }
        }

        _rowSelection.SetRows(CapabilitiesFor(tab), resolvedIndex);
        var selectedContainer = SelectedRowContainer(tab);
        if (ReferenceEquals(previousContainer, selectedContainer))
            return;

        _numericSliderRepeatTarget.Clear();
        ApplyRowSelectionVisual();
        if (bringIntoViewOnChange && selectedContainer is not null)
            BringSelectedRowIntoView();
    }

    private void ApplyRowSelectionVisual()
    {
        if (!_pageRows.TryGetValue(_tabState.SelectedTab, out var rows)) return;
        var selectedIndex = _rowSelection.SelectedIndex;
        for (var i = 0; i < rows.Count; i++)
        {
            var isActiveSelection = _tabState.SelectedTab != AddonQuickSettingsTabId.Shortcut || _shortcutSettingsSelected;
            var brush = isActiveSelection && i == selectedIndex ? _rowSelectedFillBrush : RowUnselectedFillBrush;
            if (!ReferenceEquals(rows[i].Container.Background, brush))
                rows[i].Container.Background = brush;
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
            if (_tabState.SelectedTab == AddonQuickSettingsTabId.Shortcut)
                _shortcutSettingsSelected = true;
            _numericSliderRepeatTarget.Clear();
            if (_rowSelection.TrySelect(index)) ApplyShortcutSelectionVisual();
            ApplyRowSelectionVisual();
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
