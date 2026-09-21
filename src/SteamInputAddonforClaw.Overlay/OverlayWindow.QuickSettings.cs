using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SteamInputAddonforClaw.Contracts.Frontend;
using SteamInputAddonforClaw.Overlay.Diagnostics;

namespace SteamInputAddonforClaw.Overlay;

public sealed partial class OverlayWindow
{
    // The flattened (RowId, ControlKind, SliderKind, WellFormed) shape of a last-rendered
    // Quick Settings page. Equal shape plus equal page identity means only row values changed.
    private readonly record struct QuickSettingsRowShape(QuickSettingsRowId RowId, QuickSettingsControlKind ControlKind, QuickSettingsSliderKind? SliderKind, bool Visible, bool WellFormed);

    // Page-local state shared by the generic Device/Profile renderer. Binding is assigned only after
    // App supplies the narrow mutation delegate through ConfigureQuickSettings.
    private sealed class QuickSettingsSurface
    {
        internal required QuickSettingsPageId PageId { get; init; }
        internal required AddonQuickSettingsTabId TabId { get; init; }
        internal required StackPanel Content { get; init; }
        internal required TextBlock FailureText { get; init; }
        internal OverlayQuickSettingsPageBinding? Binding { get; set; }
        internal Dictionary<QuickSettingsRowId, OverlayToggleRow> ToggleRows { get; } = new();
        internal Dictionary<QuickSettingsRowId, OverlayValueRow> ValueRows { get; } = new();
        internal QuickSettingsRowShape[]? RowShape { get; set; }
        // The row shape alone does not capture Profile game identity, which renders from section
        // Label/Message text. The fast path must fail closed when AppId or section text changes.
        internal uint? RenderedAppId { get; set; }
        internal (QuickSettingsSectionId Id, string? Label, string? Message)[]? RenderedSections { get; set; }
    }

    private readonly Dictionary<QuickSettingsPageId, QuickSettingsSurface> _quickSettingsSurfaces = new();

    // SF-V2-07/09 section 10.2/44/12.1: App owns the NamedPipeOverlayClient; the narrow mutation
    // delegate is the only transport seam received by the Window/binders.
    internal void ConfigureQuickSettings(Func<QuickSettingsMutationIntent, Task<QuickSettingsMutationResult>> mutate)
    {
        ConfigureQuickSettingsSurface(QuickSettingsPageId.Device, mutate);
        ConfigureQuickSettingsSurface(QuickSettingsPageId.Profile, mutate);
    }

    private void ConfigureQuickSettingsSurface(QuickSettingsPageId pageId, Func<QuickSettingsMutationIntent, Task<QuickSettingsMutationResult>> mutate)
    {
        if (!_quickSettingsSurfaces.TryGetValue(pageId, out var surface)) return;
        surface.Binding?.Dispose();
        var binding = new OverlayQuickSettingsPageBinding(
            pageId, mutate, action => DispatcherQueue.TryEnqueue(() => action()));
        binding.SettledAsynchronously += () => RenderQuickSettingsPage(surface);
        surface.Binding = binding;
        RenderQuickSettingsPage(surface);
    }

    // Called on the UI thread by App whenever the Runtime republishes a Device or Profile page.
    internal void ApplyQuickSettingsPage(QuickSettingsPageSnapshot page)
    {
        if (!_quickSettingsSurfaces.TryGetValue(page.PageId, out var surface))
        {
            OverlayLog.Warn("QuickSettings", "Ignoring a Quick Settings page for an unconfigured surface.", exception: null, ("PageId", page.PageId));
            return;
        }
        surface.Binding?.ApplyAuthoritativePage(page);
        RenderQuickSettingsPage(surface);
    }

