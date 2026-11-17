using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SteamInputAddonforClaw.Contracts.Frontend;
using SteamInputAddonforClaw.Overlay.Diagnostics;

namespace SteamInputAddonforClaw.Overlay;

public sealed partial class OverlayWindow
{
    private enum SettingDetailId { ClawHud, QuickSettings, TabOrder }

    private readonly List<OverlayRow> _settingRootRows = new();
    private readonly List<OverlayRow> _clawHudRows = new();
    private readonly List<OverlayRow> _quickSettingsRows = new();
    private FrameworkElement? _settingRootView;
    private OverlayToggleRow? _clawHudEnabledRow;
    private OverlayValueRow? _clawHudDisplayModeRow;
    private OverlayValueRow? _clawHudSizeRow;
    private OverlayValueRow? _clawHudFontRow;
    private OverlayValueRow? _clawHudAlignmentRow;
    private OverlayValueRow? _clawHudBackgroundRow;
    private OverlayValueRow? _clawHudOpacityRow;
    private OverlayToggleRow? _clawHudVrrRow;
    private OverlayValueRow? _quickSettingsYButtonQuickJumpRow;
    private QuickOverlayYButtonTarget _quickSettingsYButtonQuickJumpTarget;
    private QuickOverlayYButtonTarget? _pendingYButtonQuickJumpTarget;
    private bool _quickSettingsYButtonQuickJumpAvailable;
    private bool _yButtonQuickJumpMutationInFlight;
    private OverlayToggleRow? _quickSettingsCurrentPowerSourceOnlyRow;
    private bool _quickSettingsCurrentPowerSourceOnly;
    private bool? _pendingQuickSettingsCurrentPowerSourceOnly;
    private OverlayToggleRow? _quickSettingsSimplifiedTdpControlsRow;
    private bool _quickSettingsSimplifiedTdpControls;
    private bool? _pendingQuickSettingsSimplifiedTdpControls;
    private bool _quickSettingsSettingsAvailable;
    private bool _quickSettingsMutationInFlight;
    private readonly OverlayClawHudMutationState _clawHudMutation = new();

    internal event Action<bool>? ClawHudEnabledRequested;
    internal event Action<FrontendClawHudMutationIntent>? ClawHudSettingMutationRequested;
    internal event Action<bool>? QuickSettingsCurrentPowerSourceOnlyRequested;
    internal event Action<bool>? SimplifiedTdpControlsRequested;
    internal event Action<QuickOverlayYButtonTarget>? YButtonQuickJumpTargetRequested;

    private FrameworkElement BuildSettingPage(List<OverlayRow> rows)
    {
        _settingRootRows.Clear();
        _clawHudRows.Clear();
        _quickSettingsRows.Clear();

        var clawHudDetailView = CreateSettingDetailPage(BuildClawHudPage());
        var quickSettingsDetailView = CreateSettingDetailPage(BuildQuickSettingsPage());
        var tabOrderDetailView = CreateSettingDetailPage(BuildTabOrderEditorPage());

        var settingRootView = new StackPanel { Spacing = OverlayQamResources.Get("QamSectionSpacing", 0.0) };
        _settingRootView = settingRootView;
        _settingRootRows.Add(CreateSettingRootRow(
            SettingDetailId.ClawHud, "ClawHUD", clawHudDetailView, () => _clawHudRows));
        _settingRootRows.Add(CreateSettingRootRow(
            SettingDetailId.QuickSettings, "Quick Settings", quickSettingsDetailView, () => _quickSettingsRows));
        _settingRootRows.Add(CreateSettingRootRow(
            SettingDetailId.TabOrder, "Quick Overlay Tab Order", tabOrderDetailView,
            () => BuildTabOrderDetailRows(_tabState.Order)));

        SetOverlaySectionSeparator(_settingRootRows[0].Container, visible: false);
        SetOverlaySectionSeparator(_settingRootRows[1].Container, visible: true);
        SetOverlaySectionSeparator(_settingRootRows[2].Container, visible: true);

        foreach (var row in _settingRootRows)
            settingRootView.Children.Add(row.Container);

        var host = new Grid();
        host.Children.Add(settingRootView);
        host.Children.Add(clawHudDetailView);
        host.Children.Add(quickSettingsDetailView);
        host.Children.Add(tabOrderDetailView);
        rows.AddRange(_settingRootRows);
        return host;
    }

