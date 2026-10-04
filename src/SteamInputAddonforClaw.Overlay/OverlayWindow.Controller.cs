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

public sealed partial class OverlayWindow
{
    private BackButtonMappingSettings _backButtonMapping = BackButtonMappingSettings.Default;
    private bool _backButtonMappingAvailable;
    private bool _backButtonMutationInFlight;
    private OverlayValueRow? _m1MappingRow;
    private OverlayValueRow? _m2MappingRow;
    private FrontendSettingsSnapshot? _frontendSettingsSnapshot;
    private bool _controllerLedAvailable;
    private bool _controllerLedMutationInFlight;
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
    internal event Action<int, int>? ControllerVibrationStrengthEditRequested;

    private FrameworkElement BuildControllerPage(List<OverlayRow> rows)
    {
        var page = new StackPanel { Spacing = OverlayQamResources.Get("QamSectionSpacing", 24.0) };
        var mappingSection = CreateControllerSection("M1 / M2");
        var ledSection = new StackPanel { Spacing = OverlayQamResources.Get("QamSectionHeaderSpacing", 4.0) };
        var vibrationSection = CreateControllerSection("Vibration Strength");

        var m1Row = new OverlayValueRow("M1", FormatBackButtonTarget,
            value => RequestBackButtonMappingChange(isM1: true, value),
            OverlayValueButtonKind.DiscreteChoice);
        var m2Row = new OverlayValueRow("M2", FormatBackButtonTarget,
            value => RequestBackButtonMappingChange(isM1: false, value),
            OverlayValueButtonKind.DiscreteChoice);
        _m1MappingRow = m1Row;
        _m2MappingRow = m2Row;

        AddBackButtonMappingRow(mappingSection, rows, m1Row);
        AddBackButtonMappingRow(mappingSection, rows, m2Row);
        RenderBackButtonMappingRows();

        BuildControllerLedRows(ledSection, rows);
        BuildControllerVibrationRows(vibrationSection, rows);
        page.Children.Add(CreateOverlaySectionCard(mappingSection));
        _controllerLedSectionCard = CreateOverlaySectionCard(ledSection);
        page.Children.Add(_controllerLedSectionCard);
        page.Children.Add(CreateOverlaySectionCard(vibrationSection));
        RenderControllerLedRows();
        return page;
    }

    private static StackPanel CreateControllerSection(string title)
    {
        var section = new StackPanel { Spacing = OverlayQamResources.Get("QamSectionHeaderSpacing", 4.0) };
        var heading = new TextBlock { Text = title };
        OverlayQamResources.ApplyTextStyle(heading, "QamBodyStrongTextStyle");
        section.Children.Add(heading);
        return section;
    }

    private void BuildControllerLedRows(StackPanel section, List<OverlayRow> rows)
    {
        _controllerLedEnabledRow = new OverlayToggleRow("Joystick LED", RequestControllerLedEnabled);
        AddControllerRow(section, rows, _controllerLedEnabledRow.Container, _controllerLedEnabledRow.Capabilities);

        var details = new StackPanel
        {
            Spacing = OverlayQamResources.Get("QamSectionHeaderSpacing", 4.0),
            Margin = OverlayQamResources.Get("QamDetailIndent", new Thickness(16, 0, 0, 0)),
        };
        _controllerLedDetailStack = details;

        _controllerLedBrightnessRow = new OverlayValueRow("Brightness", OverlayValueRow.FormatInteger,
            value => RequestControllerLedBrightness((int)Math.Round(value)), OverlayValueButtonKind.NumericStepper);
        AddControllerRow(details, rows, _controllerLedBrightnessRow.Container, _controllerLedBrightnessRow.Capabilities);

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
        AddControllerRow(details, rows, _controllerLedRedRow.Container, _controllerLedRedRow.Capabilities);
        AddControllerRow(details, rows, _controllerLedGreenRow.Container, _controllerLedGreenRow.Capabilities);
        AddControllerRow(details, rows, _controllerLedBlueRow.Container, _controllerLedBlueRow.Capabilities);
        section.Children.Add(details);
        RenderControllerLedRows();
    }

    private void BuildControllerVibrationRows(StackPanel section, List<OverlayRow> rows)
    {
        var details = new StackPanel
        {
            Spacing = OverlayQamResources.Get("QamSectionHeaderSpacing", 4.0),
            Margin = OverlayQamResources.Get("QamDetailIndent", new Thickness(16, 0, 0, 0)),
        };
        _leftVibrationRow = new OverlayValueRow("Left Motor", FormatPercent,
            value => RequestVibrationEdit(left: (int)Math.Round(value), right: null), OverlayValueButtonKind.NumericStepper);
        _rightVibrationRow = new OverlayValueRow("Right Motor", FormatPercent,
            value => RequestVibrationEdit(left: null, right: (int)Math.Round(value)), OverlayValueButtonKind.NumericStepper);
        AddControllerRow(details, rows, _leftVibrationRow.Container, _leftVibrationRow.Capabilities);
        AddControllerRow(details, rows, _rightVibrationRow.Container, _rightVibrationRow.Capabilities);
        section.Children.Add(details);
        RenderControllerVibrationRows();
    }

    private void AddControllerRow(StackPanel section, List<OverlayRow> rows, Border container, OverlayRowCapabilities capabilities)
    {
        section.Children.Add(container);
        rows.Add(new(container, capabilities));
        RegisterRowPointerSelection(container);
    }

