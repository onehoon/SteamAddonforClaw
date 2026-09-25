using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SteamInputAddonforClaw.Contracts.Frontend;
using SteamInputAddonforClaw.Overlay.Diagnostics;

namespace SteamInputAddonforClaw.Overlay;

internal static class OverlayQuickSettingsSectionRendering
{
    private readonly record struct RowShape(
        QuickSettingsRowId RowId,
        QuickSettingsControlKind ControlKind,
        QuickSettingsSliderKind? SliderKind,
        bool Visible,
        bool WellFormed,
        string Label,
        string? NumericSuffix,
        QuickSettingsDiscreteOption[]? DiscreteOptions);

    internal static bool TryGetFeatureHeaderToggle(QuickSettingsSection section, out QuickSettingsRow toggleRow)
    {
        toggleRow = null!;
        if (string.IsNullOrWhiteSpace(section.Label)) return false;

        var firstVisibleRow = section.Rows.FirstOrDefault(row => row.Visible);
        if (firstVisibleRow is null || firstVisibleRow.ControlKind != QuickSettingsControlKind.Toggle ||
            !QuickSettingsRowRendering.IsWellFormed(firstVisibleRow))
            return false;

        toggleRow = firstVisibleRow;
        return true;
    }

    internal static bool HasSameShape(QuickSettingsSection previous, QuickSettingsSection current)
    {
        if (previous.SectionId != current.SectionId ||
            !string.Equals(previous.Label, current.Label, StringComparison.Ordinal) ||
            !string.Equals(previous.Message, current.Message, StringComparison.Ordinal) ||
            previous.Rows.Count != current.Rows.Count)
            return false;

        for (var index = 0; index < previous.Rows.Count; index++)
        {
            var oldShape = RowShapeOf(previous.Rows[index]);
            var newShape = RowShapeOf(current.Rows[index]);
            if (oldShape.RowId != newShape.RowId ||
                oldShape.ControlKind != newShape.ControlKind ||
                oldShape.SliderKind != newShape.SliderKind ||
                oldShape.Visible != newShape.Visible ||
                oldShape.WellFormed != newShape.WellFormed ||
                !string.Equals(oldShape.Label, newShape.Label, StringComparison.Ordinal) ||
                !string.Equals(oldShape.NumericSuffix, newShape.NumericSuffix, StringComparison.Ordinal) ||
                !DiscreteOptionsEqual(oldShape.DiscreteOptions, newShape.DiscreteOptions))
                return false;
        }

        return true;
    }

    private static RowShape RowShapeOf(QuickSettingsRow row)
    {
        var spec = row.SliderSpec;
        return new(
            row.RowId,
            row.ControlKind,
            row.ControlKind == QuickSettingsControlKind.Slider ? spec?.Kind : null,
            row.Visible,
            QuickSettingsRowRendering.IsWellFormed(row),
            row.Label,
            spec is { Kind: QuickSettingsSliderKind.Numeric } ? spec.Suffix : null,
            spec is { Kind: QuickSettingsSliderKind.Discrete } ? spec.Options?.ToArray() : null);
    }

    private static bool DiscreteOptionsEqual(
        QuickSettingsDiscreteOption[]? left,
        QuickSettingsDiscreteOption[]? right)
    {
        if (ReferenceEquals(left, right)) return true;
        if (left is null || right is null || left.Length != right.Length) return false;
        return left.SequenceEqual(right);
    }
}

