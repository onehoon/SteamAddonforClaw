using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SteamInputAddonforClaw.Contracts.Frontend;
using SteamInputAddonforClaw.FrontendTransport;

namespace SteamInputAddonforClaw.Views;

public sealed partial class QuickOverlayPage : UserControl
{
    private IAddonFrontendControl? _frontend;
    private bool _applyingPowerSourcePreference;
    private bool _powerSourceMutationInProgress;
    private bool _powerSourceRefreshInProgress;
    private bool _discardInFlightPowerSourceRefresh;
    private bool _lastKnownQuickSettingsCurrentPowerSourceOnly;

    public QuickOverlayPage()
    {
        InitializeComponent();
    }

    internal void Initialize(FrontendBootstrapSnapshot bootstrap, IAddonFrontendControl frontend)
    {
        _frontend = frontend ?? throw new ArgumentNullException(nameof(frontend));
        _lastKnownQuickSettingsCurrentPowerSourceOnly = bootstrap.Settings.QuickSettingsCurrentPowerSourceOnly;
        SetQuickSettingsPowerSourceToggle(_lastKnownQuickSettingsCurrentPowerSourceOnly);
    }

    internal void Activate() => RequestRefresh();

    internal void RequestRefresh() => _ = RefreshQuickSettingsPowerSourcePreferenceAsync();

    private async Task RefreshQuickSettingsPowerSourcePreferenceAsync()
    {
        if (_frontend is null || _powerSourceMutationInProgress || _powerSourceRefreshInProgress) return;

        _powerSourceRefreshInProgress = true;
        try
        {
            var bootstrap = await _frontend.GetBootstrapAsync().ConfigureAwait(true);
            if (_powerSourceMutationInProgress || _discardInFlightPowerSourceRefresh)
                return;

            _lastKnownQuickSettingsCurrentPowerSourceOnly = bootstrap.Settings.QuickSettingsCurrentPowerSourceOnly;
            SetQuickSettingsPowerSourceToggle(_lastKnownQuickSettingsCurrentPowerSourceOnly);
        }
        catch (Exception exception)
        {
            AppLog.Warn("QuickSettings", "Current power-source preference refresh failed.", exception);
        }
        finally
        {
            _powerSourceRefreshInProgress = false;
            _discardInFlightPowerSourceRefresh = false;
        }
    }

    private void SetQuickSettingsPowerSourceToggle(bool enabled)
    {
        _applyingPowerSourcePreference = true;
        try { QuickSettingsPowerSourceToggleSwitch.IsOn = enabled; }
        finally { _applyingPowerSourcePreference = false; }
    }

    private async void QuickSettingsPowerSourceToggleSwitch_Toggled(object sender, RoutedEventArgs args)
    {
        if (_applyingPowerSourcePreference || _frontend is null || _powerSourceMutationInProgress) return;

        // A snapshot started before this edit must not overwrite its authoritative mutation result.
        _discardInFlightPowerSourceRefresh = _powerSourceRefreshInProgress;
        _powerSourceMutationInProgress = true;
        QuickSettingsPowerSourceToggleSwitch.IsEnabled = false;
        try
        {
            var settings = await _frontend.SetQuickSettingsCurrentPowerSourceOnlyAsync(
                QuickSettingsPowerSourceToggleSwitch.IsOn).ConfigureAwait(true);
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
        finally
        {
            _powerSourceMutationInProgress = false;
            QuickSettingsPowerSourceToggleSwitch.IsEnabled = true;
        }
    }

    internal static bool ResolveQuickSettingsPowerSourcePreference(bool lastKnownValue, bool? refreshedValue) =>
        refreshedValue ?? lastKnownValue;
}
