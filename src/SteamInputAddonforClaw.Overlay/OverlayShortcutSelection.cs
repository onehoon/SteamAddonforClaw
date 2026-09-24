namespace SteamInputAddonforClaw.Overlay;

// OQ5-UI-11: pure selection state for the temporary fixed 2x2 Shortcut POC. Not a navigation-graph
// framework -- the geometry is small and fixed, so it is expressed directly. Bounded / no-wrap;
// each move returns whether the selection actually changed so the caller can skip a redundant redraw.
//
//   index 0 (0,0)  index 1 (0,1)
//   index 2 (1,0)  index 3 (1,1)
internal sealed class OverlayShortcutSelection
{
    private const int TileCount = 4;
    private const int ColumnCount = 2;

    internal int SelectedIndex { get; private set; }

    // Entering the Shortcut tab always starts on the first temporary tile.
    internal void Reset() => SelectedIndex = 0;

    internal bool Select(int index)
    {
        if (index is < 0 or >= TileCount || index == SelectedIndex)
            return false;
        SelectedIndex = index;
        return true;
    }

    internal bool MoveUp() => TryMove(deltaRow: -1, deltaColumn: 0);

    internal bool MoveDown() => TryMove(deltaRow: 1, deltaColumn: 0);

    internal bool MoveLeft() => TryMove(deltaRow: 0, deltaColumn: -1);

    internal bool MoveRight() => TryMove(deltaRow: 0, deltaColumn: 1);

    private bool TryMove(int deltaRow, int deltaColumn)
    {
        var (row, column) = PositionOf(SelectedIndex);
        var nextRow = row + deltaRow;
        var nextColumn = column + deltaColumn;
        if (nextRow is < 0 or > 1 || nextColumn is < 0 or > 1)
            return false; // bounded, no wrap

        SelectedIndex = SlotAt(nextRow, nextColumn);
        return true;
    }

    private static (int Row, int Column) PositionOf(int index) => (index / ColumnCount, index % ColumnCount);

    private static int SlotAt(int row, int column) => row * ColumnCount + column;
}
