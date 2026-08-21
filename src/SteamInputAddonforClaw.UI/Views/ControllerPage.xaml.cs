using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml;
using SteamInputAddonforClaw.Contracts.Frontend;
using SteamInputAddonforClaw.Contracts.Oem1;
using SteamInputAddonforClaw.Contracts.Wing;

namespace SteamInputAddonforClaw.Views;

public sealed partial class ControllerPage : UserControl
{
    private IAddonFrontendControl? _frontend;
    private bool _isLoading;
    private bool _lastKnownSteamInputRoutingEnabled;
    /// <summary>The last mapping this page knows to be persisted. Kept whole so the remapping
    /// toggle can send back the four bindings UNCHANGED alongside the new switch value -- switching
    /// the feature off must never erase what the user configured.</summary>
    private Oem1MappingSettings _oem1Mapping = Oem1MappingSettings.Default;

    /// <summary>Whether the Center M Button feature exists on this hardware at all, taken verbatim
    /// from the runtime's single startup hardware-support result
    /// (<see cref="FrontendBootstrapSnapshot.Oem1MappingAvailable"/>). Never derived from routing,
    /// Steam, BPM, or the persisted remapping switch. False until Initialize runs, so the card can
    /// never briefly offer a feature this machine does not have.</summary>
    private bool _oem1MappingAvailable;
    private bool _wingMappingAvailable;
    private WingMappingSettings _wingMapping = WingMappingSettings.Default;

    public ControllerPage()
    {
        InitializeComponent();
    }

    /// <summary>Raised when the card (not its toggle) is clicked. The host owns navigation, exactly
    /// as it does for the Settings page's Developer Menu card.</summary>
    internal event EventHandler? CenterMButtonRequested;

    /// <summary>
    /// Review fix (BLOCKER): this page used to persist the toggle itself and only notify the detail
    /// page AFTER a successful save -- but an edit already in flight on the detail page when the
    /// user switched back here had no relationship to this toggle's own write, so either surface's
    /// RPC could land last and silently undo the other. The host now owns the single ordered
    /// mutation path for both surfaces; this page only reports the mapping it wants next.
    /// </summary>
    internal event EventHandler<Oem1MappingSettings>? MappingEditRequested;

    internal void Initialize(IAddonFrontendControl frontend, FrontendBootstrapSnapshot bootstrap)
    {
        _frontend = frontend;
        _isLoading = true;
        SteamInputRoutingToggleSwitch.IsOn = bootstrap.Settings.SteamInputRoutingEnabled;
        _lastKnownSteamInputRoutingEnabled = bootstrap.Settings.SteamInputRoutingEnabled;
        _oem1MappingAvailable = bootstrap.Oem1MappingAvailable;
        _wingMappingAvailable = bootstrap.WingMappingAvailable;
        ApplyOem1MappingAvailability();
        ApplyOem1Mapping(bootstrap.Settings.Oem1Mapping);
        ApplyWingMapping(bootstrap.Settings.WingMapping ?? WingMappingSettings.Default);
        _isLoading = false;
    }

    private void ApplyWingMapping(WingMappingSettings mapping)
    {
        _wingMapping = mapping;
        WingSingleActionComboBox.ItemsSource = Enum.GetValues<WingAction>();
        WingDoubleActionComboBox.ItemsSource = Enum.GetValues<WingAction>();
        WingSingleActionComboBox.SelectedItem = mapping.Single.Action;
        WingDoubleActionComboBox.SelectedItem = mapping.Double.Action;
        WingSingleExecutableTextBox.Text = mapping.Single.Launch.ExecutablePath;
        WingSingleArgumentsTextBox.Text = mapping.Single.Launch.Arguments;
        WingDoubleExecutableTextBox.Text = mapping.Double.Launch.ExecutablePath;
        WingDoubleArgumentsTextBox.Text = mapping.Double.Launch.Arguments;
        ApplyWingDetails();
    }