    // Root holds the local failure banner above the actual row content; the surface is populated or
    // updated after ConfigureQuickSettings/ApplyQuickSettingsPage runs.
    private FrameworkElement BuildQuickSettingsPage(AddonQuickSettingsTabId tabId, QuickSettingsPageId pageId)
    {
        var failureText = CreateQuickSettingsMessageText(string.Empty, "CaptionTextBlockStyle");
        failureText.Visibility = Visibility.Collapsed;
        var content = new StackPanel { Spacing = 16 };
        var root = new StackPanel { Spacing = 4 };
        root.Children.Add(failureText);
        root.Children.Add(content);
        _quickSettingsSurfaces[pageId] = new QuickSettingsSurface
        {
            PageId = pageId,
            TabId = tabId,
            Content = content,
            FailureText = failureText,
        };
        return root;
    }

    // Render the binder's current effective page. A same-shape page only refreshes values in place,
    // preserving local value previews, selection, pointer interaction, and scroll.
    private void RenderQuickSettingsPage(QuickSettingsSurface surface)
    {
        if (surface.Binding is null) return;
        var page = surface.Binding.BuildEffectivePage();

        if (page.Available)
        {
            var rowShape = page.Sections.SelectMany(s => s.Rows).Select(QuickSettingsRowShapeOf).ToArray();
            var sectionShape = QuickSettingsSectionShapeOf(page);
            if (surface.RowShape is not null && surface.RowShape.SequenceEqual(rowShape) &&
                surface.RenderedAppId == page.AppId &&
                surface.RenderedSections is not null && surface.RenderedSections.SequenceEqual(sectionShape))
            {
                UpdateQuickSettingsRowValues(surface, page);
                ApplyQuickSettingsLocalFailure(surface);
                return;
            }
        }

        RebuildQuickSettingsContent(surface, page);
        ApplyQuickSettingsLocalFailure(surface);
    }

    // A typed failure or operation/transport failure remains visible until the binder's own failure
    // fact clears. Device and Profile banners remain independent because each owns its surface.
    private static void ApplyQuickSettingsLocalFailure(QuickSettingsSurface surface)
    {
        if (surface.Binding is null) return;
        var message = surface.Binding.LastLocalFailureMessage;
        surface.FailureText.Text = message ?? string.Empty;
        surface.FailureText.Visibility = string.IsNullOrWhiteSpace(message) ? Visibility.Collapsed : Visibility.Visible;
    }

    private static QuickSettingsRowShape QuickSettingsRowShapeOf(QuickSettingsRow row) => new(
        row.RowId,
        row.ControlKind,
        row.ControlKind == QuickSettingsControlKind.Slider ? row.SliderSpec?.Kind : null,
        row.Visible,
        QuickSettingsRowRendering.IsWellFormed(row));

    private static (QuickSettingsSectionId Id, string? Label, string? Message)[] QuickSettingsSectionShapeOf(QuickSettingsPageSnapshot page) =>
        page.Sections.Select(s => (s.SectionId, s.Label, s.Message)).ToArray();

    // Fast path: the rendered row set/kinds are unchanged -- push new values into existing controls.
    private static void UpdateQuickSettingsRowValues(QuickSettingsSurface surface, QuickSettingsPageSnapshot page)
    {
        foreach (var row in page.Sections.SelectMany(s => s.Rows))
        {
            if (surface.ToggleRows.TryGetValue(row.RowId, out var toggle))
                ApplyQuickSettingsToggleState(toggle, row);
            else if (surface.ValueRows.TryGetValue(row.RowId, out var valueRow))
                ApplyQuickSettingsValueState(valueRow, row);
        }
    }

    private static void ApplyQuickSettingsToggleState(OverlayToggleRow toggle, QuickSettingsRow row)
    {
        var isOn = row.Value is { Kind: QuickSettingsValueKind.Boolean, BooleanValue: true };
        var isAvailable = row.Visible && row.Available && row.Writable && row.Value is { Kind: QuickSettingsValueKind.Boolean };
        toggle.ApplyState(isAvailable, isOn);
    }

