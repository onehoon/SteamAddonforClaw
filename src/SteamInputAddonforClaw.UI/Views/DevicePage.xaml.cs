using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Dispatching;
using SteamInputAddonforClaw.Contracts.DeviceProfiles;
using SteamInputAddonforClaw.Contracts.Frontend;

namespace SteamInputAddonforClaw.Views;

/// <summary>
/// Projects the Runtime-owned CPU Boost feature (PR276's <c>CpuBoostRuntime</c>, reached only through
/// <see cref="IAddonFrontendControl"/>) onto two independent AC/DC dropdowns. Owns transient
/// presentation state only (selection-suppression flags, the InfoBar) -- it never persists anything
/// itself and always renders from the latest frontend snapshot (work order PR277 section 15).
/// </summary>
public sealed partial class DevicePage : UserControl
{
    private IAddonFrontendControl? _frontend;
    private bool _active;
    // Set while a snapshot is being applied to the ComboBoxes programmatically, so
    // SelectionChanged does not treat that render as a user edit and write it back out (work order
    // section 17 -- the same feedback-loop-prevention shape already used elsewhere in this UI).
    private bool _suppressSelectionEvents;

    private static readonly CpuBoostModeItem[] Modes =
    [
        new(CpuBoostMode.Disabled, "Disabled"),
        new(CpuBoostMode.Enabled, "Enabled"),
        new(CpuBoostMode.Aggressive, "Aggressive"),
        new(CpuBoostMode.EfficientEnabled, "Efficient Enabled"),
        new(CpuBoostMode.EfficientAggressive, "Efficient Aggressive"),
        new(CpuBoostMode.AggressiveAtGuaranteed, "Aggressive At Guaranteed"),
        new(CpuBoostMode.EfficientAggressiveAtGuaranteed, "Efficient Aggressive At Guaranteed"),
    ];

    public DevicePage()
    {
        InitializeComponent();
        CpuBoostAcComboBox.ItemsSource = Modes;
        CpuBoostDcComboBox.ItemsSource = Modes;
    }

    internal void Initialize(IAddonFrontendControl frontend) => _frontend = frontend;

    internal void Activate()
    {
        _active = true;
        if (_frontend is not null) _frontend.StateInvalidated += OnStateInvalidated;
        _ = RefreshAsync();
    }

    internal void Deactivate()
    {
        _active = false;
        if (_frontend is not null) _frontend.StateInvalidated -= OnStateInvalidated;
    }

    private void OnStateInvalidated(object? sender, EventArgs e)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            if (_active) _ = RefreshAsync();
        });
    }

    internal async Task RefreshAsync()
    {
        if (_frontend is null) return;
        FrontendCpuBoostSnapshot snapshot;
        try { snapshot = await _frontend.CaptureCpuBoostAsync(); }
        catch (Exception exception)
        {
            AppLog.Warn("Device", "CPU Boost snapshot capture failed.", exception, ("Reason", exception.GetType().Name));
            return;
        }
        Render(snapshot);
    }

    private void Render(FrontendCpuBoostSnapshot snapshot)
    {
        _suppressSelectionEvents = true;
        try
        {
            // No Addon default and no synthetic eighth option (work order sections 8/20): the
            // displayed selection is the Addon's desired value if one exists, else the actual
            // current Windows value if it is Known, else no selection at all -- never a fabricated
            // mode for Unknown/Unavailable.
            CpuBoostAcComboBox.SelectedItem = SelectedItem(snapshot.Ac);
            CpuBoostDcComboBox.SelectedItem = SelectedItem(snapshot.Dc);
            CpuBoostEnabledToggleSwitch.IsOn = snapshot.Enabled;
        }
        finally { _suppressSelectionEvents = false; }

        // Device CPU Boost Toggle addendum sections 8/9: the saved AC/DC selections stay visible
        // (never nulled out) and the Expander stays expanded while the feature is OFF -- only
        // editing is disabled, so the user can see what will re-apply the moment it's turned back on.
        CpuBoostEnabledToggleSwitch.IsEnabled = snapshot.PersistenceWritable;
        var selectorsEditable = snapshot.PersistenceWritable && snapshot.Enabled;
        CpuBoostAcComboBox.IsEnabled = selectorsEditable;
        CpuBoostDcComboBox.IsEnabled = selectorsEditable;

        if (!snapshot.PersistenceWritable)
        {
            CpuBoostInfoBar.Severity = InfoBarSeverity.Error;
            CpuBoostInfoBar.Message = "CPU Boost settings could not be loaded, so changes are disabled to avoid overwriting the existing profile.";
            CpuBoostInfoBar.IsOpen = true;
        }
        else if (snapshot.LastFailure is { } failure)
        {
            CpuBoostInfoBar.Severity = InfoBarSeverity.Warning;
            CpuBoostInfoBar.Message = $"The last CPU Boost change could not be applied to Windows: {failure}";
            CpuBoostInfoBar.IsOpen = true;
        }
        else
        {
            CpuBoostInfoBar.IsOpen = false;
        }
    }

    private static CpuBoostModeItem? SelectedItem(FrontendCpuBoostSideSnapshot side)
    {
        var mode = side.Desired ?? (side.CurrentStatus == FrontendCpuBoostReadStatus.Known ? side.Current : null);
        return mode is null ? null : Array.Find(Modes, item => item.Mode == mode.Value);
    }

    private async void CpuBoostAcComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressSelectionEvents || _frontend is null) return;
        if (CpuBoostAcComboBox.SelectedItem is not CpuBoostModeItem item) return;
        var result = await _frontend.SetDeviceCpuBoostAcAsync(item.Mode);
        Render(result.Snapshot);
    }

    private async void CpuBoostDcComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressSelectionEvents || _frontend is null) return;
        if (CpuBoostDcComboBox.SelectedItem is not CpuBoostModeItem item) return;
        var result = await _frontend.SetDeviceCpuBoostDcAsync(item.Mode);
        Render(result.Snapshot);
    }

    private async void CpuBoostEnabledToggleSwitch_Toggled(object sender, RoutedEventArgs e)
    {
        if (_suppressSelectionEvents || _frontend is null) return;
        var result = await _frontend.SetDeviceCpuBoostEnabledAsync(CpuBoostEnabledToggleSwitch.IsOn);
        Render(result.Snapshot);
    }

    private sealed record CpuBoostModeItem(CpuBoostMode Mode, string Label);
}
