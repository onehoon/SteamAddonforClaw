namespace SteamInputAddonforClaw.Overlay;

/// <summary>Bounded three-column selection for the Overlay Profile catalog. It owns only local
/// presentation selection; catalog identity and Profile mutation remain Runtime-owned.</summary>
internal sealed class OverlayProfileCatalogSelection
{
    private const int Columns = 3;
    private int _count;
    private int _selectedIndex = -1;

    internal int Count => _count;
    internal int SelectedIndex => _selectedIndex;

    internal void Reset(int count)
    {
        _count = Math.Max(0, count);
        _selectedIndex = _count == 0 ? -1 : 0;
    }

    internal bool Select(int index)
    {
        if (index < 0 || index >= _count || index == _selectedIndex) return false;
        _selectedIndex = index;
        return true;
    }

    internal bool MoveLeft() => MoveTo(_selectedIndex - 1, _selectedIndex % Columns != 0);
    internal bool MoveRight() => MoveTo(_selectedIndex + 1, _selectedIndex >= 0 && _selectedIndex % Columns < Columns - 1);

    internal bool MoveUp()
    {
        if (_selectedIndex < 0) return false;
        var row = _selectedIndex / Columns;
        if (row == 0) return false;
        var column = _selectedIndex % Columns;
        return MoveTo(Math.Min((row - 1) * Columns + column, _count - 1), true);
    }

    internal bool MoveDown()
    {
        if (_selectedIndex < 0) return false;
        var row = _selectedIndex / Columns;
        var target = (row + 1) * Columns + (_selectedIndex % Columns);
        if (target >= _count)
        {
            var lastRowStart = (row + 1) * Columns;
            if (lastRowStart >= _count) return false;
            target = _count - 1;
        }
        return MoveTo(target, true);
    }

    private bool MoveTo(int index, bool allowed)
    {
        if (!allowed || index < 0 || index >= _count || index == _selectedIndex) return false;
        _selectedIndex = index;
        return true;
    }
}
