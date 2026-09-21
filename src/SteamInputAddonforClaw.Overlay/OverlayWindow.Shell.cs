using System.Linq;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SteamInputAddonforClaw.Contracts.Frontend;
using SteamInputAddonforClaw.Overlay.Diagnostics;

namespace SteamInputAddonforClaw.Overlay;

public sealed partial class OverlayWindow
{
    private readonly OverlayTabState _tabState = new();
    private readonly Dictionary<AddonQuickSettingsTabId, Button> _tabButtons = new();
    private readonly Dictionary<AddonQuickSettingsTabId, Grid> _tabHosts = new();
    private readonly Dictionary<AddonQuickSettingsTabId, Border> _tabIndicators = new();
    private readonly Dictionary<AddonQuickSettingsTabId, FrameworkElement> _tabPages = new();
    private readonly Dictionary<AddonQuickSettingsTabId, AddonQuickSettingsTabOrderRow> _tabOrderRows = new();

    // PR3: the Setting page emits only the shared one-position move intent. OverlayWindow never owns
    // the transport client or constructs a replacement whole order.
    internal event Action<AddonQuickSettingsTabOrderMoveIntent>? TabOrderMoveRequested;

    // OQ5-UI-01: five-tab shell. Tab buttons and placeholder pages are built once from the
    // current tab order; identity (AddonQuickSettingsTabId) is carried on Button.Tag and kept separate
    // from the visible label text so a later persisted order can reorder known IDs.
    private void BuildShell()
    {
        var order = _tabState.Order;
        for (var column = 0; column < order.Count; column++)
        {
            var id = order[column];

            var button = new Button
            {
                Content = LabelFor(id),
                Tag = id,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Center,
                Padding = new Thickness(6, 6, 6, 8),
                MinWidth = 0,
                MinHeight = 34,
                CornerRadius = new CornerRadius(0),
                FontSize = 13,
            };
            button.Click += OnTabHeaderClick;

            var tabHost = new Grid
            {
                RowDefinitions =
                {
                    new RowDefinition { Height = GridLength.Auto },
                    new RowDefinition { Height = GridLength.Auto },
                },
            };
            Grid.SetRow(button, 0);
            tabHost.Children.Add(button);

            var indicator = new Border
            {
                Height = 3,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Background = _rowSelectedBrush,
                Visibility = Visibility.Collapsed,
            };
            Grid.SetRow(indicator, 1);
            tabHost.Children.Add(indicator);

            Grid.SetColumn(tabHost, column);
            TabStrip.Children.Add(tabHost);
            _tabButtons[id] = button;
            _tabHosts[id] = tabHost;
            _tabIndicators[id] = indicator;

            var rows = new List<OverlayRow>();
            var page = BuildPage(id, rows);
            page.Visibility = Visibility.Collapsed;
            TabBody.Children.Add(page);
            _tabPages[id] = page;
            _pageRows[id] = rows;
        }

        ApplySelectedTabVisualState();
    }

    // Device and Profile both get the SF-V2-07/09 generic Quick Settings renderer; Setting gets the
    // OQ5-UI-10 tab-order editor; Shortcut gets the OQ5-UI-11 2x2 slot shell; every other tab keeps
    // its OQ5-UI-01 placeholder with zero selectable rows.
    private FrameworkElement BuildPage(AddonQuickSettingsTabId id, List<OverlayRow> rows) => id switch
    {
        AddonQuickSettingsTabId.Setting => BuildSettingPage(rows),
        AddonQuickSettingsTabId.Shortcut => BuildShortcutPage(),
        AddonQuickSettingsTabId.Device => BuildQuickSettingsPage(id, QuickSettingsPageId.Device),
        AddonQuickSettingsTabId.Profile => BuildQuickSettingsPage(id, QuickSettingsPageId.Profile),
        _ => CreatePlaceholderPage(id),
    };

    // OQ5-UI-10: the five fixed tab-order rows live in one 5-row Grid, created once and kept by
    // AddonQuickSettingsTabId. ApplyTabOrder repositions them via Grid.SetRow -- instances are never recreated.
    private FrameworkElement BuildTabOrderEditorPage(List<OverlayRow> rows)
    {
        var section = new StackPanel { Spacing = 5 };

        var heading = new TextBlock { Text = "Tab Order" };
        if (Application.Current.Resources.TryGetValue("BodyStrongTextBlockStyle", out var style) && style is Style headingStyle)
            heading.Style = headingStyle;
        section.Children.Add(heading);

        var grid = new Grid { RowSpacing = 4 };
        var order = _tabState.Order;
        for (var i = 0; i < order.Count; i++)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var id = order[i];
            var row = new AddonQuickSettingsTabOrderRow(id, LabelFor(id), delta => RequestTabOrderMove(id, delta));
            row.SetPosition(i, order.Count);
            Grid.SetRow(row.Container, i);
            grid.Children.Add(row.Container);
            _tabOrderRows[id] = row;
            rows.Add(new OverlayRow(row.Container, row.Capabilities));
            RegisterRowPointerSelection(row.Container);
        }

