using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace SteamInputAddonforClaw.Overlay;

public sealed partial class OverlayWindow : Window
{
    // Shared selection resources are initialized here because both the shell and navigation
    // responsibilities use them while remaining part of the same OverlayWindow owner.
    private readonly Brush _rowSelectedBrush;

    internal event Action<OverlayOutsideClick>? OutsideClickDismissRequested;

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
        BuildShell();
        Closed += (_, _) =>
        {
            foreach (var surface in _quickSettingsSurfaces.Values) surface.Binding?.Dispose();
        };
    }
}
