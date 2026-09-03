using SteamInputAddonforClaw.Contracts.Overlay;

namespace SteamInputAddonforClaw.Overlay;

// Narrow Overlay-only tab selection/order state. Not a navigation framework: it only knows the
// current order, the selected tab, and how to reset selection to order[0] on every Show. Tab
// identity (OverlayTabId) and the five-tab order invariant now live in the shared Contracts
// assembly so Runtime settings persistence and the future .Overlay transport use the same rule.
internal sealed class OverlayTabState
{
    internal static IReadOnlyList<OverlayTabId> DefaultOrder => OverlayTabOrderContract.DefaultOrder;

    private IReadOnlyList<OverlayTabId> _order;
    private OverlayTabId _selectedTab;

    internal OverlayTabState()
        : this(OverlayTabOrderContract.DefaultOrder)
    {
    }

    internal OverlayTabState(IReadOnlyList<OverlayTabId> order)
    {
        ArgumentNullException.ThrowIfNull(order);
        // Any missing / duplicate / unknown order deterministically resolves to the frozen default
        // so the shell can never enter an invalid tab state.
        _order = OverlayTabOrderContract.NormalizeOrDefault(order);
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

    // OQ5-UI-09: apply an authoritative order pushed from the Runtime. Every valid order contains all
    // five identities, so the currently selected tab stays valid and is preserved -- the new first
    // tab only takes effect on the next ResetForShow(). An invalid order is ignored.
    internal bool TryApplyOrder(IReadOnlyList<OverlayTabId> order)
    {
        if (!OverlayTabOrderContract.TryNormalize(order, out var normalized))
            return false;
        _order = normalized;
        return true;
    }

    // OQ5-UI-10: build the one-position-move proposal the Setting-page editor sends to the Runtime.
    // Pure: it never mutates the current order -- only TryApplyOrder(authoritative) does that. delta
    // must be -1 (earlier) or +1 (later); a boundary move produces no proposal.
    internal bool TryCreateMovedOrder(OverlayTabId tab, int delta, out IReadOnlyList<OverlayTabId> proposed)
    {
        proposed = _order;
        if (delta != -1 && delta != 1)
            return false;

        var index = -1;
        for (var i = 0; i < _order.Count; i++)
            if (_order[i] == tab) { index = i; break; }

        var target = index + delta;
        if (index < 0 || target < 0 || target >= _order.Count)
            return false;

        var moved = _order.ToArray();
        (moved[index], moved[target]) = (moved[target], moved[index]);
        if (!OverlayTabOrderContract.TryNormalize(moved, out var normalized))
            return false;

        proposed = normalized;
        return true;
    }

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
}
