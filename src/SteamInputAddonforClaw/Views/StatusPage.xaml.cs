using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SteamInputAddonforClaw.Devices;
using SteamInputAddonforClaw.Prerequisites;
using SteamInputAddonforClaw.Status;
using System.Collections.ObjectModel;

namespace SteamInputAddonforClaw.Views;

public sealed partial class StatusPage : UserControl
{
    private readonly ObservableCollection<StatusCardViewModel> _softwareCards = [];
    private readonly ObservableCollection<StatusCardViewModel> _componentCards = [];
    private readonly ObservableCollection<StatusCardViewModel> _runtimeCards = [];

    public event EventHandler? RefreshRequested;

    public StatusPage()
    {
        InitializeComponent();
        ControllerSoftwareRepeater.ItemsSource = _softwareCards;
        RoutingComponentsRepeater.ItemsSource = _componentCards;
        RuntimeStatusList.ItemsSource = _runtimeCards;
    }

    internal void SetRefreshing(bool isRefreshing)
    {
        RefreshStatusButton.IsEnabled = !isRefreshing;
    }

    internal void Render(SystemStatusSnapshot snapshot, FirstTimeSetupAddonPresentation addonPresentation)
    {
        DeviceManufacturerText.Text = snapshot.Device.Manufacturer;
        DeviceModelText.Text = snapshot.Device.Model;
        DeviceSupportText.Text = snapshot.HardwareCompatibility.Status switch
        {
            HardwareCompatibilityStatus.Supported => "Supported",
            HardwareCompatibilityStatus.Unsupported => "Unsupported",
            _ => "Compatibility unknown"
        };
        DeviceBoardGpuText.Text = $"Board: {snapshot.Device.BaseBoardProduct}  GPU: {string.Join(", ", snapshot.Device.GpuModels)}";
        Replace(_softwareCards, snapshot.ControllerSoftware.Select(item => new StatusCardViewModel(item.DisplayName, MainWindow.FormatSoftwareStatus(item), item.Reason)));
        Replace(_componentCards,
        [
            new("HidHide", snapshot.Prerequisites.HidHide.Status.ToString(), snapshot.Prerequisites.HidHide.Reason),
            new("usbip-win2", snapshot.Prerequisites.UsbIpWin2.Status.ToString(), snapshot.Prerequisites.UsbIpWin2.Reason),
            new("VIIPER", snapshot.Prerequisites.Viiper.Status.ToString(), snapshot.Prerequisites.Viiper.Reason)
        ]);
        Replace(_runtimeCards,
        [
            new("Steam", snapshot.Steam.IsActive ? "Active" : "Inactive", $"RunningAppID: {snapshot.Steam.RunningAppId}"),
            new("Steam Input Addon", addonPresentation.Status, addonPresentation.Reason)
        ]);
    }

    private void RefreshStatusButton_Click(object sender, RoutedEventArgs args)
    {
        RefreshRequested?.Invoke(this, EventArgs.Empty);
    }

    private static void Replace(ObservableCollection<StatusCardViewModel> destination, IEnumerable<StatusCardViewModel> source)
    {
        destination.Clear();
        foreach (var item in source) destination.Add(item);
    }
}