    private FrameworkElement CreateSettingDetailPage(FrameworkElement content)
    {
        var detailPage = CreateOverlaySectionCard(content);
        SetOverlaySectionSeparator((Border)detailPage, visible: false);
        detailPage.Visibility = Visibility.Collapsed;
        return detailPage;
    }

    private OverlayRow CreateSettingRootRow(
        SettingDetailId id,
        string title,
        FrameworkElement detailView,
        Func<IReadOnlyList<OverlayRow>> detailRows)
    {
        var (container, headerButton) = CreateDetailMenuChrome(title, $"Open {title} settings");
        var row = new OverlayRow(container, new OverlayRowCapabilities(
            IsSelectable: () => true,
            Activate: () => OpenSettingDetail(id, detailView, detailRows, container, title)));
        headerButton.Click += (_, _) => OpenSettingDetail(id, detailView, detailRows, container, title);
        RegisterRowPointerSelection(container);
        return row;
    }

    private static (Border Container, Button Button) CreateDetailMenuChrome(string label, string automationName)
    {
        var titleText = new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap };
        OverlayQamResources.ApplyTextStyle(titleText, "QamBodyStrongTextStyle");

        var chevron = new TextBlock
        {
            Text = "›",
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(12, 0, 4, 0),
        };
        OverlayQamResources.ApplyTextStyle(chevron, "QamBodyStrongTextStyle");

