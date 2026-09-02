using System.Diagnostics;
using System.Numerics;
using Microsoft.UI;
using Microsoft.UI.Composition;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using Windows.UI.ViewManagement;
using SteamInputAddonforClaw.Overlay.Diagnostics;

namespace SteamInputAddonforClaw.Overlay;

public sealed partial class OverlayWindow : Window
{
    // OQ5-UI-04: temporary, non-feature navigation-preview rows so the row-selection / scroll
    // model is hardware-testable before real Device controls exist. Replaced by OQ5-UI-05/06.
    private const int NavigationPreviewRowCount = 12;

    private sealed record OverlayRow(Border Container, OverlayRowCapabilities Capabilities);

    private const double ContentSlideDistanceDip = 32.0;
    private const double HiddenOpacity = 0.90;
    private static readonly TimeSpan ShowDuration = TimeSpan.FromMilliseconds(180);
    private static readonly TimeSpan HideDuration = TimeSpan.FromMilliseconds(150);
    private uint _lastConfiguredDpi;

    private readonly OverlayTabState _tabState = new();
    private readonly Dictionary<OverlayTabId, Button> _tabButtons = new();
    private readonly Dictionary<OverlayTabId, FrameworkElement> _tabPages = new();
    private readonly Dictionary<OverlayTabId, IReadOnlyList<OverlayRow>> _pageRows = new();
    private readonly OverlayRowSelection _rowSelection = new();
    private readonly Brush _rowSelectedBrush;
    private static readonly Brush RowUnselectedBrush = new SolidColorBrush(Colors.Transparent);

    // OQ5-UI-07: the delayed-commit helper behind the temporary "Slider Preview" fixture only.
    private OverlayDelayedSliderCommit? _sliderPreviewCommit;

    internal event Action<OverlayOutsideClick>? OutsideClickDismissRequested;

