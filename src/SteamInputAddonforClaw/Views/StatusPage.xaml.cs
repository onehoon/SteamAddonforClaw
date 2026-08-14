using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SteamInputAddonforClaw.Devices;
using SteamInputAddonforClaw.Prerequisites;
using SteamInputAddonforClaw.Status;
using System.Collections.ObjectModel;

namespace SteamInputAddonforClaw.Views;

internal sealed record StatusTileViewModel(string Header, string Value, string Secondary);

public sealed partial class StatusPage : UserControl
{
    private readonly ObservableCollection<StatusTileViewModel> _summaryTiles = [];
    private readonly ObservableCollection<StatusCardViewModel> _softwareCards = [];
    private readonly ObservableCollection<StatusCardViewModel> _componentCards = [];
    private readonly ObservableCollection<StatusCardViewModel> _runtimeCards = [];

    public event EventHandler? RefreshRequested;

    public StatusPage()
    {
        InitializeComponent();
        SummaryTilesRepeater.ItemsSource = _summaryTiles;
        ControllerSoftwareRepeater.ItemsSource = _softwareCards;
        RoutingComponentsRepeater.ItemsSource = _componentCards;
        RuntimeStatusRepeater.ItemsSource = _runtimeCards;
    }

    internal void SetRefreshing(bool isRefreshing)
    {
        RefreshStatusButton.IsEnabled = !isRefreshing;
    }

    internal void Render(SystemStatusSnapshot snapshot, FirstTimeSetupAddonPresentation addonPresentation)
    {
        var deviceSupport = snapshot.HardwareCompatibility.Status switch
        {
            HardwareCompatibilityStatus.Supported => "Supported",
            HardwareCompatibilityStatus.Unsupported => "Unsupported",
            _ => "Compatibility unknown"
        };

        RenderHero(snapshot.Addon.Status, addonPresentation.Reason);

        Replace(_summaryTiles,
        [
            new("Device", $"{snapshot.Device.Manufacturer} {snapshot.Device.Model}", deviceSupport),
            new("Steam Session", snapshot.Steam.IsActive ? "Active" : "Inactive", $"RunningAppID: {snapshot.Steam.RunningAppId}"),
            new("Steam Input Addon", addonPresentation.Status, addonPresentation.Reason)
        ]);

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

    private void RenderHero(AddonOperationalStatus status, string reason)
    {
        var (title, symbol, severity) = status switch
        {
            AddonOperationalStatus.Ready => ("Steam Input Addon is active", Symbol.Accept, InfoBarSeverity.Success),
            AddonOperationalStatus.WaitingForSteam => ("Ready. Waiting for a Steam session.", Symbol.Sync, InfoBarSeverity.Informational),
            AddonOperationalStatus.Passive => ("Controller remains native.", Symbol.Repair, InfoBarSeverity.Informational),
            AddonOperationalStatus.SetupRequired => ("Setup required", Symbol.Important, InfoBarSeverity.Warning),
            AddonOperationalStatus.RecoveryRequired => ("Recovery required", Symbol.Important, InfoBarSeverity.Warning),
            AddonOperationalStatus.Unsupported => ("Not supported on this device", Symbol.Important, InfoBarSeverity.Warning),
            _ => ("Status indeterminate", Symbol.Help, InfoBarSeverity.Informational)
        };

        HeroIcon.Symbol = symbol;
        HeroTitleText.Text = title;
        HeroReasonText.Text = reason;

        if (severity == InfoBarSeverity.Warning)
        {
            StatusInfoBar.Severity = severity;
            StatusInfoBar.Message = reason;
            StatusInfoBar.IsOpen = true;
        }
        else
        {
            StatusInfoBar.IsOpen = false;
        }
    }

    private void RefreshStatusButton_Click(object sender, RoutedEventArgs args)
    {
        RefreshRequested?.Invoke(this, EventArgs.Empty);
    }

    private static void Replace<T>(ObservableCollection<T> destination, IEnumerable<T> source)
    {
        destination.Clear();
        foreach (var item in source) destination.Add(item);
    }
}
