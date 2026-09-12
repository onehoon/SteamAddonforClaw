using System.Diagnostics;
using System.Linq;
using System.Numerics;
using Microsoft.UI;
using Microsoft.UI.Composition;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using Windows.UI.ViewManagement;
using SteamInputAddonforClaw.Contracts.Frontend;
using SteamInputAddonforClaw.Contracts.Overlay;
using SteamInputAddonforClaw.Overlay.Diagnostics;

namespace SteamInputAddonforClaw.Overlay;

public sealed partial class OverlayWindow : Window
{
    // SF-V2-07 section 13: the shared Quick Settings identity carried alongside a rendered Device
    // row's WinUI container/capabilities pair, so the selected row can be re-found by RowId across
    // an ordinary authoritative page rebuild instead of by numeric index.
    private sealed record OverlayRow(Border Container, OverlayRowCapabilities Capabilities, QuickSettingsRowId? QuickSettingsRowId = null);

    // SF-V2-07 section 44: the flattened (RowId, ControlKind, SliderKind, WellFormed) shape of the
    // last-rendered Device page. Equal shape on a new page means only row VALUES changed (the
    // common case while a slider is being edited/settled) -- update the existing WinUI controls in
    // place. A different shape means a row appeared/disappeared/changed kind -- rebuild.
    private readonly record struct DeviceRowShape(QuickSettingsRowId RowId, QuickSettingsControlKind ControlKind, QuickSettingsSliderKind? SliderKind, bool WellFormed);

    private const double ContentSlideDistanceDip = 32.0;
    private const double HiddenOpacity = 0.90;
    private static readonly TimeSpan ShowDuration = TimeSpan.FromMilliseconds(180);
    private static readonly TimeSpan HideDuration = TimeSpan.FromMilliseconds(150);
    private uint _lastConfiguredDpi;

    private readonly OverlayTabState _tabState = new();
    private readonly Dictionary<OverlayTabId, Button> _tabButtons = new();
    private readonly Dictionary<OverlayTabId, FrameworkElement> _tabPages = new();
    private readonly Dictionary<OverlayTabId, IReadOnlyList<OverlayRow>> _pageRows = new();
    private readonly Dictionary<OverlayTabId, OverlayTabOrderRow> _tabOrderRows = new();
    private readonly OverlayRowSelection _rowSelection = new();
    private readonly Brush _rowSelectedBrush;
    private static readonly Brush RowUnselectedBrush = new SolidColorBrush(Colors.Transparent);

    // OQ5 UI Polish A s.4.8: ordinary list rows use a subtle selected-row fill instead of the
    // former 2-DIP accent border outline. The Shortcut 2x2 tiles are an explicit exception (s.4.8
    // "Shortcut exception") and keep their existing border-based selection treatment.
    private readonly Brush _rowSelectedFillBrush;
    private static readonly Brush RowUnselectedFillBrush = new SolidColorBrush(Colors.Transparent);

    // OQ5 UI Polish A s.4.4: compact accent-filled selected tab / low-chrome inactive tab.
    private readonly Brush _tabSelectedBackgroundBrush;
    private readonly Brush _tabSelectedForegroundBrush;

    // OQ5-UI-11: the fixed 2x2 Shortcut grid -- one pure selection model + the four tile borders.
    private readonly OverlayShortcutSelection _shortcutSelection = new();
    private readonly Dictionary<OverlayShortcutSlotId, Border> _shortcutTiles = new();

    // SF-V2-07: the Device page's generic Quick Settings renderer state. _deviceBinding is
    // configured once App has a transport client (ConfigureQuickSettings); the row-control
    // dictionaries and _deviceRowShape let ordinary value-only refreshes update existing WinUI
    // controls in place instead of tearing down/rebuilding the page on every edit/settlement.
    private OverlayQuickSettingsPageBinding? _deviceBinding;
    private StackPanel? _devicePageContent;
    private TextBlock? _deviceFailureText;
    private readonly Dictionary<QuickSettingsRowId, OverlayToggleRow> _deviceToggleRows = new();
    private readonly Dictionary<QuickSettingsRowId, OverlaySliderRow> _deviceSliderRows = new();
    private DeviceRowShape[]? _deviceRowShape;

    internal event Action<OverlayOutsideClick>? OutsideClickDismissRequested;

    // OQ5-UI-10: the Setting-page editor proposes a one-position tab move; App forwards it through the
    // existing OQ5-UI-09 SendSetTabOrderAsync seam. OverlayWindow never owns the transport client.
    internal event Action<IReadOnlyList<OverlayTabId>>? TabOrderChangeRequested;

