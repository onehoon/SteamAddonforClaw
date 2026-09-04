using CommunityToolkit.WinUI.Controls;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SteamInputAddonforClaw.Contracts.Frontend;
using SteamInputAddonforClaw.Status;

namespace SteamInputAddonforClaw.Views;

public sealed partial class SettingsPage : UserControl
{
    public event EventHandler? DeveloperMenuRequested;

    public SettingsPage()
    {
        InitializeComponent();
    }

    internal void Initialize(FrontendBootstrapSnapshot bootstrap)
    {
        DeveloperMenuCard.Visibility = GetDeveloperMenuCardVisibility(bootstrap.Settings.DeveloperMenuEnabled);
    }

    /// <summary>Renders the read-only Required Components list (moved here from the Status page) from
    /// the same authoritative frontend status snapshot MainWindow already captures. Diagnostic only --
    /// no repair/install controls: Runtime lifecycle/reconciliation stays the authority for setup.</summary>
    internal void RenderRequiredComponents(FrontendStatusSnapshot snapshot)
    {
        var components = new List<StatusCardViewModel>
        {
            new("HidHide", snapshot.Prerequisites.HidHideStatus.ToString(), snapshot.Prerequisites.HidHideReason),
            new("usbip-win2", snapshot.Prerequisites.UsbIpStatus.ToString(), snapshot.Prerequisites.UsbIpReason),
            new("VIIPER", snapshot.Prerequisites.ViiperStatus.ToString(), snapshot.Prerequisites.ViiperReason),
        };

        var readyCount = components.Count(item => string.Equals(item.Status, "Ready", StringComparison.OrdinalIgnoreCase));
        RequiredComponentsExpander.Description = new TextBlock
        {
            Text = $"{readyCount} of {components.Count} ready",
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
        };

        RequiredComponentsExpander.Items.Clear();
        foreach (var item in components)
        {
            RequiredComponentsExpander.Items.Add(new SettingsCard
            {
                Header = item.Name,
                Description = item.Secondary,
                Content = new TextBlock { Text = item.Status, Opacity = 0.7 },
            });
        }
    }

    internal static Visibility GetDeveloperMenuCardVisibility(bool developerMenuEnabled) =>
        developerMenuEnabled ? Visibility.Visible : Visibility.Collapsed;

    private void DeveloperMenuButton_Click(object sender, RoutedEventArgs args)
    {
        DeveloperMenuRequested?.Invoke(this, EventArgs.Empty);
    }
}
