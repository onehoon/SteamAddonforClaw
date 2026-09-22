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
    private int _enterBiosOperationInProgress;
    private FrontendSteamFseSnapshot _steamFseSnapshot = FrontendSteamFseSnapshot.Unavailable("Steam Big Picture Full Screen Experience is unavailable.");
    private int _steamFseMutationInProgress;
    private bool _applyingSteamFseState;
    private bool _applyingQuickSettingsPowerSourcePreference;
    private bool _lastKnownQuickSettingsCurrentPowerSourceOnly;
    public event EventHandler? DeveloperMenuRequested;

    public SettingsPage()
    {
        InitializeComponent();
    }

    internal void Initialize(FrontendBootstrapSnapshot bootstrap, IAddonFrontendControl frontend)
    {
        _frontend = frontend ?? throw new ArgumentNullException(nameof(frontend));
        DeveloperMenuCard.Visibility = GetDeveloperMenuCardVisibility(bootstrap.Settings.DeveloperMenuEnabled);
        _lastKnownQuickSettingsCurrentPowerSourceOnly = bootstrap.Settings.QuickSettingsCurrentPowerSourceOnly;
        SetQuickSettingsPowerSourceToggle(_lastKnownQuickSettingsCurrentPowerSourceOnly);
        _ = RefreshAppUpdateAsync();
        _ = RefreshSteamFseAsync();
    }

    internal void RequestAppUpdateRefresh() => _ = RefreshAppUpdateAsync();
    internal void RequestSteamFseRefresh() => _ = RefreshSteamFseAsync();

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

    private async void EnterBiosButton_Click(object sender, RoutedEventArgs args)
    {
        if (_frontend is null || !TryBeginEnterBiosOperation(ref _enterBiosOperationInProgress)) return;

        EnterBiosCard.IsEnabled = false;
        EnterBiosButton.IsEnabled = false;
        try
        {
            var confirmation = new ContentDialog
            {
                Title = "Enter BIOS?",
                Content = "The device will restart directly into BIOS settings.\n\nSave your work before continuing.",
                PrimaryButtonText = "Restart and Enter BIOS",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = XamlRoot,
            };
            if (await confirmation.ShowAsync() != ContentDialogResult.Primary)
                return;

            var result = await _frontend.RequestEnterBiosAsync().ConfigureAwait(true);
            if (!result.Succeeded)
                await ShowEnterBiosFailureAsync(result.FailureMessage ?? "Enter BIOS could not be started. Try again.").ConfigureAwait(true);
        }
        catch (Exception exception)
        {
            AppLog.Warn("EnterBios", "Enter BIOS UI action failed.", exception);
            await ShowEnterBiosFailureAsync("Enter BIOS could not be started. Try again.").ConfigureAwait(true);
        }
        finally
        {
            EnterBiosButton.IsEnabled = true;
            EnterBiosCard.IsEnabled = true;
            Volatile.Write(ref _enterBiosOperationInProgress, 0);
        }
    }

    private async Task ShowEnterBiosFailureAsync(string message)
    {
        if (XamlRoot is null) return;
        await new ContentDialog
        {
            Title = "Enter BIOS unavailable",
            Content = message,
            CloseButtonText = "OK",
            XamlRoot = XamlRoot,
        }.ShowAsync();
    }

    internal static bool TryBeginEnterBiosOperation(ref int operationInProgress) =>
        Interlocked.Exchange(ref operationInProgress, 1) == 0;

    private async Task RefreshSteamFseAsync()
    {
        if (_frontend is null || Volatile.Read(ref _steamFseMutationInProgress) != 0) return;
        try { RenderSteamFse(await _frontend.CaptureSteamFseAsync().ConfigureAwait(true)); }
        catch (Exception exception)
        {
            AppLog.Warn("SteamFSE", "Main UI SteamFSE state refresh failed.", exception);
            RenderSteamFse(FrontendSteamFseSnapshot.Unavailable("Steam Big Picture Full Screen Experience could not be verified."));
        }
    }

    private void RenderSteamFse(FrontendSteamFseSnapshot snapshot)
    {
        _steamFseSnapshot = snapshot;
        _applyingSteamFseState = true;
        try { SteamFseToggleSwitch.IsOn = snapshot.Enabled; }
        finally { _applyingSteamFseState = false; }
        SteamFseCard.Description = snapshot.Available
            ? "Start Windows directly in Steam Big Picture."
            : snapshot.UnavailableReason ?? "Steam Big Picture Full Screen Experience is unavailable.";
        SteamFseToggleSwitch.IsEnabled = snapshot.Available && Volatile.Read(ref _steamFseMutationInProgress) == 0;
    }

    private async void SteamFseToggleSwitch_Toggled(object sender, RoutedEventArgs args)
    {
        if (_applyingSteamFseState || _frontend is null || Interlocked.Exchange(ref _steamFseMutationInProgress, 1) != 0) return;
        SteamFseToggleSwitch.IsEnabled = false;
        try
        {
            var result = await _frontend.SetSteamFseEnabledAsync(SteamFseToggleSwitch.IsOn).ConfigureAwait(true);
            RenderSteamFse(result.Snapshot);
            if (!result.Succeeded && result.FailureMessage is not null)
                SteamFseCard.Description = result.FailureMessage;
        }
        catch (Exception exception)
        {
            AppLog.Warn("SteamFSE", "Main UI SteamFSE mutation failed.", exception);
            try { RenderSteamFse(await _frontend.CaptureSteamFseAsync().ConfigureAwait(true)); }
            catch (Exception refreshException)
            {
                AppLog.Warn("SteamFSE", "Main UI SteamFSE rollback refresh failed.", refreshException);
                RenderSteamFse(_steamFseSnapshot);
                SteamFseCard.Description = "The Steam Big Picture Full Screen Experience setting could not be changed.";
            }
        }
        finally
        {
            Volatile.Write(ref _steamFseMutationInProgress, 0);
            SteamFseToggleSwitch.IsEnabled = _steamFseSnapshot.Available;
        }
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
            await RefreshAppUpdateAsync().ConfigureAwait(true);
        }
    }

    private void SetQuickSettingsPowerSourceToggle(bool enabled)
    {
        _applyingQuickSettingsPowerSourcePreference = true;
        try { QuickSettingsPowerSourceToggleSwitch.IsOn = enabled; }
        finally { _applyingQuickSettingsPowerSourcePreference = false; }
    }

    private async void QuickSettingsPowerSourceToggleSwitch_Toggled(object sender, RoutedEventArgs args)
    {
        if (_applyingQuickSettingsPowerSourcePreference || _frontend is null) return;

        try
        {
            var settings = await _frontend.SetQuickSettingsCurrentPowerSourceOnlyAsync(QuickSettingsPowerSourceToggleSwitch.IsOn).ConfigureAwait(true);
            _lastKnownQuickSettingsCurrentPowerSourceOnly = settings.QuickSettingsCurrentPowerSourceOnly;
            SetQuickSettingsPowerSourceToggle(_lastKnownQuickSettingsCurrentPowerSourceOnly);
        }
        catch (Exception exception)
        {
            AppLog.Warn("QuickSettings", "Current power-source preference update failed.", exception);
            bool? refreshedValue = null;
            try
            {
                var bootstrap = await _frontend.GetBootstrapAsync().ConfigureAwait(true);
                refreshedValue = bootstrap.Settings.QuickSettingsCurrentPowerSourceOnly;
            }
            catch (Exception refreshException)
            {
                AppLog.Warn("QuickSettings", "Current power-source preference rollback failed.", refreshException);
            }

            _lastKnownQuickSettingsCurrentPowerSourceOnly = ResolveQuickSettingsPowerSourcePreference(
                _lastKnownQuickSettingsCurrentPowerSourceOnly,
                refreshedValue);
            SetQuickSettingsPowerSourceToggle(_lastKnownQuickSettingsCurrentPowerSourceOnly);
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

    internal static bool ResolveQuickSettingsPowerSourcePreference(bool lastKnownValue, bool? refreshedValue) =>
        refreshedValue ?? lastKnownValue;

    private void DeveloperMenuButton_Click(object sender, RoutedEventArgs args)
    {
        DeveloperMenuRequested?.Invoke(this, EventArgs.Empty);
    }
}
