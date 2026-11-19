using CommunityToolkit.WinUI.Controls;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SteamInputAddonforClaw.Contracts.Frontend;
using SteamInputAddonforClaw.FrontendTransport;
using SteamInputAddonforClaw.UI;
using System.Diagnostics;

namespace SteamInputAddonforClaw.Views;

public sealed partial class SettingsPage : UserControl
{
    private IAddonFrontendControl? _frontend;
    private bool _active;
    private bool _applyingQuickSettingsPowerSourceOnly;
    private bool _applyingSimplifiedTdpControls;
    private bool _applyingSteamBigPictureScaling;
    private bool _quickSettingsPreferencesRefreshInProgress;
    private bool _quickSettingsPreferencesRefreshPending;
    private bool _discardInFlightPowerSourceRefresh;
    private bool _discardInFlightSimplifiedTdpRefresh;
    private bool _discardInFlightSteamBigPictureScalingRefresh;
    private bool _powerSourceMutationInProgress;
    private bool _simplifiedTdpMutationInProgress;
    private bool _steamBigPictureScalingMutationInProgress;
    private bool _lastKnownQuickSettingsPowerSourceOnly;
    private bool _lastKnownSimplifiedTdpControls;
    private bool _lastKnownSteamBigPictureScalingTo100Percent;
    private FrontendUpdateSnapshot _updateSnapshot = FrontendUpdateSnapshot.Unavailable;
    private int _updateOperationInProgress;
    private FrontendGamingHomeSnapshot _gamingHomeSnapshot = FrontendGamingHomeSnapshot.Unavailable(
        MainUiStrings.Get("Settings_GamingHome_InitiallyUnavailable", "Windows Gaming Full Screen Experience is unavailable."));
    private int _gamingHomeMutationInProgress;
    private bool _applyingGamingHomeState;
    private bool _isInitializingLogLevel;
    private bool _isApplyingAppThemeSelection;
    private FrontendLogLevel _lastKnownLogLevel;
    private string _logDirectoryPath = string.Empty;
    private const string DefaultAppThemeDescription = "Choose the main app appearance.";
    private bool IsStartupWritable =>
        _gamingHomeSnapshot.Available
        && _gamingHomeSnapshot.Selection is FrontendGamingHomeSelection.Xbox or FrontendGamingHomeSelection.SteamBigPicture
        && Volatile.Read(ref _gamingHomeMutationInProgress) == 0;
    public event EventHandler? DeveloperMenuRequested;
    internal event Action<MainUiTheme>? AppThemeSelectionRequested;
    internal event Action<FrontendUpdateSnapshot>? AppUpdateSnapshotRendered;

    public SettingsPage()
    {
        InitializeComponent();
    }

    internal void Initialize(FrontendBootstrapSnapshot bootstrap, IAddonFrontendControl frontend, MainUiTheme appTheme)
    {
        _frontend = frontend ?? throw new ArgumentNullException(nameof(frontend));
        _lastKnownQuickSettingsPowerSourceOnly = bootstrap.Settings.QuickSettingsCurrentPowerSourceOnly;
        _lastKnownSimplifiedTdpControls = bootstrap.Settings.SimplifiedTdpControls;
        _lastKnownSteamBigPictureScalingTo100Percent = bootstrap.Settings.SteamBigPictureScalingTo100Percent;
        SetQuickSettingsPowerSourceOnly(_lastKnownQuickSettingsPowerSourceOnly);
        SetSimplifiedTdpControls(_lastKnownSimplifiedTdpControls);
        SetSteamBigPictureScaling(_lastKnownSteamBigPictureScalingTo100Percent);
        UpdateQuickSettingsPreferenceControlStates();
        DeveloperMenuCard.Visibility = GetDeveloperMenuCardVisibility(bootstrap.Settings.DeveloperMenuEnabled);
        _lastKnownLogLevel = bootstrap.Settings.LogLevel;
        _logDirectoryPath = bootstrap.LogDirectoryPath;
        SetAppThemeSelection(appTheme);
        SetLogLevel(_lastKnownLogLevel);
        _ = RefreshAppUpdateAsync();
        _ = RefreshGamingHomeAsync();
    }