    public OverlayWindow()
    {
        InitializeComponent();
        _rowSelectedBrush =
            Application.Current.Resources.TryGetValue("AccentFillColorDefaultBrush", out var accent) && accent is Brush brush
                ? brush
                : new SolidColorBrush(Colors.SlateGray);
        BuildShell();
        Closed += (_, _) => _sliderPreviewCommit?.Dispose();
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
        // OQ5-UI-07 s.11/s.13: hide never waits for the 2s timer -- drop any unsubmitted preview
        // draft so a hidden Overlay cannot fire an obsolete fake mutation later. Already-submitted
        // work settles on its own and stays subject to the generation check.
        _sliderPreviewCommit?.CancelUnsubmitted();
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
                Padding = new Thickness(4, 6, 4, 6),
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

    // Device gets the temporary preview fixture (Toggle + Slider primitives + navigation rows);
    // every other tab keeps its OQ5-UI-01 placeholder with zero selectable rows.
    private FrameworkElement BuildPage(OverlayTabId id, List<OverlayRow> rows)
    {
        if (id != OverlayTabId.Device)
            return CreatePlaceholderPage(id);

        var stack = new StackPanel { Spacing = 4 };

        // OQ5-UI-05 temporary fixture: not product features, no persistence, no Runtime transport.
        // The enabled preview's requestChange is a local echo standing in for a future Runtime
        // authoritative readback so the primitive can be hardware-tested.
        OverlayToggleRow enabledToggle = null!;
        enabledToggle = new OverlayToggleRow("Toggle Preview",
            desired => enabledToggle.ApplyState(isAvailable: true, isOn: desired));
        enabledToggle.ApplyState(isAvailable: true, isOn: false);
        rows.Add(new OverlayRow(enabledToggle.Container, enabledToggle.Capabilities));
        stack.Children.Add(enabledToggle.Container);

        var unavailableToggle = new OverlayToggleRow("Unavailable Toggle Preview", _ => { });
        unavailableToggle.ApplyState(isAvailable: false, isOn: false);
        rows.Add(new OverlayRow(unavailableToggle.Container, unavailableToggle.Capabilities));
        stack.Children.Add(unavailableToggle.Container);

        // OQ5-UI-06/07 temporary fixture: neutral 0..100 step 5 numbers, not a product feature.
        // The preview stays immediate; the desired value is routed through the OQ5-UI-07 delayed
        // helper, and a fake commit that just echoes the value settles it ~2s after the last edit.
        OverlaySliderRow enabledSlider = null!;
        _sliderPreviewCommit = new OverlayDelayedSliderCommit(
            commitAsync: desired =>
            {
                OverlayLog.Info("SliderPreview", "Preview commit submitted.", ("Value", desired));
                return Task.FromResult(new OverlaySliderCommitSettlement(true, desired, null));
            },
            onCurrentSettlement: settlement => DispatcherQueue.TryEnqueue(() =>
            {
                OverlayLog.Info("SliderPreview", "Preview commit settled.",
                    ("Succeeded", settlement.Succeeded), ("Value", settlement.AuthoritativeValue));
                if (settlement is { Succeeded: true, AuthoritativeValue: { } value })
                    enabledSlider.ApplyState(isAvailable: true, minimum: 0, maximum: 100, step: 5, value: value);
            }),
            delay: OverlayDelayedSliderCommit.ProductionDelay);
        enabledSlider = new OverlaySliderRow("Slider Preview", OverlaySliderRow.FormatInteger,
            desired => _sliderPreviewCommit.Schedule(desired));
        enabledSlider.ApplyState(isAvailable: true, minimum: 0, maximum: 100, step: 5, value: 50);
        rows.Add(new OverlayRow(enabledSlider.Container, enabledSlider.Capabilities));
        stack.Children.Add(enabledSlider.Container);

        var unavailableSlider = new OverlaySliderRow("Unavailable Slider Preview", OverlaySliderRow.FormatInteger, _ => { });
        unavailableSlider.ApplyState(isAvailable: false, minimum: 0, maximum: 100, step: 5, value: 50);
        rows.Add(new OverlayRow(unavailableSlider.Container, unavailableSlider.Capabilities));
        stack.Children.Add(unavailableSlider.Container);

        for (var i = 1; i <= NavigationPreviewRowCount; i++)
        {
            var label = new TextBlock { Text = $"Navigation Preview {i:00}" };
            if (Application.Current.Resources.TryGetValue("BodyTextBlockStyle", out var style) && style is Style bodyStyle)
                label.Style = bodyStyle;

            var container = new Border
            {
                Child = label,
                Padding = new Thickness(12, 10, 12, 10),
                CornerRadius = new CornerRadius(4),
                BorderThickness = new Thickness(2),
                BorderBrush = RowUnselectedBrush,
            };
            // Preview rows are navigation/highlight only: always selectable, no Activate/Adjust.
            rows.Add(new OverlayRow(container, new OverlayRowCapabilities(() => true)));
            stack.Children.Add(container);
        }

        return stack;
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
    internal void NavigateUp() => MoveRowSelection(up: true);

    internal void NavigateDown() => MoveRowSelection(up: false);

    // If the selected row became unselectable, the selection method normalizes to another row and
    // reports it; on that same press we only refresh the highlight and skip the adjust/activate so
    // the fallback row is never mutated under a stale highlight.
    internal void AdjustSelectedRow(int delta)
    {
        if (_rowSelection.AdjustSelected(delta)) RefreshRowSelectionAfterMove();
    }

    internal void ActivateSelectedRow()
    {
        if (_rowSelection.ActivateSelected()) RefreshRowSelectionAfterMove();
    }

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

    // s.12: deterministic tab-change ordering -- tab visuals, then show the page and reset the
    // shared scroll to top, then reset that page's row selection to its first selectable row,
    // then apply the row-selection visual.
    private void ApplySelectedTabVisualState()
    {
        var selected = _tabState.SelectedTab;
        foreach (var (id, button) in _tabButtons)
            button.FontWeight = id == selected ? FontWeights.SemiBold : FontWeights.Normal;
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
            rows[i].Container.BorderBrush = i == selectedIndex ? _rowSelectedBrush : RowUnselectedBrush;
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
