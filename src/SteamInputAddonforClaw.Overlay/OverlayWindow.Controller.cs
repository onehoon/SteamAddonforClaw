using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI;
using Windows.UI;
using SteamInputAddonforClaw.Contracts.BackButtons;
using SteamInputAddonforClaw.Contracts.ControllerLed;
using SteamInputAddonforClaw.Contracts.Frontend;
using SteamInputAddonforClaw.FrontendTransport;

namespace SteamInputAddonforClaw.Overlay;

internal enum ControllerForSteamMutationKind
{
    Controller,
    Gyroscope,
}

internal readonly record struct OverlayBackButtonMappingPreview(bool IsM1, Xbox360BackButtonTarget Target);

internal sealed record OverlayControllerLedPendingEdit(ControllerLedSettings Baseline, ControllerLedSettings Desired);

public sealed partial class OverlayWindow
{
    private BackButtonMappingSettings _backButtonMapping = BackButtonMappingSettings.Default;
    private bool _backButtonMappingAvailable;
    private bool _backButtonMutationInFlight;
    private OverlayBackButtonMappingPreview? _pendingBackButtonMappingPreview;
    private OverlayValueRow? _m1MappingRow;
    private OverlayValueRow? _m2MappingRow;
    private OverlayValueRow? _steamControllerRow;
    private OverlayToggleRow? _gyroscopeRow;
    private FrontendSettingsSnapshot? _frontendSettingsSnapshot;
    private FrontendControllerForSteamSnapshot _controllerForSteam = FrontendControllerForSteamSnapshot.Unavailable(SteamVirtualControllerPreference.SteamDeck);
    private bool _frontendSettingsAvailable;
    private bool _steamControllerMutationInFlight;
    private bool _gyroscopeMutationInFlight;
    private SteamVirtualControllerPreference? _pendingSteamControllerPreference;
    private bool? _pendingGyroscopeEnabled;
    private bool _controllerLedAvailable;
    private bool _controllerLedMutationInFlight;
    private OverlayControllerLedPendingEdit? _pendingControllerLedEdit;
    private bool _controllerVibrationMutationInFlight;
    private ControllerLedSettings _controllerLed = ControllerLedSettings.Default;
    private OverlayToggleRow? _controllerLedEnabledRow;
    private OverlayValueRow? _controllerLedBrightnessRow;
    private OverlayValueRow? _controllerLedRedRow;
    private OverlayValueRow? _controllerLedGreenRow;
    private OverlayValueRow? _controllerLedBlueRow;
    private Border? _controllerLedColorSwatch;
    private StackPanel? _controllerLedDetailStack;
    private Border? _controllerLedSectionCard;
    private OverlayValueRow? _leftVibrationRow;
    private OverlayValueRow? _rightVibrationRow;
    private FrontendControllerVibrationStrengthSnapshot _vibrationSnapshot = FrontendControllerVibrationStrengthSnapshot.Unavailable();
    private int? _vibrationDraftLeft;
    private int? _vibrationDraftRight;
    private bool _vibrationDraftDirty;
    private CancellationTokenSource? _vibrationCommitDelay;

    internal event Action<BackButtonMappingSettings>? BackButtonMappingEditRequested;
    internal event Action<ControllerLedSettings>? ControllerLedEditRequested;
    internal event Action<SteamVirtualControllerPreference?, bool?, ControllerForSteamMutationKind>? ControllerForSteamEditRequested;
    internal event Action<int, int>? ControllerVibrationStrengthEditRequested;