    internal void RequestAppUpdateRefresh() => _ = RefreshAppUpdateAsync();
    internal void RequestGamingHomeRefresh() => _ = RefreshGamingHomeAsync();
    internal void Activate()
    {
        _active = true;
        RequestQuickSettingsPreferencesRefresh();
    }

    internal void Deactivate()
    {
        _active = false;
        _quickSettingsPreferencesRefreshPending = false;
    }

    internal void RequestQuickSettingsPreferencesRefresh()
    {
        if (!_active) return;
        if (_quickSettingsPreferencesRefreshInProgress || _powerSourceMutationInProgress || _simplifiedTdpMutationInProgress || _steamBigPictureScalingMutationInProgress)
        {
            _quickSettingsPreferencesRefreshPending = true;
            if (_quickSettingsPreferencesRefreshInProgress)
            {
                _discardInFlightPowerSourceRefresh = true;
                _discardInFlightSimplifiedTdpRefresh = true;
                _discardInFlightSteamBigPictureScalingRefresh = true;
            }
            return;
        }

        _ = RefreshQuickSettingsPreferencesAsync();
    }

    private async Task RefreshQuickSettingsPreferencesAsync()
    {
        if (!_active || _frontend is null) return;
        if (_quickSettingsPreferencesRefreshInProgress || _powerSourceMutationInProgress || _simplifiedTdpMutationInProgress || _steamBigPictureScalingMutationInProgress)
        {
            _quickSettingsPreferencesRefreshPending = true;
            return;
        }
        _quickSettingsPreferencesRefreshInProgress = true;
        try
        {
            var bootstrap = await _frontend.GetBootstrapAsync().ConfigureAwait(true);
            if (!_powerSourceMutationInProgress && !_discardInFlightPowerSourceRefresh)
            {
                _lastKnownQuickSettingsPowerSourceOnly = bootstrap.Settings.QuickSettingsCurrentPowerSourceOnly;
                SetQuickSettingsPowerSourceOnly(_lastKnownQuickSettingsPowerSourceOnly);
            }
            if (!_simplifiedTdpMutationInProgress && !_discardInFlightSimplifiedTdpRefresh)
            {
                _lastKnownSimplifiedTdpControls = bootstrap.Settings.SimplifiedTdpControls;
                SetSimplifiedTdpControls(_lastKnownSimplifiedTdpControls);
            }
            if (!_steamBigPictureScalingMutationInProgress && !_discardInFlightSteamBigPictureScalingRefresh)
            {
                _lastKnownSteamBigPictureScalingTo100Percent = bootstrap.Settings.SteamBigPictureScalingTo100Percent;
                SetSteamBigPictureScaling(_lastKnownSteamBigPictureScalingTo100Percent);
            }
        }
        catch (Exception exception)
        {
            AppLog.Warn("Settings", "Quick Settings preferences refresh failed.", exception);
        }
        finally
        {
            _quickSettingsPreferencesRefreshInProgress = false;
            _discardInFlightPowerSourceRefresh = false;
            _discardInFlightSimplifiedTdpRefresh = false;
            _discardInFlightSteamBigPictureScalingRefresh = false;
            DrainPendingQuickSettingsPreferencesRefresh();
        }
    }

    private async void QuickSettingsPowerSourceOnlyToggleSwitch_Toggled(object sender, RoutedEventArgs args)
    {
        if (_applyingQuickSettingsPowerSourceOnly || _frontend is null || _powerSourceMutationInProgress) return;
        _discardInFlightPowerSourceRefresh = _quickSettingsPreferencesRefreshInProgress;
        _powerSourceMutationInProgress = true;
        UpdateQuickSettingsPreferenceControlStates();
        try
        {
            var settings = await _frontend.SetQuickSettingsCurrentPowerSourceOnlyAsync(
                QuickSettingsPowerSourceOnlyToggleSwitch.IsOn).ConfigureAwait(true);
            _lastKnownQuickSettingsPowerSourceOnly = settings.QuickSettingsCurrentPowerSourceOnly;
            SetQuickSettingsPowerSourceOnly(_lastKnownQuickSettingsPowerSourceOnly);
        }
        catch (Exception exception)
        {
            AppLog.Warn("Settings", "Current power-source preference update failed.", exception);
            bool? refreshedValue = null;
            try { refreshedValue = (await _frontend.GetBootstrapAsync().ConfigureAwait(true)).Settings.QuickSettingsCurrentPowerSourceOnly; }
            catch (Exception readbackException) { AppLog.Warn("Settings", "Current power-source preference readback failed; keeping the last confirmed value.", readbackException); }
            _lastKnownQuickSettingsPowerSourceOnly = ResolvePreference(_lastKnownQuickSettingsPowerSourceOnly, refreshedValue);
            SetQuickSettingsPowerSourceOnly(_lastKnownQuickSettingsPowerSourceOnly);
        }
        finally
        {
            _powerSourceMutationInProgress = false;
            UpdateQuickSettingsPreferenceControlStates();
            DrainPendingQuickSettingsPreferencesRefresh();
        }
    }

