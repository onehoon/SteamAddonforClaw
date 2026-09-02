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
    internal void SetRows(IReadOnlyList<OverlayRowCapabilities> rows)
    {
        _rows = rows ?? [];
        _selectedIndex = FirstSelectableFrom(0, 1);
    }

    internal bool MovePrevious() => Move(-1);

    internal bool MoveNext() => Move(1);

    internal void ActivateSelected()
    {
        NormalizeSelection();
        if (Current is { Activate: { } activate } row && row.IsSelectable())
            activate();
    }

    internal void AdjustSelected(int delta)
    {
        NormalizeSelection();
        if (Current is { Adjust: { } adjust } row && row.IsSelectable())
            adjust(delta);
    }

    private OverlayRowCapabilities? Current =>
        _selectedIndex is { } index && index >= 0 && index < _rows.Count ? _rows[index] : null;

    private bool Move(int direction)
    {
        NormalizeSelection();
        var start = _selectedIndex is { } index ? index + direction : 0;
        var next = FirstSelectableFrom(start, direction);
        if (next is null || next == _selectedIndex)
            return false;
        _selectedIndex = next;
        return true;
    }

    // If the selected row vanished or became unselectable since the last action, snap back to
    // the first selectable row (or clear). Bounded and local -- no subscription machinery.
    private void NormalizeSelection()
    {
        if (_selectedIndex is { } index && index >= 0 && index < _rows.Count && _rows[index].IsSelectable())
            return;
        _selectedIndex = FirstSelectableFrom(0, 1);
    }

    private int? FirstSelectableFrom(int start, int direction)
    {
        for (var i = start; i >= 0 && i < _rows.Count; i += direction)
            if (_rows[i].IsSelectable())
                return i;
        return null;
    }
}