    private FrameworkElement BuildControllerPage(List<OverlayRow> rows)
    {
        var page = new StackPanel { Spacing = OverlayQamResources.Get("QamSectionSpacing", 0.0) };
        var steamSection = CreateControllerSection("Controller for Steam");
        var steamDetails = CreateOverlayDetailStack();
        var mappingSection = CreateControllerSection("M1 / M2");
        var mappingDetails = CreateOverlayDetailStack();
        mappingSection.Children.Add(mappingDetails);
        var ledSection = new StackPanel { Spacing = OverlayQamResources.Get("QamSectionHeaderSpacing", 4.0) };
        var vibrationSection = CreateControllerSection("Vibration Strength");

        _steamControllerRow = new OverlayValueRow("Controller", FormatSteamController,
            RequestSteamControllerChange, OverlayValueButtonKind.DiscreteChoice);
        AddControllerRow(steamDetails, rows, _steamControllerRow.Container, _steamControllerRow.Capabilities,
            () => _steamControllerMutationInFlight && _frontendSettingsAvailable
                && _controllerForSteam.Available && _controllerForSteam.CanChangeController);
        _gyroscopeRow = new OverlayToggleRow("Gyroscope", RequestGyroscopeChange);
        AddControllerRow(steamDetails, rows, _gyroscopeRow.Container, _gyroscopeRow.Capabilities,
            () => _gyroscopeMutationInFlight && _frontendSettingsAvailable
                && _frontendSettingsSnapshot is not null);
        steamSection.Children.Add(steamDetails);

        var m1Row = new OverlayValueRow("M1", FormatBackButtonTarget,
            value => RequestBackButtonMappingChange(isM1: true, value),
            OverlayValueButtonKind.DiscreteChoice);
        var m2Row = new OverlayValueRow("M2", FormatBackButtonTarget,
            value => RequestBackButtonMappingChange(isM1: false, value),
            OverlayValueButtonKind.DiscreteChoice);
        _m1MappingRow = m1Row;
        _m2MappingRow = m2Row;

        AddBackButtonMappingRow(mappingDetails, rows, m1Row);
        AddBackButtonMappingRow(mappingDetails, rows, m2Row);
        RenderBackButtonMappingRows();

        BuildControllerLedRows(ledSection, rows);
        BuildControllerVibrationRows(vibrationSection, rows);
        var steamCard = CreateOverlaySectionCard(steamSection);
        SetOverlaySectionSeparator(steamCard, visible: false);
        page.Children.Add(steamCard);
        var mappingCard = CreateOverlaySectionCard(mappingSection);
        SetOverlaySectionSeparator(mappingCard, visible: true);
        page.Children.Add(mappingCard);
        _controllerLedSectionCard = CreateOverlaySectionCard(ledSection);
        SetOverlaySectionSeparator(_controllerLedSectionCard, visible: true);
        page.Children.Add(_controllerLedSectionCard);
        var vibrationCard = CreateOverlaySectionCard(vibrationSection);
        SetOverlaySectionSeparator(vibrationCard, visible: true);
        page.Children.Add(vibrationCard);
        RenderControllerLedRows();
        RenderControllerForSteamRows();
        return page;
    }

    private static string FormatSteamController(double value) => (int)Math.Round(value) switch
    {
        0 => "Steam Deck",
        1 => "DualSense Edge",
        _ => "Unavailable",
    };

    private void RequestSteamControllerChange(double value)
    {
        if (!_frontendSettingsAvailable || !_controllerForSteam.Available || !_controllerForSteam.CanChangeController
            || _steamControllerMutationInFlight || !double.IsFinite(value)
            || value != Math.Truncate(value) || value is < 0 or > 1)
            return;

        var preference = (SteamVirtualControllerPreference)(int)value;
        if (preference == _controllerForSteam.SavedPreference) return;
        _steamControllerMutationInFlight = true;
        _pendingSteamControllerPreference = preference;
        RenderControllerForSteamRows();
        ControllerForSteamEditRequested?.Invoke(preference, null, ControllerForSteamMutationKind.Controller);
    }

    private void RequestGyroscopeChange(bool enabled)
    {
        if (!_frontendSettingsAvailable || _gyroscopeMutationInFlight || _frontendSettingsSnapshot is null
            || _frontendSettingsSnapshot.GyroscopeEnabled == enabled)
            return;

        _gyroscopeMutationInFlight = true;
        _pendingGyroscopeEnabled = enabled;
        RenderControllerForSteamRows();
        ControllerForSteamEditRequested?.Invoke(null, enabled, ControllerForSteamMutationKind.Gyroscope);
    }

