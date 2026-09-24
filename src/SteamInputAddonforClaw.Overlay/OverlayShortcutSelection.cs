namespace SteamInputAddonforClaw.Overlay;

/// <summary>Transient bounded selection for the currently rendered Shortcut grid.</summary>
internal sealed class OverlayShortcutSelection
{
    internal int TileCount { get; private set; }
    internal int ColumnCount { get; private set; }
    internal int? SelectedIndex { get; private set; }

    internal void Configure(int tileCount, int columnCount, int? preferredIndex = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(tileCount);
        if (tileCount == 0)
        {
            if (columnCount != 0)
                throw new ArgumentOutOfRangeException(nameof(columnCount));
            TileCount = 0;
            ColumnCount = 0;
            SelectedIndex = null;
            return;
        }

        if (columnCount < 1 || columnCount > tileCount)
            throw new ArgumentOutOfRangeException(nameof(columnCount));

        TileCount = tileCount;
        ColumnCount = columnCount;
        SelectedIndex = preferredIndex is { } index && index >= 0 && index < tileCount ? index : 0;
    }

    internal void Reset() => Configure(tileCount: 0, columnCount: 0);

    internal bool Select(int index)
    {
        if (index < 0 || index >= TileCount || index == SelectedIndex)
            return false;
        SelectedIndex = index;
        return true;
    }

    internal bool MoveUp() => TryMoveVertical(-1);

    internal bool MoveDown() => TryMoveVertical(+1);

    internal bool MoveLeft() => TryMoveHorizontal(-1);

    internal bool MoveRight() => TryMoveHorizontal(+1);

    private bool TryMoveVertical(int deltaRow)
    {
        if (SelectedIndex is not { } selected || ColumnCount == 0)
            return false;

        var row = selected / ColumnCount;
        var targetRow = row + deltaRow;
        var rowCount = (TileCount + ColumnCount - 1) / ColumnCount;
        if (targetRow < 0 || targetRow >= rowCount)
            return false;

        var column = selected % ColumnCount;
        var targetIndex = targetRow * ColumnCount + column;
        var targetRowEndExclusive = Math.Min((targetRow + 1) * ColumnCount, TileCount);
        if (targetIndex >= targetRowEndExclusive)
            targetIndex = targetRowEndExclusive - 1;
        if (targetIndex == selected)
            return false;

        SelectedIndex = targetIndex;
        return true;
    }

    private bool TryMoveHorizontal(int deltaColumn)
    {
        if (SelectedIndex is not { } selected || ColumnCount == 0)
            return false;

        var rowStart = selected / ColumnCount * ColumnCount;
        var rowEndExclusive = Math.Min(rowStart + ColumnCount, TileCount);
        var targetIndex = selected + deltaColumn;
        if (targetIndex < rowStart || targetIndex >= rowEndExclusive)
            return false;

        SelectedIndex = targetIndex;
        return true;
    }
}
