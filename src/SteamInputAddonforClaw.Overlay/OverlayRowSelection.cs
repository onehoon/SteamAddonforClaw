namespace SteamInputAddonforClaw.Overlay;

// OQ5-UI-04: the narrow capability contract a selectable Overlay row exposes. Enough for the
// OQ5-UI-05 toggle row and OQ5-UI-06 slider row without pre-building those controls. Adjust
// receives -1 / +1. Activate/Adjust are null when the row does not support them.
internal sealed record OverlayRowCapabilities(
    Func<bool> IsSelectable,
    Action? Activate = null,
    Action<int>? Adjust = null);

// One logical controller selection for the active Overlay page. Pure state: it knows nothing
// about controller hardware, tabs, XAML, scrolling, settings, or feature mutation. Bounded /
// no-wrap; unselectable rows are skipped; zero selectable rows is a valid (null) state.
internal sealed class OverlayRowSelection
{
    private IReadOnlyList<OverlayRowCapabilities> _rows = [];
    private int? _selectedIndex;

    internal int? SelectedIndex => _selectedIndex;

    // Swap in the active page's rows and select its first selectable row (or clear).
    internal void SetRows(IReadOnlyList<OverlayRowCapabilities> rows) => SetRows(rows, preferredIndex: null);

    // OQ5-UI-10: same as SetRows(rows) but keeps `preferredIndex` selected when it is a valid,
    // selectable row -- used when the Setting-page tab-order editor reorders its own rows and the
    // caller wants to preserve the selected row identity rather than snap back to the first row.
    internal void SetRows(IReadOnlyList<OverlayRowCapabilities> rows, int? preferredIndex)
    {
        _rows = rows ?? [];
        _selectedIndex =
            preferredIndex is { } index && index >= 0 && index < _rows.Count && _rows[index].IsSelectable()
                ? index
                : FirstSelectableFrom(0, 1);
    }

    internal bool MovePrevious() => Move(-1);

    internal bool MoveNext() => Move(1);

    // Returns true when normalization moved the selection instead of activating: the caller must
    // refresh the highlight and must NOT treat this same controller press as an activation of the
    // newly-selected row (the user still sees the old row highlighted).
    internal bool ActivateSelected()
    {
        if (NormalizeSelection())
            return true;

        if (Current is { Activate: { } activate } row && row.IsSelectable())
            activate();
        return false;
    }

    // Same contract as ActivateSelected for Left/Right adjustment.
    internal bool AdjustSelected(int delta)
    {
        if (NormalizeSelection())
            return true;

        if (Current is { Adjust: { } adjust } row && row.IsSelectable())
            adjust(delta);
        return false;
    }

    private OverlayRowCapabilities? Current =>
        _selectedIndex is { } index && index >= 0 && index < _rows.Count ? _rows[index] : null;

    private bool Move(int direction)
    {
        var changed = NormalizeSelection();
        var start = _selectedIndex is { } index ? index + direction : 0;
        var next = FirstSelectableFrom(start, direction);
        if (next is null || next == _selectedIndex)
            return changed;
        _selectedIndex = next;
        return true;
    }

    // If the selected row vanished or became unselectable since the last action, snap back to the
    // first selectable row (or clear). Returns true when that actually moved the selection so the
    // caller can refresh the highlight. Bounded and local -- no subscription machinery.
    private bool NormalizeSelection()
    {
        if (_selectedIndex is { } index && index >= 0 && index < _rows.Count && _rows[index].IsSelectable())
            return false;

        var before = _selectedIndex;
        _selectedIndex = FirstSelectableFrom(0, 1);
        return before != _selectedIndex;
    }

    private int? FirstSelectableFrom(int start, int direction)
    {
        for (var i = start; i >= 0 && i < _rows.Count; i += direction)
            if (_rows[i].IsSelectable())
                return i;
        return null;
    }
}
