namespace SteamInputAddonforClaw.Overlay;

// Five fixed top-level Overlay tab identities. Identity is kept separate from the visible
// label so a later persisted tab order can reorder known IDs without treating localized
// display text as authority (OQ5-UI-01).
internal enum OverlayTabId
{
    Device,
    Profile,
    Controller,
    Shortcut,
    Setting,
}

// Narrow Overlay-only tab selection/order state. Not a navigation framework: it only knows
// the current order, the selected tab, and how to reset selection to order[0] on every Show.
internal sealed class OverlayTabState
{
    internal static readonly IReadOnlyList<OverlayTabId> DefaultOrder =
    [
        OverlayTabId.Device,
        OverlayTabId.Profile,
        OverlayTabId.Controller,
        OverlayTabId.Shortcut,
        OverlayTabId.Setting,
    ];

    private readonly IReadOnlyList<OverlayTabId> _order;
    private OverlayTabId _selectedTab;

    internal OverlayTabState()
        : this(DefaultOrder)
    {
    }

    internal OverlayTabState(IReadOnlyList<OverlayTabId> order)
    {
        ArgumentNullException.ThrowIfNull(order);
        _order = Normalize(order);
        _selectedTab = _order[0];
    }

    internal IReadOnlyList<OverlayTabId> Order => _order;

    internal OverlayTabId SelectedTab => _selectedTab;

    // Select a known tab. Unknown identity is never accepted as valid local state.
    internal void Select(OverlayTabId tab)
    {
        if (!_order.Contains(tab))
            throw new ArgumentOutOfRangeException(nameof(tab), tab, "Unknown Overlay tab identity.");
        _selectedTab = tab;
    }

    // Every successful Overlay Show starts on the first tab in the current order.
    internal void ResetForShow() => _selectedTab = _order[0];

    // OQ5-UI-02: LB moves one tab earlier in the current order, RB one tab later. Bounded/no-wrap:
    // at either boundary the call is a no-op. Traversal derives position from the current order,
    // never a hard-coded Device -> ... -> Setting sequence, so a later persisted order just works.
    internal bool SelectPrevious() => MoveBy(-1);

    internal bool SelectNext() => MoveBy(1);

    private bool MoveBy(int delta)
    {
        var current = 0;
        for (var i = 0; i < _order.Count; i++)
            if (_order[i] == _selectedTab) { current = i; break; }

        var target = current + delta;
        if (target < 0 || target >= _order.Count) return false;
        _selectedTab = _order[target];
        return true;
    }

    // Any missing / duplicate / unknown order deterministically resolves to the frozen
    // default order so the shell can never enter an invalid tab state.
    private static IReadOnlyList<OverlayTabId> Normalize(IReadOnlyList<OverlayTabId> order)
    {
        var seen = new HashSet<OverlayTabId>();
        foreach (var id in order)
        {
            if (!Enum.IsDefined(id) || !seen.Add(id))
                return DefaultOrder;
        }

        return seen.SetEquals(DefaultOrder) ? order.ToArray() : DefaultOrder;
    }
}
