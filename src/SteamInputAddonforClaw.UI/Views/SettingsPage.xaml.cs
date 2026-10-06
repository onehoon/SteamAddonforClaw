using CommunityToolkit.WinUI.Controls;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SteamInputAddonforClaw.Contracts.Frontend;
using SteamInputAddonforClaw.FrontendTransport;

namespace SteamInputAddonforClaw.Views;

public sealed partial class SettingsPage : UserControl
{
    private IAddonFrontendControl? _frontend;
    private FrontendUpdateSnapshot _updateSnapshot = FrontendUpdateSnapshot.Unavailable;
    private int _updateOperationInProgress;
    private FrontendGamingHomeSnapshot _gamingHomeSnapshot = FrontendGamingHomeSnapshot.Unavailable("Windows Gaming Full Screen Experience is unavailable.");
    private int _gamingHomeMutationInProgress;
    private bool _applyingGamingHomeState;
    private bool _applyingQuickSettingsPowerSourcePreference;
    private bool _lastKnownQuickSettingsCurrentPowerSourceOnly;
    private bool IsStartupWritable =>
        _gamingHomeSnapshot.Available
        && _gamingHomeSnapshot.Selection is FrontendGamingHomeSelection.Xbox or FrontendGamingHomeSelection.SteamBigPicture
        && Volatile.Read(ref _gamingHomeMutationInProgress) == 0;
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
        _ = RefreshGamingHomeAsync();
    }

    internal void RequestAppUpdateRefresh() => _ = RefreshAppUpdateAsync();
    internal void RequestGamingHomeRefresh() => _ = RefreshGamingHomeAsync();

    private async Task RefreshAppUpdateAsync()
    {
        if (_frontend is null || Volatile.Read(ref _updateOperationInProgress) != 0) return;
        try { RenderAppUpdate(await _frontend.CaptureAppUpdateAsync().ConfigureAwait(true)); }
        catch (Exception exception)
        {
            if (exception is FrontendTransportException
                && exception is not FrontendProtocolException
                && exception is not FrontendRemoteException)
            {
                AppLog.Debug("Update", "Main UI update state refresh skipped because Runtime transport is unavailable.",
                    ("Reason", exception.Message));
            }
            else
            {
                AppLog.Warn("Update", "Main UI update state refresh failed.", exception);
            }
        }
    }

    private void RenderAppUpdate(FrontendUpdateSnapshot snapshot)
    {
        _updateSnapshot = snapshot;
        UpdateCard.Description = snapshot.Message;
        UpdateButton.Content = snapshot.CanInstall ? "Install update" : "Check";
        UpdateButton.IsEnabled = snapshot.CanCheck || snapshot.CanInstall;
    }

    private async Task RefreshGamingHomeAsync()
    {
        if (_frontend is null || Volatile.Read(ref _gamingHomeMutationInProgress) != 0) return;
        try { RenderGamingHome(await _frontend.CaptureGamingHomeAsync().ConfigureAwait(true)); }
        catch (Exception exception)
        {
            if (exception is FrontendTransportException
                && exception is not FrontendProtocolException
                && exception is not FrontendRemoteException)
            {
                AppLog.Debug("GamingHome", "Main UI Gaming Home state refresh skipped because Runtime transport is unavailable.",
                    ("Reason", exception.Message));
            }
            else
            {
                AppLog.Warn("GamingHome", "Main UI Gaming Home state refresh failed.", exception);
            }
            RenderGamingHome(FrontendGamingHomeSnapshot.Unavailable("Windows Gaming Full Screen Experience could not be verified."));
        }
    }

    private void RenderGamingHome(FrontendGamingHomeSnapshot snapshot)
    {
        _gamingHomeSnapshot = snapshot;
        _applyingGamingHomeState = true;
        try
        {
            GamingHomeComboBox.SelectedItem = snapshot.Available
                ? FindGamingHomeSelectionItem(snapshot.Selection)
                : null;
            GamingHomeStartupToggleSwitch.IsOn = snapshot.StartupEnabled;
        }
        finally
        {
            _applyingGamingHomeState = false;
        }

        GamingHomeExpander.Description = snapshot.Available
            ? snapshot.Selection == FrontendGamingHomeSelection.Other
                ? "The current Windows app is outside Addon control. Choose Off, Xbox, or Steam Big Picture to replace it."
                : "Choose the home app used by Windows gaming full screen experience."
            : snapshot.UnavailableReason ?? "The current Windows setting could not be verified.";
        GamingHomeStartupCard.Description = snapshot.Available && snapshot.Selection == FrontendGamingHomeSelection.Other
            ? "The current value is shown but startup behavior for this app is not managed by the Addon."
            : "Start Windows directly in the selected gaming home.";
        UpdateGamingHomeControlStates();
    }

    private static string SelectionTag(FrontendGamingHomeSelection selection) => selection switch
    {
        FrontendGamingHomeSelection.None => "None",
        FrontendGamingHomeSelection.Xbox => "Xbox",
        FrontendGamingHomeSelection.SteamBigPicture => "SteamBigPicture",
        FrontendGamingHomeSelection.Other => "Other",
        _ => string.Empty,
    };

    private ComboBoxItem? FindGamingHomeSelectionItem(FrontendGamingHomeSelection selection) =>
        GamingHomeComboBox.Items.OfType<ComboBoxItem>()
            .FirstOrDefault(item => string.Equals(item.Tag as string, SelectionTag(selection), StringComparison.Ordinal));

    private void UpdateGamingHomeControlStates()
    {
        var mutationInProgress = Volatile.Read(ref _gamingHomeMutationInProgress) != 0;
        GamingHomeComboBox.IsEnabled = _gamingHomeSnapshot.Available && !mutationInProgress;
        GamingHomeOtherSelectionItem.IsEnabled = _gamingHomeSnapshot.Selection == FrontendGamingHomeSelection.Other;
        GamingHomeStartupToggleSwitch.IsEnabled = IsStartupWritable;
    }

    private async void GamingHomeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs args)
    {
        if (_applyingGamingHomeState || _frontend is null || !_gamingHomeSnapshot.Available) return;
        if (GamingHomeComboBox.SelectedItem is not ComboBoxItem { Tag: string selectionText }
            || !Enum.TryParse<FrontendGamingHomeSelection>(selectionText, out var selection))
            return;
        if (selection == FrontendGamingHomeSelection.Other || selection == _gamingHomeSnapshot.Selection)
        {
            RenderGamingHome(_gamingHomeSnapshot);
            return;
        }

        if (Interlocked.Exchange(ref _gamingHomeMutationInProgress, 1) != 0) return;
        UpdateGamingHomeControlStates();
        await RunGamingHomeMutationAsync(
            () => _frontend.SetGamingHomeSelectionAsync(selection),
            "The Gaming Home selection could not be changed.",
            startupMutation: false);
    }

    private async void GamingHomeStartupToggleSwitch_Toggled(object sender, RoutedEventArgs args)
    {
        if (_applyingGamingHomeState || _frontend is null || !_gamingHomeSnapshot.Available) return;
        if (_gamingHomeSnapshot.Selection is not (FrontendGamingHomeSelection.Xbox or FrontendGamingHomeSelection.SteamBigPicture))
        {
            RenderGamingHome(_gamingHomeSnapshot);
            return;
        }

        if (Interlocked.Exchange(ref _gamingHomeMutationInProgress, 1) != 0) return;
        UpdateGamingHomeControlStates();
        await RunGamingHomeMutationAsync(
            () => _frontend.SetGamingHomeStartupAsync(GamingHomeStartupToggleSwitch.IsOn),
            "The Gaming Home startup setting could not be changed.",
            startupMutation: true);
    }

    private async Task RunGamingHomeMutationAsync(
        Func<Task<FrontendGamingHomeMutationResult>> mutation,
        string fallbackFailureMessage,
        bool startupMutation)
    {
        try
        {
            var result = await mutation().ConfigureAwait(true);
            RenderGamingHome(result.Snapshot);
            if (!result.Succeeded)
                SetGamingHomeFailureDescription(startupMutation, result.FailureMessage ?? fallbackFailureMessage);
        }
        catch (Exception exception)
        {
            AppLog.Warn("GamingHome", "Main UI Gaming Home mutation failed.", exception);
            try
            {
                RenderGamingHome(await _frontend!.CaptureGamingHomeAsync().ConfigureAwait(true));
            }
            catch (Exception refreshException)
            {
                AppLog.Warn("GamingHome", "Main UI Gaming Home state refresh after mutation failure failed.", refreshException);
                RenderGamingHome(FrontendGamingHomeSnapshot.Unavailable("Windows Gaming Full Screen Experience could not be verified."));
            }
            SetGamingHomeFailureDescription(startupMutation, fallbackFailureMessage);
        }
        finally
        {
            Volatile.Write(ref _gamingHomeMutationInProgress, 0);
            UpdateGamingHomeControlStates();
        }
    }

    private void SetGamingHomeFailureDescription(bool startupMutation, string message)
    {
        if (startupMutation)
            GamingHomeStartupCard.Description = message;
        else
            GamingHomeExpander.Description = message;
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
