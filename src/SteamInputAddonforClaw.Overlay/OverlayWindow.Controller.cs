using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SteamInputAddonforClaw.Contracts.BackButtons;
using SteamInputAddonforClaw.Contracts.Frontend;
using SteamInputAddonforClaw.FrontendTransport;

namespace SteamInputAddonforClaw.Overlay;

public sealed partial class OverlayWindow
{
    private const string BackButtonMappingCaption =
        "Xbox 360 mode only. Steam Game / Big Picture keeps M1 as R4 and M2 as L4.";

    private BackButtonMappingSettings _backButtonMapping = BackButtonMappingSettings.Default;
    private bool _backButtonMappingAvailable;
    private bool _backButtonMutationInFlight;
    private OverlayValueRow? _m1MappingRow;
    private OverlayValueRow? _m2MappingRow;
    private TextBlock? _backButtonStatusText;

    internal event Action<BackButtonMappingSettings>? BackButtonMappingEditRequested;

    private FrameworkElement BuildControllerPage(List<OverlayRow> rows)
    {
        var section = new StackPanel { Spacing = 8 };
        section.Children.Add(new TextBlock { Text = "M1 / M2", FontWeight = FontWeights.SemiBold });
        _backButtonStatusText = CreateStatusText(BackButtonMappingCaption);
        section.Children.Add(_backButtonStatusText);

        var m1Row = new OverlayValueRow("M1", FormatBackButtonTarget,
            value => RequestBackButtonMappingChange(isM1: true, value),
            OverlayValueButtonKind.DiscreteChoice);
        var m2Row = new OverlayValueRow("M2", FormatBackButtonTarget,
            value => RequestBackButtonMappingChange(isM1: false, value),
            OverlayValueButtonKind.DiscreteChoice);
        _m1MappingRow = m1Row;
        _m2MappingRow = m2Row;

        AddBackButtonMappingRow(section, rows, m1Row);
        AddBackButtonMappingRow(section, rows, m2Row);
        RenderBackButtonMappingRows();
        return CreateOverlaySectionCard(section);
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
        if (_backButtonStatusText is not null) _backButtonStatusText.Text = "Updating M1 / M2 mapping…";
        RenderBackButtonMappingRows();
        BackButtonMappingEditRequested?.Invoke(candidate);
    }

    internal void ApplyBackButtonMappingState(OverlayBackButtonMappingState state)
    {
        _backButtonMapping = state.Mapping;
        _backButtonMappingAvailable = state.Available;
        // A StateInvalidated snapshot may arrive before the correlated mutation result. Keep the
        // controls disabled until that result settles the one outstanding whole-record request.
        var mutationPending = _backButtonMutationInFlight;
        if (_backButtonStatusText is not null)
            _backButtonStatusText.Text = state.FailureMessage
                ?? (mutationPending ? "Updating M1 / M2 mapping…"
                    : state.Available ? BackButtonMappingCaption : "M1 / M2 mapping is unavailable.");
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
        if (_backButtonStatusText is not null)
            _backButtonStatusText.Text = string.IsNullOrWhiteSpace(message)
                ? "M1 / M2 mapping update failed."
                : message;
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
            if (!_backButtonMutationInFlight)
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
