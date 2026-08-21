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
    private bool _suppressTdpEvents;
    private FrontendTdpSnapshot _tdpSnapshot = FrontendTdpSnapshot.Unavailable;
    private int? _acPl1Draft, _acPl2Draft, _dcPl1Draft, _dcPl2Draft;
    private CancellationTokenSource? _tdpEditDebounce;
    private long _tdpEditGeneration;
    private bool _tdpDraftDirty;

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
        FrontendCpuBoostSnapshot snapshot = FrontendCpuBoostSnapshot.Unavailable;
        try { snapshot = await _frontend.CaptureCpuBoostAsync(); }
        catch (Exception exception)
        {
            AppLog.Warn("Device", "CPU Boost snapshot capture failed.", exception, ("Reason", exception.GetType().Name));
        }
        Render(snapshot);
        try { RenderTdp(await _frontend.CaptureTdpAsync()); }
        catch (Exception exception)
        {
            AppLog.Warn("Device", "TDP snapshot capture failed.", exception, ("Reason", exception.GetType().Name));
            TdpInfoBar.Severity = InfoBarSeverity.Error;
            TdpInfoBar.Message = "TDP settings could not be loaded.";
            TdpInfoBar.IsOpen = true;
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
        if (!keepDraft && snapshot.Configuration is { } configuration)
        {
            _acPl1Draft = configuration.Ac.Pl1Watts; _acPl2Draft = configuration.Ac.Pl2Watts;
            _dcPl1Draft = configuration.Dc.Pl1Watts; _dcPl2Draft = configuration.Dc.Pl2Watts;
        }
        _suppressTdpEvents = true;
        try
        {
            TdpEnabledToggleSwitch.IsOn = snapshot.Configuration?.Enabled == true;
            SetNumberBox(TdpAcPl1NumberBox, _acPl1Draft); SetNumberBox(TdpAcPl2NumberBox, _acPl2Draft);
            SetNumberBox(TdpDcPl1NumberBox, _dcPl1Draft); SetNumberBox(TdpDcPl2NumberBox, _dcPl2Draft);
        }
        finally { _suppressTdpEvents = false; }
        var editable = snapshot.Available && snapshot.PersistenceWritable && (snapshot.Configuration is null || snapshot.Configuration.Enabled);
        TdpEnabledToggleSwitch.IsEnabled = snapshot.Available && snapshot.PersistenceWritable;
        foreach (var box in new[] { TdpAcPl1NumberBox, TdpAcPl2NumberBox, TdpDcPl1NumberBox, TdpDcPl2NumberBox }) box.IsEnabled = editable;
        if (snapshot.Limits is { } limits)
        {
            foreach (var box in new[] { TdpAcPl1NumberBox, TdpDcPl1NumberBox }) { box.Minimum = limits.Pl1MinimumWatts; box.Maximum = limits.Pl1MaximumWatts; }
            foreach (var box in new[] { TdpAcPl2NumberBox, TdpDcPl2NumberBox }) { box.Minimum = limits.Pl2MinimumWatts; box.Maximum = limits.Pl2MaximumWatts; }
        }
        if (!snapshot.Available) { TdpInfoBar.Message = "TDP Control is unavailable on this device."; TdpInfoBar.IsOpen = true; }
        else if (!snapshot.PersistenceWritable) { TdpInfoBar.Message = "TDP settings could not be loaded, so changes are disabled to avoid overwriting the existing profile."; TdpInfoBar.Severity = InfoBarSeverity.Error; TdpInfoBar.IsOpen = true; }
        else if (snapshot.Available) TdpInfoBar.IsOpen = false;
    }

    private static void SetNumberBox(NumberBox box, int? value) => box.Value = value ?? double.NaN;
    private void TdpNumberBox_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (_suppressTdpEvents) return;
        var value = double.IsFinite(args.NewValue) && args.NewValue == Math.Truncate(args.NewValue) ? (int)args.NewValue : (int?)null;
        if (sender == TdpAcPl1NumberBox) _acPl1Draft = value;
        else if (sender == TdpAcPl2NumberBox) _acPl2Draft = value;
        else if (sender == TdpDcPl1NumberBox) _dcPl1Draft = value;
        else _dcPl2Draft = value;
        _tdpDraftDirty = true;
        if (_tdpSnapshot.Configuration?.Enabled == true) ScheduleTdpEdit();
    }

    private async void TdpEnabledToggleSwitch_Toggled(object sender, RoutedEventArgs e)
    {
        if (_suppressTdpEvents || _frontend is null) return;
        CancelPendingTdpEdit();
        if (TdpEnabledToggleSwitch.IsOn && !CompleteTdpDraft())
        {
            _suppressTdpEvents = true; TdpEnabledToggleSwitch.IsOn = false; _suppressTdpEvents = false;
            TdpInfoBar.Message = "Set valid PL1 and PL2 values for Plugged in and On battery before enabling TDP Control."; TdpInfoBar.IsOpen = true; return;
        }
        var configuration = BuildTdpConfiguration(TdpEnabledToggleSwitch.IsOn);
        if (configuration is not null) await RunTdpMutationAsync(configuration, Volatile.Read(ref _tdpEditGeneration));
    }

    private void ScheduleTdpEdit()
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
                    if (configuration is not null) _ = RunTdpMutationAsync(configuration, generation);
                });
            }
            catch (OperationCanceledException) { }
        });
    }
    private void CancelPendingTdpEdit() { Interlocked.Increment(ref _tdpEditGeneration); _tdpEditDebounce?.Cancel(); }
    private bool CompleteTdpDraft() => _acPl1Draft is not null && _acPl2Draft is not null && _dcPl1Draft is not null && _dcPl2Draft is not null;
    private FrontendTdpConfiguration? BuildTdpConfiguration(bool enabled)
    {
        return TdpDraftPolicy.TryBuildConfiguration(enabled, _acPl1Draft, _acPl2Draft, _dcPl1Draft, _dcPl2Draft, _tdpSnapshot.Configuration);
    }
    private async Task RunTdpMutationAsync(FrontendTdpConfiguration configuration, long submittedGeneration)
    {
        try { var result = await _frontend!.SetDeviceTdpAsync(configuration); var newerEditExists = TdpDraftPolicy.ShouldPreserveDirtyDraft(_tdpDraftDirty, submittedGeneration, Volatile.Read(ref _tdpEditGeneration)); if (!newerEditExists) _tdpDraftDirty = false; RenderTdp(result.Snapshot, preserveDirtyDraft: newerEditExists); if (!result.Succeeded) { TdpInfoBar.Message = result.FailureMessage ?? "The TDP change failed."; TdpInfoBar.Severity = result.Outcome == FrontendTdpMutationOutcome.PersistenceFailed ? InfoBarSeverity.Error : InfoBarSeverity.Warning; TdpInfoBar.IsOpen = true; } }
        catch (Exception exception) { AppLog.Warn("Device", "TDP mutation failed.", exception); TdpInfoBar.Message = "TDP could not be updated because the Runtime connection was interrupted."; TdpInfoBar.Severity = InfoBarSeverity.Error; TdpInfoBar.IsOpen = true; await RefreshAsync(); }
    }

    internal static class TdpDraftPolicy
    {
        internal static bool CanSubmitDebouncedEdit(long generation, long currentGeneration, bool enabled) =>
            generation == currentGeneration && enabled;

        internal static bool ShouldPreserveDirtyDraft(bool dirty, long submittedGeneration, long currentGeneration) =>
            dirty && submittedGeneration != currentGeneration;

        internal static FrontendTdpConfiguration? TryBuildConfiguration(bool enabled, int? acPl1, int? acPl2, int? dcPl1, int? dcPl2, FrontendTdpConfiguration? saved)
        {
            acPl1 ??= saved?.Ac.Pl1Watts; acPl2 ??= saved?.Ac.Pl2Watts;
            dcPl1 ??= saved?.Dc.Pl1Watts; dcPl2 ??= saved?.Dc.Pl2Watts;
            return acPl1 is null || acPl2 is null || dcPl1 is null || dcPl2 is null
                ? null : new(enabled, new(acPl1.Value, acPl2.Value), new(dcPl1.Value, dcPl2.Value));
        }
    }

    private sealed record CpuBoostModeItem(CpuBoostMode Mode, string Label);
}