    private static void ApplyQuickSettingsValueState(OverlayValueRow valueRow, QuickSettingsRow row)
    {
        if (row.SliderSpec is not { } spec) return;
        if (spec.Kind == QuickSettingsSliderKind.Numeric)
        {
            var hasValue = row.Value is { Kind: QuickSettingsValueKind.Integer, IntegerValue: not null };
            var value = hasValue ? row.Value!.IntegerValue!.Value : spec.Minimum;
            var available = row.Visible && row.Available && row.Writable && hasValue;
            valueRow.ApplyState(available, spec.Minimum, spec.Maximum, spec.Step, value);
        }
        else
        {
            var options = spec.Options ?? [];
            var index = row.Value is { Kind: QuickSettingsValueKind.Integer, IntegerValue: { } iv }
                ? QuickSettingsRowRendering.FindDiscreteIndex(options, iv)
                : -1;
            var available = row.Visible && row.Available && row.Writable && index >= 0;
            valueRow.ApplyState(available, 0, Math.Max(0, options.Count - 1), 1, Math.Max(0, index));
        }
    }

    // Structural rebuild: row set/kind changed, or the whole page became (un)available. Preserve
    // the selected RowId while this surface is visible and never reset body scroll.
    private void RebuildQuickSettingsContent(QuickSettingsSurface surface, QuickSettingsPageSnapshot page)
    {
        QuickSettingsRowId? preferredRowId = null;
        if (_tabState.SelectedTab == surface.TabId &&
            _pageRows.TryGetValue(surface.TabId, out var oldRows) &&
            _rowSelection.SelectedIndex is { } selectedIndex && selectedIndex >= 0 && selectedIndex < oldRows.Count)
        {
            preferredRowId = oldRows[selectedIndex].QuickSettingsRowId;
        }

        surface.ToggleRows.Clear();
        surface.ValueRows.Clear();
        surface.Content.Children.Clear();
        var rows = new List<OverlayRow>();

        if (!page.Available)
        {
            surface.Content.Children.Add(CreateQuickSettingsMessageText(page.Message ?? "Quick Settings are unavailable.", "BodyTextBlockStyle"));
            surface.RowShape = [];
        }
        else
        {
            foreach (var section in page.Sections)
            {
                var visibleRows = section.Rows.Where(row => row.Visible).ToArray();
                if (visibleRows.Length == 0) continue;

                var sectionPanel = new StackPanel { Spacing = 5 };
                if (!string.IsNullOrEmpty(section.Label))
                    sectionPanel.Children.Add(CreateQuickSettingsMessageText(section.Label, "BodyStrongTextBlockStyle"));
                if (!string.IsNullOrEmpty(section.Message))
                    sectionPanel.Children.Add(CreateQuickSettingsMessageText(section.Message, "CaptionTextBlockStyle"));

                var rowStack = new StackPanel { Spacing = 4 };
                foreach (var row in visibleRows)
                {
                    if (!TryCreateQuickSettingsRow(surface, row, out var overlayRow)) continue;
                    rows.Add(overlayRow);
                    RegisterRowPointerSelection(overlayRow.Container);
                    rowStack.Children.Add(overlayRow.Container);
                }

                sectionPanel.Children.Add(rowStack);
                surface.Content.Children.Add(sectionPanel);
            }

            surface.RowShape = page.Sections.SelectMany(s => s.Rows).Select(QuickSettingsRowShapeOf).ToArray();
        }

        surface.RenderedAppId = page.AppId;
        surface.RenderedSections = QuickSettingsSectionShapeOf(page);

        _pageRows[surface.TabId] = rows;

        if (_tabState.SelectedTab == surface.TabId)
        {
            int? preferredIndex = null;
            if (preferredRowId is { } rid)
                for (var i = 0; i < rows.Count; i++)
                    if (rows[i].QuickSettingsRowId == rid) { preferredIndex = i; break; }

            _rowSelection.SetRows(CapabilitiesFor(surface.TabId), preferredIndex);
            ApplyRowSelectionVisual();
            BringSelectedRowIntoView();
        }
    }

