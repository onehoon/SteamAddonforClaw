using CommunityToolkit.WinUI.Controls;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SteamInputAddonforClaw.Contracts.Frontend;

namespace SteamInputAddonforClaw.Views;

public sealed partial class SettingsPage : UserControl
{
    private IAddonFrontendControl? _frontend;
    private FrontendUpdateSnapshot _updateSnapshot = FrontendUpdateSnapshot.Unavailable;
    private int _updateOperationInProgress;
    public event EventHandler? DeveloperMenuRequested;

    public SettingsPage()
    {
        InitializeComponent();
    }

    internal void Initialize(FrontendBootstrapSnapshot bootstrap, IAddonFrontendControl frontend)
    {
        _frontend = frontend ?? throw new ArgumentNullException(nameof(frontend));
        DeveloperMenuCard.Visibility = GetDeveloperMenuCardVisibility(bootstrap.Settings.DeveloperMenuEnabled);
        _ = RefreshAppUpdateAsync();
    }

    internal void RequestAppUpdateRefresh() => _ = RefreshAppUpdateAsync();

    private async Task RefreshAppUpdateAsync()
    {
        if (_frontend is null || Volatile.Read(ref _updateOperationInProgress) != 0) return;
        try { RenderAppUpdate(await _frontend.CaptureAppUpdateAsync().ConfigureAwait(true)); }
        catch (Exception exception) { AppLog.Warn("Update", "Main UI update state refresh failed.", exception); }
    }

    private void RenderAppUpdate(FrontendUpdateSnapshot snapshot)
    {
        _updateSnapshot = snapshot;
        UpdateCard.Description = snapshot.Message;
        UpdateButton.Content = snapshot.CanInstall ? "Install update" : "Check";
        UpdateButton.IsEnabled = snapshot.CanCheck || snapshot.CanInstall;
    }

    private async void UpdateButton_Click(object sender, RoutedEventArgs args)
    {
        if (_frontend is null || Interlocked.Exchange(ref _updateOperationInProgress, 1) != 0) return;
        try
        {
            if (_updateSnapshot.CanInstall)
            {
                var result = await _frontend.InstallAppUpdateAsync();
                RenderAppUpdate(result.Snapshot);
                if (!result.Succeeded && result.FailureMessage is not null)
                    UpdateCard.Description = result.FailureMessage;
                return;
            }

            RenderAppUpdate(new(FrontendUpdateState.Checking, "Checking for updates…"));
            RenderAppUpdate(await _frontend.CheckAndDownloadAppUpdateAsync().ConfigureAwait(true));
        }
        catch (Exception exception)
        {
            AppLog.Warn("Update", "Main UI update action failed.", exception);
            RenderAppUpdate(new(FrontendUpdateState.Failed, "The update operation failed. Try again."));
        }
        finally
        {
            Volatile.Write(ref _updateOperationInProgress, 0);
        }
    }

    /// <summary>Renders the read-only Required Components list (moved here from the Status page) from
    /// the same authoritative frontend status snapshot MainWindow already captures. Diagnostic only --
    /// no repair/install controls: Runtime lifecycle/reconciliation stays the authority for setup.</summary>
    internal void RenderRequiredComponents(FrontendStatusSnapshot snapshot)
    {
        var components = new (string Name, string Status, string Reason)[]
        {
            ("HidHide", snapshot.Prerequisites.HidHideStatus.ToString(), snapshot.Prerequisites.HidHideReason),
            ("usbip-win2", snapshot.Prerequisites.UsbIpStatus.ToString(), snapshot.Prerequisites.UsbIpReason),
            ("VIIPER", snapshot.Prerequisites.ViiperStatus.ToString(), snapshot.Prerequisites.ViiperReason),
        };

        var readyCount = components.Count(item => string.Equals(item.Status, "Ready", StringComparison.OrdinalIgnoreCase));
        RequiredComponentsExpander.Description = new TextBlock
        {
            Text = $"{readyCount} of {components.Length} ready",
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
        };

        RequiredComponentsExpander.Items.Clear();
        foreach (var (name, status, reason) in components)
        {
            RequiredComponentsExpander.Items.Add(new SettingsCard
            {
                Header = name,
                Description = reason,
                Content = new TextBlock { Text = status, Opacity = 0.7 },
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