    public OverlayWindow()
    {
        InitializeComponent();
        _rowSelectedBrush =
            Application.Current.Resources.TryGetValue("AccentFillColorDefaultBrush", out var accent) && accent is Brush brush
                ? brush
                : new SolidColorBrush(Colors.SlateGray);
        _rowSelectedFillBrush =
            Application.Current.Resources.TryGetValue("SubtleFillColorSecondaryBrush", out var subtleFill) && subtleFill is Brush subtleBrush
                ? subtleBrush
                : new SolidColorBrush(Colors.Gray) { Opacity = 0.25 };
        _tabSelectedBackgroundBrush = _rowSelectedBrush;
        _tabSelectedForegroundBrush =
            Application.Current.Resources.TryGetValue("TextOnAccentFillColorPrimaryBrush", out var onAccent) && onAccent is Brush onAccentBrush
                ? onAccentBrush
                : new SolidColorBrush(Colors.White);
        BuildShell();
        Closed += (_, _) => _deviceBinding?.Dispose();
    }

    // SF-V2-07 section 10.2/44: App owns the NamedPipeOverlayClient; this is the narrow mutation
    // delegate the Window/binder receives instead. Called once, right after App creates the
    // client and before the transport loop starts reading Device pages.
    internal void ConfigureQuickSettings(Func<QuickSettingsMutationIntent, Task<QuickSettingsMutationResult>> mutate)
    {
        _deviceBinding?.Dispose();
        var binding = new OverlayQuickSettingsPageBinding(
            QuickSettingsPageId.Device, mutate, action => DispatcherQueue.TryEnqueue(() => action()));
        binding.SettledAsynchronously += RenderDevicePage;
        _deviceBinding = binding;
        RenderDevicePage();
    }

    // SF-V2-07 section 10.1: called (already marshalled to the UI thread by App) whenever the
    // Runtime republishes the Device Quick Settings page.
    internal void ApplyQuickSettingsPage(QuickSettingsPageSnapshot page)
    {
        if (page.PageId != QuickSettingsPageId.Device)
        {
            // Section 11: Profile publication is not rendered until SF-V2-09.
            OverlayLog.Warn("Device", "Ignoring a non-Device Quick Settings page.", exception: null, ("PageId", page.PageId));
            return;
        }
        _deviceBinding?.ApplyAuthoritativePage(page);
        RenderDevicePage();
    }

    internal nint HandleForDiagnostics => WindowInterop.GetWindowHandle(this);

    internal void PrepareHidden() => ConfigureWindow();

    internal async Task ShowForPocAsync()
    {
        // Commit the startup tab before any visual work so a warm process that was previously
        // showing another tab never flashes it for a frame during the reveal (OQ5-UI-01 s.6).
        ResetUiForShow();
        ConfigureWindow();
        var initialStatePrepared = true;
        try
        {
            SetVisualState(-ContentSlideDistanceDip, HiddenOpacity);
        }
        catch (Exception exception)
        {
            initialStatePrepared = false;
            OverlayLog.Error("Animation", "Show animation initial state failed; keeping Overlay visible.", exception);
        }
        WindowInterop.ShowWithoutActivation(this);
        WindowInterop.ArmOutsideClickDismissal(this, outsideClick => OutsideClickDismissRequested?.Invoke(outsideClick));
        if (!initialStatePrepared || !AnimationsEnabled())
        {
            TrySetVisibleVisualState();
            LogSurfaceBounds("Show.Visible");
            return;
        }

        var stopwatch = Stopwatch.StartNew();
        OverlayLog.Info("Animation", "Show animation started",
            ("DurationMs", ShowDuration.TotalMilliseconds),
            ("StartOpacity", HiddenOpacity), ("EndOpacity", 1.0),
            ("ContentSlideDistanceDip", ContentSlideDistanceDip));
        try
        {
            await AnimateAsync(-ContentSlideDistanceDip, 0, HiddenOpacity, 1.0, ShowDuration, easeIn: false);
            TrySetVisibleVisualState();
            LogSurfaceBounds("Show.Visible");
            OverlayLog.Info("Animation", "Show animation completed", ("ElapsedMs", stopwatch.Elapsed.TotalMilliseconds));
        }
        catch (Exception exception)
        {
            OverlayLog.Error("Animation", "Show animation failed; keeping Overlay visible.", exception);
            TrySetVisibleVisualState();
            LogSurfaceBounds("Show.Visible.AnimationFallback");
        }
    }

