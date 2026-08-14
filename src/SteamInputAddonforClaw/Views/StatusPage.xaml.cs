using CommunityToolkit.WinUI.Controls;
using Microsoft.UI.Text;
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

    public event EventHandler? RefreshRequested;

    public StatusPage()
    {
        InitializeComponent();
        SummaryTilesRepeater.ItemsSource = _summaryTiles;
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

        var software = snapshot.ControllerSoftware
            .Select(item => new StatusCardViewModel(item.DisplayName, MainWindow.FormatSoftwareStatus(item), item.Reason))
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

    private void RenderHero(AddonOperationalStatus status, string reason)
    {
        var (title, symbol, severity) = status switch
        {
            AddonOperationalStatus.Ready => ("Ready for Steam Input routing", Symbol.Accept, InfoBarSeverity.Success),
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
