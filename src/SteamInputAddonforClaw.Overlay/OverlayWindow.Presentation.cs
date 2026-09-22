using System.Diagnostics;
using System.Numerics;
using Microsoft.UI.Composition;
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
    private static readonly TimeSpan ShowDuration = TimeSpan.FromMilliseconds(180);
    private static readonly TimeSpan HideDuration = TimeSpan.FromMilliseconds(140);
    private uint _lastConfiguredDpi;

    internal nint HandleForDiagnostics => WindowInterop.GetWindowHandle(this);

    private void OnSurfaceHostSizeChanged(object sender, SizeChangedEventArgs args)
    {
        OpaquePanel.Width = Math.Max(0.0, args.NewSize.Width);
    }

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
            SetVisualState(HiddenScale, HiddenTranslateYDip, HiddenOpacity);
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
            ("StartScale", HiddenScale), ("EndScale", 1.0),
            ("StartTranslateYDip", HiddenTranslateYDip), ("EndTranslateYDip", 0.0),
            ("StartOpacity", HiddenOpacity), ("EndOpacity", 1.0),
            ("AnimationTranslateYPhysical", HiddenTranslateYDip * AnimationRasterizationScale()));
        try
        {
            await AnimateAsync(HiddenScale, 1.0f, HiddenTranslateYDip, 0.0, HiddenOpacity, 1.0, ShowDuration, easeIn: false);
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
        // SF-V2-07 section 23/39: hide never waits for the trailing debounce window -- drop any
        // unsubmitted Device/Profile draft so a hidden Overlay cannot fire an obsolete mutation
        // later. Already-submitted work settles on its own and stays subject to the generation check.
        foreach (var surface in _quickSettingsSurfaces.Values) surface.Binding?.CancelUnsubmittedDrafts();
        WindowInterop.DisarmOutsideClickDismissal();
        if (AnimationsEnabled())
        {
            var stopwatch = Stopwatch.StartNew();
            OverlayLog.Info("Animation", "Hide animation started",
                ("DurationMs", HideDuration.TotalMilliseconds),
                ("StartScale", 1.0), ("EndScale", HiddenScale),
                ("StartTranslateYDip", 0.0), ("EndTranslateYDip", HiddenTranslateYDip),
                ("StartOpacity", 1.0), ("EndOpacity", HiddenOpacity));
            try
            {
                await AnimateAsync(1.0f, HiddenScale, 0.0, HiddenTranslateYDip, 1.0, HiddenOpacity, HideDuration, easeIn: true);
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

    private void ConfigureWindow()
    {
        WindowInterop.Configure(this, out var rect, out _lastConfiguredDpi, out var monitorText);
        var scale = _lastConfiguredDpi / 96.0;
        OverlayLog.Info("Geometry", "Overlay window configured",
            ("Monitor", monitorText),
            ("OverlayX", rect.X), ("OverlayY", rect.Y),
            ("Dpi", _lastConfiguredDpi), ("Scale", scale),
            ("OverlayWidthPx", rect.Width),
            ("OverlayHeightPx", rect.Height));
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