    internal async Task HideForPocAsync()
    {
        // SF-V2-07 section 39: hide never waits for the trailing debounce window -- drop any
        // unsubmitted Device draft so a hidden Overlay cannot fire an obsolete mutation later.
        // Already-submitted work settles on its own and stays subject to the generation check.
        _deviceBinding?.CancelUnsubmittedDrafts();
        WindowInterop.DisarmOutsideClickDismissal();
        if (AnimationsEnabled())
        {
            var stopwatch = Stopwatch.StartNew();
            OverlayLog.Info("Animation", "Hide animation started",
                ("DurationMs", HideDuration.TotalMilliseconds),
                ("StartOpacity", 1.0), ("EndOpacity", HiddenOpacity),
                ("ContentSlideDistanceDip", ContentSlideDistanceDip));
            try
            {
                await AnimateAsync(0, -ContentSlideDistanceDip, 1.0, HiddenOpacity, HideDuration, easeIn: true);
                OverlayLog.Info("Animation", "Hide animation completed", ("ElapsedMs", stopwatch.Elapsed.TotalMilliseconds));
            }
            catch (Exception exception)
            {
                OverlayLog.Error("Animation", "Hide animation failed; hiding Overlay immediately.", exception);
            }
        }

        try
        {
            WindowInterop.Hide(this);
        }
        finally
        {
            try
            {
                SetHiddenVisualState();
            }
            catch (Exception exception)
            {
                OverlayLog.Error("Animation", "Could not reset hidden visual state.", exception);
            }
        }
    }