public sealed partial class OverlayWindow
{
    private sealed class RenderedQuickSettingsSection
    {
        internal required QuickSettingsSection Section { get; set; }
        internal Border? Card { get; init; }
        internal required IReadOnlyList<OverlayRow> Rows { get; init; }
    }

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
        internal Dictionary<QuickSettingsSectionId, RenderedQuickSettingsSection> RenderedSections { get; } = new();
        internal bool? RenderedAvailable { get; set; }
        internal TextBlock? UnavailableText { get; set; }
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
        surface.RenderedAvailable = null;
        RenderQuickSettingsPage(surface);
    }

    // Called on the UI thread by App whenever the Runtime republishes a Device or Profile page.
    internal void ApplyQuickSettingsPage(QuickSettingsPageSnapshot page)
    {
        if (page.PageId == QuickSettingsPageId.Profile)
        {
            ApplyActiveProfilePage(page);
            return;
        }
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

    // Render the binder's current effective page. Stable sections update their existing controls;
    // only sections whose renderer shape changed replace their own card and rows.
    private void RenderQuickSettingsPage(QuickSettingsSurface surface)
    {
        if (surface.Binding is null) return;
        var page = surface.Binding.BuildEffectivePage();

        if (!page.Available)
        {
            if (surface.RenderedAvailable == false && surface.UnavailableText is not null)
                surface.UnavailableText.Text = page.Message ?? "Quick Settings are unavailable.";
            else
                RebuildQuickSettingsContent(surface, page);
        }
        else if (surface.RenderedAvailable == true)
            ReconcileQuickSettingsSections(surface, page);
        else
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

    // Value-only updates change neither the section container nor any row control instance.
    private static void UpdateQuickSettingsRowValues(QuickSettingsSurface surface, QuickSettingsSection section)
    {
        foreach (var row in section.Rows)
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

    // Full rebuild is reserved for the initial render and whole-page availability transitions.
    private void RebuildQuickSettingsContent(QuickSettingsSurface surface, QuickSettingsPageSnapshot page)
    {
        var (preferredRowId, previousSelection) = CaptureQuickSettingsSelection(surface);

        surface.ToggleRows.Clear();
        surface.ValueRows.Clear();
        surface.RenderedSections.Clear();
        surface.UnavailableText = null;
        surface.Content.Children.Clear();

        if (!page.Available)
        {
            surface.UnavailableText = CreateQuickSettingsMessageText(page.Message ?? "Quick Settings are unavailable.", "BodyTextBlockStyle");
            surface.Content.Children.Add(surface.UnavailableText);
            surface.RenderedAvailable = false;
        }
        else
        {
            foreach (var section in page.Sections)
            {
                var rendered = BuildQuickSettingsSection(surface, section);
                surface.RenderedSections.Add(section.SectionId, rendered);
                if (rendered.Card is not null)
                    surface.Content.Children.Add(rendered.Card);
            }

            surface.RenderedAvailable = true;
        }

        UpdateQuickSettingsPageRows(surface, page);
        RestoreQuickSettingsSelection(surface, preferredRowId, previousSelection, bringIntoView: true);
    }

    // An authoritative section update keeps every same-ID/same-shape section attached and replaces
    // only changed or removed sections. In particular, a feature toggle may add/remove child rows
    // without detaching unrelated feature cards and their ToggleSwitch instances.
    private void ReconcileQuickSettingsSections(QuickSettingsSurface surface, QuickSettingsPageSnapshot page)
    {
        var (preferredRowId, previousSelection) = CaptureQuickSettingsSelection(surface);
        var requestedIds = page.Sections.Select(section => section.SectionId).ToHashSet();

        foreach (var sectionId in surface.RenderedSections.Keys.Where(id => !requestedIds.Contains(id)).ToArray())
        {
            RemoveQuickSettingsSection(surface, surface.RenderedSections[sectionId]);
            surface.RenderedSections.Remove(sectionId);
        }

        foreach (var section in page.Sections)
        {
            if (surface.RenderedSections.TryGetValue(section.SectionId, out var rendered) &&
                OverlayQuickSettingsSectionRendering.HasSameShape(rendered.Section, section))
            {
                rendered.Section = section;
                UpdateQuickSettingsRowValues(surface, section);
                continue;
            }

            if (rendered is not null)
                RemoveQuickSettingsSection(surface, rendered);

            surface.RenderedSections[section.SectionId] = BuildQuickSettingsSection(surface, section);
        }

        ReorderQuickSettingsSectionCards(surface, page);
        UpdateQuickSettingsPageRows(surface, page);
        RestoreQuickSettingsSelection(surface, preferredRowId, previousSelection, bringIntoView: false);
    }

    private RenderedQuickSettingsSection BuildQuickSettingsSection(QuickSettingsSurface surface, QuickSettingsSection section)
    {
        var visibleRows = section.Rows.Where(row => row.Visible).ToArray();
        if (visibleRows.Length == 0)
            return new RenderedQuickSettingsSection { Section = section, Rows = [] };

        var sectionPanel = new StackPanel { Spacing = 5 };
        var usesFeatureHeader = OverlayQuickSettingsSectionRendering.TryGetFeatureHeaderToggle(section, out var featureHeaderToggle);
        if (!usesFeatureHeader && !string.IsNullOrEmpty(section.Label))
            sectionPanel.Children.Add(CreateQuickSettingsMessageText(section.Label, "BodyStrongTextBlockStyle"));
        if (!string.IsNullOrEmpty(section.Message))
            sectionPanel.Children.Add(CreateQuickSettingsMessageText(section.Message, "CaptionTextBlockStyle"));

        var rows = new List<OverlayRow>();
        var rowStack = new StackPanel { Spacing = 4 };
        if (usesFeatureHeader && TryCreateQuickSettingsRow(surface, featureHeaderToggle, out var headerRow, section.Label, strongLabel: true))
        {
            rows.Add(headerRow);
            RegisterRowPointerSelection(headerRow.Container);
            rowStack.Children.Add(headerRow.Container);
        }

        var detailStack = usesFeatureHeader
            ? new StackPanel { Spacing = 4, Margin = new Thickness(16, 0, 0, 0) }
            : rowStack;
        foreach (var row in usesFeatureHeader ? visibleRows.Skip(1) : visibleRows)
        {
            if (!TryCreateQuickSettingsRow(surface, row, out var overlayRow)) continue;
            rows.Add(overlayRow);
            RegisterRowPointerSelection(overlayRow.Container);
            detailStack.Children.Add(overlayRow.Container);
        }

        if (usesFeatureHeader && detailStack.Children.Count > 0)
            rowStack.Children.Add(detailStack);

        sectionPanel.Children.Add(rowStack);
        return new RenderedQuickSettingsSection
        {
            Section = section,
            Card = CreateOverlaySectionCard(sectionPanel),
            Rows = rows,
        };
    }

    private static void RemoveQuickSettingsSection(QuickSettingsSurface surface, RenderedQuickSettingsSection section)
    {
        if (section.Card is not null)
            surface.Content.Children.Remove(section.Card);

        foreach (var row in section.Rows)
        {
            if (row.QuickSettingsRowId is not { } rowId) continue;
            surface.ToggleRows.Remove(rowId);
            surface.ValueRows.Remove(rowId);
        }
    }

    private static void ReorderQuickSettingsSectionCards(QuickSettingsSurface surface, QuickSettingsPageSnapshot page)
    {
        var targetIndex = 0;
        foreach (var section in page.Sections)
        {
            if (!surface.RenderedSections.TryGetValue(section.SectionId, out var rendered) || rendered.Card is null)
                continue;

            var currentIndex = surface.Content.Children.IndexOf(rendered.Card);
            if (currentIndex < 0)
                surface.Content.Children.Insert(targetIndex, rendered.Card);
            else if (currentIndex != targetIndex)
            {
                surface.Content.Children.RemoveAt(currentIndex);
                surface.Content.Children.Insert(targetIndex, rendered.Card);
            }

            targetIndex++;
        }

        while (surface.Content.Children.Count > targetIndex)
            surface.Content.Children.RemoveAt(targetIndex);
    }

    private void UpdateQuickSettingsPageRows(QuickSettingsSurface surface, QuickSettingsPageSnapshot page)
    {
        var rows = new List<OverlayRow>();
        if (page.Available)
        {
            foreach (var section in page.Sections)
                if (surface.RenderedSections.TryGetValue(section.SectionId, out var rendered))
                    rows.AddRange(rendered.Rows);
        }
        _pageRows[surface.TabId] = rows;
    }

    private (QuickSettingsRowId? RowId, int? Index) CaptureQuickSettingsSelection(QuickSettingsSurface surface)
    {
        if (_tabState.SelectedTab != surface.TabId ||
            !_pageRows.TryGetValue(surface.TabId, out var oldRows) ||
            _rowSelection.SelectedIndex is not { } selectedIndex || selectedIndex < 0 || selectedIndex >= oldRows.Count)
            return (null, _rowSelection.SelectedIndex);

        return (oldRows[selectedIndex].QuickSettingsRowId, selectedIndex);
    }

    private void RestoreQuickSettingsSelection(
        QuickSettingsSurface surface,
        QuickSettingsRowId? preferredRowId,
        int? previousSelection,
        bool bringIntoView)
    {
        if (_tabState.SelectedTab != surface.TabId) return;

        int? preferredIndex = null;
        if (preferredRowId is { } rowId && _pageRows.TryGetValue(surface.TabId, out var rows))
            for (var index = 0; index < rows.Count; index++)
                if (rows[index].QuickSettingsRowId == rowId) { preferredIndex = index; break; }

        _rowSelection.SetRows(CapabilitiesFor(surface.TabId), preferredIndex);
        ApplyRowSelectionVisual();
        var selectedIndex = _rowSelection.SelectedIndex;
        var selectedRowId = selectedIndex is { } selectedRowIndex && _pageRows.TryGetValue(surface.TabId, out var currentRows) &&
            selectedRowIndex >= 0 && selectedRowIndex < currentRows.Count
                ? currentRows[selectedRowIndex].QuickSettingsRowId
                : null;
        if (bringIntoView || selectedIndex != previousSelection || selectedRowId != preferredRowId)
            BringSelectedRowIntoView();
    }

    private static TextBlock CreateQuickSettingsMessageText(string text, string styleKey)
    {
        var block = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap };
        if (Application.Current.Resources.TryGetValue(styleKey, out var style) && style is Style textStyle)
            block.Style = textStyle;
        return block;
    }

    private static Border CreateOverlaySectionCard(UIElement child)
    {
        var card = new Border
        {
            Child = child,
            Padding = new Thickness(8),
            CornerRadius = new CornerRadius(8),
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        if (Application.Current.Resources.TryGetValue("CardBackgroundFillColorDefaultBrush", out var fill) && fill is Brush brush)
            card.Background = brush;
        return card;
    }

    // Malformed/unsupported rows are skipped entirely: they are never registered for selection and
    // can never emit a mutation.
    private bool TryCreateQuickSettingsRow(
        QuickSettingsSurface surface,
        QuickSettingsRow row,
        out OverlayRow overlayRow,
        string? displayLabelOverride = null,
        bool strongLabel = false)
    {
        if (!QuickSettingsRowRendering.IsWellFormed(row))
        {
            OverlayLog.Warn("QuickSettings", "Skipped a malformed/unsupported Quick Settings row.", exception: null,
                ("PageId", surface.PageId), ("RowId", row.RowId), ("ControlKind", row.ControlKind));
            overlayRow = default!;
            return false;
        }

        overlayRow = row.ControlKind == QuickSettingsControlKind.Toggle
            ? CreateQuickSettingsToggleRow(surface, row, displayLabelOverride, strongLabel)
            : CreateQuickSettingsValueRow(surface, row);
        return true;
    }

    private OverlayRow CreateQuickSettingsToggleRow(
        QuickSettingsSurface surface,
        QuickSettingsRow row,
        string? displayLabelOverride = null,
        bool strongLabel = false)
    {
        var rowId = row.RowId;
        var toggleRow = new OverlayToggleRow(
            displayLabelOverride ?? row.Label,
            desired => _ = SubmitQuickSettingsToggleAsync(surface, rowId, desired),
            strongLabel);
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
                desired => ScheduleQuickSettingsSlider(surface, rowId, QuickSettingsValue.Integer((int)Math.Round(desired))),
                OverlayValueButtonKind.NumericStepper);
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
                },
                OverlayValueButtonKind.DiscreteChoice);
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
