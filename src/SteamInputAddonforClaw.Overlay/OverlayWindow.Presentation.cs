using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Hosting;
using Windows.UI.ViewManagement;
using SteamInputAddonforClaw.Overlay.Diagnostics;

namespace SteamInputAddonforClaw.Overlay;

public sealed partial class OverlayWindow
{
    private const float HiddenScale = 0.98f;
    private const float HiddenTranslateYDip = 8.0f;
    private const double HiddenOpacity = 0.90;
    private static readonly TimeSpan ShowDuration = TimeSpan.FromMilliseconds(120);
    private static readonly TimeSpan HideDuration = TimeSpan.FromMilliseconds(140);
    private uint _lastConfiguredDpi;
    private int _lastAppliedClawHudTopInsetPx;
    private DispatcherQueueTimer? _railClockTimer;
    private bool _railClockUpdateWarningLogged;

    internal nint HandleForDiagnostics => WindowInterop.GetWindowHandle(this);

    private void OnSurfaceHostSizeChanged(object sender, SizeChangedEventArgs args)
    {
        OpaquePanel.Width = Math.Max(0.0, args.NewSize.Width);
    }

    internal void PrepareHidden() => ConfigureWindow();

    internal async Task ShowForPocAsync()
    {
        // Commit the configured first tab before any visual work so a warm process that was
        // previously showing another tab never flashes it for a frame during the reveal.
        ResetUiForShow();
        ConfigureWindow();
        var initialStatePrepared = true;
        try
        {
            SetVisualState(HiddenScale, HiddenTranslateYDip, HiddenOpacity);
        }
        catch (Exception exception)
        {
            initialStatePrepared = false;
            OverlayLog.Error("Animation", "Show animation initial state failed; keeping Overlay visible.", exception);
        }
        UpdateRailClockText();
        WindowInterop.ShowWithoutActivation(this);
        _isVisible = true;
        StartRailClockUpdates();
        WindowInterop.ArmOutsideClickDismissal(this, outsideClick => OutsideClickDismissRequested?.Invoke(outsideClick));
        if (!initialStatePrepared || !AnimationsEnabled())
        {
            TrySetVisibleVisualState();
            LogSurfaceBounds("Show.Visible");
            return;
        }

        var stopwatch = Stopwatch.StartNew();
        OverlayLog.Debug("Animation", "Show animation started",
            ("DurationMs", ShowDuration.TotalMilliseconds),
            ("StartScale", HiddenScale), ("EndScale", 1.0),
            ("StartTranslateYDip", HiddenTranslateYDip), ("EndTranslateYDip", 0.0),
            ("StartOpacity", HiddenOpacity), ("EndOpacity", 1.0),
            ("AnimationTranslateYPhysical", HiddenTranslateYDip * AnimationRasterizationScale()));
        try
        {
            await AnimateAsync(HiddenScale, 1.0f, HiddenTranslateYDip, 0.0, HiddenOpacity, 1.0, ShowDuration, easeIn: false);
            TrySetVisibleVisualState();
            LogSurfaceBounds("Show.Visible");
            OverlayLog.Debug("Animation", "Show animation completed", ("ElapsedMs", stopwatch.Elapsed.TotalMilliseconds));
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
        StopRailClockUpdates();
        _isVisible = false;
        _numericSliderRepeatTarget.Clear();
        // Normal user Back/outside-click flushes valid drafts before sending DismissRequested.
        // A Runtime-forced Hide remains lifecycle cleanup: cancel any still-unsubmitted work and
        // never wait for a debounce or feature mutation here.
        foreach (var surface in _quickSettingsSurfaces.Values) surface.Binding?.CancelUnsubmittedDrafts();
        CancelPendingControllerVibrationDraft();
        WindowInterop.DisarmOutsideClickDismissal();
        if (AnimationsEnabled())
        {
            var stopwatch = Stopwatch.StartNew();
            OverlayLog.Debug("Animation", "Hide animation started",
                ("DurationMs", HideDuration.TotalMilliseconds),
                ("StartScale", 1.0), ("EndScale", HiddenScale),
                ("StartTranslateYDip", 0.0), ("EndTranslateYDip", HiddenTranslateYDip),
                ("StartOpacity", 1.0), ("EndOpacity", HiddenOpacity));
            try
            {
                await AnimateAsync(1.0f, HiddenScale, 0.0, HiddenTranslateYDip, 1.0, HiddenOpacity, HideDuration, easeIn: true);
                OverlayLog.Debug("Animation", "Hide animation completed", ("ElapsedMs", stopwatch.Elapsed.TotalMilliseconds));
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
            ClearProfileArtwork();
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

    private void ConfigureWindow()
    {
        var topInsetPx = CurrentClawHudTopInsetPx();
        WindowInterop.Configure(this, topInsetPx, out var rect, out _lastConfiguredDpi, out var monitorText);
        _lastAppliedClawHudTopInsetPx = topInsetPx;
        var scale = _lastConfiguredDpi / 96.0;
        OverlayLog.Debug("Geometry", "Overlay window configured",
            ("Monitor", monitorText),
            ("OverlayX", rect.X), ("OverlayY", rect.Y),
            ("Dpi", _lastConfiguredDpi), ("Scale", scale),
            ("ClawHudTopInsetPx", topInsetPx),
            ("OverlayWidthPx", rect.Width),
            ("OverlayHeightPx", rect.Height));
    }

    private void ReconcileVisibleClawHudTopInset(int topInsetPx)
    {
        if (!_isVisible || topInsetPx == _lastAppliedClawHudTopInsetPx)
            return;

        if (WindowInterop.TryApplyVisibleClawHudTopInset(this, topInsetPx, out _))
            _lastAppliedClawHudTopInsetPx = topInsetPx;
    }

    private void UpdateRailClockText()
    {
        try
        {
            RailClockText.Text = DateTime.Now.ToString("HH:mm", CultureInfo.InvariantCulture);
        }
        catch (Exception exception)
        {
            if (_railClockUpdateWarningLogged) return;
            _railClockUpdateWarningLogged = true;
            OverlayLog.Warn("Window", "Overlay clock could not be refreshed; continuing without affecting Overlay setup.", exception);
        }
    }

    private void StartRailClockUpdates()
    {
        try
        {
            if (_railClockTimer is null)
            {
                var dispatcherQueue = DispatcherQueue.GetForCurrentThread();
                if (dispatcherQueue is null)
                {
                    OverlayLog.Warn("Window", "Overlay clock dispatcher is unavailable; continuing without periodic clock updates.");
                    return;
                }

                _railClockTimer = dispatcherQueue.CreateTimer();
                _railClockTimer.Tick += OnRailClockTimerTick;
            }

            _railClockTimer.Interval = TimeSpan.FromSeconds(20);
            _railClockTimer.IsRepeating = true;
            _railClockTimer.Stop();
            _railClockTimer.Start();
        }
        catch (Exception exception)
        {
            OverlayLog.Warn("Window", "Overlay clock timer could not be started; continuing without periodic clock updates.", exception);
        }
    }

    private void StopRailClockUpdates() => _railClockTimer?.Stop();

    private void OnRailClockTimerTick(DispatcherQueueTimer sender, object args) => UpdateRailClockText();

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
            OverlayLog.Debug("Geometry", "Overlay surface bounds snapshot",
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
                ("AnimatedContentWidthDip", AnimatedContent.ActualWidth),
                ("AnimatedContentHeightDip", AnimatedContent.ActualHeight),
                ("AnimatedContentWidthPhysical", AnimatedContent.ActualWidth * scale),
                ("AnimatedContentHeightPhysical", AnimatedContent.ActualHeight * scale),
                ("SurfaceWidthDip", OpaquePanel.ActualWidth),
                ("SurfaceHeightDip", OpaquePanel.ActualHeight),
                ("SurfaceWidthPhysical", OpaquePanel.ActualWidth * scale),
                ("SurfaceHeightPhysical", OpaquePanel.ActualHeight * scale));
        }
        catch (Exception exception)
        {
            OverlayLog.Warn("Geometry", "Overlay surface bounds snapshot failed; continuing without diagnostics.", exception,
                ("Operation", "LogSurfaceBounds"), ("Reason", reason));
        }
    }

    private void SetVisibleVisualState() => SetVisualState(1.0f, 0.0, 1.0);

    private void SetHiddenVisualState() => SetVisualState(HiddenScale, HiddenTranslateYDip, HiddenOpacity);

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

    private void SetVisualState(float scale, double translationY, double opacity)
    {
        var visual = ElementCompositionPreview.GetElementVisual(AnimatedContent);
        SetAnimationCenterPoint(visual);
        visual.Scale = new Vector3(scale, scale, 1.0f);
        visual.Offset = new Vector3(0, (float)translationY, 0);
        visual.Opacity = (float)opacity;
    }

    private async Task AnimateAsync(
        float startScale,
        float endScale,
        double startTranslationY,
        double endTranslationY,
        double startOpacity,
        double endOpacity,
        TimeSpan duration,
        bool easeIn)
    {
        var visual = ElementCompositionPreview.GetElementVisual(AnimatedContent);
        SetAnimationCenterPoint(visual);
        var compositor = visual.Compositor;
        var easing = compositor.CreateCubicBezierEasingFunction(
            easeIn ? new Vector2(0.42f, 0.0f) : new Vector2(0.0f, 0.0f),
            easeIn ? new Vector2(1.0f, 1.0f) : new Vector2(0.58f, 1.0f));
        var scale = compositor.CreateVector3KeyFrameAnimation();
        scale.Duration = duration;
        scale.InsertKeyFrame(0.0f, new Vector3(startScale, startScale, 1.0f));
        scale.InsertKeyFrame(1.0f, new Vector3(endScale, endScale, 1.0f), easing);
        var offset = compositor.CreateVector3KeyFrameAnimation();
        offset.Duration = duration;
        offset.InsertKeyFrame(0.0f, new Vector3(0, (float)startTranslationY, 0));
        offset.InsertKeyFrame(1.0f, new Vector3(0, (float)endTranslationY, 0), easing);
        var opacity = compositor.CreateScalarKeyFrameAnimation();
        opacity.Duration = duration;
        opacity.InsertKeyFrame(0.0f, (float)startOpacity);
        opacity.InsertKeyFrame(1.0f, (float)endOpacity, easing);

        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var batch = compositor.CreateScopedBatch(CompositionBatchTypes.Animation);
        batch.Completed += (_, _) => completion.TrySetResult();
        visual.StartAnimation(nameof(visual.Scale), scale);
        visual.StartAnimation(nameof(visual.Offset), offset);
        visual.StartAnimation(nameof(visual.Opacity), opacity);
        batch.End();
        await completion.Task;
        visual.StopAnimation(nameof(visual.Scale));
        visual.StopAnimation(nameof(visual.Offset));
        visual.StopAnimation(nameof(visual.Opacity));
    }

    private void SetAnimationCenterPoint(Visual visual)
    {
        var width = AnimatedContent.ActualWidth;
        var height = AnimatedContent.ActualHeight;
        if (width <= 0 || height <= 0)
        {
            width = visual.Size.X;
            height = visual.Size.Y;
        }

        visual.CenterPoint = new Vector3((float)(width / 2.0), (float)(height / 2.0), 0);
    }

    private double AnimationRasterizationScale()
    {
        var xamlRoot = AnimationViewport.XamlRoot;
        return xamlRoot is not null && xamlRoot.RasterizationScale > 0
            ? xamlRoot.RasterizationScale
            : 1.0;
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