    private void ApplyWingDetails()
    {
        WingSingleDetails.Visibility = WingSingleActionComboBox.SelectedItem is WingAction.LaunchApplication ? Visibility.Visible : Visibility.Collapsed;
        WingDoubleDetails.Visibility = WingDoubleActionComboBox.SelectedItem is WingAction.LaunchApplication ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void WingAction_SelectionChanged(object sender, SelectionChangedEventArgs args)
    {
        if (_isLoading || _frontend is null || !_wingMappingAvailable) return;
        ApplyWingDetails();
        var single = BuildWingBinding(_wingMapping.Single, WingSingleActionComboBox.SelectedItem is WingAction action ? action : WingAction.None, WingSingleExecutableTextBox, WingSingleArgumentsTextBox);
        var doubled = BuildWingBinding(_wingMapping.Double, WingDoubleActionComboBox.SelectedItem is WingAction doubleAction ? doubleAction : WingAction.None, WingDoubleExecutableTextBox, WingDoubleArgumentsTextBox);
        try { var result = await _frontend.SetWingMappingAsync(_wingMapping with { Single = single, Double = doubled }); ApplyWingMapping(result.WingMapping ?? WingMappingSettings.Default); }
        catch (Exception exception) { AppLog.Warn("Controller", "WING mapping update failed.", exception); ApplyWingMapping(_wingMapping); }
    }

    private static WingSlotBinding BuildWingBinding(WingSlotBinding previous, WingAction action, TextBox executable, TextBox arguments) =>
        previous with { Action = action, Launch = new WingLaunchApplicationBinding(executable.Text, arguments.Text) };

    /// <summary>
    /// Unsupported-hardware presentation, composed from the patterns already in this app: the card
    /// stays visible, its toggle is disabled and replaced by the plain "Unavailable" text badge, and
    /// the navigation chevron/click is removed so the detail page is unreachable. Nothing about the
    /// persisted mapping is changed -- this is presentation only.
    /// </summary>
    private void ApplyOem1MappingAvailability()
    {
        CenterMRemappingToggleSwitch.IsEnabled = _oem1MappingAvailable;
        CenterMRemappingToggleSwitch.Visibility = _oem1MappingAvailable ? Visibility.Visible : Visibility.Collapsed;
        CenterMUnavailableText.Visibility = _oem1MappingAvailable ? Visibility.Collapsed : Visibility.Visible;
        CenterMButtonCard.IsClickEnabled = _oem1MappingAvailable;
    }

    /// <summary>Lets the host push back a mapping saved on the detail page, so returning to this
    /// page never shows a stale toggle.</summary>
    internal void ApplyOem1Mapping(Oem1MappingSettings mapping)
    {
        var wasLoading = _isLoading;
        _isLoading = true;
        _oem1Mapping = mapping;
        CenterMRemappingToggleSwitch.IsOn = mapping.RemappingEnabled;
        _isLoading = wasLoading;
    }

    /// <summary>IsClickEnabled is already false when the feature is unavailable; the guard is the
    /// authoritative one, so navigation stays impossible even if a future edit re-enables the
    /// chevron.</summary>
    private void CenterMButtonCard_Click(object sender, RoutedEventArgs args)
    {
        if (!_oem1MappingAvailable) return;
        CenterMButtonRequested?.Invoke(this, EventArgs.Empty);
    }

    private void CenterMRemappingToggleSwitch_Toggled(object sender, RoutedEventArgs args)
    {
        if (_isLoading) return;
        MappingEditRequested?.Invoke(this, _oem1Mapping with { RemappingEnabled = CenterMRemappingToggleSwitch.IsOn });
    }

    private async void SteamInputRoutingToggleSwitch_Toggled(object sender, RoutedEventArgs args)
    {
        if (_isLoading || _frontend is null) return;
        try
        {
            var result = await _frontend.SetSteamInputRoutingEnabledAsync(SteamInputRoutingToggleSwitch.IsOn);
            _lastKnownSteamInputRoutingEnabled = result.SteamInputRoutingEnabled;
            SetRouteToggle(_lastKnownSteamInputRoutingEnabled);
        }
        catch (Exception exception)
        {
            AppLog.Warn("Controller", "Steam Input routing update failed.", exception);
            try
            {
                var bootstrap = await _frontend.GetBootstrapAsync();
                _lastKnownSteamInputRoutingEnabled = bootstrap.Settings.SteamInputRoutingEnabled;
                SetRouteToggle(_lastKnownSteamInputRoutingEnabled);
            }
            catch (Exception refreshException)
            {
                AppLog.Warn("Controller", "Steam Input routing state refresh failed.", refreshException);
                SetRouteToggle(_lastKnownSteamInputRoutingEnabled);
            }
        }
    }

    private void SetRouteToggle(bool value)
    {
        _isLoading = true;
        SteamInputRoutingToggleSwitch.IsOn = value;
        _isLoading = false;
    }
}