    private async void SimplifiedTdpControlsToggleSwitch_Toggled(object sender, RoutedEventArgs args)
    {
        if (_applyingSimplifiedTdpControls || _frontend is null || _simplifiedTdpMutationInProgress) return;
        _discardInFlightSimplifiedTdpRefresh = _quickSettingsPreferencesRefreshInProgress;
        _simplifiedTdpMutationInProgress = true;
        UpdateQuickSettingsPreferenceControlStates();
        try
        {
            var settings = await _frontend.SetSimplifiedTdpControlsAsync(
                SimplifiedTdpControlsToggleSwitch.IsOn).ConfigureAwait(true);
            _lastKnownSimplifiedTdpControls = settings.SimplifiedTdpControls;
            SetSimplifiedTdpControls(_lastKnownSimplifiedTdpControls);
        }
        catch (Exception exception)
        {
            AppLog.Warn("Settings", "Simplified TDP preference update failed.", exception);
            bool? refreshedValue = null;
            try { refreshedValue = (await _frontend.GetBootstrapAsync().ConfigureAwait(true)).Settings.SimplifiedTdpControls; }
            catch (Exception readbackException) { AppLog.Warn("Settings", "Simplified TDP preference readback failed; keeping the last confirmed value.", readbackException); }
            _lastKnownSimplifiedTdpControls = ResolvePreference(_lastKnownSimplifiedTdpControls, refreshedValue);
            SetSimplifiedTdpControls(_lastKnownSimplifiedTdpControls);
        }
        finally
        {
            _simplifiedTdpMutationInProgress = false;
            UpdateQuickSettingsPreferenceControlStates();
            DrainPendingQuickSettingsPreferencesRefresh();
        }
    }

    private async void SteamBigPictureScalingToggleSwitch_Toggled(object sender, RoutedEventArgs args)
    {
        if (_applyingSteamBigPictureScaling || _frontend is null || _steamBigPictureScalingMutationInProgress) return;
        _discardInFlightSteamBigPictureScalingRefresh = _quickSettingsPreferencesRefreshInProgress;
        _steamBigPictureScalingMutationInProgress = true;
        UpdateQuickSettingsPreferenceControlStates();
        try
        {
            var settings = await _frontend.SetSteamBigPictureScalingTo100PercentAsync(
                SteamBigPictureScalingToggleSwitch.IsOn).ConfigureAwait(true);
            _lastKnownSteamBigPictureScalingTo100Percent = settings.SteamBigPictureScalingTo100Percent;
            SetSteamBigPictureScaling(_lastKnownSteamBigPictureScalingTo100Percent);
        }
        catch (Exception exception)
        {
            AppLog.Warn("Settings", "Steam Big Picture display scaling preference update failed.", exception);
            bool? refreshedValue = null;
            try { refreshedValue = (await _frontend.GetBootstrapAsync().ConfigureAwait(true)).Settings.SteamBigPictureScalingTo100Percent; }
            catch (Exception readbackException) { AppLog.Warn("Settings", "Steam Big Picture display scaling preference readback failed; keeping the last confirmed value.", readbackException); }
            _lastKnownSteamBigPictureScalingTo100Percent = ResolvePreference(_lastKnownSteamBigPictureScalingTo100Percent, refreshedValue);
            SetSteamBigPictureScaling(_lastKnownSteamBigPictureScalingTo100Percent);
        }
        finally
        {
            _steamBigPictureScalingMutationInProgress = false;
            UpdateQuickSettingsPreferenceControlStates();
            DrainPendingQuickSettingsPreferencesRefresh();
        }
    }

