using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using SteamInputAddonforClaw.Contracts.Frontend;
using SteamInputAddonforClaw.Overlay.Diagnostics;

namespace SteamInputAddonforClaw.Overlay;

public sealed partial class OverlayWindow
{
    private enum SettingCardId { ClawHud, TabOrder }

    private sealed record SettingCardView(
        SettingCardId Id,
        string Title,
        Border Container,
        StackPanel Details,
        Button HeaderButton,
        TextBlock Chevron,
        TextBlock Summary,
        OverlayRow Row);

    private readonly List<OverlayRow> _clawHudRows = new();
    private OverlayToggleRow? _clawHudEnabledRow;
    private OverlayValueRow? _clawHudDisplayModeRow;
    private OverlayValueRow? _clawHudSizeRow;
    private OverlayValueRow? _clawHudFontRow;
    private OverlayValueRow? _clawHudAlignmentRow;
    private OverlayValueRow? _clawHudBackgroundRow;
    private OverlayValueRow? _clawHudOpacityRow;
    private OverlayToggleRow? _clawHudVrrRow;
    private TextBlock? _clawHudStatusText;
    private TextBlock? _clawHudVrrStatusText;
    private SettingCardView? _clawHudCard;
    private SettingCardView? _tabOrderCard;
    private SettingCardId? _expandedSettingCard;
    private FrontendClawHudSnapshot? _clawHudSnapshot;
    private bool _clawHudMutationInFlight;

    internal event Action<bool>? ClawHudEnabledRequested;
    internal event Action<FrontendClawHudMutationIntent>? ClawHudSettingMutationRequested;

    private FrameworkElement BuildSettingPage(List<OverlayRow> rows)
    {
        _clawHudRows.Clear();

        var clawHudDetails = BuildClawHudPage();
        var tabOrderDetails = BuildTabOrderEditorPage();

        _clawHudCard = CreateSettingCard(SettingCardId.ClawHud, "ClawHUD", "Loading ClawHUD status…", clawHudDetails);
        _tabOrderCard = CreateSettingCard(SettingCardId.TabOrder, "Tab Order", string.Empty, tabOrderDetails);
        UpdateTabOrderCardSummary(_tabState.Order);

        var root = new StackPanel { Spacing = OverlayQamResources.Get("QamSectionSpacing", 24.0) };
        root.Children.Add(_clawHudCard.Container);
        root.Children.Add(_tabOrderCard.Container);
        rows.AddRange(BuildSettingRows(_tabState.Order));
        return root;
    }

    private SettingCardView CreateSettingCard(SettingCardId id, string title, string summary, FrameworkElement detailsContent)
    {
        var titleText = new TextBlock { Text = title, TextWrapping = TextWrapping.Wrap };
        OverlayQamResources.ApplyTextStyle(titleText, "QamBodyStrongTextStyle");

        var summaryText = new TextBlock
        {
            Text = summary,
            TextWrapping = TextWrapping.Wrap,
        };
        OverlayQamResources.ApplyTextStyle(summaryText, "QamCaptionTextStyle");

        var text = new StackPanel { Spacing = OverlayQamResources.Get("QamRowSpacing", 0.0) };
        text.Children.Add(titleText);
        text.Children.Add(summaryText);

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
        headerGrid.Children.Add(text);
        Grid.SetColumn(chevron, 1);
        headerGrid.Children.Add(chevron);

        var headerButton = new Button
        {
            Content = headerGrid,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Padding = new Thickness(0),
            MinHeight = 0,
            BorderThickness = new Thickness(0),
            Background = OverlayQamResources.Brush("QamSectionBrush"),
        };
        AutomationProperties.SetName(headerButton, $"{title}, expand settings");
        headerButton.Click += (_, _) => ToggleSettingCard(id);

        var header = OverlayRowChrome.Create(headerButton);
        var row = new OverlayRow(header, new OverlayRowCapabilities(
            IsSelectable: () => true,
            Activate: () => ToggleSettingCard(id)));
        RegisterRowPointerSelection(header);

        var details = new StackPanel
        {
            Spacing = OverlayQamResources.Get("QamRowSpacing", 0.0),
            Margin = OverlayQamResources.Get("QamDetailMargin", new Thickness(16, 0, 16, 0)),
            Visibility = Visibility.Collapsed,
        };
        details.Children.Add(detailsContent);

        var content = new StackPanel { Spacing = OverlayQamResources.Get("QamRowSpacing", 0.0) };
        content.Children.Add(header);
        content.Children.Add(details);

        return new SettingCardView(id, title, CreateOverlaySectionCard(content), details, headerButton, chevron, summaryText, row);
    }