    private void AddBackButtonMappingRow(StackPanel section, List<OverlayRow> rows, OverlayValueRow row)
    {
        section.Children.Add(row.Container);
        rows.Add(new(row.Container, row.Capabilities));
        RegisterRowPointerSelection(row.Container);
    }

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
        RenderBackButtonMappingRows();
    }

    private void RenderBackButtonMappingRows()
    {
        var enabled = _backButtonMappingAvailable && !_backButtonMutationInFlight;
        var minimum = (double)Xbox360BackButtonTarget.Disabled;
        var maximum = (double)Xbox360BackButtonTarget.XboxGuide;
        _m1MappingRow?.ApplyState(enabled, minimum, maximum, 1, (double)_backButtonMapping.M1);
        _m2MappingRow?.ApplyState(enabled, minimum, maximum, 1, (double)_backButtonMapping.M2);

        if (_tabState.SelectedTab == AddonQuickSettingsTabId.Controller)
        {
            var preferredIndex = _rowSelection.SelectedIndex;
            if (!_backButtonMutationInFlight && !_controllerLedMutationInFlight && !_controllerVibrationMutationInFlight)
                _rowSelection.SetRows(CapabilitiesFor(AddonQuickSettingsTabId.Controller), preferredIndex);
            ApplyRowSelectionVisual();
        }
    }

    internal void ApplyFrontendSettingsState(FrontendSettingsSnapshot settings, bool controllerLedAvailable, bool settingsAvailable)
    {
        _frontendSettingsSnapshot = settings;
        _controllerLedAvailable = settingsAvailable && controllerLedAvailable;
        _controllerLed = settings.ControllerLed;
        RenderControllerLedRows();
        ApplyQuickSettingsCurrentPowerSourceOnly(settings.QuickSettingsCurrentPowerSourceOnly, settingsAvailable);
    }

    internal void ApplyFrontendSettingsMutationResult(OverlayFrontendSettingsMutationResponse response, bool led)
    {
        if (led) _controllerLedMutationInFlight = false;
        else SetQuickSettingsCurrentPowerSourceMutationInFlight(false);
        ApplyFrontendSettingsState(response.Settings, response.ControllerLedAvailable, response.SettingsAvailable);
    }

    internal void ApplyFrontendSettingsMutationFailure(bool led)
    {
        if (led) _controllerLedMutationInFlight = false;
        else SetQuickSettingsCurrentPowerSourceMutationInFlight(false);
        RenderControllerLedRows();
        RenderQuickSettingsCurrentPowerSourceRow();
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
        RenderControllerLedRows();
        ControllerLedEditRequested?.Invoke(settings);
    }

    private void RenderControllerLedRows()
    {
        if (_controllerLedSectionCard is not null)
            _controllerLedSectionCard.Visibility = _controllerLedAvailable ? Visibility.Visible : Visibility.Collapsed;
        if (_controllerLedDetailStack is not null)
            _controllerLedDetailStack.Visibility = _controllerLed.Enabled ? Visibility.Visible : Visibility.Collapsed;
        var available = _controllerLedAvailable && !_controllerLedMutationInFlight;
        _controllerLedEnabledRow?.ApplyState(available, _controllerLed.Enabled);
        _controllerLedBrightnessRow?.ApplyState(available && _controllerLed.Enabled, 0, 100, 1, _controllerLed.Brightness);
        var colorAvailable = available && _controllerLed.Enabled;
        _controllerLedRedRow?.ApplyState(colorAvailable, 0, 255, 1, _controllerLed.Red);
        _controllerLedGreenRow?.ApplyState(colorAvailable, 0, 255, 1, _controllerLed.Green);
        _controllerLedBlueRow?.ApplyState(colorAvailable, 0, 255, 1, _controllerLed.Blue);
        if (_controllerLedColorSwatch is not null)
            RenderControllerLedColor(Color.FromArgb(255, _controllerLed.Red, _controllerLed.Green, _controllerLed.Blue));
        if (_tabState.SelectedTab == AddonQuickSettingsTabId.Controller)
        {
            var preferredIndex = _rowSelection.SelectedIndex;
            if (!_controllerLedMutationInFlight && !_backButtonMutationInFlight && !_controllerVibrationMutationInFlight)
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
            if (!_controllerVibrationMutationInFlight && !_backButtonMutationInFlight && !_controllerLedMutationInFlight)
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

    internal static string FormatBackButtonTarget(double value)
    {
        if (!double.IsFinite(value)
            || value < (double)Xbox360BackButtonTarget.Disabled
            || value > (double)Xbox360BackButtonTarget.XboxGuide
            || value != Math.Truncate(value))
            return "--";
        return ((Xbox360BackButtonTarget)(int)value) switch
        {
            Xbox360BackButtonTarget.Disabled => "Disabled",
            Xbox360BackButtonTarget.A => "A",
            Xbox360BackButtonTarget.B => "B",
            Xbox360BackButtonTarget.X => "X",
            Xbox360BackButtonTarget.Y => "Y",
            Xbox360BackButtonTarget.DPadUp => "D-Pad Up",
            Xbox360BackButtonTarget.DPadRight => "D-Pad Right",
            Xbox360BackButtonTarget.DPadDown => "D-Pad Down",
            Xbox360BackButtonTarget.DPadLeft => "D-Pad Left",
            Xbox360BackButtonTarget.LeftBumper => "Left Bumper (LB)",
            Xbox360BackButtonTarget.RightBumper => "Right Bumper (RB)",
            Xbox360BackButtonTarget.LeftTrigger => "Left Trigger (LT)",
            Xbox360BackButtonTarget.RightTrigger => "Right Trigger (RT)",
            Xbox360BackButtonTarget.LeftStickClick => "Left Stick Click (L3)",
            Xbox360BackButtonTarget.RightStickClick => "Right Stick Click (R3)",
            Xbox360BackButtonTarget.View => "View",
            Xbox360BackButtonTarget.Menu => "Menu",
            Xbox360BackButtonTarget.XboxGuide => "Xbox Guide",
            _ => "--",
        };
    }
}