        section.Children.Add(grid);
        return section;
    }

    // PR3: request only a single typed move. The visible order changes only when the Runtime returns
    // or republishes authoritative typed state.
    private void RequestTabOrderMove(AddonQuickSettingsTabId tab, int delta)
    {
        if (delta is not (-1 or 1)) return;
        OverlayLog.Info("TabOrder", "Tab order move requested.", ("Tab", tab), ("Delta", delta < 0 ? -1 : 1));
        TabOrderMoveRequested?.Invoke(new(tab, delta));
    }

    // OQ5-UI-11: the fixed four-slot Shortcut shell. A 2x2 Grid of four Unassigned tiles kept by
    // slot identity. Not registered as _pageRows -- OverlayShortcutSelection owns the one selected
    // tile while this page is active, and A does nothing because no slot has an action yet.
    private FrameworkElement BuildShortcutPage()
    {
        var grid = new Grid
        {
            ColumnSpacing = 8,
            RowSpacing = 8,
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
            },
            RowDefinitions =
            {
                new RowDefinition { Height = GridLength.Auto },
                new RowDefinition { Height = GridLength.Auto },
            },
        };

        var shortcut = AddonQuickSettingsShortcutContract.Create();
        foreach (var (slot, index) in shortcut.Slots.Select((slot, index) => (slot, index)))
        {
            var row = index / 2;
            var column = index % 2;
            var title = new TextBlock { Text = slot.Label };
            if (Application.Current.Resources.TryGetValue("BodyStrongTextBlockStyle", out var titleStyle) && titleStyle is Style ts)
                title.Style = ts;

            var state = new TextBlock { Text = slot.StatusLabel, Opacity = 0.6 };
            if (Application.Current.Resources.TryGetValue("CaptionTextBlockStyle", out var stateStyle) && stateStyle is Style ss)
                state.Style = ss;

            var content = new StackPanel { Spacing = 2 };
            content.Children.Add(title);
            content.Children.Add(state);

            var tile = new Border
            {
                Child = content,
                Padding = new Thickness(14, 16, 14, 16),
                MinHeight = 72,
                CornerRadius = new CornerRadius(6),
                BorderThickness = new Thickness(2),
                BorderBrush = RowUnselectedBrush,
                HorizontalAlignment = HorizontalAlignment.Stretch,
            };
            if (Application.Current.Resources.TryGetValue("CardBackgroundFillColorDefaultBrush", out var fill) && fill is Brush fillBrush)
                tile.Background = fillBrush;
            tile.Tapped += (_, _) => SelectShortcutSlot(slot.SlotId, "Pointer");
            Grid.SetRow(tile, row);
            Grid.SetColumn(tile, column);
            grid.Children.Add(tile);
            _shortcutTiles[slot.SlotId] = tile;
        }

        ApplyShortcutSelectionVisual();
        return grid;
    }

    private static string LabelFor(AddonQuickSettingsTabId id) => AddonQuickSettingsShellContract.LabelFor(id);

    private static FrameworkElement CreatePlaceholderPage(AddonQuickSettingsTabId id)
    {
        var page = new TextBlock
        {
            Text = LabelFor(id),
            Opacity = 0.6,
            TextWrapping = TextWrapping.Wrap,
        };
        if (Application.Current.Resources.TryGetValue("BodyTextBlockStyle", out var style) && style is Style bodyStyle)
            page.Style = bodyStyle;
        return page;
    }

    private void OnTabHeaderClick(object sender, RoutedEventArgs args)
    {
        if (sender is Button { Tag: AddonQuickSettingsTabId id } && id != _tabState.SelectedTab)
        {
            _tabState.Select(id);
            ApplySelectedTabVisualState();
        }
    }

    // Reset selection to the first tab in the current order before every visual reveal.
    private void ResetUiForShow()
    {
        _tabState.ResetForShow();
        ApplySelectedTabVisualState();
    }

    // OQ5-UI-02: LB/RB semantic tab navigation from the Runtime capture path. Keeps all visual
    // dictionary/page-visibility logic here; only re-applies visuals when selection actually moved
    // (no-op at a boundary). App marshals the semantic action, it never touches tab state directly.
    internal void SelectPreviousTab()
    {
        if (_tabState.SelectPrevious()) ApplySelectedTabVisualState();
    }

    internal void SelectNextTab()
    {
        if (_tabState.SelectNext()) ApplySelectedTabVisualState();
    }

    // OQ5-UI-09: apply an authoritative tab order from the Runtime without disturbing the visible
    // session. The five page/button/row instances are preserved; only the tab-strip column order and
    // the selected-header accent change. Selected page/row/scroll position stay exactly as they are
    // (s.11.1) -- the new first tab only takes effect on the next Show via ResetForShow().
    internal void ApplyTabOrderState(AddonQuickSettingsTabOrderSnapshot state)
    {
        // Capture the Setting-editor row identity selected right now (before the order changes) so a
        // live reorder preserves the selected tab rather than the old numeric row slot.
        AddonQuickSettingsTabId? selectedEditorTab = null;
        var settingVisible = _tabState.SelectedTab == AddonQuickSettingsTabId.Setting;
        if (settingVisible && _rowSelection.SelectedIndex is { } selected && selected >= _clawHudRows.Count && selected - _clawHudRows.Count < _tabState.Order.Count)
            selectedEditorTab = _tabState.Order[selected - _clawHudRows.Count];

        if (!state.Available || state.Rows.Count != 5)
        {
            OverlayLog.Warn("Shell", "Ignored an unavailable or malformed authoritative Overlay tab order.");
            return;
        }

        var order = state.Rows.Select(row => row.TabId).ToArray();
        if (!AddonQuickSettingsTabOrderContract.TryNormalize(order, out var normalized) ||
            !state.Rows.SequenceEqual(AddonQuickSettingsTabOrderProduct.Create(normalized).Rows))
        {
            OverlayLog.Warn("Shell", "Ignored an invalid authoritative Overlay tab-order projection.");
            return;
        }

        if (!_tabState.TryApplyOrder(normalized))
        {
            OverlayLog.Warn("Shell", "Ignored an invalid authoritative Overlay tab order.");
            return;
        }

        var applied = _tabState.Order;
        for (var position = 0; position < applied.Count; position++)
        {
            if (_tabHosts.TryGetValue(applied[position], out var tabHost))
                Grid.SetColumn(tabHost, position);
            if (_tabOrderRows.TryGetValue(applied[position], out var editorRow))
            {
                Grid.SetRow(editorRow.Container, position);
            }
        }

        foreach (var rowState in state.Rows)
            if (_tabOrderRows.TryGetValue(rowState.TabId, out var editorRow))
                editorRow.ApplyState(rowState);

        // Rebuild the Setting page's ordered row list so CapabilitiesFor(Setting) / the selection
        // model see the authoritative order. The AddonQuickSettingsTabOrderRow instances are reused.
        if (_tabOrderRows.Count == applied.Count)
            _pageRows[AddonQuickSettingsTabId.Setting] = _clawHudRows.Concat(applied
                .Select(id => new OverlayRow(_tabOrderRows[id].Container, _tabOrderRows[id].Capabilities))
                .ToArray()).ToArray();

        ApplySelectedHeaderVisual();

        // s.8.2/8.4: if the Setting editor is what the user is looking at, re-point selection at the
        // same identity and refresh just the row highlight -- do NOT run the full tab-change path
        // (which would reset body scroll and selection).
        if (settingVisible)
        {
            int? preferredIndex = null;
            if (selectedEditorTab is { } tab)
                for (var i = 0; i < applied.Count; i++)
                    if (applied[i] == tab) { preferredIndex = _clawHudRows.Count + i; break; }
            _rowSelection.SetRows(CapabilitiesFor(AddonQuickSettingsTabId.Setting), preferredIndex);
            ApplyRowSelectionVisual();
        }

        OverlayLog.Info("Shell", "Authoritative Overlay tab order applied.", ("SelectedTab", _tabState.SelectedTab));
    }

    private void ApplySelectedHeaderVisual()
    {
        var selected = _tabState.SelectedTab;
        foreach (var (id, button) in _tabButtons)
        {
            var isSelected = id == selected;
            button.FontWeight = isSelected ? FontWeights.SemiBold : FontWeights.Normal;
            // Keep the native Button interaction states, but never use a permanent selected fill.
            button.ClearValue(Control.BackgroundProperty);
            button.ClearValue(Control.ForegroundProperty);
            if (_tabIndicators.TryGetValue(id, out var indicator))
                indicator.Visibility = isSelected ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    // s.12: deterministic tab-change ordering -- tab visuals, then show the page and reset the
    // shared scroll to top, then reset that page's row selection to its first selectable row,
    // then apply the row-selection visual.
    private void ApplySelectedTabVisualState()
    {
        var selected = _tabState.SelectedTab;
        ApplySelectedHeaderVisual();
        foreach (var (id, page) in _tabPages)
            page.Visibility = id == selected ? Visibility.Visible : Visibility.Collapsed;

        try
        {
            BodyScroll.ChangeView(null, 0, null, disableAnimation: true);
        }
        catch (Exception exception)
        {
            OverlayLog.Warn("Shell", "Could not reset the body scroll position on tab change.", exception);
        }

        _rowSelection.SetRows(CapabilitiesFor(selected));
        ApplyRowSelectionVisual();

        // OQ5-UI-11 s.7.4: entering the Shortcut page selects Slot 1. CapabilitiesFor(Shortcut) is
        // empty, so _rowSelection has no selected row and OverlayShortcutSelection is the one
        // selection authority for that page.
        if (selected == AddonQuickSettingsTabId.Shortcut)
        {
            _shortcutSelection.Reset();
            ApplyShortcutSelectionVisual();
        }
    }
}
