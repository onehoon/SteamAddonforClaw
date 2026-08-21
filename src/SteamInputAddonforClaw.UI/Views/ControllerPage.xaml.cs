using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml;
using System.Threading;
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
    private readonly SemaphoreSlim _wingMutationGate = new(1, 1);
    private long _wingEditRevision;

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
        ApplyWingMappingAvailability();
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
        WingSingleKeyComboBox.ItemsSource = Enum.GetValues<WingHotkeyKey>();
        WingDoubleKeyComboBox.ItemsSource = Enum.GetValues<WingHotkeyKey>();
        WingSingleActionComboBox.SelectedItem = mapping.Single.Action;
        WingDoubleActionComboBox.SelectedItem = mapping.Double.Action;
        WingSingleExecutableTextBox.Text = mapping.Single.Launch.ExecutablePath;
        WingSingleArgumentsTextBox.Text = mapping.Single.Launch.Arguments;
        WingDoubleExecutableTextBox.Text = mapping.Double.Launch.ExecutablePath;
        WingDoubleArgumentsTextBox.Text = mapping.Double.Launch.Arguments;
        WingSingleControlCheckBox.IsChecked = mapping.Single.Hotkey.Modifiers.HasFlag(WingHotkeyModifiers.Control);
        WingSingleShiftCheckBox.IsChecked = mapping.Single.Hotkey.Modifiers.HasFlag(WingHotkeyModifiers.Shift);
        WingSingleAltCheckBox.IsChecked = mapping.Single.Hotkey.Modifiers.HasFlag(WingHotkeyModifiers.Alt);
        WingSingleWindowsCheckBox.IsChecked = mapping.Single.Hotkey.Modifiers.HasFlag(WingHotkeyModifiers.Windows);
        WingSingleKeyComboBox.SelectedItem = mapping.Single.Hotkey.Key;
        WingDoubleControlCheckBox.IsChecked = mapping.Double.Hotkey.Modifiers.HasFlag(WingHotkeyModifiers.Control);
        WingDoubleShiftCheckBox.IsChecked = mapping.Double.Hotkey.Modifiers.HasFlag(WingHotkeyModifiers.Shift);
        WingDoubleAltCheckBox.IsChecked = mapping.Double.Hotkey.Modifiers.HasFlag(WingHotkeyModifiers.Alt);
        WingDoubleWindowsCheckBox.IsChecked = mapping.Double.Hotkey.Modifiers.HasFlag(WingHotkeyModifiers.Windows);
        WingDoubleKeyComboBox.SelectedItem = mapping.Double.Hotkey.Key;
        ApplyWingDetails();
    }

    private void ApplyWingDetails()
    {
        WingSingleHotkeyDetails.Visibility = WingSingleActionComboBox.SelectedItem is WingAction.KeyboardHotkey ? Visibility.Visible : Visibility.Collapsed;
        WingSingleLaunchDetails.Visibility = WingSingleActionComboBox.SelectedItem is WingAction.LaunchApplication ? Visibility.Visible : Visibility.Collapsed;
        WingDoubleHotkeyDetails.Visibility = WingDoubleActionComboBox.SelectedItem is WingAction.KeyboardHotkey ? Visibility.Visible : Visibility.Collapsed;
        WingDoubleLaunchDetails.Visibility = WingDoubleActionComboBox.SelectedItem is WingAction.LaunchApplication ? Visibility.Visible : Visibility.Collapsed;
    }

    private void WingAction_SelectionChanged(object sender, SelectionChangedEventArgs args)
    {
        if (_isLoading || _frontend is null || !_wingMappingAvailable) return;
        ApplyWingDetails();
        QueueWingMappingSave(BuildCurrentWingMapping());
    }

    private void WingConfiguration_Changed(object sender, RoutedEventArgs args) => QueueWingMappingSave(BuildCurrentWingMapping());
    private void WingConfiguration_LostFocus(object sender, RoutedEventArgs args) => QueueWingMappingSave(BuildCurrentWingMapping());

    private WingMappingSettings BuildCurrentWingMapping()
    {
        var single = BuildWingBinding(WingSingleActionComboBox, WingSingleControlCheckBox, WingSingleShiftCheckBox, WingSingleAltCheckBox, WingSingleWindowsCheckBox, WingSingleKeyComboBox, WingSingleExecutableTextBox, WingSingleArgumentsTextBox);
        var doubled = BuildWingBinding(WingDoubleActionComboBox, WingDoubleControlCheckBox, WingDoubleShiftCheckBox, WingDoubleAltCheckBox, WingDoubleWindowsCheckBox, WingDoubleKeyComboBox, WingDoubleExecutableTextBox, WingDoubleArgumentsTextBox);
        return _wingMapping with { Single = single, Double = doubled };
    }

    private async void QueueWingMappingSave(WingMappingSettings desired)
    {
        if (_frontend is null || !_wingMappingAvailable) return;
        if (ContainsBlockedWinG(desired)) { ApplyWingMapping(_wingMapping); return; }
        var revision = Interlocked.Increment(ref _wingEditRevision);
        await _wingMutationGate.WaitAsync();
        try
        {
            var result = await _frontend.SetWingMappingAsync(desired);
            _wingMapping = result.WingMapping ?? WingMappingSettings.Default;
            if (revision == Volatile.Read(ref _wingEditRevision)) ApplyWingMapping(_wingMapping);
        }
        catch (Exception exception)
        {
            AppLog.Warn("Controller", "WING mapping update failed.", exception);
            if (revision == Volatile.Read(ref _wingEditRevision)) ApplyWingMapping(_wingMapping);
        }
        finally { _wingMutationGate.Release(); }
    }

    private static bool ContainsBlockedWinG(WingMappingSettings mapping) =>
        (mapping.Single.Hotkey.Modifiers.HasFlag(WingHotkeyModifiers.Windows) && mapping.Single.Hotkey.Key == WingHotkeyKey.G)
        || (mapping.Double.Hotkey.Modifiers.HasFlag(WingHotkeyModifiers.Windows) && mapping.Double.Hotkey.Key == WingHotkeyKey.G);

    private static WingSlotBinding BuildWingBinding(ComboBox actionBox, CheckBox control, CheckBox shift, CheckBox alt, CheckBox windows, ComboBox keyBox, TextBox executable, TextBox arguments)
    {
        var action = actionBox.SelectedItem is WingAction selected ? selected : WingAction.None;
        var modifiers = (control.IsChecked == true ? WingHotkeyModifiers.Control : WingHotkeyModifiers.None)
            | (shift.IsChecked == true ? WingHotkeyModifiers.Shift : WingHotkeyModifiers.None)
            | (alt.IsChecked == true ? WingHotkeyModifiers.Alt : WingHotkeyModifiers.None)
            | (windows.IsChecked == true ? WingHotkeyModifiers.Windows : WingHotkeyModifiers.None);
        var key = keyBox.SelectedItem is WingHotkeyKey selectedKey ? selectedKey : WingHotkeyKey.None;
        return new WingSlotBinding { Action = action, Hotkey = new WingHotkeyBinding(modifiers, key), Launch = new WingLaunchApplicationBinding(executable.Text, arguments.Text) };
    }

    private void ApplyWingMappingAvailability() => WingMappingCard.IsEnabled = _wingMappingAvailable;

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
