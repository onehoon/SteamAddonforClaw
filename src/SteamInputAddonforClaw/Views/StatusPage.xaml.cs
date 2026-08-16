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
        DeviceManufacturerText.Text = snapshot.Device.Manufacturer;
        DeviceModelText.Text = snapshot.Device.Model;
        DeviceSupportText.Text = snapshot.Hardware.Status;
        DeviceBoardGpuText.Text = $"Board: {snapshot.Device.BaseBoard} · GPU: {string.Join(", ", snapshot.Device.GpuModels)}";

        SteamGameStatusText.Text = snapshot.Steam.Source == "BigPicture" ? "Big Picture Mode" : snapshot.Steam.AppId != 0 ? "Running" : "Not Running";
        ControllerStatusText.Text = snapshot.Routing.OperationalState == "OverrideActive" && snapshot.Routing.SteamOutputActive && snapshot.Routing.NativeDirectInputActive ? "Steam Controller (DInput)" : snapshot.Hardware.Status == "Supported" && snapshot.RecoverySafe ? "MSI Center M Native" : "Unavailable";

        var isWarning = !snapshot.RecoverySafe || snapshot.AddonOwnedOutputIdentityUncertain || snapshot.SetupStatus != FrontendSetupStatus.Complete;
        StatusInfoBar.Severity = InfoBarSeverity.Warning;
        StatusInfoBar.Message = snapshot.SetupReason;
        StatusInfoBar.IsOpen = isWarning;

        var software = snapshot.ControllerSoftware
            .Select(item => new StatusCardViewModel(item.DisplayName, $"{item.Installation} / {item.Runtime}", item.Reason))
            .ToList();
        RenderGroup(ControllerSoftwareExpander, software, "installed",
            status => status is not ("Not installed" or "Indeterminate"));

        var routing = new List<StatusCardViewModel>
        {
            new("HidHide", snapshot.Prerequisites.HidHideStatus, snapshot.Prerequisites.HidHideReason),
            new("usbip-win2", snapshot.Prerequisites.UsbIpStatus, snapshot.Prerequisites.UsbIpReason),
            new("VIIPER", snapshot.Prerequisites.ViiperStatus, snapshot.Prerequisites.ViiperReason)
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
