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
        AddonQuickSettingsTabId.Device => "\uE772",
        AddonQuickSettingsTabId.Profile => "\uE8A5",
        AddonQuickSettingsTabId.Controller => "\uE7FC",
        AddonQuickSettingsTabId.Shortcut => "\uE71B",
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
        var section = new StackPanel { Spacing = OverlayQamResources.Get("QamSectionSpacing", 0.0) };
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
    private void ResetUiForShow()
    {
        ResetShortcutQuickSettingsForShow();
        PrepareActiveProfileFirstShow();
        ResetDetailForTabChangeOrShow();
        _tabState.ResetForShow();
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

    internal void QuickJumpToConfiguredTab()
    {
        if (!_frontendSettingsAvailable || _frontendSettingsSnapshot is not { } settings)
            return;

        var target = ResolveQuickJumpTabId(settings.YButtonQuickJumpTarget);
        if (target is not { } selected || selected == _tabState.SelectedTab)
            return;

        _tabState.Select(selected);
        ApplySelectedTabVisualState();
    }

    internal static AddonQuickSettingsTabId? ResolveQuickJumpTabId(QuickOverlayYButtonTarget target) => target switch
    {
        QuickOverlayYButtonTarget.Profile => AddonQuickSettingsTabId.Profile,
        QuickOverlayYButtonTarget.Shortcut => AddonQuickSettingsTabId.Shortcut,
        QuickOverlayYButtonTarget.Device => AddonQuickSettingsTabId.Device,
        QuickOverlayYButtonTarget.Controller => AddonQuickSettingsTabId.Controller,
        QuickOverlayYButtonTarget.Setting => AddonQuickSettingsTabId.Setting,
        QuickOverlayYButtonTarget.Off => null,
        _ => null,
    };

    // OQ5-UI-09: apply an authoritative tab order from the Runtime without disturbing the visible
    // session. The five page/button/row instances are preserved; only the tab-strip row order and
    // the selected-header accent change. Selected page/row/scroll position stay exactly as they are
    // (s.11.1) -- the new first tab only takes effect on the next Show via ResetForShow().
    internal void ApplyTabOrderState(AddonQuickSettingsTabOrderSnapshot state)
    {
        var tabOrderDetailActive = _activeDetailPage is
            { Tab: AddonQuickSettingsTabId.Setting, Identity: SettingDetailId.TabOrder };
        var selectedSettingContainer = tabOrderDetailActive
            ? SelectedRowContainer(AddonQuickSettingsTabId.Setting)
            : null;
        var selectedSettingIndex = tabOrderDetailActive ? _rowSelection.SelectedIndex : null;

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

        // Only the visible Tab Order detail needs a new row projection. Its five native row controls
        // remain the same instances, and a selected Border is resolved in the authoritative order.
        if (tabOrderDetailActive && _tabOrderRows.Count == applied.Count)
        {
            _pageRows[AddonQuickSettingsTabId.Setting] = BuildTabOrderDetailRows(applied);
            ReconcileRowSelection(
                AddonQuickSettingsTabId.Setting,
                selectedSettingContainer,
                selectedSettingIndex,
                bringIntoViewOnChange: false);
        }

        ApplySelectedHeaderVisual();

        OverlayLog.Debug("Shell", "Authoritative Overlay tab order applied.", ("SelectedTab", _tabState.SelectedTab));
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
        if (_activeDetailPage is { } activeDetail && activeDetail.Tab != _tabState.SelectedTab)
            ResetDetailForTabChangeOrShow();

        _numericSliderRepeatTarget.Clear();
        var selected = _tabState.SelectedTab;
        if (_activeDetailPage is null)
        {
            DetailBackButton.Visibility = Visibility.Collapsed;
            PageTitle.Text = LabelFor(selected);
        }
        ApplySelectedHeaderVisual();
        foreach (var (id, page) in _tabPages)
            page.Visibility = id == selected ? Visibility.Visible : Visibility.Collapsed;

        if (selected == AddonQuickSettingsTabId.Shortcut)
            RefreshShortcutQuickSettingsView();
        else
        {
            _shortcutSettingsSelected = false;
            RestoreShortcutQuickSettingsHome();
        }

        try
        {
            BodyScroll.ChangeView(null, 0, null, disableAnimation: true);
        }
        catch (Exception exception)
        {
            OverlayLog.Warn("Shell", "Could not reset the body scroll position on tab change.", exception);
        }

        if (selected == AddonQuickSettingsTabId.Shortcut)
        {
            ResetShortcutSelection();
            _shortcutSettingsSelected = CapabilitiesFor(selected).Any(capability => capability.IsSelectable());
            _rowSelection.SetRows(_shortcutSettingsSelected ? CapabilitiesFor(selected) : []);
        }
        else
            _rowSelection.SetRows(CapabilitiesFor(selected));
        ApplyRowSelectionVisual();

        // The Shortcut tab has two adjacent areas, each retaining its existing local selection model.
        if (selected == AddonQuickSettingsTabId.Shortcut)
        {
            ApplyShortcutSelectionVisual();
        }

        OnProfileTabSelectionChanged(selected == AddonQuickSettingsTabId.Profile);
    }
}
