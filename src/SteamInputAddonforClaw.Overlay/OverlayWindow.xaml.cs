using System.Diagnostics;
using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Hosting;
using Windows.UI.ViewManagement;
using SteamInputAddonforClaw.Overlay.Diagnostics;

namespace SteamInputAddonforClaw.Overlay;

public sealed partial class OverlayWindow : Window
{
    private const double ContentSlideDistanceDip = 32.0;
    private const double HiddenOpacity = 0.90;
    private static readonly TimeSpan ShowDuration = TimeSpan.FromMilliseconds(180);
    private static readonly TimeSpan HideDuration = TimeSpan.FromMilliseconds(150);

    internal event Action<OverlayOutsideClick>? OutsideClickDismissRequested;

    public OverlayWindow() => InitializeComponent();

    internal nint HandleForDiagnostics => WindowInterop.GetWindowHandle(this);

    internal void PrepareHidden() => ConfigureWindow();

    internal async Task ShowForPocAsync()
    {
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
            OverlayLog.Info("Animation", "Show animation completed", ("ElapsedMs", stopwatch.Elapsed.TotalMilliseconds));
        }
        catch (Exception exception)
        {
            OverlayLog.Error("Animation", "Show animation failed; keeping Overlay visible.", exception);
            TrySetVisibleVisualState();
        }
    }

    internal async Task HideForPocAsync()
    {
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

    private void ConfigureWindow()
    {
        WindowInterop.Configure(this, out var rect, out var dpi, out var monitorText);
        var scale = dpi / 96.0;
        GeometryText.Text = $"{monitorText}\nWorkArea: {rect.X},{rect.Y} {rect.Width}x{rect.Height}\nDPI / Scale: {dpi} / {scale:0.##}\nPanel DIP / physical width: {OverlayWindowGeometry.PocPanelWidthDip:0} / {rect.Width}px";
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
