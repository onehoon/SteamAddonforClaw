using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SteamInputAddonforClaw.Contracts.Frontend;
using SteamInputAddonforClaw.Overlay.Diagnostics;

namespace SteamInputAddonforClaw.Overlay;

public sealed partial class OverlayWindow
{
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
    private FrontendClawHudSnapshot? _clawHudSnapshot;
    private bool _clawHudMutationInFlight;

    internal event Action<bool>? ClawHudEnabledRequested;
    internal event Action<FrontendClawHudMutationIntent>? ClawHudSettingMutationRequested;

    private FrameworkElement BuildSettingPage(List<OverlayRow> rows)
    {
        var root = new StackPanel { Spacing = 16 };
        root.Children.Add(BuildClawHudPage(rows));
        root.Children.Add(BuildTabOrderEditorPage(rows));
        return root;
    }

    private FrameworkElement BuildClawHudPage(List<OverlayRow> rows)
    {
        var section = new StackPanel { Spacing = 5 };
        var heading = new TextBlock { Text = "ClawHUD" };
        if (Application.Current.Resources.TryGetValue("BodyStrongTextBlockStyle", out var headingStyle) && headingStyle is Style style)
            heading.Style = style;
        section.Children.Add(heading);

        _clawHudStatusText = CreateStatusText("Waiting for ClawHUD state.");
        section.Children.Add(_clawHudStatusText);

        _clawHudEnabledRow = new OverlayToggleRow("Enable HUD", RequestClawHudEnabled);
        AddClawHudRow(rows, _clawHudEnabledRow);

        _clawHudDisplayModeRow = CreateClawHudValueRow("Display Mode", ["Always", "In Game Only"], value =>
            RequestClawHudSetting(new(FrontendClawHudMutationKind.DisplayMode, DisplayMode: (FrontendClawHudDisplayMode)(int)Math.Round(value))));
        AddClawHudRow(rows, _clawHudDisplayModeRow);

        _clawHudSizeRow = new OverlayValueRow("HUD Size", OverlayValueRow.FormatInteger,
            value => RequestClawHudSetting(new(FrontendClawHudMutationKind.HudSizeOffset, HudSizeOffset: (int)Math.Round(value))));
        AddClawHudRow(rows, _clawHudSizeRow);

        _clawHudFontRow = CreateClawHudValueRow("Font", ["Unispace", "Segoe UI Variable"], value =>
            RequestClawHudSetting(new(FrontendClawHudMutationKind.Font, Font: (FrontendClawHudFont)(int)Math.Round(value))));
        AddClawHudRow(rows, _clawHudFontRow);

        _clawHudAlignmentRow = CreateClawHudValueRow("Alignment", ["Left", "Center", "Right"], value =>
            RequestClawHudSetting(new(FrontendClawHudMutationKind.Alignment, Alignment: (FrontendClawHudAlignment)(int)Math.Round(value))));
        AddClawHudRow(rows, _clawHudAlignmentRow);

        _clawHudBackgroundRow = CreateClawHudValueRow("Background", ["Full Width", "Content Width"], value =>
            RequestClawHudSetting(new(FrontendClawHudMutationKind.BackgroundMode, BackgroundMode: (FrontendClawHudBackgroundMode)(int)Math.Round(value))));
        AddClawHudRow(rows, _clawHudBackgroundRow);

        _clawHudOpacityRow = new OverlayValueRow("Opacity", value => $"{value:0}%",
            value => RequestClawHudSetting(new(FrontendClawHudMutationKind.CommitOpacity, OpacityPercent: (int)Math.Round(value))));
        AddClawHudRow(rows, _clawHudOpacityRow);

        _clawHudVrrRow = new OverlayToggleRow("Intel VRR Range Fix", desired =>
            RequestClawHudSetting(new(FrontendClawHudMutationKind.IntelVrrRangeFixEnabled, IntelVrrRangeFixEnabled: desired)));
        AddClawHudRow(rows, _clawHudVrrRow);

        _clawHudVrrStatusText = CreateStatusText(string.Empty);
        section.Children.Add(_clawHudVrrStatusText);
        _clawHudRows.Clear();
        _clawHudRows.AddRange(rows);
        return section;
    }

    private OverlayValueRow CreateClawHudValueRow(string label, IReadOnlyList<string> values, Action<double> request)
    {
        return new OverlayValueRow(label, value =>
        {
            var index = (int)Math.Round(value);
            return index >= 0 && index < values.Count ? values[index] : "--";
        }, request);
    }

    private void AddClawHudRow(List<OverlayRow> rows, OverlayToggleRow row)
    {
        rows.Add(new OverlayRow(row.Container, row.Capabilities));
        RegisterRowPointerSelection(row.Container);
    }

    private void AddClawHudRow(List<OverlayRow> rows, OverlayValueRow row)
    {
        rows.Add(new OverlayRow(row.Container, row.Capabilities));
        RegisterRowPointerSelection(row.Container);
    }

    private static TextBlock CreateStatusText(string text)
    {
        var block = new TextBlock { Text = text, Opacity = 0.75, TextWrapping = TextWrapping.Wrap };
        if (Application.Current.Resources.TryGetValue("CaptionTextBlockStyle", out var style) && style is Style captionStyle)
            block.Style = captionStyle;
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
        RenderClawHudControls();
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
