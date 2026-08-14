using CommunityToolkit.WinUI.Controls;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SteamInputAddonforClaw.Prerequisites;
using SteamInputAddonforClaw.Routing;
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

    internal void Render(SystemStatusSnapshot snapshot, FirstTimeSetupAddonPresentation addonPresentation, RoutingRuntimeStatusSnapshot routingStatus)
    {
        DeviceManufacturerText.Text = StatusPresentation.FormatManufacturerForDisplay(snapshot.Device.Manufacturer);
        DeviceModelText.Text = snapshot.Device.Model;
        DeviceSupportText.Text = StatusPresentation.FormatDeviceCompatibility(snapshot.HardwareCompatibility.Status);
        DeviceBoardGpuText.Text = $"Board: {snapshot.Device.BaseBoardProduct} · GPU: {string.Join(", ", snapshot.Device.GpuModels)}";

        SteamGameStatusText.Text = StatusPresentation.FormatSteamGame(snapshot.Steam.IsActive);
        var stateTrusted = StatusPresentation.IsControllerStateTrusted(snapshot);
        // No independent, non-probing signal currently confirms the native mode is XInput ahead
        // of routing entry; fail conservative and omit the "(XInput)" qualifier rather than guess.
        ControllerStatusText.Text = StatusPresentation.FormatControllerStatus(stateTrusted, routingStatus, nativeXInputVerified: false);

        var isWarning = StatusPresentation.IsWarning(snapshot);
        StatusInfoBar.Severity = InfoBarSeverity.Warning;
        StatusInfoBar.Message = addonPresentation.Reason;
        StatusInfoBar.IsOpen = isWarning;

        var software = snapshot.ControllerSoftware
            .Select(item => new StatusCardViewModel(item.DisplayName, ControllerSoftwareStatusFormatter.Format(item), item.Reason))
            .ToList();
        RenderGroup(ControllerSoftwareExpander, software, "installed",
            status => status is not ("Not installed" or "Indeterminate"));

        var routing = new List<StatusCardViewModel>
        {
            new("HidHide", snapshot.Prerequisites.HidHide.Status.ToString(), snapshot.Prerequisites.HidHide.Reason),
            new("usbip-win2", snapshot.Prerequisites.UsbIpWin2.Status.ToString(), snapshot.Prerequisites.UsbIpWin2.Reason),
            new("VIIPER", snapshot.Prerequisites.Viiper.Status.ToString(), snapshot.Prerequisites.Viiper.Reason)
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
