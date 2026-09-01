using Microsoft.UI.Xaml;

namespace SteamInputAddonforClaw.Overlay;

public sealed partial class OverlayWindow : Window
{
    public OverlayWindow() => InitializeComponent();

    internal void PrepareHidden() => ConfigureWindow();

    internal void ShowForPoc()
    {
        ConfigureWindow();
        WindowInterop.ShowWithoutActivation(this);
    }

    internal void HideForPoc() => WindowInterop.Hide(this);

    private void ConfigureWindow()
    {
        WindowInterop.Configure(this, out var rect, out var dpi, out var monitorText);
        var scale = dpi / 96.0;
        GeometryText.Text = $"{monitorText}\nWorkArea: {rect.X},{rect.Y} {rect.Width}x{rect.Height}\nDPI / Scale: {dpi} / {scale:0.##}\nPanel DIP / physical width: {OverlayWindowGeometry.PocPanelWidthDip:0} / {rect.Width}px";
    }

    private void OnCloseClicked(object sender, RoutedEventArgs args) => Close();
}
