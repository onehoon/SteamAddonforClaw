using CommunityToolkit.WinUI.Controls;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SteamInputAddonforClaw.Contracts.Frontend;
using SteamInputAddonforClaw.Status;

namespace SteamInputAddonforClaw.Views;

public sealed partial class StatusPage : UserControl
{
    public event EventHandler? RefreshRequested;

    public StatusPage()
    {
        InitializeComponent();
    }

    internal void SetRefreshing(bool isRefreshing)
    {
        RefreshStatusButton.IsEnabled = !isRefreshing;
    }

    internal void Render(FrontendStatusSnapshot snapshot)
    {
        DeviceManufacturerText.Text = StatusPresentation.FormatManufacturerForDisplay(snapshot.Device.Manufacturer);
        DeviceModelText.Text = snapshot.Device.Model;
        DeviceSupportText.Text = StatusPresentation.FormatDeviceCompatibility(snapshot.Hardware.Status);
        DeviceBoardGpuText.Text = $"Board: {snapshot.Device.BaseBoard} · GPU: {string.Join(", ", snapshot.Device.GpuModels)}";

        SteamGameStatusText.Text = StatusPresentation.FormatSteamGame(snapshot.Steam);

        var isWarning = StatusPresentation.IsWarning(snapshot);
        StatusInfoBar.Severity = InfoBarSeverity.Warning;
        StatusInfoBar.Message = snapshot.AddonReason;
        StatusInfoBar.IsOpen = isWarning;

        var routing = new List<StatusCardViewModel>
        {
            new("HidHide", snapshot.Prerequisites.HidHideStatus.ToString(), snapshot.Prerequisites.HidHideReason),
            new("usbip-win2", snapshot.Prerequisites.UsbIpStatus.ToString(), snapshot.Prerequisites.UsbIpReason),
            new("VIIPER", snapshot.Prerequisites.ViiperStatus.ToString(), snapshot.Prerequisites.ViiperReason)
        };
        RenderGroup(RoutingComponentsExpander, routing, "ready",
            status => string.Equals(status, "Ready", StringComparison.OrdinalIgnoreCase));
    }

    private static void RenderGroup(SettingsExpander expander, IReadOnlyList<StatusCardViewModel> items, string goodLabel, Func<string, bool> isGood)
    {
        var goodCount = items.Count(item => isGood(item.Status));
        expander.Description = new TextBlock
        {
            Text = $"{goodCount} of {items.Count} {goodLabel}",
            FontSize = 14,
            FontWeight = FontWeights.SemiBold
        };

        expander.Items.Clear();
        foreach (var item in items)
        {
            expander.Items.Add(new SettingsCard
            {
                Header = item.Name,
                Description = item.Secondary,
                Content = new TextBlock { Text = item.Status, Opacity = 0.7 }
            });
        }
    }

    private void RefreshStatusButton_Click(object sender, RoutedEventArgs args)
    {
        RefreshRequested?.Invoke(this, EventArgs.Empty);
    }
}
