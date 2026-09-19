using SteamInputAddonforClaw.Contracts.Frontend;

namespace SteamInputAddonforClaw.Overlay;

// OQ5-UI-11: pure selection state for the fixed 2x2 Shortcut grid. Not a navigation-graph framework
// -- the geometry is small and fixed, so it is expressed directly. Bounded / no-wrap; each move
// returns whether the selection actually changed so the caller can skip a redundant redraw.
//
//   Slot1 (0,0)  Slot2 (0,1)
//   Slot3 (1,0)  Slot4 (1,1)
internal sealed class OverlayShortcutSelection
{
    internal AddonQuickSettingsShortcutSlotId SelectedSlot { get; private set; } = AddonQuickSettingsShortcutSlotId.Slot1;

    // Entering the Shortcut tab always starts on Slot 1 (matches "entering a page selects its first item").
    internal void Reset() => SelectedSlot = AddonQuickSettingsShortcutSlotId.Slot1;

    internal bool Select(AddonQuickSettingsShortcutSlotId slot)
    {
        if (slot == SelectedSlot)
            return false;
        SelectedSlot = slot;
        return true;
    }

    internal bool MoveUp() => TryMove(deltaRow: -1, deltaColumn: 0);

    internal bool MoveDown() => TryMove(deltaRow: 1, deltaColumn: 0);

    internal bool MoveLeft() => TryMove(deltaRow: 0, deltaColumn: -1);

    internal bool MoveRight() => TryMove(deltaRow: 0, deltaColumn: 1);

    private bool TryMove(int deltaRow, int deltaColumn)
    {
        var (row, column) = PositionOf(SelectedSlot);
        var nextRow = row + deltaRow;
        var nextColumn = column + deltaColumn;
        if (nextRow is < 0 or > 1 || nextColumn is < 0 or > 1)
            return false; // bounded, no wrap

        SelectedSlot = SlotAt(nextRow, nextColumn);
        return true;
    }

    private static (int Row, int Column) PositionOf(AddonQuickSettingsShortcutSlotId slot) => slot switch
    {
        AddonQuickSettingsShortcutSlotId.Slot1 => (0, 0),
        AddonQuickSettingsShortcutSlotId.Slot2 => (0, 1),
        AddonQuickSettingsShortcutSlotId.Slot3 => (1, 0),
        AddonQuickSettingsShortcutSlotId.Slot4 => (1, 1),
        _ => (0, 0),
    };

    private static AddonQuickSettingsShortcutSlotId SlotAt(int row, int column) => (row, column) switch
    {
        (0, 0) => AddonQuickSettingsShortcutSlotId.Slot1,
        (0, 1) => AddonQuickSettingsShortcutSlotId.Slot2,
        (1, 0) => AddonQuickSettingsShortcutSlotId.Slot3,
        _ => AddonQuickSettingsShortcutSlotId.Slot4,
    };
}
