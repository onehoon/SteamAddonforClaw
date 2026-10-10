using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
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
        for (var position = 0; position < order.Count; position++)
        {
            var id = order[position];
            var label = LabelFor(id);

            var button = new Button
            {
                Content = new FontIcon
                {
                    FontFamily = new FontFamily("Segoe Fluent Icons"),
                    Glyph = GlyphFor(id),
                    FontSize = OverlayQamResources.Get("QamRailIconSize", 24.0),
                    Width = OverlayQamResources.Get("QamRailIconSize", 24.0),
                    Height = OverlayQamResources.Get("QamRailIconSize", 24.0),
                },
                Tag = id,
                Style = OverlayQamResources.Style("QamRailButtonStyle"),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Background = OverlayQamResources.Brush("QamSectionBrush"),
                Foreground = OverlayQamResources.Brush("QamRailIconBrush"),
            };
            AutomationProperties.SetName(button, label);
            ToolTipService.SetToolTip(button, label);
            button.Click += OnTabHeaderClick;

            var tabHost = new Grid { Height = OverlayQamResources.Get("QamRailItemHeight", 64.0) };
            Grid.SetRow(button, 0);
            tabHost.Children.Add(button);

            TabStrip.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Grid.SetRow(tabHost, position);
            TabStrip.Children.Add(tabHost);
            _tabButtons[id] = button;
            _tabHosts[id] = tabHost;

            var rows = new List<OverlayRow>();
            var page = BuildPage(id, rows);
            page.Visibility = Visibility.Collapsed;
            TabBody.Children.Add(page);
            _tabPages[id] = page;
            _pageRows[id] = rows;
        }

        ApplySelectedTabVisualState();
    }

    private static string GlyphFor(AddonQuickSettingsTabId id) => id switch
    {
        AddonQuickSettingsTabId.Device => "\uE945",
        AddonQuickSettingsTabId.Profile => "\uE71D",
        AddonQuickSettingsTabId.Controller => "\uE7FC",
        AddonQuickSettingsTabId.Shortcut => "\uE75F",
        AddonQuickSettingsTabId.Setting => "\uE713",
        _ => throw new ArgumentOutOfRangeException(nameof(id), id, "Unknown Overlay tab identity."),
    };

    // Device/Profile use shared Quick Settings, Controller projects the Runtime-owned M1/M2 mapping,
    // Setting owns tab-order editing, and Shortcut renders the Runtime-owned dynamic dashboard.
    private FrameworkElement BuildPage(AddonQuickSettingsTabId id, List<OverlayRow> rows)
    {
        var content = id switch
        {
            AddonQuickSettingsTabId.Setting => BuildSettingPage(rows),
            AddonQuickSettingsTabId.Shortcut => BuildShortcutPage(),
            AddonQuickSettingsTabId.Controller => BuildControllerPage(rows),
            AddonQuickSettingsTabId.Device => BuildQuickSettingsPage(id, QuickSettingsPageId.Device),
            AddonQuickSettingsTabId.Profile => BuildProfilePage(),
            _ => new Grid(),
        };

        return content;
    }

    private StackPanel BuildTabOrderEditorPage()
    {
        var section = new StackPanel { Spacing = OverlayQamResources.Get("QamSectionHeaderSpacing", 4.0) };
        var grid = new Grid { RowSpacing = OverlayQamResources.Get("QamRowSpacing", 0.0) };
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

    private static string LabelFor(AddonQuickSettingsTabId id) => AddonQuickSettingsShellContract.LabelFor(id);

    private void OnTabHeaderClick(object sender, RoutedEventArgs args)
    {
        if (sender is Button { Tag: AddonQuickSettingsTabId id } && id != _tabState.SelectedTab)
        {
            _tabState.Select(id);
            ApplySelectedTabVisualState();
        }
    }

    // Reset selection to the first tab in the current order before every visual reveal.
    private void ResetUiForShow(bool preferActiveProfile = false)
    {
        PrepareActiveProfileFirstShow();
        _tabState.ResetForShow();
        if (preferActiveProfile) _tabState.Select(AddonQuickSettingsTabId.Profile);
        ResetShortcutForShow();
        ResetSettingCardsForShow();
        ApplySelectedTabVisualState();
    }

    // OQ5-UI-02: LB/RB semantic tab navigation from the Runtime capture path. Keeps all visual
    // dictionary/page-visibility logic here; App marshals the semantic action and never touches tab state.
    internal void SelectPreviousTab()
    {
        if (_tabState.SelectPrevious()) ApplySelectedTabVisualState();
    }

    internal void SelectNextTab()
    {
        if (_tabState.SelectNext()) ApplySelectedTabVisualState();
    }

    // OQ5-UI-09: apply an authoritative tab order from the Runtime without disturbing the visible
    // session. The five page/button/row instances are preserved; only the tab-strip row order and
    // the selected-header accent change. Selected page/row/scroll position stay exactly as they are
    // (s.11.1) -- the new first tab only takes effect on the next Show via ResetForShow().
    internal void ApplyTabOrderState(AddonQuickSettingsTabOrderSnapshot state)
    {
        // Capture the Setting-editor row identity selected right now (before the order changes) so a
        // live reorder preserves the selected tab rather than the old numeric row slot.
        var settingVisible = _tabState.SelectedTab == AddonQuickSettingsTabId.Setting;
        var selectedSettingIndex = settingVisible ? _rowSelection.SelectedIndex : null;
        var previousOrder = _tabState.Order;
        var previousSettingRows = _pageRows.TryGetValue(AddonQuickSettingsTabId.Setting, out var currentSettingRows) ? currentSettingRows : [];
        var tabOrderEditorStartIndex = previousOrder.Count > 0 && _tabOrderRows.TryGetValue(previousOrder[0], out var firstPreviousEditor)
            ? FindSettingRowIndex(previousSettingRows, firstPreviousEditor.Container)
            : null;

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
                Grid.SetRow(tabHost, position);
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
        {
            _pageRows[AddonQuickSettingsTabId.Setting] = BuildSettingRows(applied);
        }

        ApplySelectedHeaderVisual();

        // s.8.2/8.4: if the Setting editor is what the user is looking at, re-point selection at the
        // same identity and refresh just the row highlight -- do NOT run the full tab-change path
        // (which would reset body scroll and selection).
        if (settingVisible)
        {
            var preferredIndex = ResolvePreferredSettingRowIndex(
                selectedSettingIndex,
                tabOrderEditorStartIndex,
                previousOrder,
                applied);
            _rowSelection.SetRows(CapabilitiesFor(AddonQuickSettingsTabId.Setting), preferredIndex);
            ApplyRowSelectionVisual();
        }

        OverlayLog.Info("Shell", "Authoritative Overlay tab order applied.", ("SelectedTab", _tabState.SelectedTab));
    }

    // Keep the fixed Setting rows anchored by their local index and remap only tab-order rows by
    // identity. The caller finds the actual first editor row from its retained row container, so
    // adding a card cannot silently shift an arithmetic header offset.
    internal static int? ResolvePreferredSettingRowIndex(
        int? selectedSettingIndex,
        int? editorRowStart,
        IReadOnlyList<AddonQuickSettingsTabId> previousOrder,
        IReadOnlyList<AddonQuickSettingsTabId> appliedOrder)
    {
        if (editorRowStart is not { } start)
            return selectedSettingIndex;
        AddonQuickSettingsTabId? selectedEditorTab = null;
        if (selectedSettingIndex is { } selected &&
            selected >= start &&
            selected - start < previousOrder.Count)
        {
            selectedEditorTab = previousOrder[selected - start];
        }

        int? preferredIndex = selectedSettingIndex is { } index && index >= 0 && index < start
            ? index
            : null;

        if (selectedEditorTab is { } tab)
        {
            for (var i = 0; i < appliedOrder.Count; i++)
            {
                if (appliedOrder[i] == tab)
                {
                    preferredIndex = start + i;
                    break;
                }
            }
        }

        return preferredIndex;
    }

    private void ApplySelectedHeaderVisual()
    {
        var selected = _tabState.SelectedTab;
        foreach (var (id, button) in _tabButtons)
        {
            var isSelected = id == selected;
            button.Background = isSelected
                ? OverlayQamResources.Brush("QamRailSelectedFillBrush")
                : OverlayQamResources.Brush("QamSectionBrush");
            button.Foreground = isSelected
                ? OverlayQamResources.Brush("QamRailIconSelectedBrush")
                : OverlayQamResources.Brush("QamRailIconBrush");
        }
    }

    // s.12: deterministic tab-change ordering -- tab visuals, then show the page and reset the
    // shared scroll to top, then reset that page's row selection to its first selectable row,
    // then apply the row-selection visual.
    private void ApplySelectedTabVisualState()
    {
        var selected = _tabState.SelectedTab;
        PageTitle.Text = LabelFor(selected);
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

        // Shortcut has its own bounded two-dimensional selection model; the shared row-selection
        // model remains empty for this page.
        if (selected == AddonQuickSettingsTabId.Shortcut)
        {
            ResetShortcutSelection();
            ApplyShortcutSelectionVisual();
        }

        OnProfileTabSelectionChanged(selected == AddonQuickSettingsTabId.Profile);
    }
}
