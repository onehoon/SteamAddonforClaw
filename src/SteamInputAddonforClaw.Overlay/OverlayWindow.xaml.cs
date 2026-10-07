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
        _rowSelectedBrush = OverlayQamResources.Brush("QamFocusBorderBrush");
        _rowSelectedFillBrush = OverlayQamResources.Brush("QamSelectedFillBrush");
        BuildShell();
        Closed += (_, _) =>
        {
            CancelPendingControllerVibrationDraft();
            foreach (var surface in _quickSettingsSurfaces.Values) surface.Binding?.Dispose();
        };
    }
}