    private void RenderControllerForSteamRows()
    {
        var controllerAvailable = _frontendSettingsAvailable && _controllerForSteam.Available
            && _controllerForSteam.CanChangeController && !_steamControllerMutationInFlight;
        var displayValue = _steamControllerMutationInFlight && _pendingSteamControllerPreference is { } pendingPreference
            ? (int)pendingPreference
            : !_controllerForSteam.Available
                ? 2
                : (int)(_controllerForSteam.ActiveSteamPreference ?? _controllerForSteam.SavedPreference);
        _steamControllerRow?.ApplyState(controllerAvailable, 0, controllerAvailable ? 1 : 2, 1, displayValue);
        var gyroscopeValue = _gyroscopeMutationInFlight && _pendingGyroscopeEnabled is { } pendingGyroscope
            ? pendingGyroscope
            : _frontendSettingsSnapshot?.GyroscopeEnabled == true;
        _gyroscopeRow?.ApplyState(_frontendSettingsAvailable && !_gyroscopeMutationInFlight, gyroscopeValue);

        if (_tabState.SelectedTab == AddonQuickSettingsTabId.Controller)
        {
            var preferredIndex = _rowSelection.SelectedIndex;
            _rowSelection.SetRows(CapabilitiesFor(AddonQuickSettingsTabId.Controller), preferredIndex);
            ApplyRowSelectionVisual();
        }
    }

    private static StackPanel CreateControllerSection(string title)
    {
        var section = new StackPanel { Spacing = OverlayQamResources.Get("QamRowSpacing", 0.0) };
        section.Children.Add(CreateOverlaySectionHeadingRow(title));
        return section;
    }