    private void DrainPendingQuickSettingsPreferencesRefresh()
    {
        if (!_quickSettingsPreferencesRefreshPending || _quickSettingsPreferencesRefreshInProgress
            || _powerSourceMutationInProgress || _simplifiedTdpMutationInProgress || _steamBigPictureScalingMutationInProgress || !_active)
            return;

        _quickSettingsPreferencesRefreshPending = false;
        RequestQuickSettingsPreferencesRefresh();
    }

    private void SetQuickSettingsPowerSourceOnly(bool enabled)
    {
        _applyingQuickSettingsPowerSourceOnly = true;
        try { QuickSettingsPowerSourceOnlyToggleSwitch.IsOn = enabled; }
        finally { _applyingQuickSettingsPowerSourceOnly = false; }
    }

    private void SetSimplifiedTdpControls(bool enabled)
    {
        _applyingSimplifiedTdpControls = true;
        try { SimplifiedTdpControlsToggleSwitch.IsOn = enabled; }
        finally { _applyingSimplifiedTdpControls = false; }
    }

    private void SetSteamBigPictureScaling(bool enabled)
    {
        _applyingSteamBigPictureScaling = true;
        try { SteamBigPictureScalingToggleSwitch.IsOn = enabled; }
        finally { _applyingSteamBigPictureScaling = false; }
    }

    private void UpdateQuickSettingsPreferenceControlStates()
    {
        QuickSettingsPowerSourceOnlyToggleSwitch.IsEnabled = !_powerSourceMutationInProgress;
        SimplifiedTdpControlsToggleSwitch.IsEnabled = !_simplifiedTdpMutationInProgress;
        SteamBigPictureScalingToggleSwitch.IsEnabled = !_steamBigPictureScalingMutationInProgress;
    }

    internal static bool ResolvePreference(bool lastKnownValue, bool? refreshedValue) => refreshedValue ?? lastKnownValue;

    internal void SetAppThemeSelection(MainUiTheme theme, string? failureDescription = null)
    {
        _isApplyingAppThemeSelection = true;
        try
        {
            AppThemeComboBox.SelectedItem = AppThemeComboBox.Items
                .OfType<ComboBoxItem>()
                .FirstOrDefault(item => string.Equals(item.Tag as string, theme.ToString(), StringComparison.Ordinal));
            AppThemeCard.Description = failureDescription ?? GetDefaultAppThemeDescription();
        }
        finally
        {
            _isApplyingAppThemeSelection = false;
        }
    }

    private static string GetDefaultAppThemeDescription()
    {
        return MainUiStrings.Get("Settings_AppTheme_Description", DefaultAppThemeDescription);
    }

