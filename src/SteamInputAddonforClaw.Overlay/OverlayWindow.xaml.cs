using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace SteamInputAddonforClaw.Overlay;

public sealed partial class OverlayWindow : Window
{
    private readonly OverlayStartupClawHudSettings _startupClawHudSettings;

    // Shared selection resources are initialized here because both the shell and navigation
    // responsibilities use them while remaining part of the same OverlayWindow owner.
    private readonly Brush _rowSelectedBrush;

    internal event Action<OverlayOutsideClick>? OutsideClickDismissRequested;

    public OverlayWindow() : this(OverlayStartupClawHudSettings.Default)
    {
    }

    internal OverlayWindow(OverlayStartupClawHudSettings startupClawHudSettings)
    {
        _startupClawHudSettings = startupClawHudSettings;
        InitializeComponent();
        ProfileArtworkHost.Loaded += ProfileArtworkHost_Loaded;
        ProfileArtworkHost.Unloaded += ProfileArtworkHost_Unloaded;
        _rowSelectedBrush = OverlayQamResources.Brush("QamFocusBorderBrush");
        _rowSelectedFillBrush = OverlayQamResources.Brush("QamSelectedFillBrush");
        TabRailCluster.RenderTransform = new TranslateTransform { Y = QuickJumpFooter.Height / 2.0 };
        BuildShell();
        Closed += (_, _) =>
        {
            StopRailClockUpdates();
            ObserveProfileArtworkXamlRoot(null);
            ClearProfileArtwork();
            CancelPendingControllerVibrationDraft();
            foreach (var surface in _quickSettingsSurfaces.Values) surface.Binding?.Dispose();
        };
    }
}