    private List<OverlayRow> BuildSettingRows(IReadOnlyList<AddonQuickSettingsTabId> tabOrder)
    {
        var rows = new List<OverlayRow>();
        if (_clawHudCard is not null)
            rows.Add(_clawHudCard.Row);
        rows.AddRange(_clawHudRows);
        if (_tabOrderCard is not null)
            rows.Add(_tabOrderCard.Row);

        foreach (var id in tabOrder)
        {
            if (_tabOrderRows.TryGetValue(id, out var row))
            {
                rows.Add(CreateSettingDetailRow(
                    new OverlayRow(row.Container, row.Capabilities),
                    SettingCardId.TabOrder));
            }
        }

        return rows;
    }

    private OverlayRow CreateSettingDetailRow(OverlayRow row, SettingCardId card)
    {
        var capabilities = row.Capabilities;
        return new OverlayRow(row.Container, new OverlayRowCapabilities(
            IsSelectable: () => _expandedSettingCard == card && capabilities.IsSelectable(),
            Activate: capabilities.Activate,
            Adjust: capabilities.Adjust));
    }

    private void ToggleSettingCard(SettingCardId id)
    {
        SettingCardId? expanded = _expandedSettingCard == id ? null : id;
        SetExpandedSettingCard(expanded, GetSettingCard(id).Row.Container);
    }

    private void SetExpandedSettingCard(SettingCardId? id, Border? preferredContainer)
    {
        _expandedSettingCard = id;
        UpdateSettingCardVisual(_clawHudCard);
        UpdateSettingCardVisual(_tabOrderCard);

        if (_tabState.SelectedTab != AddonQuickSettingsTabId.Setting)
            return;

        var rows = _pageRows.TryGetValue(AddonQuickSettingsTabId.Setting, out var pageRows) ? pageRows : [];
        int? preferredIndex = preferredContainer is null
            ? null
            : FindSettingRowIndex(rows, preferredContainer);
        _rowSelection.SetRows(CapabilitiesFor(AddonQuickSettingsTabId.Setting), preferredIndex);
        ApplyRowSelectionVisual();
        BringSelectedRowIntoView();
    }

    private void UpdateSettingCardVisual(SettingCardView? card)
    {
        if (card is null)
            return;

        var expanded = _expandedSettingCard == card.Id;
        card.Details.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
        card.Chevron.Text = expanded ? "⌄" : "›";
        AutomationProperties.SetName(card.HeaderButton,
            $"{card.Title}, {(expanded ? "collapse" : "expand")} settings");
    }

    private SettingCardView GetSettingCard(SettingCardId id) => id == SettingCardId.ClawHud
        ? _clawHudCard ?? throw new InvalidOperationException("ClawHUD settings card has not been built.")
        : _tabOrderCard ?? throw new InvalidOperationException("Tab Order settings card has not been built.");

    private int? FindSettingRowIndex(IReadOnlyList<OverlayRow> rows, Border container)
    {
        for (var index = 0; index < rows.Count; index++)
            if (ReferenceEquals(rows[index].Container, container))
                return index;
        return null;
    }

    internal bool TryHandleSettingBack()
    {
        if (_tabState.SelectedTab != AddonQuickSettingsTabId.Setting || _expandedSettingCard is not { } expanded)
            return false;

        SetExpandedSettingCard(null, GetSettingCard(expanded).Row.Container);
        return true;
    }

    private void ResetSettingCardsForShow()
    {
        if (_expandedSettingCard is null)
            return;

        _expandedSettingCard = null;
        UpdateSettingCardVisual(_clawHudCard);
        UpdateSettingCardVisual(_tabOrderCard);
    }

    private StackPanel BuildClawHudPage()
    {
        var section = new StackPanel { Spacing = OverlayQamResources.Get("QamSectionHeaderSpacing", 4.0) };

        _clawHudStatusText = CreateStatusText("Waiting for ClawHUD state.");
        section.Children.Add(_clawHudStatusText);

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

        _clawHudVrrStatusText = CreateStatusText(string.Empty);
        section.Children.Add(_clawHudVrrStatusText);
        return section;
    }