    private void BuildControllerLedRows(StackPanel section, List<OverlayRow> rows)
    {
        _controllerLedEnabledRow = new OverlayToggleRow("Joystick LED", RequestControllerLedEnabled, strongLabel: true);
        AddControllerRow(section, rows, _controllerLedEnabledRow.Container, _controllerLedEnabledRow.Capabilities,
            () => _controllerLedMutationInFlight && _controllerLedAvailable);

        var details = CreateOverlayDetailStack();
        _controllerLedDetailStack = details;

        _controllerLedBrightnessRow = new OverlayValueRow("Brightness", OverlayValueRow.FormatInteger,
            value => RequestControllerLedBrightness((int)Math.Round(value)), OverlayValueButtonKind.NumericStepper);
        AddControllerRow(details, rows, _controllerLedBrightnessRow.Container, _controllerLedBrightnessRow.Capabilities,
            () => _controllerLedMutationInFlight && _controllerLedAvailable && _controllerLed.Enabled);

        var colorPreview = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            MinHeight = OverlayQamResources.Get("QamValueButtonHeight", 22.0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        colorPreview.Children.Add(new TextBlock { Text = "Color", VerticalAlignment = VerticalAlignment.Center });
        var swatch = new Border
        {
            Width = 26,
            Height = 20,
            CornerRadius = new CornerRadius(4),
            BorderThickness = new Thickness(1),
            BorderBrush = OverlayQamResources.Brush("QamSubtleStrokeBrush"),
            VerticalAlignment = VerticalAlignment.Center,
        };
        _controllerLedColorSwatch = swatch;
        colorPreview.Children.Add(swatch);
        details.Children.Add(colorPreview);

        _controllerLedRedRow = new OverlayValueRow("Red", OverlayValueRow.FormatInteger,
            value => RequestControllerLedRgb(red: (int)Math.Round(value), green: null, blue: null), OverlayValueButtonKind.NumericStepper);
        _controllerLedGreenRow = new OverlayValueRow("Green", OverlayValueRow.FormatInteger,
            value => RequestControllerLedRgb(red: null, green: (int)Math.Round(value), blue: null), OverlayValueButtonKind.NumericStepper);
        _controllerLedBlueRow = new OverlayValueRow("Blue", OverlayValueRow.FormatInteger,
            value => RequestControllerLedRgb(red: null, green: null, blue: (int)Math.Round(value)), OverlayValueButtonKind.NumericStepper);
        AddControllerRow(details, rows, _controllerLedRedRow.Container, _controllerLedRedRow.Capabilities,
            () => _controllerLedMutationInFlight && _controllerLedAvailable && _controllerLed.Enabled);
        AddControllerRow(details, rows, _controllerLedGreenRow.Container, _controllerLedGreenRow.Capabilities,
            () => _controllerLedMutationInFlight && _controllerLedAvailable && _controllerLed.Enabled);
        AddControllerRow(details, rows, _controllerLedBlueRow.Container, _controllerLedBlueRow.Capabilities,
            () => _controllerLedMutationInFlight && _controllerLedAvailable && _controllerLed.Enabled);
        section.Children.Add(details);
        RenderControllerLedRows();
    }

    private void BuildControllerVibrationRows(StackPanel section, List<OverlayRow> rows)
    {
        var details = CreateOverlayDetailStack();
        _leftVibrationRow = new OverlayValueRow("Left Motor", FormatPercent,
            value => RequestVibrationEdit(left: (int)Math.Round(value), right: null), OverlayValueButtonKind.NumericStepper);
        _rightVibrationRow = new OverlayValueRow("Right Motor", FormatPercent,
            value => RequestVibrationEdit(left: null, right: (int)Math.Round(value)), OverlayValueButtonKind.NumericStepper);
        AddControllerRow(details, rows, _leftVibrationRow.Container, _leftVibrationRow.Capabilities,
            () => _controllerVibrationMutationInFlight && _vibrationSnapshot.Available
                && _vibrationSnapshot.Writable && _vibrationSnapshot.LeftPercent is not null);
        AddControllerRow(details, rows, _rightVibrationRow.Container, _rightVibrationRow.Capabilities,
            () => _controllerVibrationMutationInFlight && _vibrationSnapshot.Available
                && _vibrationSnapshot.Writable && _vibrationSnapshot.RightPercent is not null);
        section.Children.Add(details);
        RenderControllerVibrationRows();
    }

    private void AddControllerRow(
        StackPanel section,
        List<OverlayRow> rows,
        Border container,
        OverlayRowCapabilities capabilities,
        Func<bool> selectableDuringPendingMutation)
    {
        section.Children.Add(container);
        rows.Add(new(container, PreserveControllerPendingSelection(capabilities, selectableDuringPendingMutation)));
        RegisterRowPointerSelection(container);
    }

    private void AddBackButtonMappingRow(StackPanel section, List<OverlayRow> rows, OverlayValueRow row)
    {
        section.Children.Add(row.Container);
        rows.Add(new(row.Container, PreserveControllerPendingSelection(row.Capabilities,
            () => _backButtonMutationInFlight && _backButtonMappingAvailable)));
        RegisterRowPointerSelection(row.Container);
    }

    internal static OverlayRowCapabilities PreserveControllerPendingSelection(
        OverlayRowCapabilities capabilities,
        Func<bool> selectableDuringPendingMutation) => capabilities with
    {
        // Keep navigation stable only while the same row remains genuinely available. The
        // underlying row model still rejects activation/adjustment while its control is disabled.
        IsSelectable = () => capabilities.IsSelectable() || selectableDuringPendingMutation(),
    };

    private void RequestBackButtonMappingChange(bool isM1, double selected)
    {
        if (!_backButtonMappingAvailable || _backButtonMutationInFlight
            || !double.IsFinite(selected)
            || selected < (double)Xbox360BackButtonTarget.Disabled
            || selected > (double)Xbox360BackButtonTarget.XboxGuide
            || selected != Math.Truncate(selected))
            return;

        var target = (Xbox360BackButtonTarget)(int)selected;
        if (!Enum.IsDefined(target)) return;

        var candidate = CreateBackButtonMappingCandidate(_backButtonMapping, isM1, target);
        _backButtonMutationInFlight = true;
        _pendingBackButtonMappingPreview = new(isM1, target);
        RenderBackButtonMappingRows();
        BackButtonMappingEditRequested?.Invoke(candidate);
    }

    internal void ApplyBackButtonMappingState(OverlayBackButtonMappingState state)
    {
        _backButtonMapping = state.Mapping;
        _backButtonMappingAvailable = state.Available;
        // A StateInvalidated snapshot may arrive before the correlated mutation result. Keep the
        // controls disabled until that result settles the one outstanding whole-record request.
        RenderBackButtonMappingRows();
    }

    internal void ApplyBackButtonMappingMutationResult(OverlayBackButtonMappingMutationResponse response)
    {
        _backButtonMutationInFlight = false;
        _pendingBackButtonMappingPreview = null;
        ApplyBackButtonMappingState(response.State with
        {
            FailureMessage = response.Succeeded
                ? null
                : response.FailureMessage ?? "M1 / M2 mapping update failed.",
        });
    }

    internal void ApplyBackButtonMappingFailure(string message)
    {
        _backButtonMutationInFlight = false;
        _pendingBackButtonMappingPreview = null;
        RenderBackButtonMappingRows();
    }

    private void RenderBackButtonMappingRows()
    {
        var enabled = _backButtonMappingAvailable && !_backButtonMutationInFlight;
        var minimum = (double)Xbox360BackButtonTarget.Disabled;
        var maximum = (double)Xbox360BackButtonTarget.XboxGuide;
        var displayMapping = ProjectBackButtonMappingPreview(_backButtonMapping, _pendingBackButtonMappingPreview);
        _m1MappingRow?.ApplyState(enabled, minimum, maximum, 1, (double)displayMapping.M1);
        _m2MappingRow?.ApplyState(enabled, minimum, maximum, 1, (double)displayMapping.M2);

        if (_tabState.SelectedTab == AddonQuickSettingsTabId.Controller)
        {
            var preferredIndex = _rowSelection.SelectedIndex;
            _rowSelection.SetRows(CapabilitiesFor(AddonQuickSettingsTabId.Controller), preferredIndex);
            ApplyRowSelectionVisual();
        }
    }

    internal void ApplyFrontendSettingsState(FrontendSettingsSnapshot settings, bool controllerLedAvailable, bool settingsAvailable)
    {
        var preferredControllerIndex = _tabState.SelectedTab == AddonQuickSettingsTabId.Controller
            ? _rowSelection.SelectedIndex
            : null;
        _frontendSettingsSnapshot = settings;
        _frontendSettingsAvailable = settingsAvailable;
        _controllerForSteam = settings.ControllerForSteam;
        _controllerLedAvailable = settingsAvailable && controllerLedAvailable;
        _controllerLed = settings.ControllerLed;
        RenderControllerForSteamRows();
        RenderControllerLedRows();
        ApplyQuickSettingsCurrentPowerSourceOnly(settings.QuickSettingsCurrentPowerSourceOnly, settingsAvailable);
        ApplyQuickSettingsYButtonQuickJumpTarget(settings.YButtonQuickJumpTarget, settingsAvailable);
        RestoreControllerRowSelection(preferredControllerIndex);
    }

    internal void ApplyFrontendSettingsMutationResult(
        OverlayFrontendSettingsMutationResponse response,
        bool led,
        ControllerForSteamMutationKind? controllerForSteamMutation = null,
        bool yButtonQuickJumpMutation = false)
    {
        if (led)
        {
            _controllerLedMutationInFlight = false;
            _pendingControllerLedEdit = null;
        }
        else if (controllerForSteamMutation == ControllerForSteamMutationKind.Controller)
        {
            _steamControllerMutationInFlight = false;
            _pendingSteamControllerPreference = null;
        }
        else if (controllerForSteamMutation == ControllerForSteamMutationKind.Gyroscope)
        {
            _gyroscopeMutationInFlight = false;
            _pendingGyroscopeEnabled = null;
        }
        if (yButtonQuickJumpMutation)
            SetYButtonQuickJumpMutationInFlight(false);
        var quickSettingsMutation = !led && controllerForSteamMutation is null && !yButtonQuickJumpMutation;
        ApplyFrontendSettingsState(response.Settings, response.ControllerLedAvailable, response.SettingsAvailable);
        if (quickSettingsMutation)
            SetQuickSettingsCurrentPowerSourceMutationInFlight(false);
    }

    internal void ApplyFrontendSettingsMutationFailure(
        bool led,
        ControllerForSteamMutationKind? controllerForSteamMutation = null,
        bool yButtonQuickJumpMutation = false)
    {
        var preferredControllerIndex = _tabState.SelectedTab == AddonQuickSettingsTabId.Controller
            ? _rowSelection.SelectedIndex
            : null;
        if (led)
        {
            _controllerLedMutationInFlight = false;
            _pendingControllerLedEdit = null;
        }
        else if (controllerForSteamMutation == ControllerForSteamMutationKind.Controller)
        {
            _steamControllerMutationInFlight = false;
            _pendingSteamControllerPreference = null;
        }
        else if (controllerForSteamMutation == ControllerForSteamMutationKind.Gyroscope)
        {
            _gyroscopeMutationInFlight = false;
            _pendingGyroscopeEnabled = null;
        }
        else if (yButtonQuickJumpMutation)
        {
            SetYButtonQuickJumpMutationInFlight(false);
            return;
        }
        else SetQuickSettingsCurrentPowerSourceMutationInFlight(false);
        RenderControllerForSteamRows();
        RenderControllerLedRows();
        RenderQuickSettingsCurrentPowerSourceRow();
        RenderQuickSettingsYButtonQuickJumpRow();
        RestoreControllerRowSelection(preferredControllerIndex);
    }

    private void RestoreControllerRowSelection(int? preferredIndex)
    {
        if (_tabState.SelectedTab != AddonQuickSettingsTabId.Controller)
            return;

        // Pending-row capabilities preserve legitimate in-flight selections while this pass still
        // applies genuine availability and visibility changes from the completed snapshot.
        _rowSelection.SetRows(CapabilitiesFor(AddonQuickSettingsTabId.Controller), preferredIndex);
        ApplyRowSelectionVisual();
    }

    private void RequestControllerLedEnabled(bool enabled)
    {
        if (!_controllerLedAvailable || _controllerLedMutationInFlight || _frontendSettingsSnapshot is null) return;
        RequestControllerLedMutation(_controllerLed with { Enabled = enabled });
    }

    private void RequestControllerLedBrightness(int brightness)
    {
        if (!_controllerLedAvailable || _controllerLedMutationInFlight || !_controllerLed.Enabled || _frontendSettingsSnapshot is null || brightness is < 0 or > 100) return;
        RequestControllerLedMutation(_controllerLed with { Brightness = brightness });
    }

    private void RequestControllerLedRgb(int? red, int? green, int? blue)
    {
        if (!_controllerLedAvailable || _controllerLedMutationInFlight || !_controllerLed.Enabled || _frontendSettingsSnapshot is null) return;
        if (red is < 0 or > 255 || green is < 0 or > 255 || blue is < 0 or > 255) return;
        RequestControllerLedMutation(_controllerLed with
        {
            Red = (byte)(red ?? _controllerLed.Red),
            Green = (byte)(green ?? _controllerLed.Green),
            Blue = (byte)(blue ?? _controllerLed.Blue),
        });
    }

    private void RequestControllerLedMutation(ControllerLedSettings settings)
    {
        _controllerLedMutationInFlight = true;
        _pendingControllerLedEdit = new(_controllerLed, settings);
        RenderControllerLedRows();
        ControllerLedEditRequested?.Invoke(settings);
    }

    private void RenderControllerLedRows()
    {
        var displaySettings = ProjectControllerLedPendingEdit(_controllerLed, _pendingControllerLedEdit);
        if (_controllerLedSectionCard is not null)
            _controllerLedSectionCard.Visibility = _controllerLedAvailable ? Visibility.Visible : Visibility.Collapsed;
        if (_controllerLedDetailStack is not null)
            _controllerLedDetailStack.Visibility = _controllerLed.Enabled ? Visibility.Visible : Visibility.Collapsed;
        var available = _controllerLedAvailable && !_controllerLedMutationInFlight;
        _controllerLedEnabledRow?.ApplyState(available, displaySettings.Enabled);
        _controllerLedBrightnessRow?.ApplyState(available && _controllerLed.Enabled, 0, 100, 1, displaySettings.Brightness);
        var colorAvailable = available && _controllerLed.Enabled;
        _controllerLedRedRow?.ApplyState(colorAvailable, 0, 255, 1, displaySettings.Red);
        _controllerLedGreenRow?.ApplyState(colorAvailable, 0, 255, 1, displaySettings.Green);
        _controllerLedBlueRow?.ApplyState(colorAvailable, 0, 255, 1, displaySettings.Blue);
        if (_controllerLedColorSwatch is not null)
            RenderControllerLedColor(Color.FromArgb(255, displaySettings.Red, displaySettings.Green, displaySettings.Blue));
        if (_tabState.SelectedTab == AddonQuickSettingsTabId.Controller)
        {
            var preferredIndex = _rowSelection.SelectedIndex;
            _rowSelection.SetRows(CapabilitiesFor(AddonQuickSettingsTabId.Controller), preferredIndex);
            ApplyRowSelectionVisual();
        }
    }

    private void RenderControllerLedColor(Color color)
    {
        if (_controllerLedColorSwatch is not null)
            _controllerLedColorSwatch.Background = new SolidColorBrush(color);
    }

    private static string FormatPercent(double value) => $"{OverlayValueRow.FormatInteger(value)}%";

    private void RequestVibrationEdit(int? left, int? right)
    {
        if (!_vibrationSnapshot.Available || !_vibrationSnapshot.Writable || _controllerVibrationMutationInFlight) return;
        _vibrationDraftLeft = left ?? _vibrationDraftLeft ?? _vibrationSnapshot.LeftPercent;
        _vibrationDraftRight = right ?? _vibrationDraftRight ?? _vibrationSnapshot.RightPercent;
        if (_vibrationDraftLeft is not { } targetLeft || _vibrationDraftRight is not { } targetRight) return;
        _vibrationDraftDirty = true;
        RenderControllerVibrationRows();
        CancelVibrationCommitDelay();
        var delay = new CancellationTokenSource();
        _vibrationCommitDelay = delay;
        _ = CommitVibrationAfterDelayAsync(targetLeft, targetRight, delay.Token);
    }

    internal void FlushPendingControllerVibrationEdit()
    {
        if (!_vibrationDraftDirty || _controllerVibrationMutationInFlight) return;
        if (!_vibrationSnapshot.Available || !_vibrationSnapshot.Writable
            || _vibrationDraftLeft is not { } left || _vibrationDraftRight is not { } right)
        {
            CancelPendingControllerVibrationDraft();
            return;
        }

        CancelVibrationCommitDelay();
        SubmitControllerVibrationPair(left, right);
    }

    internal void CancelPendingControllerVibrationDraft()
    {
        if (!_vibrationDraftDirty) return;
        CancelVibrationCommitDelay();
        _vibrationDraftDirty = false;
        _vibrationDraftLeft = _vibrationSnapshot.LeftPercent;
        _vibrationDraftRight = _vibrationSnapshot.RightPercent;
        RenderControllerVibrationRows();
    }

    private void CancelVibrationCommitDelay()
    {
        _vibrationCommitDelay?.Cancel();
        _vibrationCommitDelay?.Dispose();
        _vibrationCommitDelay = null;
    }

    private async Task CommitVibrationAfterDelayAsync(int left, int right, CancellationToken token)
    {
        try
        {
            await Task.Delay(300, token);
            if (token.IsCancellationRequested || !_vibrationDraftDirty || _controllerVibrationMutationInFlight)
                return;
            if (!_vibrationSnapshot.Available || !_vibrationSnapshot.Writable)
            {
                CancelPendingControllerVibrationDraft();
                return;
            }

            CancelVibrationCommitDelay();
            SubmitControllerVibrationPair(left, right);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
    }

    private void SubmitControllerVibrationPair(int left, int right)
    {
        if (!_vibrationDraftDirty || _controllerVibrationMutationInFlight) return;
        _vibrationDraftDirty = false;
        _controllerVibrationMutationInFlight = true;
        RenderControllerVibrationRows();
        ControllerVibrationStrengthEditRequested?.Invoke(left, right);
    }

    internal void ApplyControllerVibrationState(FrontendControllerVibrationStrengthSnapshot snapshot)
    {
        if (_controllerVibrationMutationInFlight)
        {
            RenderControllerVibrationRows();
            return;
        }

        _vibrationSnapshot = snapshot;
        if (!_vibrationDraftDirty)
        {
            _vibrationDraftLeft = snapshot.LeftPercent;
            _vibrationDraftRight = snapshot.RightPercent;
        }
        RenderControllerVibrationRows();
    }

    internal void ApplyControllerVibrationMutationResult(FrontendControllerVibrationStrengthMutationResult result)
    {
        _controllerVibrationMutationInFlight = false;
        _vibrationDraftDirty = false;
        _vibrationSnapshot = result.Snapshot;
        _vibrationDraftLeft = result.Snapshot.LeftPercent;
        _vibrationDraftRight = result.Snapshot.RightPercent;
        RenderControllerVibrationRows();
    }

    internal void ApplyControllerVibrationMutationFailure()
    {
        _controllerVibrationMutationInFlight = false;
        _vibrationDraftDirty = false;
        _vibrationDraftLeft = _vibrationSnapshot.LeftPercent;
        _vibrationDraftRight = _vibrationSnapshot.RightPercent;
        RenderControllerVibrationRows();
    }

    private void RenderControllerVibrationRows()
    {
        var enabled = _vibrationSnapshot.Available && _vibrationSnapshot.Writable && !_controllerVibrationMutationInFlight;
        var left = _vibrationDraftLeft ?? _vibrationSnapshot.LeftPercent;
        var right = _vibrationDraftRight ?? _vibrationSnapshot.RightPercent;
        _leftVibrationRow?.ApplyState(enabled && left is not null, 0, 100, 1, left ?? double.NaN);
        _rightVibrationRow?.ApplyState(enabled && right is not null, 0, 100, 1, right ?? double.NaN);
        if (_tabState.SelectedTab == AddonQuickSettingsTabId.Controller)
        {
            var preferredIndex = _rowSelection.SelectedIndex;
            _rowSelection.SetRows(CapabilitiesFor(AddonQuickSettingsTabId.Controller), preferredIndex);
            ApplyRowSelectionVisual();
        }
    }

    internal static BackButtonMappingSettings CreateBackButtonMappingCandidate(
        BackButtonMappingSettings current,
        bool isM1,
        Xbox360BackButtonTarget target) => isM1
            ? current with { M1 = target }
            : current with { M2 = target };

    internal static BackButtonMappingSettings ProjectBackButtonMappingPreview(
        BackButtonMappingSettings current,
        OverlayBackButtonMappingPreview? preview) => preview is not { } pending
            ? current
            : pending.IsM1
                ? current with { M1 = pending.Target }
                : current with { M2 = pending.Target };

    internal static ControllerLedSettings ProjectControllerLedPendingEdit(
        ControllerLedSettings current,
        OverlayControllerLedPendingEdit? pending)
    {
        if (pending is not { } edit) return current;
        return new ControllerLedSettings(
            edit.Desired.Enabled != edit.Baseline.Enabled ? edit.Desired.Enabled : current.Enabled,
            edit.Desired.Brightness != edit.Baseline.Brightness ? edit.Desired.Brightness : current.Brightness,
            edit.Desired.Red != edit.Baseline.Red ? edit.Desired.Red : current.Red,
            edit.Desired.Green != edit.Baseline.Green ? edit.Desired.Green : current.Green,
            edit.Desired.Blue != edit.Baseline.Blue ? edit.Desired.Blue : current.Blue);
    }

    internal static string FormatBackButtonTarget(double value)
    {
        if (!double.IsFinite(value)
            || value < (double)Xbox360BackButtonTarget.Disabled
            || value > (double)Xbox360BackButtonTarget.XboxGuide
            || value != Math.Truncate(value))
            return "--";
        var target = (Xbox360BackButtonTarget)(int)value;
        return Enum.IsDefined(target) ? BackButtonMappingLabels.GetDisplayName(target) : "--";
    }
}
