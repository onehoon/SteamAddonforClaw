using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
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
    private bool _suppressTdpEvents;
    private FrontendTdpSnapshot _tdpSnapshot = FrontendTdpSnapshot.Unavailable;
    private FrontendPowerModeSnapshot _powerModeSnapshot = FrontendPowerModeSnapshot.Unavailable;
    private int? _acPl1Draft, _acPl2Draft, _dcPl1Draft, _dcPl2Draft;
    private CancellationTokenSource? _tdpEditDebounce;
    private long _tdpEditGeneration;
    private bool _tdpDraftDirty;
    private FrontendCenterMStartupSnapshot _centerMStartupSnapshot = FrontendCenterMStartupSnapshot.Unavailable;
    private bool _centerMStartupBusy;

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
        PowerModeAcComboBox.ItemsSource = PowerModes;
        PowerModeDcComboBox.ItemsSource = PowerModes;
    }

    internal void Initialize(IAddonFrontendControl frontend) => _frontend = frontend;

    internal void Activate()
    {
        _active = true;
        if (_frontend is not null) _frontend.StateInvalidated += OnStateInvalidated;
        _ = RefreshAsync();
        // The MSI Center M controller-authority card lives on this page. Its reboot-bound transition
        // raises no StateInvalidated, so re-read it on every page entry -- the same reason the
        // TDP/CPU Boost/Power Mode StateInvalidated subscription is not enough for Center M.
        _ = RefreshCenterMStartupAsync();
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
        // Shared Frontend V2 (SF-V2-01): one aggregate read replaces the three separate Device
        // captures. A failed child capture is isolated server-side into that child's Unavailable
        // snapshot (Render/RenderTdp/RenderPowerMode already render Unavailable correctly), so only
        // a whole-transport failure needs to be handled here -- and it must fail closed rather than
        // leave stale editable controls (work order section 10.4).
        FrontendDeviceQuickSettingsSnapshot snapshot;
        try { snapshot = await _frontend.CaptureDeviceQuickSettingsAsync(); }
        catch (Exception exception)
        {
            AppLog.Warn("Device", "Device Quick Settings snapshot capture failed.", exception, ("Reason", exception.GetType().Name));
            Render(FrontendCpuBoostSnapshot.Unavailable);
            RenderTdp(FrontendTdpSnapshot.Unavailable, preserveDirtyDraft: false);
            RenderPowerMode(FrontendPowerModeSnapshot.Unavailable);
            return;
        }
        Render(snapshot.CpuBoost);
        RenderTdp(snapshot.Tdp);
        RenderPowerMode(snapshot.PowerMode);
    }

    private static readonly PowerModeItem[] PowerModes = [new(WindowsPowerMode.BestPowerEfficiency, "Best power efficiency"), new(WindowsPowerMode.Balanced, "Balanced"), new(WindowsPowerMode.BestPerformance, "Best performance")];
    private void RenderPowerMode(FrontendPowerModeSnapshot snapshot)
    {
        _powerModeSnapshot = snapshot; _suppressSelectionEvents = true;
        try { PowerModeEnabledToggleSwitch.IsOn = snapshot.Enabled; PowerModeExpander.IsExpanded = snapshot.Enabled; PowerModeAcComboBox.SelectedItem = PowerModeItemFor(snapshot.Ac); PowerModeDcComboBox.SelectedItem = PowerModeItemFor(snapshot.Dc); }
        finally { _suppressSelectionEvents = false; }
        var editable = snapshot.PersistenceWritable && snapshot.Ac.Desired is not null && snapshot.Dc.Desired is not null;
        PowerModeEnabledToggleSwitch.IsEnabled = editable; PowerModeAcComboBox.IsEnabled = editable && snapshot.Enabled; PowerModeDcComboBox.IsEnabled = editable && snapshot.Enabled;
        PowerModeInfoBar.IsOpen = !editable || snapshot.LastFailure is not null;
        PowerModeInfoBar.Message = snapshot.LastFailure ?? (editable ? "Power Mode settings are unavailable." : "Windows Power Mode could not be initialized.");
    }
    private static PowerModeItem? PowerModeItemFor(FrontendPowerModeSideSnapshot side) => PowerModes.FirstOrDefault(x => x.Mode == (side.Desired ?? side.Current));
    private async void PowerModeAcComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e) { if (_suppressSelectionEvents || _frontend is null || PowerModeAcComboBox.SelectedItem is not PowerModeItem item) return; await RunPowerModeMutationAsync(() => _frontend.SetDevicePowerModeAcAsync(item.Mode)); }
    private async void PowerModeDcComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e) { if (_suppressSelectionEvents || _frontend is null || PowerModeDcComboBox.SelectedItem is not PowerModeItem item) return; await RunPowerModeMutationAsync(() => _frontend.SetDevicePowerModeDcAsync(item.Mode)); }
    private async void PowerModeEnabledToggleSwitch_Toggled(object sender, RoutedEventArgs e) { if (_suppressSelectionEvents || _frontend is null) return; await RunPowerModeMutationAsync(() => _frontend.SetDevicePowerModeEnabledAsync(PowerModeEnabledToggleSwitch.IsOn)); }
    private async Task RunPowerModeMutationAsync(Func<Task<FrontendPowerModeMutationResult>> mutation)
    {
        try
        {
            var result = await mutation(); RenderPowerMode(result.Snapshot);
            if (!result.Succeeded)
            {
                PowerModeInfoBar.Severity = result.Outcome == FrontendPowerModeMutationOutcome.PersistenceFailed ? InfoBarSeverity.Error : InfoBarSeverity.Warning;
                PowerModeInfoBar.Message = result.FailureMessage ?? "Power Mode could not be updated."; PowerModeInfoBar.IsOpen = true;
            }
        }
        catch (Exception exception)
        {
            AppLog.Warn("Device", "Power Mode mutation failed.", exception);
            PowerModeInfoBar.Severity = InfoBarSeverity.Error; PowerModeInfoBar.Message = "Power Mode could not be updated because the Runtime connection was interrupted."; PowerModeInfoBar.IsOpen = true;
            await RefreshAsync();
        }
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
            CpuBoostExpander.IsExpanded = snapshot.Enabled;
        }
        finally { _suppressSelectionEvents = false; }

        // Device CPU Boost Toggle addendum sections 8/9: the saved AC/DC selections stay intact
        // (never nulled out) while the feature Expander reflects the authoritative Enabled state.
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
        await RunCpuBoostMutationAsync(() => _frontend.SetDeviceCpuBoostAcAsync(item.Mode));
    }

    private async void CpuBoostDcComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressSelectionEvents || _frontend is null) return;
        if (CpuBoostDcComboBox.SelectedItem is not CpuBoostModeItem item) return;
        await RunCpuBoostMutationAsync(() => _frontend.SetDeviceCpuBoostDcAsync(item.Mode));
    }

    private async void CpuBoostEnabledToggleSwitch_Toggled(object sender, RoutedEventArgs e)
    {
        if (_suppressSelectionEvents || _frontend is null) return;
        var enabled = CpuBoostEnabledToggleSwitch.IsOn;
        await RunCpuBoostMutationAsync(() => _frontend.SetDeviceCpuBoostEnabledAsync(enabled));
    }

    /// <summary>Shared path for all three CPU Boost mutation handlers (review fix, MAJOR): renders
    /// the returned snapshot and, on a non-succeeded outcome (PersistenceFailed/ApplyFailed), surfaces
    /// it visibly rather than letting the page silently snap back with no explanation. Also contains
    /// the frontend call in a try/catch -- these are async void UI event handlers, so an unhandled
    /// exception (e.g. the Runtime/Named Pipe connection dropping mid-change) would otherwise escape
    /// onto the UI dispatcher. On that failure path, refresh from Runtime authority rather than leave
    /// a speculative/stale UI selection in place.</summary>
    private async Task RunCpuBoostMutationAsync(Func<Task<FrontendCpuBoostMutationResult>> mutation)
    {
        try
        {
            var result = await mutation();
            Render(result.Snapshot);

            if (!result.Succeeded)
            {
                CpuBoostInfoBar.Severity = result.Outcome == FrontendCpuBoostMutationOutcome.PersistenceFailed
                    ? InfoBarSeverity.Error
                    : InfoBarSeverity.Warning;
                CpuBoostInfoBar.Message = result.FailureMessage ?? "The CPU Boost change failed.";
                CpuBoostInfoBar.IsOpen = true;
            }
        }
        catch (Exception exception)
        {
            AppLog.Warn("Device", "CPU Boost mutation failed.", exception, ("Reason", exception.GetType().Name));
            CpuBoostInfoBar.Severity = InfoBarSeverity.Error;
            CpuBoostInfoBar.Message = "CPU Boost could not be updated because the Runtime connection was interrupted.";
            CpuBoostInfoBar.IsOpen = true;

            // Restore controls from Runtime authority when the connection is still usable.
            await RefreshAsync();
        }
    }

    private void RenderTdp(FrontendTdpSnapshot snapshot, bool preserveDirtyDraft = true)
    {
        var keepDraft = preserveDirtyDraft && _tdpDraftDirty;
        _tdpSnapshot = snapshot;
        if (!keepDraft)
        {
            if (snapshot.Configuration is { } configuration)
            {
                _acPl1Draft = configuration.Ac.Pl1Watts; _acPl2Draft = configuration.Ac.Pl2Watts;
                _dcPl1Draft = configuration.Dc.Pl1Watts; _dcPl2Draft = configuration.Dc.Pl2Watts;
            }
            else
            {
                _acPl1Draft = null; _acPl2Draft = null;
                _dcPl1Draft = null; _dcPl2Draft = null;
            }
        }
        _suppressTdpEvents = true;
        try
        {
            if (snapshot.Limits is { } limits)
            {
                foreach (var slider in new[] { TdpAcPl1Slider, TdpDcPl1Slider })
                {
                    slider.Minimum = limits.Pl1MinimumWatts;
                    slider.Maximum = limits.Pl2MaximumWatts;
                    slider.StepFrequency = 1;
                }
                foreach (var slider in new[] { TdpAcPl2Slider, TdpDcPl2Slider })
                {
                    slider.Minimum = limits.Pl2MinimumWatts;
                    slider.Maximum = limits.Pl2MaximumWatts;
                    slider.StepFrequency = 1;
                }
            }

            TdpEnabledToggleSwitch.IsOn = snapshot.Configuration?.Enabled == true;
            TdpExpander.IsExpanded = snapshot.Configuration?.Enabled == true;
            SetSlider(TdpAcPl1Slider, _acPl1Draft); SetSlider(TdpAcPl2Slider, _acPl2Draft);
            SetSlider(TdpDcPl1Slider, _dcPl1Draft); SetSlider(TdpDcPl2Slider, _dcPl2Draft);
        }
        finally { _suppressTdpEvents = false; }
        var editable = snapshot.Available && snapshot.PersistenceWritable && snapshot.Configuration?.Enabled == true;
        TdpEnabledToggleSwitch.IsEnabled = snapshot.Available && snapshot.PersistenceWritable;
        foreach (var slider in new[] { TdpAcPl1Slider, TdpAcPl2Slider, TdpDcPl1Slider, TdpDcPl2Slider }) slider.IsEnabled = editable;
        SetTdpValueText(TdpAcPl1ValueText, _acPl1Draft); SetTdpValueText(TdpAcPl2ValueText, _acPl2Draft);
        SetTdpValueText(TdpDcPl1ValueText, _dcPl1Draft); SetTdpValueText(TdpDcPl2ValueText, _dcPl2Draft);
        if (!snapshot.Available) { TdpInfoBar.Message = "TDP Control is unavailable on this device."; TdpInfoBar.IsOpen = true; }
        else if (!snapshot.PersistenceWritable) { TdpInfoBar.Message = "TDP settings could not be loaded, so changes are disabled to avoid overwriting the existing profile."; TdpInfoBar.Severity = InfoBarSeverity.Error; TdpInfoBar.IsOpen = true; }
        else if (snapshot.Available) TdpInfoBar.IsOpen = false;
    }

    private static void SetSlider(Slider slider, int? value) { if (value is { } watts) slider.Value = watts; }
    private static void SetTdpValueText(TextBlock text, int? value) => text.Text = value is { } watts ? $"{watts} W" : "— W";
    private void TdpSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs args)
    {
        if (_suppressTdpEvents) return;
        var value = (int)Math.Round(args.NewValue);
        var slider = (Slider)sender;
        var isAc = ReferenceEquals(slider, TdpAcPl1Slider) || ReferenceEquals(slider, TdpAcPl2Slider);
        var isPl1 = ReferenceEquals(slider, TdpAcPl1Slider) || ReferenceEquals(slider, TdpDcPl1Slider);
        var pl1 = isAc ? _acPl1Draft : _dcPl1Draft;
        var pl2 = isAc ? _acPl2Draft : _dcPl2Draft;
        if (isPl1)
        {
            if (_tdpSnapshot.Limits is { } currentLimits)
                value = Math.Min(value, currentLimits.Pl1MaximumWatts);
            pl1 = value;
        }
        else pl2 = value;

        if (_tdpSnapshot.Limits is { } limits)
        {
            var adjusted = TdpDraftPolicy.AdjustAfterEdit(isPl1, pl1, pl2, limits);
            pl1 = adjusted.Pl1Watts;
            pl2 = adjusted.Pl2Watts;
            _suppressTdpEvents = true;
            try
            {
                SetSlider(isAc ? TdpAcPl1Slider : TdpDcPl1Slider, pl1);
                SetSlider(isAc ? TdpAcPl2Slider : TdpDcPl2Slider, pl2);
            }
            finally { _suppressTdpEvents = false; }
        }

        if (isAc) { _acPl1Draft = pl1; _acPl2Draft = pl2; }
        else { _dcPl1Draft = pl1; _dcPl2Draft = pl2; }
        SetTdpValueText(isAc ? TdpAcPl1ValueText : TdpDcPl1ValueText, pl1);
        SetTdpValueText(isAc ? TdpAcPl2ValueText : TdpDcPl2ValueText, pl2);
        SetTdpResult(isAc, null);
        _tdpDraftDirty = true;
        if (_tdpSnapshot.Configuration?.Enabled == true) ScheduleTdpEdit(isAc);
    }

    private async void TdpEnabledToggleSwitch_Toggled(object sender, RoutedEventArgs e)
    {
        if (_suppressTdpEvents || _frontend is null) return;
        CancelPendingTdpEdit();
        SetTdpMutationBusy(true);
        try
        {
            var result = await _frontend.SetDeviceTdpEnabledAsync(TdpEnabledToggleSwitch.IsOn);
            RenderTdp(result.Snapshot, preserveDirtyDraft: false);
            if (!result.Succeeded)
            {
                TdpInfoBar.Message = result.FailureMessage ?? "TDP could not be updated.";
                TdpInfoBar.IsOpen = true;
            }
        }
        catch (Exception exception) { AppLog.Warn("Device", "TDP enable mutation failed.", exception); TdpInfoBar.Message = "TDP could not be updated because the Runtime connection was interrupted."; TdpInfoBar.IsOpen = true; await RefreshAsync(); }
        finally { SetTdpMutationBusy(false); }
    }

    private void ScheduleTdpEdit(bool editedAc)
    {
        _tdpEditDebounce?.Cancel();
        var generation = Interlocked.Increment(ref _tdpEditGeneration);
        var cts = _tdpEditDebounce = new CancellationTokenSource();
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(300, cts.Token);
                DispatcherQueue.TryEnqueue(() =>
                {
                    if (!TdpDraftPolicy.CanSubmitDebouncedEdit(generation, Volatile.Read(ref _tdpEditGeneration), TdpEnabledToggleSwitch.IsOn)) return;
                    var configuration = BuildTdpConfiguration(true);
                    if (configuration is not null) _ = RunTdpMutationAsync(configuration, generation, editedAc);
                });
            }
            catch (OperationCanceledException) { }
        });
    }
    private void CancelPendingTdpEdit() { Interlocked.Increment(ref _tdpEditGeneration); _tdpEditDebounce?.Cancel(); }
    private void SetTdpMutationBusy(bool busy)
    {
        TdpEnabledToggleSwitch.IsEnabled = !busy && _tdpSnapshot.Available && _tdpSnapshot.PersistenceWritable;
        foreach (var slider in new[] { TdpAcPl1Slider, TdpAcPl2Slider, TdpDcPl1Slider, TdpDcPl2Slider })
            slider.IsEnabled = !busy && _tdpSnapshot.Available && _tdpSnapshot.PersistenceWritable
                && _tdpSnapshot.Configuration?.Enabled == true;
    }
    private bool CompleteTdpDraft() => _acPl1Draft is not null && _acPl2Draft is not null && _dcPl1Draft is not null && _dcPl2Draft is not null;
    private FrontendTdpConfiguration? BuildTdpConfiguration(bool enabled)
    {
        return TdpDraftPolicy.TryBuildCompleteConfiguration(enabled, _acPl1Draft, _acPl2Draft, _dcPl1Draft, _dcPl2Draft);
    }
    private FrontendTdpConfiguration? BuildTdpToggleConfiguration(bool enabled) =>
        TdpDraftPolicy.TryBuildToggleConfiguration(enabled, _acPl1Draft, _acPl2Draft, _dcPl1Draft, _dcPl2Draft, _tdpSnapshot.Configuration);
    private async Task RunTdpMutationAsync(FrontendTdpConfiguration configuration, long submittedGeneration, bool editedAc)
    {
        try { var result = await _frontend!.SetDeviceTdpAsync(configuration); var newerEditExists = TdpDraftPolicy.ShouldPreserveDirtyDraft(_tdpDraftDirty, submittedGeneration, Volatile.Read(ref _tdpEditGeneration)); if (!newerEditExists) _tdpDraftDirty = false; RenderTdp(result.Snapshot, preserveDirtyDraft: newerEditExists); if (!newerEditExists && result.HardwareApply is { Attempted: true } hardware && TdpDraftPolicy.ShouldShowHardwareResult(editedAc, hardware)) SetTdpResult(editedAc, hardware.Succeeded ? "Success" : "Fail"); if (!result.Succeeded) { TdpInfoBar.Message = result.FailureMessage ?? "The TDP change failed."; TdpInfoBar.Severity = result.Outcome == FrontendTdpMutationOutcome.PersistenceFailed ? InfoBarSeverity.Error : InfoBarSeverity.Warning; TdpInfoBar.IsOpen = true; } }
        catch (Exception exception) { AppLog.Warn("Device", "TDP mutation failed.", exception); TdpInfoBar.Message = "TDP could not be updated because the Runtime connection was interrupted."; TdpInfoBar.Severity = InfoBarSeverity.Error; TdpInfoBar.IsOpen = true; await RefreshAsync(); }
    }
    private void SetTdpResult(bool ac, string? result)
    {
        var value = result ?? string.Empty;
        if (ac) { TdpAcPl1ResultText.Text = value; TdpAcPl2ResultText.Text = value; }
        else { TdpDcPl1ResultText.Text = value; TdpDcPl2ResultText.Text = value; }
    }

    internal static class TdpDraftPolicy
    {
        internal readonly record struct AdjustedPair(int? Pl1Watts, int? Pl2Watts);

        internal static AdjustedPair AdjustAfterEdit(bool pl1WasEdited, int? pl1, int? pl2, FrontendTdpLimits limits)
        {
            var gap = limits switch
            {
                { Pl1MinimumWatts: 8, Pl1MaximumWatts: 30, Pl2MinimumWatts: 8, Pl2MaximumWatts: 37 } => 1,
                { Pl1MinimumWatts: 8, Pl1MaximumWatts: 35, Pl2MinimumWatts: 8, Pl2MaximumWatts: 45 } => 2,
                _ => 0
            };
            if (gap == 0) return new(pl1, pl2);
            if (pl1WasEdited)
            {
                if (pl1 is not { } editedPl1 || pl2 is not { } currentPl2 || currentPl2 >= editedPl1 + gap)
                    return new(pl1, pl2);
                return editedPl1 + gap <= limits.Pl2MaximumWatts
                    ? new(editedPl1, editedPl1 + gap)
                    : new(limits.Pl2MaximumWatts - gap, currentPl2);
            }

            if (pl2 is not { } editedPl2 || pl1 is not { } currentPl1 || currentPl1 <= editedPl2 - gap)
                return new(pl1, pl2);
            return editedPl2 - gap >= limits.Pl1MinimumWatts
                ? new(editedPl2 - gap, editedPl2)
                : new(limits.Pl1MinimumWatts, limits.Pl1MinimumWatts + gap);
        }

        internal static bool CanSubmitDebouncedEdit(long generation, long currentGeneration, bool enabled) =>
            generation == currentGeneration && enabled;

        internal static bool ShouldPreserveDirtyDraft(bool dirty, long submittedGeneration, long currentGeneration) =>
            dirty && submittedGeneration != currentGeneration;

        internal static bool ShouldShowHardwareResult(bool editedAc, FrontendTdpHardwareApplyResult hardware) =>
            (hardware.Source == FrontendTdpPowerSource.AC) == editedAc;

        internal static FrontendTdpConfiguration? TryBuildCompleteConfiguration(bool enabled, int? acPl1, int? acPl2, int? dcPl1, int? dcPl2)
        {
            return acPl1 is null || acPl2 is null || dcPl1 is null || dcPl2 is null
                ? null : new(enabled, new(acPl1.Value, acPl2.Value), new(dcPl1.Value, dcPl2.Value));
        }

        internal static FrontendTdpConfiguration? TryBuildToggleConfiguration(bool enabled, int? acPl1, int? acPl2, int? dcPl1, int? dcPl2, FrontendTdpConfiguration? saved)
        {
            if (TryBuildCompleteConfiguration(enabled, acPl1, acPl2, dcPl1, dcPl2) is { } complete) return complete;
            if (enabled || saved is null) return null;
            acPl1 ??= saved?.Ac.Pl1Watts; acPl2 ??= saved?.Ac.Pl2Watts;
            dcPl1 ??= saved?.Dc.Pl1Watts; dcPl2 ??= saved?.Dc.Pl2Watts;
            return acPl1 is null || acPl2 is null || dcPl1 is null || dcPl2 is null
                ? null : new(enabled, new(acPl1.Value, acPl2.Value), new(dcPl1.Value, dcPl2.Value));
        }
    }

    // ---- Device identity / support summary (moved here from the Status page) ----

    /// <summary>Renders the compact device identity/support line at the top of the page from the
    /// authoritative frontend status snapshot. This is identity/context, not a status dashboard --
    /// it reuses the existing <see cref="DeviceSummaryPresentation"/> formatting rather than restating it.</summary>
    internal void RenderDeviceSummary(FrontendStatusSnapshot snapshot)
    {
        DeviceManufacturerText.Text = DeviceSummaryPresentation.FormatManufacturerForDisplay(snapshot.Device.Manufacturer);
        DeviceModelText.Text = snapshot.Device.Model;
        DeviceSupportText.Text = DeviceSummaryPresentation.FormatDeviceCompatibility(snapshot.Hardware.Status);
        DeviceBoardGpuText.Text = $"Board: {snapshot.Device.BaseBoard} · GPU: {string.Join(", ", snapshot.Device.GpuModels)}";
    }

    // ---- MSI Center M controller-authority card (moved here from the Controller page) ----

    private async Task RefreshCenterMStartupAsync()
    {
        if (_frontend is null) return;
        try { RenderCenterMStartup(await _frontend.CaptureCenterMStartupAsync()); }
        catch (Exception exception)
        {
            AppLog.Warn("Device", "MSI Center M startup snapshot capture failed.", exception, ("Reason", exception.GetType().Name));
            RenderCenterMStartup(FrontendCenterMStartupSnapshot.Unavailable);
        }
    }

    /// <summary>Renders the MSI Center M controller-authority card from the authoritative snapshot.
    /// Explicit Enable/Disable buttons -- no inverted toggle; the button matching the current authority
    /// is disabled. Real Windows state is the only source of truth, so nothing is persisted here and
    /// there is no sticky "restart later" state: a confirmed transition restarts Windows immediately.</summary>
    private void RenderCenterMStartup(FrontendCenterMStartupSnapshot snapshot)
    {
        _centerMStartupSnapshot = snapshot;

        // The feature simply does not apply to this machine (non-Claw) and there is nothing to
        // report -- collapse the card rather than show a dead "Unavailable" row.
        if (snapshot.State == FrontendCenterMStartupState.Unavailable && snapshot.FailureMessage is null)
        {
            CenterMStartupCard.Visibility = Visibility.Collapsed;
            CenterMStartupInfoBar.IsOpen = false;
            return;
        }

        CenterMStartupCard.Visibility = Visibility.Visible;
        CenterMStartupStatusText.Text = snapshot.State switch
        {
            FrontendCenterMStartupState.Enabled => "Status: Enabled",
            FrontendCenterMStartupState.Disabled => "Status: Disabled",
            FrontendCenterMStartupState.Partial => "Status: Needs attention",
            _ => "Status: Unavailable",
        };

        var operable = !_centerMStartupBusy && snapshot.State != FrontendCenterMStartupState.Unavailable;
        CenterMStartupEnableButton.IsEnabled = operable && snapshot.State != FrontendCenterMStartupState.Enabled;
        CenterMStartupDisableButton.IsEnabled = operable && snapshot.State != FrontendCenterMStartupState.Disabled;

        switch (CenterMStartupPresentation.ResolveInfoBar(snapshot.State))
        {
            case CenterMStartupInfoBarKind.Partial:
                CenterMStartupInfoBar.Severity = InfoBarSeverity.Warning;
                CenterMStartupInfoBar.Message = "MSI Center M startup configuration is inconsistent. Choose Enable or Disable to repair it.";
                CenterMStartupInfoBar.IsOpen = true;
                break;
            case CenterMStartupInfoBarKind.Unavailable:
                CenterMStartupInfoBar.Severity = InfoBarSeverity.Warning;
                CenterMStartupInfoBar.Message = snapshot.FailureMessage ?? "MSI Center M controller authority control is unavailable.";
                CenterMStartupInfoBar.IsOpen = true;
                break;
            default:
                CenterMStartupInfoBar.IsOpen = false;
                break;
        }
    }

    internal enum CenterMStartupInfoBarKind { None, Partial, Unavailable }

    /// <summary>Pure InfoBar-precedence rule for the MSI Center M card, extracted so it can be tested
    /// without a XAML root.</summary>
    internal static class CenterMStartupPresentation
    {
        internal static CenterMStartupInfoBarKind ResolveInfoBar(FrontendCenterMStartupState state) => state switch
        {
            FrontendCenterMStartupState.Partial => CenterMStartupInfoBarKind.Partial,
            FrontendCenterMStartupState.Unavailable => CenterMStartupInfoBarKind.Unavailable,
            _ => CenterMStartupInfoBarKind.None,
        };
    }

    private async void CenterMStartupEnableButton_Click(object sender, RoutedEventArgs e) => await RequestCenterMTransitionAsync(centerMEnabled: true);
    private async void CenterMStartupDisableButton_Click(object sender, RoutedEventArgs e) => await RequestCenterMTransitionAsync(centerMEnabled: false);

    private async Task RequestCenterMTransitionAsync(bool centerMEnabled)
    {
        if (_frontend is null || _centerMStartupBusy) return;

        // Confirmation happens before any backend request. Cancel (or dismiss) issues zero RPC. The
        // transition always restarts immediately -- there is no deferred-restart choice.
        var dialog = new ContentDialog
        {
            Title = centerMEnabled ? "Enable MSI Center M" : "Disable MSI Center M",
            Content = new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                Text = centerMEnabled
                    ? "Restore MSI Center M controller authority.\n\nWindows must restart to apply this change."
                    : "Disable MSI Center M and switch controller authority to Steam Addon for Claw.\n\nWindows must restart to apply this change.",
            },
            PrimaryButtonText = centerMEnabled ? "Enable and Restart" : "Disable and Restart",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot,
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

        _centerMStartupBusy = true;
        CenterMStartupEnableButton.IsEnabled = false;
        CenterMStartupDisableButton.IsEnabled = false;
        try
        {
            var result = await _frontend.RequestCenterMAuthorityTransitionAsync(centerMEnabled);
            _centerMStartupBusy = false;
            RenderCenterMStartup(result.Snapshot);
            if (result.Succeeded)
            {
                // Windows is restarting now -- no long-lived success screen is needed.
                CenterMStartupEnableButton.IsEnabled = false;
                CenterMStartupDisableButton.IsEnabled = false;
                CenterMStartupInfoBar.Severity = InfoBarSeverity.Success;
                CenterMStartupInfoBar.Message = "Controller authority updated. Restarting Windows…";
                CenterMStartupInfoBar.IsOpen = true;
            }
            else
            {
                // A failed/cancelled Disable can leave verified startup/HidHide preparation behind
                // while the Center M roots are still Enabled. The backend explicitly offers
                // "Enable and Restart" as the cleanup path, so expose it here even though a plain
                // Enabled snapshot would normally disable the redundant Enable button.
                if (!centerMEnabled && result.Snapshot.State == FrontendCenterMStartupState.Enabled)
                    CenterMStartupEnableButton.IsEnabled = true;

                CenterMStartupInfoBar.Severity = result.Outcome == FrontendCenterMStartupMutationOutcome.Cancelled
                    ? InfoBarSeverity.Informational
                    : InfoBarSeverity.Warning;
                // Always prefer the backend's authoritative message: a cancelled elevation prompt on
                // Disable/Enable can still have left verified startup/HidHide preparation in place, so
                // the UI must not invent a "nothing changed" claim.
                CenterMStartupInfoBar.Message = result.FailureMessage
                    ?? (result.Outcome == FrontendCenterMStartupMutationOutcome.Cancelled
                        ? "The controller authority change was cancelled."
                        : "The controller authority change could not be completed.");
                CenterMStartupInfoBar.IsOpen = true;
            }
        }
        catch (Exception exception)
        {
            _centerMStartupBusy = false;
            AppLog.Warn("Device", "MSI Center M authority transition failed.", exception, ("Reason", exception.GetType().Name));
            CenterMStartupInfoBar.Severity = InfoBarSeverity.Error;
            CenterMStartupInfoBar.Message = "The controller authority change could not be completed because the Runtime connection was interrupted.";
            CenterMStartupInfoBar.IsOpen = true;
            await RefreshCenterMStartupAsync();
        }
    }

    private sealed record CpuBoostModeItem(CpuBoostMode Mode, string Label);
    private sealed record PowerModeItem(WindowsPowerMode Mode, string Label);
}