    private void AddClawHudRow(StackPanel section, OverlayToggleRow row)
    {
        section.Children.Add(row.Container);
        _clawHudRows.Add(CreateSettingDetailRow(
            new OverlayRow(row.Container, row.Capabilities),
            SettingCardId.ClawHud));
        RegisterRowPointerSelection(row.Container);
    }

    private void AddClawHudRow(StackPanel section, OverlayValueRow row)
    {
        section.Children.Add(row.Container);
        _clawHudRows.Add(CreateSettingDetailRow(
            new OverlayRow(row.Container, row.Capabilities),
            SettingCardId.ClawHud));
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

    private static TextBlock CreateStatusText(string text)
    {
        var block = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap };
        OverlayQamResources.ApplyTextStyle(block, "QamCaptionTextStyle");
        return block;
    }

    internal void ApplyClawHudSnapshot(FrontendClawHudSnapshot snapshot)
    {
        _clawHudSnapshot = snapshot;
        _clawHudMutationInFlight = false;
        RenderClawHud();
    }

    internal void ApplyClawHudFailure(string message)
    {
        _clawHudMutationInFlight = false;
        if (_clawHudStatusText is not null)
            _clawHudStatusText.Text = message;
        if (_clawHudCard is not null)
            _clawHudCard.Summary.Text = message;
        RenderClawHudControls();
    }

    private void RequestClawHudEnabled(bool enabled)
    {
        if (_clawHudMutationInFlight) return;
        _clawHudMutationInFlight = true;
        RenderClawHudControls();
        ClawHudEnabledRequested?.Invoke(enabled);
    }

    private void RequestClawHudSetting(FrontendClawHudMutationIntent intent)
    {
        if (_clawHudMutationInFlight) return;
        _clawHudMutationInFlight = true;
        RenderClawHudControls();
        ClawHudSettingMutationRequested?.Invoke(intent);
    }

    private void RenderClawHud()
    {
        if (_clawHudStatusText is not null)
            _clawHudStatusText.Text = _clawHudSnapshot?.StatusMessage ?? "Waiting for ClawHUD state.";
        if (_clawHudVrrStatusText is not null)
        {
            var result = _clawHudSnapshot?.Settings?.IntelVrrLastResult;
            _clawHudVrrStatusText.Text = result is null ? string.Empty : $"VRR: {result.Status} — {result.Message}";
        }
        UpdateClawHudCardSummary();
        RenderClawHudControls();
    }

    private void UpdateClawHudCardSummary()
    {
        if (_clawHudCard is null)
            return;

        _clawHudCard.Summary.Text = _clawHudSnapshot is { } snapshot
            ? $"{(snapshot.DesiredEnabled ? "Enabled" : "Disabled")} · {snapshot.RuntimeState}"
            : "Loading ClawHUD status…";
    }

    private void UpdateTabOrderCardSummary(IReadOnlyList<AddonQuickSettingsTabId> order)
    {
        if (_tabOrderCard is not null)
            _tabOrderCard.Summary.Text = string.Join("  ›  ", order.Select(LabelFor));
    }

    private void RenderClawHudControls()
    {
        var snapshot = _clawHudSnapshot;
        var settings = snapshot?.Settings;
        var available = snapshot is not null && !_clawHudMutationInFlight;
        var nestedAvailable = available && snapshot!.RuntimeState == FrontendClawHudRuntimeState.Ready && settings is not null;

        _clawHudEnabledRow?.ApplyState(available, snapshot?.DesiredEnabled == true);
        _clawHudDisplayModeRow?.ApplyState(nestedAvailable, 0, 1, 1, settings?.DisplayMode is { } display ? (int)display : 0);
        _clawHudSizeRow?.ApplyState(nestedAvailable, -2, 2, 1, settings?.HudSizeOffset ?? 0);
        _clawHudFontRow?.ApplyState(nestedAvailable, 0, 1, 1, settings?.Font is { } font ? (int)font : 0);
        _clawHudAlignmentRow?.ApplyState(nestedAvailable, 0, 2, 1, settings?.Alignment is { } alignment ? (int)alignment : 0);
        _clawHudBackgroundRow?.ApplyState(nestedAvailable, 0, 1, 1, settings?.BackgroundMode is { } background ? (int)background : 0);
        _clawHudOpacityRow?.ApplyState(nestedAvailable, 50, 100, 5, settings?.BackgroundOpacityPercent ?? 50);
        _clawHudVrrRow?.ApplyState(nestedAvailable, settings?.IntelVrrRangeFixEnabled == true);
    }
}