    private void AppThemeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs args)
    {
        if (_isApplyingAppThemeSelection
            || AppThemeComboBox.SelectedItem is not ComboBoxItem { Tag: string value }
            || !Enum.TryParse<MainUiTheme>(value, ignoreCase: false, out var theme)
            || !Enum.IsDefined(theme))
        {
            return;
        }

        AppThemeSelectionRequested?.Invoke(theme);
    }

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
        UpdateButton.Content = snapshot.CanInstall
            ? MainUiStrings.Get("Settings_Update_InstallAction", "Install update")
            : MainUiStrings.Get("Settings_UpdateButton/Content", "Check");
        UpdateButton.IsEnabled = snapshot.CanCheck || snapshot.CanInstall;
        AppUpdateSnapshotRendered?.Invoke(snapshot);
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
            RenderGamingHome(FrontendGamingHomeSnapshot.Unavailable(
                MainUiStrings.Get("Settings_GamingHome_VerifyFailed", "Windows Gaming Full Screen Experience could not be verified.")));
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
                ? MainUiStrings.Get(
                    "Settings_GamingHome_OtherDescription",
                    "The current Windows app is outside Addon control. Choose Off, Xbox, or Steam Big Picture to replace it.")
                : MainUiStrings.Get(
                    "Settings_GamingHomeExpander/Description",
                    "Choose the home app used by XBOX Mode")
            : snapshot.UnavailableReason ?? MainUiStrings.Get(
                "Settings_GamingHome_UnavailableDescription",
                "The current Windows setting could not be verified.");
        GamingHomeStartupCard.Description = snapshot.Available && snapshot.Selection == FrontendGamingHomeSelection.Other
            ? MainUiStrings.Get(
                "Settings_GamingHome_OtherStartupDescription",
                "The current value is shown but startup behavior for this app is not managed by the Addon.")
            : MainUiStrings.Get(
                "Settings_GamingHomeStartupCard/Description",
                "Start Windows directly in the selected gaming home.");
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
            MainUiStrings.Get("Settings_GamingHome_SelectionChangeFailed", "The Gaming Home selection could not be changed."),
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
            MainUiStrings.Get("Settings_GamingHome_StartupChangeFailed", "The Gaming Home startup setting could not be changed."),
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
                RenderGamingHome(FrontendGamingHomeSnapshot.Unavailable(
                    MainUiStrings.Get("Settings_GamingHome_VerifyFailed", "Windows Gaming Full Screen Experience could not be verified.")));
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

            RenderAppUpdate(new(
                FrontendUpdateState.Checking,
                MainUiStrings.Get("Settings_Update_Checking", "Checking for updates…")));
            RenderAppUpdate(await _frontend.CheckAndDownloadAppUpdateAsync().ConfigureAwait(true));
        }
        catch (Exception exception)
        {
            AppLog.Warn("Update", "Main UI update action failed.", exception);
            RenderAppUpdate(new(
                FrontendUpdateState.Failed,
                MainUiStrings.Get("Settings_Update_OperationFailed", "The update operation failed. Try again.")));
        }
        finally
        {
            Volatile.Write(ref _updateOperationInProgress, 0);
            await RefreshAppUpdateAsync().ConfigureAwait(true);
        }
    }

    internal static int GetLogLevelSelectionIndex(FrontendLogLevel level) => level switch
    {
        FrontendLogLevel.Info => 1,
        FrontendLogLevel.Debug => 2,
        _ => 0,
    };

    private void SetLogLevel(FrontendLogLevel level)
    {
        _isInitializingLogLevel = true;
        LogLevelComboBox.SelectedIndex = GetLogLevelSelectionIndex(level);
        _isInitializingLogLevel = false;
    }

    private async void LogLevelComboBox_SelectionChanged(object sender, SelectionChangedEventArgs args)
    {
        if (_isInitializingLogLevel
            || _frontend is null
            || LogLevelComboBox.SelectedItem is not ComboBoxItem item
            || item.Content is not string value)
        {
            return;
        }

        var level = value switch
        {
            "Debug" => FrontendLogLevel.Debug,
            "Info" => FrontendLogLevel.Info,
            _ => FrontendLogLevel.Off,
        };

        try
        {
            var result = await _frontend.SetLogLevelAsync(level).ConfigureAwait(true);
            _lastKnownLogLevel = result.LogLevel;
            UiLog.SetMinimumLevel(result.LogLevel);
            SetLogLevel(_lastKnownLogLevel);
        }
        catch (Exception exception)
        {
            AppLog.Warn("Settings", "Log level update failed.", exception);
            await RefreshLoggingStateAsync().ConfigureAwait(true);
        }
    }

    private async Task RefreshLoggingStateAsync()
    {
        try
        {
            var bootstrap = await _frontend!.GetBootstrapAsync().ConfigureAwait(true);
            _lastKnownLogLevel = bootstrap.Settings.LogLevel;
            UiLog.SetMinimumLevel(_lastKnownLogLevel);
            _logDirectoryPath = bootstrap.LogDirectoryPath;
            SetLogLevel(_lastKnownLogLevel);
        }
        catch (Exception refreshException)
        {
            AppLog.Warn("Settings", "Logging state refresh failed.", refreshException);
            SetLogLevel(_lastKnownLogLevel);
        }
    }

    private void OpenLogFolderButton_Click(object sender, RoutedEventArgs args)
    {
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{_logDirectoryPath}\"") { UseShellExecute = true });
        }
        catch (Exception exception)
        {
            AppLog.Warn("Settings", "Log folder could not be opened.", exception);
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
            Text = MainUiStrings.Format(
                "Settings_RequiredComponents_ReadyCount",
                "{0} of {1} ready",
                readyCount,
                components.Length),
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