    private static TextBlock CreateQuickSettingsMessageText(string text, string styleKey)
    {
        var block = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap };
        if (Application.Current.Resources.TryGetValue(styleKey, out var style) && style is Style textStyle)
            block.Style = textStyle;
        return block;
    }

    // Malformed/unsupported rows are skipped entirely: they are never registered for selection and
    // can never emit a mutation.
    private bool TryCreateQuickSettingsRow(QuickSettingsSurface surface, QuickSettingsRow row, out OverlayRow overlayRow)
    {
        if (!QuickSettingsRowRendering.IsWellFormed(row))
        {
            OverlayLog.Warn("QuickSettings", "Skipped a malformed/unsupported Quick Settings row.", exception: null,
                ("PageId", surface.PageId), ("RowId", row.RowId), ("ControlKind", row.ControlKind));
            overlayRow = default!;
            return false;
        }

        overlayRow = row.ControlKind == QuickSettingsControlKind.Toggle
            ? CreateQuickSettingsToggleRow(surface, row)
            : CreateQuickSettingsValueRow(surface, row);
        return true;
    }

    private OverlayRow CreateQuickSettingsToggleRow(QuickSettingsSurface surface, QuickSettingsRow row)
    {
        var rowId = row.RowId;
        var toggleRow = new OverlayToggleRow(row.Label, desired => _ = SubmitQuickSettingsToggleAsync(surface, rowId, desired));
        ApplyQuickSettingsToggleState(toggleRow, row);
        surface.ToggleRows[rowId] = toggleRow;
        return new OverlayRow(toggleRow.Container, toggleRow.Capabilities, rowId);
    }

    private async Task SubmitQuickSettingsToggleAsync(QuickSettingsSurface surface, QuickSettingsRowId rowId, bool desired)
    {
        if (surface.Binding is null) return;
        await surface.Binding.SubmitImmediateToggleAsync(rowId, desired);
        RenderQuickSettingsPage(surface);
    }

    private OverlayRow CreateQuickSettingsValueRow(QuickSettingsSurface surface, QuickSettingsRow row)
    {
        var rowId = row.RowId;
        var spec = row.SliderSpec!;
        OverlayValueRow valueRow;
        if (spec.Kind == QuickSettingsSliderKind.Numeric)
        {
            var suffix = spec.Suffix ?? string.Empty;
            valueRow = new OverlayValueRow(row.Label,
                value => OverlayValueRow.FormatInteger(value) + suffix,
                desired => ScheduleQuickSettingsSlider(surface, rowId, QuickSettingsValue.Integer((int)Math.Round(desired))));
        }
        else
        {
            var options = spec.Options!;
            valueRow = new OverlayValueRow(row.Label,
                index => FormatDiscreteLabel(options, index),
                desired =>
                {
                    var i = (int)Math.Round(desired);
                    if (i < 0 || i >= options.Count) return;
                    ScheduleQuickSettingsSlider(surface, rowId, QuickSettingsValue.Integer(options[i].Value));
                });
        }

        ApplyQuickSettingsValueState(valueRow, row);
        surface.ValueRows[rowId] = valueRow;
        return new OverlayRow(valueRow.Container, valueRow.Capabilities, rowId);
    }

    private void ScheduleQuickSettingsSlider(QuickSettingsSurface surface, QuickSettingsRowId rowId, QuickSettingsValue desired)
    {
        if (surface.Binding is null) return;
        surface.Binding.ScheduleSlider(rowId, desired);
        RenderQuickSettingsPage(surface);
    }

    private static string FormatDiscreteLabel(IReadOnlyList<QuickSettingsDiscreteOption> options, double index)
    {
        var i = (int)Math.Round(index);
        return i >= 0 && i < options.Count ? options[i].Label : "--";
    }
}