        var headerGrid = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                new ColumnDefinition { Width = GridLength.Auto },
            },
        };
        headerGrid.Children.Add(titleText);
        Grid.SetColumn(chevron, 1);
        headerGrid.Children.Add(chevron);

        var button = new Button
        {
            Content = headerGrid,
            Style = OverlayQamResources.Style("QamFlatButtonStyle"),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Padding = OverlayQamResources.Get("QamRowPadding", new Thickness(16, 10, 16, 10)),
            MinHeight = OverlayQamResources.Get("QamRowMinHeight", 42.0),
            BorderThickness = new Thickness(0),
            Background = new SolidColorBrush(Colors.Transparent),
            IsTabStop = false,
            AllowFocusOnInteraction = false,
        };
        AutomationProperties.SetName(button, automationName);

        var header = OverlayRowChrome.Create(button);
        header.Padding = new Thickness(0);
        header.Background = new SolidColorBrush(Colors.Transparent);
        return (CreateOverlaySectionCard(header), button);
    }

    private void OpenSettingDetail(
        SettingDetailId id,
        FrameworkElement detailView,
        Func<IReadOnlyList<OverlayRow>> detailRows,
        Border originContainer,
        string title)
    {
        if (_settingRootView is null) return;
        EnterDetailPage(
            AddonQuickSettingsTabId.Setting,
            id,
            _settingRootView,
            detailView,
            detailRows(),
            originContainer,
            title);
    }

    private List<OverlayRow> BuildTabOrderDetailRows(IReadOnlyList<AddonQuickSettingsTabId> order)
    {
        var rows = new List<OverlayRow>(order.Count);
        foreach (var tab in order)
        {
            if (_tabOrderRows.TryGetValue(tab, out var row))
                rows.Add(CreateSettingDetailRow(new OverlayRow(row.Container, row.Capabilities), SettingDetailId.TabOrder));
        }
        return rows;
    }

    private OverlayRow CreateSettingDetailRow(OverlayRow row, SettingDetailId detail)
    {
        var capabilities = row.Capabilities;
        return new OverlayRow(row.Container, new OverlayRowCapabilities(
            IsSelectable: () => IsSettingDetailActive(detail) &&
                (capabilities.IsSelectable() || IsNavigableDuringPendingSettingMutation(detail, row.Container)),
            Activate: capabilities.Activate,
            Adjust: capabilities.Adjust));
    }

    private bool IsSettingDetailActive(SettingDetailId detail) =>
        _activeDetailPage is { Tab: AddonQuickSettingsTabId.Setting } active && active.Identity.Equals(detail);

    private bool IsNavigableDuringPendingSettingMutation(SettingDetailId detail, Border container)
    {
        if (!IsSettingDetailActive(detail))
            return false;

        if (detail == SettingDetailId.ClawHud && _clawHudMutation.IsInFlight)
        {
            var snapshot = _clawHudMutation.Snapshot;
            return ReferenceEquals(container, _clawHudEnabledRow?.Container)
                ? snapshot is not null
                : snapshot?.RuntimeState == FrontendClawHudRuntimeState.Ready &&
                    snapshot.Settings is not null &&
                    _clawHudRows.Any(row => ReferenceEquals(row.Container, container));
        }

        if (!_pageRows.TryGetValue(AddonQuickSettingsTabId.Setting, out var rows) ||
            _rowSelection.SelectedIndex is not { } selectedIndex ||
            selectedIndex < 0 || selectedIndex >= rows.Count ||
            !ReferenceEquals(rows[selectedIndex].Container, container))
            return false;

        return detail == SettingDetailId.QuickSettings && _quickSettingsMutationInFlight &&
            _quickSettingsSettingsAvailable &&
            (ReferenceEquals(container, _quickSettingsCurrentPowerSourceOnlyRow?.Container) ||
             ReferenceEquals(container, _quickSettingsSimplifiedTdpControlsRow?.Container));
    }

    private StackPanel BuildQuickSettingsPage()
    {
        var section = new StackPanel { Spacing = OverlayQamResources.Get("QamRowSpacing", 0.0) };
        _quickSettingsYButtonQuickJumpRow = new OverlayValueRow(
            "Y Button Quick Jump",
            FormatYButtonQuickJumpChoice,
            value => RequestYButtonQuickJumpChange((QuickOverlayYButtonTarget)(int)Math.Round(value)),
            OverlayValueButtonKind.DiscreteChoice);
        OverlayRowChrome.ApplyDetailPadding(_quickSettingsYButtonQuickJumpRow.Container);
        section.Children.Add(_quickSettingsYButtonQuickJumpRow.Container);
        _quickSettingsRows.Add(CreateSettingDetailRow(
            new OverlayRow(_quickSettingsYButtonQuickJumpRow.Container, _quickSettingsYButtonQuickJumpRow.Capabilities),
            SettingDetailId.QuickSettings));
        RegisterRowPointerSelection(_quickSettingsYButtonQuickJumpRow.Container);

        _quickSettingsCurrentPowerSourceOnlyRow = new OverlayToggleRow(
            "Show only current power source", RequestQuickSettingsCurrentPowerSourceOnly);
        OverlayRowChrome.ApplyDetailPadding(_quickSettingsCurrentPowerSourceOnlyRow.Container);
        section.Children.Add(_quickSettingsCurrentPowerSourceOnlyRow.Container);
        _quickSettingsRows.Add(CreateSettingDetailRow(
            new OverlayRow(_quickSettingsCurrentPowerSourceOnlyRow.Container, _quickSettingsCurrentPowerSourceOnlyRow.Capabilities),
            SettingDetailId.QuickSettings));
        RegisterRowPointerSelection(_quickSettingsCurrentPowerSourceOnlyRow.Container);

        _quickSettingsSimplifiedTdpControlsRow = new OverlayToggleRow(
            "Simplified TDP Controls", RequestSimplifiedTdpControls);
        OverlayRowChrome.ApplyDetailPadding(_quickSettingsSimplifiedTdpControlsRow.Container);
        section.Children.Add(_quickSettingsSimplifiedTdpControlsRow.Container);
        _quickSettingsRows.Add(CreateSettingDetailRow(
            new OverlayRow(_quickSettingsSimplifiedTdpControlsRow.Container, _quickSettingsSimplifiedTdpControlsRow.Capabilities),
            SettingDetailId.QuickSettings));
        RegisterRowPointerSelection(_quickSettingsSimplifiedTdpControlsRow.Container);
        return section;
    }

    internal void ApplyQuickSettingsCurrentPowerSourceOnly(bool enabled, bool available)
    {
        _quickSettingsCurrentPowerSourceOnly = enabled;
        _quickSettingsSettingsAvailable = available;
        RenderQuickSettingsCurrentPowerSourceRow();
    }

    internal void ApplySimplifiedTdpControls(bool enabled, bool available)
    {
        _quickSettingsSimplifiedTdpControls = enabled;
        _quickSettingsSettingsAvailable = available;
        RenderSimplifiedTdpControlsRow();
    }

    internal void ApplyQuickSettingsYButtonQuickJumpTarget(QuickOverlayYButtonTarget target, bool available)
    {
        if (!Enum.IsDefined(target))
            available = false;
        else
            _quickSettingsYButtonQuickJumpTarget = target;
        _quickSettingsYButtonQuickJumpAvailable = available;
        RenderQuickSettingsYButtonQuickJumpRow();
    }

    private static string FormatYButtonQuickJumpChoice(double value) => (int)value switch
    {
        (int)QuickOverlayYButtonTarget.Profile => "Profile",
        (int)QuickOverlayYButtonTarget.Shortcut => "Shortcut",
        (int)QuickOverlayYButtonTarget.Device => "Device",
        (int)QuickOverlayYButtonTarget.Controller => "Controller",
        (int)QuickOverlayYButtonTarget.Setting => "Setting",
        (int)QuickOverlayYButtonTarget.Off => "Off",
        _ => "--",
    };

    private void ApplyYButtonQuickJumpFooterTarget(QuickOverlayYButtonTarget target, bool available) =>
        YButtonFooterTargetText.Text = FormatYButtonQuickJumpFooterTarget(target, available);

    internal static string FormatYButtonQuickJumpFooterTarget(QuickOverlayYButtonTarget target, bool available) =>
        available && Enum.IsDefined(target)
            ? FormatYButtonQuickJumpChoice((double)target)
            : "—";

    private void RequestYButtonQuickJumpChange(QuickOverlayYButtonTarget target)
    {
        if (!_quickSettingsYButtonQuickJumpAvailable || _yButtonQuickJumpMutationInFlight || !Enum.IsDefined(target))
        {
            RenderQuickSettingsYButtonQuickJumpRow();
            return;
        }
        if (target == _quickSettingsYButtonQuickJumpTarget)
            return;

        _yButtonQuickJumpMutationInFlight = true;
        _pendingYButtonQuickJumpTarget = target;
        RenderQuickSettingsYButtonQuickJumpRow();
        YButtonQuickJumpTargetRequested?.Invoke(target);
    }

    private void SetYButtonQuickJumpMutationInFlight(bool inFlight)
    {
        _yButtonQuickJumpMutationInFlight = inFlight;
        if (!inFlight)
            _pendingYButtonQuickJumpTarget = null;
        RenderQuickSettingsYButtonQuickJumpRow();
    }

    internal void RenderQuickSettingsYButtonQuickJumpRow()
    {
        if (_quickSettingsYButtonQuickJumpRow is null)
            return;

        if (_frontendSettingsSnapshot is not { } settings)
        {
            // No independent UI default: keep the row unavailable until Runtime has supplied state.
            _quickSettingsYButtonQuickJumpRow.ApplyState(false, 0, 5, 1, double.NaN, canInteract: false);
            return;
        }

        var displayedTarget = _pendingYButtonQuickJumpTarget ?? settings.YButtonQuickJumpTarget;
        _quickSettingsYButtonQuickJumpRow.ApplyState(
            _quickSettingsYButtonQuickJumpAvailable,
            0,
            5,
            1,
            (double)displayedTarget,
            canInteract: _quickSettingsYButtonQuickJumpAvailable && !_yButtonQuickJumpMutationInFlight);
    }

    private void RequestQuickSettingsCurrentPowerSourceOnly(bool enabled)
    {
        if (!_quickSettingsSettingsAvailable || _quickSettingsMutationInFlight)
        {
            RenderQuickSettingsCurrentPowerSourceRow();
            return;
        }
        _quickSettingsMutationInFlight = true;
        _pendingQuickSettingsCurrentPowerSourceOnly = enabled;
        RenderQuickSettingsCurrentPowerSourceRow();
        QuickSettingsCurrentPowerSourceOnlyRequested?.Invoke(enabled);
    }

    private void RequestSimplifiedTdpControls(bool enabled)
    {
        if (!_quickSettingsSettingsAvailable || _quickSettingsMutationInFlight)
        {
            RenderSimplifiedTdpControlsRow();
            return;
        }
        _quickSettingsMutationInFlight = true;
        _pendingQuickSettingsSimplifiedTdpControls = enabled;
        RenderSimplifiedTdpControlsRow();
        SimplifiedTdpControlsRequested?.Invoke(enabled);
    }

    private void SetQuickSettingsCurrentPowerSourceMutationInFlight(bool inFlight)
    {
        _quickSettingsMutationInFlight = inFlight;
        if (!inFlight)
            _pendingQuickSettingsCurrentPowerSourceOnly = null;
        RenderQuickSettingsCurrentPowerSourceRow();
    }

    private void SetSimplifiedTdpControlsMutationInFlight(bool inFlight)
    {
        _quickSettingsMutationInFlight = inFlight;
        if (!inFlight)
            _pendingQuickSettingsSimplifiedTdpControls = null;
        RenderSimplifiedTdpControlsRow();
    }

    internal void RenderQuickSettingsCurrentPowerSourceRow()
    {
        _quickSettingsCurrentPowerSourceOnlyRow?.ApplyState(
            _quickSettingsSettingsAvailable && !_quickSettingsMutationInFlight,
            _pendingQuickSettingsCurrentPowerSourceOnly ?? _quickSettingsCurrentPowerSourceOnly);
    }

    internal void RenderSimplifiedTdpControlsRow()
    {
        _quickSettingsSimplifiedTdpControlsRow?.ApplyState(
            _quickSettingsSettingsAvailable && !_quickSettingsMutationInFlight,
            _pendingQuickSettingsSimplifiedTdpControls ?? _quickSettingsSimplifiedTdpControls);
    }

    private StackPanel BuildClawHudPage()
    {
        var section = new StackPanel { Spacing = OverlayQamResources.Get("QamRowSpacing", 0.0) };

        _clawHudEnabledRow = new OverlayToggleRow("Enable HUD", RequestClawHudEnabled);
        AddClawHudRow(section, _clawHudEnabledRow);

        _clawHudDisplayModeRow = CreateClawHudValueRow("Display Mode", ["Always", "In Game Only"], value =>
            RequestClawHudSetting(new(FrontendClawHudMutationKind.DisplayMode, DisplayMode: (FrontendClawHudDisplayMode)(int)Math.Round(value))));
        AddClawHudRow(section, _clawHudDisplayModeRow);

        _clawHudSizeRow = new OverlayValueRow("HUD Size", OverlayValueRow.FormatInteger,
            value => RequestClawHudSetting(new(FrontendClawHudMutationKind.HudSizeOffset, HudSizeOffset: (int)Math.Round(value))),
            OverlayValueButtonKind.NumericStepper);
        AddClawHudRow(section, _clawHudSizeRow);

        _clawHudFontRow = CreateClawHudValueRow("Font", ["Unispace", "Segoe UI Variable"], value =>
            RequestClawHudSetting(new(FrontendClawHudMutationKind.Font, Font: (FrontendClawHudFont)(int)Math.Round(value))));
        AddClawHudRow(section, _clawHudFontRow);

        _clawHudAlignmentRow = CreateClawHudValueRow("Alignment", ["Left", "Center", "Right"], value =>
            RequestClawHudSetting(new(FrontendClawHudMutationKind.Alignment, Alignment: (FrontendClawHudAlignment)(int)Math.Round(value))));
        AddClawHudRow(section, _clawHudAlignmentRow);

        _clawHudBackgroundRow = CreateClawHudValueRow("Background", ["Full Width", "Content Width"], value =>
            RequestClawHudSetting(new(FrontendClawHudMutationKind.BackgroundMode, BackgroundMode: (FrontendClawHudBackgroundMode)(int)Math.Round(value))));
        AddClawHudRow(section, _clawHudBackgroundRow);

        _clawHudOpacityRow = new OverlayValueRow("Opacity", value => $"{value:0}%",
            value => RequestClawHudSetting(new(FrontendClawHudMutationKind.CommitOpacity, OpacityPercent: (int)Math.Round(value))),
            OverlayValueButtonKind.NumericStepper);
        AddClawHudRow(section, _clawHudOpacityRow);

        _clawHudVrrRow = new OverlayToggleRow("Intel VRR Range Fix", desired =>
            RequestClawHudSetting(new(FrontendClawHudMutationKind.IntelVrrRangeFixEnabled, IntelVrrRangeFixEnabled: desired)));
        AddClawHudRow(section, _clawHudVrrRow);

        return section;
    }

    private void AddClawHudRow(StackPanel section, OverlayToggleRow row)
    {
        OverlayRowChrome.ApplyDetailPadding(row.Container);
        section.Children.Add(row.Container);
        _clawHudRows.Add(CreateSettingDetailRow(
            new OverlayRow(row.Container, row.Capabilities),
            SettingDetailId.ClawHud));
        RegisterRowPointerSelection(row.Container);
    }

    private void AddClawHudRow(StackPanel section, OverlayValueRow row)
    {
        OverlayRowChrome.ApplyDetailPadding(row.Container);
        section.Children.Add(row.Container);
        _clawHudRows.Add(CreateSettingDetailRow(
            new OverlayRow(row.Container, row.Capabilities),
            SettingDetailId.ClawHud));
        RegisterRowPointerSelection(row.Container);
    }

    private OverlayValueRow CreateClawHudValueRow(string label, IReadOnlyList<string> values, Action<double> request)
    {
        return new OverlayValueRow(label, value =>
        {
            var index = (int)Math.Round(value);
            return index >= 0 && index < values.Count ? values[index] : "--";
        }, request, OverlayValueButtonKind.DiscreteChoice);
    }

    internal void ApplyClawHudState(FrontendClawHudSnapshot snapshot)
    {
        var previousTopInsetPx = CurrentClawHudTopInsetPx();
        _clawHudMutation.ApplyState(snapshot);
        RenderClawHudControls();
        ReconcileRowSelectionIfNeeded(AddonQuickSettingsTabId.Setting);
        ReconcileClawHudTopInsetIfChanged(previousTopInsetPx);
    }

    internal void CompleteClawHudMutation(FrontendClawHudMutationResult result)
    {
        var previousTopInsetPx = CurrentClawHudTopInsetPx();
        _clawHudMutation.Complete(result);
        RenderClawHudControls();
        ReconcileRowSelectionIfNeeded(AddonQuickSettingsTabId.Setting);
        ReconcileClawHudTopInsetIfChanged(previousTopInsetPx);
    }

    internal void ApplyClawHudFailure(string message)
    {
        var previousTopInsetPx = CurrentClawHudTopInsetPx();
        _clawHudMutation.Fail();
        RenderClawHudControls();
        ReconcileRowSelectionIfNeeded(AddonQuickSettingsTabId.Setting);
        ReconcileClawHudTopInsetIfChanged(previousTopInsetPx);
    }

    private void RequestClawHudEnabled(bool enabled)
    {
        var previousTopInsetPx = CurrentClawHudTopInsetPx();
        if (!_clawHudMutation.TryBeginEnabled(enabled))
        {
            RenderClawHudControls();
            return;
        }
        RenderClawHudControls();
        ReconcileClawHudTopInsetIfChanged(previousTopInsetPx);
        ClawHudEnabledRequested?.Invoke(enabled);
    }

    private void RequestClawHudSetting(FrontendClawHudMutationIntent intent)
    {
        var previousTopInsetPx = CurrentClawHudTopInsetPx();
        if (!_clawHudMutation.TryBeginSetting(intent))
        {
            RenderClawHudControls();
            return;
        }
        RenderClawHudControls();
        ReconcileClawHudTopInsetIfChanged(previousTopInsetPx);
        ClawHudSettingMutationRequested?.Invoke(intent);
    }

    private int CurrentClawHudTopInsetPx() => _startupClawHudSettings.ResolveTopInsetPx(
        _clawHudMutation.PendingEnabled ?? _clawHudMutation.Snapshot?.DesiredEnabled,
        _clawHudMutation.DisplaySettings,
        _clawHudMutation.PendingIntent is { Kind: FrontendClawHudMutationKind.Font, Font: { } pendingFont }
            ? pendingFont
            : null,
        _clawHudMutation.PendingIntent is { Kind: FrontendClawHudMutationKind.HudSizeOffset, HudSizeOffset: { } pendingSize }
            ? pendingSize
            : null);

    private void ReconcileClawHudTopInsetIfChanged(int previousInsetPx)
    {
        var currentInsetPx = CurrentClawHudTopInsetPx();
        if (currentInsetPx != previousInsetPx)
            ReconcileVisibleClawHudTopInset(currentInsetPx);
    }

    private void RenderClawHudControls()
    {
        var snapshot = _clawHudMutation.Snapshot;
        var settings = _clawHudMutation.DisplaySettings;
        var available = snapshot is not null && !_clawHudMutation.IsInFlight;
        var nestedAvailable = available && snapshot!.RuntimeState == FrontendClawHudRuntimeState.Ready && settings is not null;

        _clawHudEnabledRow?.ApplyState(available, _clawHudMutation.DisplayEnabled);
        _clawHudDisplayModeRow?.ApplyState(nestedAvailable, 0, 1, 1, settings?.DisplayMode is { } display ? (int)display : 0);
        _clawHudSizeRow?.ApplyState(nestedAvailable, -2, 2, 1, settings?.HudSizeOffset ?? 0);
        _clawHudFontRow?.ApplyState(nestedAvailable, 0, 1, 1, settings?.Font is { } font ? (int)font : 0);
        _clawHudAlignmentRow?.ApplyState(nestedAvailable, 0, 2, 1, settings?.Alignment is { } alignment ? (int)alignment : 0);
        _clawHudBackgroundRow?.ApplyState(nestedAvailable, 0, 1, 1, settings?.BackgroundMode is { } background ? (int)background : 0);
        _clawHudOpacityRow?.ApplyState(nestedAvailable, 50, 100, 5, settings?.BackgroundOpacityPercent ?? 50);
        _clawHudVrrRow?.ApplyState(nestedAvailable, settings?.IntelVrrRangeFixEnabled == true);
    }
}