    // OQ5-UI-01: five-tab shell. Tab buttons and placeholder pages are built once from the
    // current tab order; identity (OverlayTabId) is carried on Button.Tag and kept separate
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
                Padding = new Thickness(6, 6, 6, 6),
                MinWidth = 0,
                MinHeight = 34,
                CornerRadius = new CornerRadius(8),
                FontSize = 13,
            };
            button.Click += OnTabHeaderClick;
            Grid.SetColumn(button, column);
            TabStrip.Children.Add(button);
            _tabButtons[id] = button;

            var rows = new List<OverlayRow>();
            var page = BuildPage(id, rows);
            page.Visibility = Visibility.Collapsed;
            TabBody.Children.Add(page);
            _tabPages[id] = page;
            _pageRows[id] = rows;
        }

        ApplySelectedTabVisualState();
    }

    // Device gets the SF-V2-07 generic Quick Settings renderer; Setting gets the OQ5-UI-10
    // tab-order editor; Shortcut gets the OQ5-UI-11 2x2 slot shell; every other tab keeps its
    // OQ5-UI-01 placeholder with zero selectable rows.
    private FrameworkElement BuildPage(OverlayTabId id, List<OverlayRow> rows)
    {
        if (id == OverlayTabId.Setting)
            return BuildTabOrderEditorPage(rows);
        if (id == OverlayTabId.Shortcut)
            return BuildShortcutPage();
        if (id != OverlayTabId.Device)
            return CreatePlaceholderPage(id);

        // Root holds the SF-V2-30/32 local failure banner above the actual row content;
        // RenderDevicePage() populates/updates both once ConfigureQuickSettings/ApplyQuickSettingsPage
        // runs. `rows` (== _pageRows[Device]) starts empty and is replaced wholesale on first render.
        var root = new StackPanel { Spacing = 4 };
        _deviceFailureText = CreateDeviceMessageText(string.Empty, "CaptionTextBlockStyle");
        _deviceFailureText.Visibility = Visibility.Collapsed;
        root.Children.Add(_deviceFailureText);
        _devicePageContent = new StackPanel { Spacing = 4 };
        root.Children.Add(_devicePageContent);
        return root;
    }

    // SF-V2-07 section 12/28: render the binder's current effective page (authoritative rows with
    // any pending draft overlaid). A same-shape page (section 44) only needs its rows' values
    // refreshed in place -- important so editing/settling one slider never disrupts an in-progress
    // drag on another WinUI Slider by tearing down and recreating the control tree.
    private void RenderDevicePage()
    {
        if (_devicePageContent is null || _deviceBinding is null) return;
        var page = _deviceBinding.BuildEffectivePage();

        if (page.Available)
        {
            var shape = page.Sections.SelectMany(s => s.Rows).Select(DeviceRowShapeOf).ToArray();
            if (_deviceRowShape is not null && _deviceRowShape.SequenceEqual(shape))
            {
                UpdateDeviceRowValues(page);
                ApplyDeviceLocalFailure();
                return;
            }
        }

        RebuildDeviceContent(page);
        ApplyDeviceLocalFailure();
    }

    // Sections 30/32: a typed failure's FailureMessage or an operation/transport failure's narrow
    // local message must be visible -- silently snapping back to authoritative state with no
    // indication is not acceptable. Page-local only: no notification framework, cleared the moment
    // the binder's own failure fact clears (a later success settlement/refresh).
    private void ApplyDeviceLocalFailure()
    {
        if (_deviceFailureText is null || _deviceBinding is null) return;
        var message = _deviceBinding.LastLocalFailureMessage;
        _deviceFailureText.Text = message ?? string.Empty;
        _deviceFailureText.Visibility = string.IsNullOrWhiteSpace(message) ? Visibility.Collapsed : Visibility.Visible;
    }

    private static DeviceRowShape DeviceRowShapeOf(QuickSettingsRow row) => new(
        row.RowId,
        row.ControlKind,
        row.ControlKind == QuickSettingsControlKind.Slider ? row.SliderSpec?.Kind : null,
        QuickSettingsRowRendering.IsWellFormed(row));

    // Fast path: the rendered row set/kinds are unchanged from the last render -- push new values
    // into the existing controls without touching the WinUI tree, selection, or scroll.
    private void UpdateDeviceRowValues(QuickSettingsPageSnapshot page)
    {
        foreach (var row in page.Sections.SelectMany(s => s.Rows))
        {
            if (_deviceToggleRows.TryGetValue(row.RowId, out var toggle))
                ApplyDeviceToggleState(toggle, row);
            else if (_deviceSliderRows.TryGetValue(row.RowId, out var slider))
                ApplyDeviceSliderState(slider, row);
        }
    }

    private static void ApplyDeviceToggleState(OverlayToggleRow toggle, QuickSettingsRow row)
    {
        var isOn = row.Value is { Kind: QuickSettingsValueKind.Boolean, BooleanValue: true };
        var isAvailable = row.Available && row.Writable && row.Value is { Kind: QuickSettingsValueKind.Boolean };
        toggle.ApplyState(isAvailable, isOn);
    }

    private static void ApplyDeviceSliderState(OverlaySliderRow slider, QuickSettingsRow row)
    {
        if (row.SliderSpec is not { } spec) return;
        if (spec.Kind == QuickSettingsSliderKind.Numeric)
        {
            var hasValue = row.Value is { Kind: QuickSettingsValueKind.Integer, IntegerValue: not null };
            var value = hasValue ? row.Value!.IntegerValue!.Value : spec.Minimum;
            var available = row.Available && row.Writable && hasValue;
            slider.ApplyState(available, spec.Minimum, spec.Maximum, spec.Step, value);
        }
        else
        {
            var options = spec.Options ?? [];
            var index = row.Value is { Kind: QuickSettingsValueKind.Integer, IntegerValue: { } iv }
                ? QuickSettingsRowRendering.FindDiscreteIndex(options, iv)
                : -1;
            var available = row.Available && row.Writable && index >= 0;
            slider.ApplyState(available, 0, Math.Max(0, options.Count - 1), 1, Math.Max(0, index));
        }
    }

    // Section 34/35/36/44: structural rebuild -- row set/kind changed, or the whole page became
    // (un)available. Preserves the selected Device RowId (only while Device is the visible tab) and
    // never resets body scroll; that stays reserved for an actual tab change.
    private void RebuildDeviceContent(QuickSettingsPageSnapshot page)
    {
        QuickSettingsRowId? preferredRowId = null;
        if (_tabState.SelectedTab == OverlayTabId.Device &&
            _pageRows.TryGetValue(OverlayTabId.Device, out var oldRows) &&
            _rowSelection.SelectedIndex is { } selectedIndex && selectedIndex >= 0 && selectedIndex < oldRows.Count)
        {
            preferredRowId = oldRows[selectedIndex].QuickSettingsRowId;
        }

        _deviceToggleRows.Clear();
        _deviceSliderRows.Clear();
        _devicePageContent!.Children.Clear();
        var rows = new List<OverlayRow>();

        if (!page.Available)
        {
            _devicePageContent.Children.Add(CreateDeviceMessageText(page.Message ?? "Quick Settings are unavailable.", "BodyTextBlockStyle"));
            _deviceRowShape = [];
        }
        else
        {
            foreach (var section in page.Sections)
            {
                if (!string.IsNullOrEmpty(section.Label))
                    _devicePageContent.Children.Add(CreateDeviceMessageText(section.Label, "BodyStrongTextBlockStyle"));
                if (!string.IsNullOrEmpty(section.Message))
                    _devicePageContent.Children.Add(CreateDeviceMessageText(section.Message, "CaptionTextBlockStyle"));

                foreach (var row in section.Rows)
                {
                    if (!TryCreateDeviceRow(row, out var overlayRow)) continue;
                    rows.Add(overlayRow);
                    _devicePageContent.Children.Add(overlayRow.Container);
                }
            }

            _deviceRowShape = page.Sections.SelectMany(s => s.Rows).Select(DeviceRowShapeOf).ToArray();
        }

        _pageRows[OverlayTabId.Device] = rows;

        if (_tabState.SelectedTab == OverlayTabId.Device)
        {
            int? preferredIndex = null;
            if (preferredRowId is { } rid)
                for (var i = 0; i < rows.Count; i++)
                    if (rows[i].QuickSettingsRowId == rid) { preferredIndex = i; break; }

            _rowSelection.SetRows(CapabilitiesFor(OverlayTabId.Device), preferredIndex);
            ApplyRowSelectionVisual();
            BringSelectedRowIntoView();
        }
    }

    private static TextBlock CreateDeviceMessageText(string text, string styleKey)
    {
        var block = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap };
        if (Application.Current.Resources.TryGetValue(styleKey, out var style) && style is Style textStyle)
            block.Style = textStyle;
        return block;
    }

    // Section 48: a malformed/unsupported row (non-Boolean Toggle value, Slider without a
    // SliderSpec, invalid numeric range/step, empty discrete options, or an unsupported
    // ControlKind) is skipped entirely -- it is never registered for selection and can never emit
    // a mutation.
    private bool TryCreateDeviceRow(QuickSettingsRow row, out OverlayRow overlayRow)
    {
        if (!QuickSettingsRowRendering.IsWellFormed(row))
        {
            OverlayLog.Warn("Device", "Skipped a malformed/unsupported Quick Settings row.", exception: null, ("RowId", row.RowId), ("ControlKind", row.ControlKind));
            overlayRow = default!;
            return false;
        }

        overlayRow = row.ControlKind == QuickSettingsControlKind.Toggle
            ? CreateDeviceToggleRow(row)
            : CreateDeviceSliderRow(row);
        return true;
    }

    private OverlayRow CreateDeviceToggleRow(QuickSettingsRow row)
    {
        var rowId = row.RowId;
        var toggleRow = new OverlayToggleRow(row.Label, desired => _ = SubmitDeviceToggleAsync(rowId, desired));
        ApplyDeviceToggleState(toggleRow, row);
        _deviceToggleRows[rowId] = toggleRow;
        return new OverlayRow(toggleRow.Container, toggleRow.Capabilities, rowId);
    }

    private async Task SubmitDeviceToggleAsync(QuickSettingsRowId rowId, bool desired)
    {
        if (_deviceBinding is null) return;
        await _deviceBinding.SubmitImmediateToggleAsync(rowId, desired);
        RenderDevicePage();
    }

    private OverlayRow CreateDeviceSliderRow(QuickSettingsRow row)
    {
        var rowId = row.RowId;
        var spec = row.SliderSpec!;
        OverlaySliderRow sliderRow;
        if (spec.Kind == QuickSettingsSliderKind.Numeric)
        {
            var suffix = spec.Suffix ?? string.Empty;
            sliderRow = new OverlaySliderRow(row.Label,
                value => OverlaySliderRow.FormatInteger(value) + suffix,
                desired => ScheduleDeviceSlider(rowId, QuickSettingsValue.Integer((int)Math.Round(desired))));
        }
        else
        {
            var options = spec.Options!;
            sliderRow = new OverlaySliderRow(row.Label,
                index => FormatDiscreteLabel(options, index),
                desired =>
                {
                    var i = (int)Math.Round(desired);
                    if (i < 0 || i >= options.Count) return;
                    ScheduleDeviceSlider(rowId, QuickSettingsValue.Integer(options[i].Value));
                });
        }

        ApplyDeviceSliderState(sliderRow, row);
        _deviceSliderRows[rowId] = sliderRow;
        return new OverlayRow(sliderRow.Container, sliderRow.Capabilities, rowId);
    }

    private void ScheduleDeviceSlider(QuickSettingsRowId rowId, QuickSettingsValue desired)
    {
        if (_deviceBinding is null) return;
        _deviceBinding.ScheduleSlider(rowId, desired);
        RenderDevicePage();
    }

    private static string FormatDiscreteLabel(IReadOnlyList<QuickSettingsDiscreteOption> options, double index)
    {
        var i = (int)Math.Round(index);
        return i >= 0 && i < options.Count ? options[i].Label : "--";
    }

    // OQ5-UI-10: the five fixed tab-order rows live in one 5-row Grid, created once and kept by
    // OverlayTabId. ApplyTabOrder repositions them via Grid.SetRow -- instances are never recreated.
    private FrameworkElement BuildTabOrderEditorPage(List<OverlayRow> rows)
    {
        var section = new StackPanel { Spacing = 8 };

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
            var row = new OverlayTabOrderRow(id, LabelFor(id), delta => RequestTabOrderMove(id, delta));
            row.SetPosition(i, order.Count);
            Grid.SetRow(row.Container, i);
            grid.Children.Add(row.Container);
            _tabOrderRows[id] = row;
            rows.Add(new OverlayRow(row.Container, row.Capabilities));
        }

        section.Children.Add(grid);
        return section;
    }

    // OQ5-UI-10: a proposal is only a request. The visible order changes only when the Runtime
    // republishes TabOrderState -> ApplyTabOrder. A boundary move produces no proposal and no request.
    private void RequestTabOrderMove(OverlayTabId tab, int delta)
    {
        if (!_tabState.TryCreateMovedOrder(tab, delta, out var proposed))
            return;
        OverlayLog.Info("TabOrder", "Tab order move requested.", ("Tab", tab), ("Delta", delta < 0 ? -1 : 1));
        TabOrderChangeRequested?.Invoke(proposed);
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

        var slots = new (OverlayShortcutSlotId Id, string Label, int Row, int Column)[]
        {
            (OverlayShortcutSlotId.Slot1, "Slot 1", 0, 0),
            (OverlayShortcutSlotId.Slot2, "Slot 2", 0, 1),
            (OverlayShortcutSlotId.Slot3, "Slot 3", 1, 0),
            (OverlayShortcutSlotId.Slot4, "Slot 4", 1, 1),
        };

        foreach (var (id, label, row, column) in slots)
        {
            var title = new TextBlock { Text = label };
            if (Application.Current.Resources.TryGetValue("BodyStrongTextBlockStyle", out var titleStyle) && titleStyle is Style ts)
                title.Style = ts;

            var state = new TextBlock { Text = "Unassigned", Opacity = 0.6 };
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
            tile.Tapped += (_, _) => SelectShortcutSlot(id, "Pointer");
            Grid.SetRow(tile, row);
            Grid.SetColumn(tile, column);
            grid.Children.Add(tile);
            _shortcutTiles[id] = tile;
        }

        ApplyShortcutSelectionVisual();
        return grid;
    }

    private void SelectShortcutSlot(OverlayShortcutSlotId slot, string source)
    {
        if (!_shortcutSelection.Select(slot)) return;
        OverlayLog.Debug("Shortcut", "Shortcut tile selected.", ("Slot", slot), ("Source", source));
        ApplyShortcutSelectionVisual();
    }

    private void ApplyShortcutSelectionVisual()
    {
        foreach (var (id, tile) in _shortcutTiles)
            tile.BorderBrush = id == _shortcutSelection.SelectedSlot ? _rowSelectedBrush : RowUnselectedBrush;
    }

    private static string LabelFor(OverlayTabId id) => id switch
    {
        OverlayTabId.Device => "Device",
        OverlayTabId.Profile => "Profile",
        OverlayTabId.Controller => "Controller",
        OverlayTabId.Shortcut => "Shortcut",
        OverlayTabId.Setting => "Setting",
        _ => id.ToString(),
    };

    private static FrameworkElement CreatePlaceholderPage(OverlayTabId id)
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
        if (sender is Button { Tag: OverlayTabId id } && id != _tabState.SelectedTab)
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

    // OQ5-UI-04 s.11: NavigateUp/Down move logical row selection; Left/Right and Accept dispatch to
    // the selected row only when it registered that capability. All row/selection state stays private
    // to OverlayWindow -- App only forwards the semantic action.
    // OQ5-UI-11 s.7.5: the Shortcut page is the one 2D exception -- the same semantic actions drive
    // the fixed 2x2 grid instead of the linear row model while that page is active.
    internal void NavigateUp()
    {
        if (OnShortcutPage()) { if (_shortcutSelection.MoveUp()) ApplyShortcutSelectionVisual(); return; }
        MoveRowSelection(up: true);
    }

    internal void NavigateDown()
    {
        if (OnShortcutPage()) { if (_shortcutSelection.MoveDown()) ApplyShortcutSelectionVisual(); return; }
        MoveRowSelection(up: false);
    }

    // If the selected row became unselectable, the selection method normalizes to another row and
    // reports it; on that same press we only refresh the highlight and skip the adjust/activate so
    // the fallback row is never mutated under a stale highlight.
    internal void AdjustSelectedRow(int delta)
    {
        if (OnShortcutPage())
        {
            if (delta < 0 ? _shortcutSelection.MoveLeft() : _shortcutSelection.MoveRight())
                ApplyShortcutSelectionVisual();
            return;
        }
        if (_rowSelection.AdjustSelected(delta)) RefreshRowSelectionAfterMove();
    }

    internal void ActivateSelectedRow()
    {
        if (OnShortcutPage())
        {
            // Every PR11 slot is Unassigned: A performs no product action.
            OverlayLog.Debug("Shortcut", "Accept on an unassigned Shortcut slot; no action.", ("Slot", _shortcutSelection.SelectedSlot));
            return;
        }
        if (_rowSelection.ActivateSelected()) RefreshRowSelectionAfterMove();
    }

    private bool OnShortcutPage() => _tabState.SelectedTab == OverlayTabId.Shortcut;

    private void MoveRowSelection(bool up)
    {
        if (up ? _rowSelection.MovePrevious() : _rowSelection.MoveNext())
            RefreshRowSelectionAfterMove();
    }

    private void RefreshRowSelectionAfterMove()
    {
        ApplyRowSelectionVisual();
        BringSelectedRowIntoView();
    }

    // OQ5-UI-09: apply an authoritative tab order from the Runtime without disturbing the visible
    // session. The five page/button/row instances are preserved; only the tab-strip column order and
    // the selected-header accent change. Selected page/row/scroll position stay exactly as they are
    // (s.11.1) -- the new first tab only takes effect on the next Show via ResetForShow().
    internal void ApplyTabOrder(IReadOnlyList<OverlayTabId> order)
    {
        // Capture the Setting-editor row identity selected right now (before the order changes) so a
        // live reorder preserves the selected tab rather than the old numeric row slot.
        OverlayTabId? selectedEditorTab = null;
        var settingVisible = _tabState.SelectedTab == OverlayTabId.Setting;
        if (settingVisible && _rowSelection.SelectedIndex is { } selected && selected >= 0 && selected < _tabState.Order.Count)
            selectedEditorTab = _tabState.Order[selected];

        if (!_tabState.TryApplyOrder(order))
        {
            OverlayLog.Warn("Shell", "Ignored an invalid authoritative Overlay tab order.");
            return;
        }

        var applied = _tabState.Order;
        for (var position = 0; position < applied.Count; position++)
        {
            if (_tabButtons.TryGetValue(applied[position], out var button))
                Grid.SetColumn(button, position);
            if (_tabOrderRows.TryGetValue(applied[position], out var editorRow))
            {
                Grid.SetRow(editorRow.Container, position);
                editorRow.SetPosition(position, applied.Count);
            }
        }

        // Rebuild the Setting page's ordered row list so CapabilitiesFor(Setting) / the selection
        // model see the authoritative order. The OverlayTabOrderRow instances are reused.
        if (_tabOrderRows.Count == applied.Count)
            _pageRows[OverlayTabId.Setting] = applied
                .Select(id => new OverlayRow(_tabOrderRows[id].Container, _tabOrderRows[id].Capabilities))
                .ToArray();

        ApplySelectedHeaderVisual();

        // s.8.2/8.4: if the Setting editor is what the user is looking at, re-point selection at the
        // same identity and refresh just the row highlight -- do NOT run the full tab-change path
        // (which would reset body scroll and selection).
        if (settingVisible)
        {
            int? preferredIndex = null;
            if (selectedEditorTab is { } tab)
                for (var i = 0; i < applied.Count; i++)
                    if (applied[i] == tab) { preferredIndex = i; break; }
            _rowSelection.SetRows(CapabilitiesFor(OverlayTabId.Setting), preferredIndex);
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
            if (isSelected)
            {
                button.Background = _tabSelectedBackgroundBrush;
                button.Foreground = _tabSelectedForegroundBrush;
            }
            else
            {
                // Restore the standard WinUI Button chrome/foreground for the low-chrome inactive
                // state instead of hardcoding a second product color.
                button.ClearValue(Control.BackgroundProperty);
                button.ClearValue(Control.ForegroundProperty);
            }
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
        if (selected == OverlayTabId.Shortcut)
        {
            _shortcutSelection.Reset();
            ApplyShortcutSelectionVisual();
        }
    }

    private IReadOnlyList<OverlayRowCapabilities> CapabilitiesFor(OverlayTabId tab) =>
        _pageRows.TryGetValue(tab, out var rows)
            ? rows.Select(row => row.Capabilities).ToArray()
            : [];

    private void ApplyRowSelectionVisual()
    {
        if (!_pageRows.TryGetValue(_tabState.SelectedTab, out var rows)) return;
        var selectedIndex = _rowSelection.SelectedIndex;
        for (var i = 0; i < rows.Count; i++)
            rows[i].Container.Background = i == selectedIndex ? _rowSelectedFillBrush : RowUnselectedFillBrush;
    }

    private void BringSelectedRowIntoView()
    {
        if (!_pageRows.TryGetValue(_tabState.SelectedTab, out var rows)) return;
        if (_rowSelection.SelectedIndex is not { } index || index < 0 || index >= rows.Count) return;
        try
        {
            rows[index].Container.StartBringIntoView(new BringIntoViewOptions { AnimationDesired = false });
        }
        catch (Exception exception)
        {
            OverlayLog.Warn("Shell", "Could not bring the selected row into view.", exception);
        }
    }

    private void ConfigureWindow()
    {
        WindowInterop.Configure(this, out var rect, out _lastConfiguredDpi, out var monitorText);
        var scale = _lastConfiguredDpi / 96.0;
        OverlayLog.Info("Geometry", "Overlay window configured",
            ("Monitor", monitorText),
            ("WorkAreaX", rect.X), ("WorkAreaY", rect.Y),
            ("WorkAreaWidth", rect.Width), ("WorkAreaHeight", rect.Height),
            ("Dpi", _lastConfiguredDpi), ("Scale", scale),
            ("PanelWidthDip", OverlayWindowGeometry.PocPanelWidthDip),
            ("PanelWidthPhysical", rect.Width));
    }

    private void LogSurfaceBounds(string reason)
    {
        try
        {
            if (!WindowInterop.TryGetDiagnosticBounds(this, out var native)) return;
            var xamlRoot = AnimationViewport.XamlRoot;
            if (xamlRoot is null)
            {
                OverlayLog.Warn("Geometry", "Overlay XAML bounds unavailable; continuing without the snapshot.",
                    null, ("Operation", "XamlRoot"), ("OverlayHwnd", HandleForDiagnostics));
                return;
            }

            var scale = xamlRoot.RasterizationScale;
            OverlayLog.Info("Geometry", "Overlay surface bounds snapshot",
                ("Reason", reason), ("OverlayHwnd", HandleForDiagnostics), ("Dpi", _lastConfiguredDpi),
                ("RasterizationScale", scale),
                ("WindowLeft", native.WindowRect.X), ("WindowTop", native.WindowRect.Y),
                ("WindowWidth", native.WindowRect.Width), ("WindowHeight", native.WindowRect.Height),
                ("ClientWidth", native.ClientWidth), ("ClientHeight", native.ClientHeight),
                ("ClientScreenX", native.ClientScreenX), ("ClientScreenY", native.ClientScreenY),
                ("ClientInsetLeft", native.ClientInsetLeft), ("ClientInsetTop", native.ClientInsetTop),
                ("ClientInsetRight", native.ClientInsetRight), ("ClientInsetBottom", native.ClientInsetBottom),
                ("AnimationViewportWidthDip", AnimationViewport.ActualWidth),
                ("AnimationViewportHeightDip", AnimationViewport.ActualHeight),
                ("AnimationViewportWidthPhysical", AnimationViewport.ActualWidth * scale),
                ("AnimationViewportHeightPhysical", AnimationViewport.ActualHeight * scale),
                ("OpaquePanelWidthDip", OpaquePanel.ActualWidth),
                ("OpaquePanelHeightDip", OpaquePanel.ActualHeight),
                ("OpaquePanelWidthPhysical", OpaquePanel.ActualWidth * scale),
                ("OpaquePanelHeightPhysical", OpaquePanel.ActualHeight * scale));
        }
        catch (Exception exception)
        {
            OverlayLog.Warn("Geometry", "Overlay surface bounds snapshot failed; continuing without diagnostics.", exception,
                ("Operation", "LogSurfaceBounds"), ("Reason", reason));
        }
    }

    private void SetVisibleVisualState() => SetVisualState(0, 1.0);

    private void SetHiddenVisualState() => SetVisualState(0, 1.0);

    private void TrySetVisibleVisualState()
    {
        try
        {
            SetVisibleVisualState();
        }
        catch (Exception exception)
        {
            OverlayLog.Error("Animation", "Could not commit the visible visual state.", exception);
        }
    }

    private void SetVisualState(double translationX, double opacity)
    {
        var visual = ElementCompositionPreview.GetElementVisual(AnimatedContent);
        visual.Offset = new Vector3((float)translationX, 0, 0);
        visual.Opacity = (float)opacity;
    }

    private async Task AnimateAsync(
        double startTranslationX,
        double endTranslationX,
        double startOpacity,
        double endOpacity,
        TimeSpan duration,
        bool easeIn)
    {
        var visual = ElementCompositionPreview.GetElementVisual(AnimatedContent);
        var compositor = visual.Compositor;
        var easing = compositor.CreateCubicBezierEasingFunction(
            easeIn ? new Vector2(0.42f, 0.0f) : new Vector2(0.0f, 0.0f),
            easeIn ? new Vector2(1.0f, 1.0f) : new Vector2(0.58f, 1.0f));
        var offset = compositor.CreateVector3KeyFrameAnimation();
        offset.Duration = duration;
        offset.InsertKeyFrame(0.0f, new Vector3((float)startTranslationX, 0, 0));
        offset.InsertKeyFrame(1.0f, new Vector3((float)endTranslationX, 0, 0), easing);
        var opacity = compositor.CreateScalarKeyFrameAnimation();
        opacity.Duration = duration;
        opacity.InsertKeyFrame(0.0f, (float)startOpacity);
        opacity.InsertKeyFrame(1.0f, (float)endOpacity, easing);

        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var batch = compositor.CreateScopedBatch(CompositionBatchTypes.Animation);
        batch.Completed += (_, _) => completion.TrySetResult();
        visual.StartAnimation(nameof(visual.Offset), offset);
        visual.StartAnimation(nameof(visual.Opacity), opacity);
        batch.End();
        await completion.Task;
        visual.StopAnimation(nameof(visual.Offset));
        visual.StopAnimation(nameof(visual.Opacity));
    }

    private static bool AnimationsEnabled()
    {
        try
        {
            return new UISettings().AnimationsEnabled;
        }
        catch (Exception exception)
        {
            OverlayLog.Warn("Animation", "Could not read the system animation preference; keeping animations enabled.", exception);
            return true;
        }
    }

}
