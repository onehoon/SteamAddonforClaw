using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SteamInputAddonforClaw.Contracts.Frontend;
using SteamInputAddonforClaw.Overlay.Diagnostics;

namespace SteamInputAddonforClaw.Overlay;

internal static class OverlayQuickSettingsSectionRendering
{
    private readonly record struct RowShape(
        QuickSettingsRowId RowId,
        QuickSettingsControlKind ControlKind,
        QuickSettingsSliderKind? SliderKind,
        QuickSettingsCommitPolicy CommitPolicy,
        bool Visible,
        bool WellFormed,
        string Label,
        string? NumericSuffix,
        QuickSettingsDiscreteOption[]? DiscreteOptions);

    internal static IReadOnlyList<QuickSettingsRenderedRowDescriptor> DescribeRows(QuickSettingsSection section)
    {
        var usesFeatureHeader = TryGetFeatureHeaderToggle(section, out var headerToggle);
        return section.Rows
            .Where(row => row.Visible && QuickSettingsRowRendering.IsWellFormed(row))
            .Select(row =>
            {
                var isHeader = usesFeatureHeader && row.RowId == headerToggle.RowId;
                return new QuickSettingsRenderedRowDescriptor(
                    row,
                    isHeader,
                    isHeader ? section.Label! : row.Label,
                    isHeader);
            })
            .ToArray();
    }

    internal static QuickSettingsSectionRowPlan PlanRows(
        IReadOnlyDictionary<QuickSettingsRowId, QuickSettingsRenderedRowDescriptor> existingRows,
        QuickSettingsSection section)
    {
        var desiredRows = DescribeRows(section);
        var desiredById = desiredRows.ToDictionary(row => row.Row.RowId);
        var changedRowIds = existingRows.Keys
            .Concat(desiredById.Keys)
            .Where(rowId => !existingRows.TryGetValue(rowId, out var existing) ||
                !desiredById.TryGetValue(rowId, out var desired) ||
                !CanReuseRenderer(existing, desired))
            .ToHashSet();
        return new QuickSettingsSectionRowPlan(desiredRows, changedRowIds);
    }

    internal static int? FindPreferredRowIndex(
        IReadOnlyList<QuickSettingsRowId?> rowIds,
        QuickSettingsRowId? preferredRowId)
    {
        if (preferredRowId is not { } target) return null;
        for (var index = 0; index < rowIds.Count; index++)
            if (rowIds[index] == target)
                return index;
        return null;
    }

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
            previous.Rows.Count != current.Rows.Count)
            return false;

        for (var index = 0; index < previous.Rows.Count; index++)
        {
            var oldShape = RowShapeOf(previous.Rows[index]);
            var newShape = RowShapeOf(current.Rows[index]);
            if (oldShape.RowId != newShape.RowId ||
                oldShape.ControlKind != newShape.ControlKind ||
                oldShape.SliderKind != newShape.SliderKind ||
                oldShape.CommitPolicy != newShape.CommitPolicy ||
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
            row.CommitPolicy,
            row.Visible,
            QuickSettingsRowRendering.IsWellFormed(row),
            row.Label,
            spec is { Kind: QuickSettingsSliderKind.Numeric } ? spec.Suffix : null,
            spec is { Kind: QuickSettingsSliderKind.Discrete } ? spec.Options?.ToArray() : null);
    }

    private static bool CanReuseRenderer(
        QuickSettingsRenderedRowDescriptor previous,
        QuickSettingsRenderedRowDescriptor current)
    {
        var oldShape = RowShapeOf(previous.Row);
        var newShape = RowShapeOf(current.Row);
        return oldShape.RowId == newShape.RowId &&
            oldShape.ControlKind == newShape.ControlKind &&
            oldShape.SliderKind == newShape.SliderKind &&
            oldShape.CommitPolicy == newShape.CommitPolicy &&
            oldShape.Visible == newShape.Visible &&
            oldShape.WellFormed == newShape.WellFormed &&
            (current.IsFeatureHeader || string.Equals(oldShape.Label, newShape.Label, StringComparison.Ordinal)) &&
            string.Equals(oldShape.NumericSuffix, newShape.NumericSuffix, StringComparison.Ordinal) &&
            DiscreteOptionsEqual(oldShape.DiscreteOptions, newShape.DiscreteOptions) &&
            previous.IsFeatureHeader == current.IsFeatureHeader &&
            previous.StrongLabel == current.StrongLabel &&
            string.Equals(previous.DisplayLabel, current.DisplayLabel, StringComparison.Ordinal);
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

internal sealed record QuickSettingsRenderedRowDescriptor(
    QuickSettingsRow Row,
    bool IsFeatureHeader,
    string DisplayLabel,
    bool StrongLabel);

internal sealed record QuickSettingsSectionRowPlan(
    IReadOnlyList<QuickSettingsRenderedRowDescriptor> DesiredRows,
    IReadOnlySet<QuickSettingsRowId> ChangedRowIds);

public sealed partial class OverlayWindow
{
    private sealed class RenderedQuickSettingsRow
    {
        internal required QuickSettingsRenderedRowDescriptor Descriptor { get; set; }
        internal required OverlayRow Row { get; init; }
    }

    private sealed class RenderedQuickSettingsSection
    {
        internal required QuickSettingsSection Section { get; set; }
        internal Border? Card { get; set; }
        internal StackPanel? RowStack { get; set; }
        internal StackPanel? DetailStack { get; set; }
        internal Border? Heading { get; set; }
        internal string? HeadingLabel { get; set; }
        internal Dictionary<QuickSettingsRowId, RenderedQuickSettingsRow> RenderedRows { get; } = new();
        internal IReadOnlyList<OverlayRow> Rows { get; set; } = [];
    }

    // Page-local state shared by the generic Device/Profile renderer. Binding is assigned only after
    // App supplies the narrow mutation delegate through ConfigureQuickSettings.
    private sealed class QuickSettingsSurface
    {
        internal required QuickSettingsPageId PageId { get; init; }
        internal required AddonQuickSettingsTabId TabId { get; init; }
        internal required StackPanel Content { get; init; }
        internal OverlayQuickSettingsPageBinding? Binding { get; set; }
        internal Dictionary<QuickSettingsRowId, OverlayToggleRow> ToggleRows { get; } = new();
        internal Dictionary<QuickSettingsRowId, OverlayNumericSliderRow> NumericSliderRows { get; } = new();
        internal Dictionary<QuickSettingsRowId, OverlayValueRow> ValueRows { get; } = new();
        internal Dictionary<QuickSettingsSectionId, RenderedQuickSettingsSection> RenderedSections { get; } = new();
        internal bool? RenderedAvailable { get; set; }
        // Last applied projection for renderer delta classification only. The binding remains the
        // sole source of effective/authoritative values and mutation context.
        internal QuickSettingsPageSnapshot? RenderedPage { get; set; }
    }

    private readonly Dictionary<QuickSettingsPageId, QuickSettingsSurface> _quickSettingsSurfaces = new();

    // SF-V2-07/09 section 10.2/44/12.1: App owns the NamedPipeOverlayClient; the narrow mutation
    // delegate is the only transport seam received by the Window/binders.
    internal void ConfigureQuickSettings(Func<QuickSettingsMutationIntent, Task<QuickSettingsMutationResult>> mutate)
    {
        ConfigureQuickSettingsSurface(QuickSettingsPageId.Device, mutate);
        ConfigureQuickSettingsSurface(QuickSettingsPageId.Profile, mutate);
    }

    internal Task FlushPendingUserEditsAsync() => Task.WhenAll(
        _quickSettingsSurfaces.OrderBy(pair => pair.Key)
            .Select(pair => pair.Value.Binding?.FlushPendingUserEditsAsync() ?? Task.CompletedTask));

    internal Task FlushProfilePendingUserEditsAsync() =>
        _quickSettingsSurfaces.TryGetValue(QuickSettingsPageId.Profile, out var surface)
            ? surface.Binding?.FlushPendingUserEditsAsync() ?? Task.CompletedTask
            : Task.CompletedTask;

    private void ConfigureQuickSettingsSurface(QuickSettingsPageId pageId, Func<QuickSettingsMutationIntent, Task<QuickSettingsMutationResult>> mutate)
    {
        if (!_quickSettingsSurfaces.TryGetValue(pageId, out var surface)) return;
        surface.Binding?.Dispose();
        var binding = new OverlayQuickSettingsPageBinding(
            pageId, mutate, action => DispatcherQueue.TryEnqueue(() => action()));
        binding.SettledAsynchronously += () => RenderQuickSettingsPage(surface);
        surface.Binding = binding;
        surface.RenderedAvailable = null;
        surface.RenderedPage = null;
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

    // The surface is populated or updated after ConfigureQuickSettings/ApplyQuickSettingsPage runs.
    private FrameworkElement BuildQuickSettingsPage(AddonQuickSettingsTabId tabId, QuickSettingsPageId pageId)
    {
        var content = new StackPanel { Spacing = OverlayQamResources.Get("QamSectionSpacing", 0.0) };
        _quickSettingsSurfaces[pageId] = new QuickSettingsSurface
        {
            PageId = pageId,
            TabId = tabId,
            Content = content,
        };
        return content;
    }

    // Render the binder's current effective page. Value-only updates touch changed rows; structural
    // updates retain each section card and every compatible row control.
    private void RenderQuickSettingsPage(QuickSettingsSurface surface, QuickSettingsRowId? forceRowId = null)
    {
        if (surface.Binding is null) return;
        var page = surface.Binding.BuildEffectivePage();

        if (surface.RenderedPage is { } previousPage &&
            QuickSettingsRowRendering.IsValueOnlyPageUpdate(previousPage, page))
        {
            var rowIdsToApply = QuickSettingsRowRendering.FindValueRowsToApply(previousPage, page, forceRowId);
            if (rowIdsToApply.Count > 0)
                ApplyQuickSettingsRowValues(surface, page, rowIdsToApply);
            surface.RenderedPage = page;
            return;
        }

        if (!page.Available)
        {
            if (surface.RenderedAvailable != false)
                RebuildQuickSettingsContent(surface, page);
        }
        else if (surface.RenderedAvailable == true)
            ReconcileQuickSettingsSections(surface, page);
        else
            RebuildQuickSettingsContent(surface, page);

        surface.RenderedPage = page;
    }

    // Value-only updates change neither the section container nor any row control instance.
    private static void UpdateQuickSettingsRowValues(QuickSettingsSurface surface, QuickSettingsSection section)
    {
        foreach (var row in section.Rows)
            ApplyQuickSettingsRowValue(surface, row);
    }

    private static void ApplyQuickSettingsRowValues(
        QuickSettingsSurface surface,
        QuickSettingsPageSnapshot page,
        IReadOnlySet<QuickSettingsRowId> rowIds)
    {
        foreach (var row in page.Sections.SelectMany(section => section.Rows))
            if (rowIds.Contains(row.RowId))
                ApplyQuickSettingsRowValue(surface, row);
    }

    private static void ApplyQuickSettingsRowValue(QuickSettingsSurface surface, QuickSettingsRow row)
    {
        if (surface.ToggleRows.TryGetValue(row.RowId, out var toggle))
            ApplyQuickSettingsToggleState(toggle, row);
        else if (surface.NumericSliderRows.TryGetValue(row.RowId, out var numericSlider))
            ApplyQuickSettingsNumericSliderState(numericSlider, row);
        else if (surface.ValueRows.TryGetValue(row.RowId, out var valueRow))
            ApplyQuickSettingsValueState(valueRow, row);
    }

    private static void ApplyQuickSettingsToggleState(OverlayToggleRow toggle, QuickSettingsRow row)
    {
        var isOn = row.Value is { Kind: QuickSettingsValueKind.Boolean, BooleanValue: true };
        var isAvailable = row.Visible && row.Available && row.Writable && row.Value is { Kind: QuickSettingsValueKind.Boolean };
        toggle.ApplyState(isAvailable, isOn);
    }

    private static void ApplyQuickSettingsNumericSliderState(OverlayNumericSliderRow sliderRow, QuickSettingsRow row)
    {
        if (row.SliderSpec is not { Kind: QuickSettingsSliderKind.Numeric } spec) return;
        var hasValue = row.Value is { Kind: QuickSettingsValueKind.Integer, IntegerValue: not null };
        var value = hasValue ? row.Value!.IntegerValue!.Value : spec.Minimum;
        var available = row.Visible && row.Available && row.Writable && hasValue;
        sliderRow.ApplyState(available, spec.Minimum, spec.Maximum, spec.Step, value);
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
        var preferredRowId = CaptureQuickSettingsSelection(surface);
        if (_tabState.SelectedTab == surface.TabId)
            _numericSliderRepeatTarget.Clear();

        surface.ToggleRows.Clear();
        surface.NumericSliderRows.Clear();
        surface.ValueRows.Clear();
        surface.RenderedSections.Clear();
        surface.Content.Children.Clear();

        if (!page.Available)
        {
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
        ApplyQuickSettingsSectionSeparators(surface, page);
        RestoreQuickSettingsSelection(surface, preferredRowId, bringIntoView: true);
    }

    // An authoritative section update keeps same-ID sections attached and reconciles changed rows
    // inside their existing card. A feature toggle may add/remove child rows without detaching its
    // header ToggleSwitch or unrelated feature cards.
    private void ReconcileQuickSettingsSections(QuickSettingsSurface surface, QuickSettingsPageSnapshot page)
    {
        var preferredRowId = CaptureQuickSettingsSelection(surface);
        var requestedIds = page.Sections.Select(section => section.SectionId).ToHashSet();
        var shapeChanged = surface.RenderedSections.Count != requestedIds.Count || page.Sections.Any(section =>
            !surface.RenderedSections.TryGetValue(section.SectionId, out var rendered) ||
            !OverlayQuickSettingsSectionRendering.HasSameShape(rendered.Section, section));
        if (shapeChanged && _tabState.SelectedTab == surface.TabId)
            _numericSliderRepeatTarget.Clear();

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

            surface.RenderedSections[section.SectionId] = rendered is null
                ? BuildQuickSettingsSection(surface, section)
                : ReconcileQuickSettingsSection(surface, rendered, section);
        }

        ReorderQuickSettingsSectionCards(surface, page);
        ApplyQuickSettingsSectionSeparators(surface, page);
        UpdateQuickSettingsPageRows(surface, page);
        RestoreQuickSettingsSelection(surface, preferredRowId, bringIntoView: false);
    }

    private RenderedQuickSettingsSection BuildQuickSettingsSection(QuickSettingsSurface surface, QuickSettingsSection section)
    {
        var desiredRows = OverlayQuickSettingsSectionRendering.DescribeRows(section);
        if (desiredRows.Count == 0)
            return new RenderedQuickSettingsSection { Section = section, Rows = [] };

        var sectionPanel = new StackPanel();
        var rowStack = new StackPanel { Spacing = OverlayQamResources.Get("QamRowSpacing", 0.0) };
        var detailStack = CreateOverlayDetailStack();
        var rendered = new RenderedQuickSettingsSection
        {
            Section = section,
            RowStack = rowStack,
            DetailStack = detailStack,
        };

        if (!desiredRows.Any(row => row.IsFeatureHeader) && !string.IsNullOrEmpty(section.Label))
        {
            rendered.Heading = CreateOverlaySectionHeadingRow(section.Label);
            rendered.HeadingLabel = section.Label;
            rowStack.Children.Add(rendered.Heading);
        }

        rowStack.Children.Add(detailStack);
        sectionPanel.Children.Add(rowStack);
        rendered.Card = CreateOverlaySectionCard(sectionPanel);

        foreach (var descriptor in desiredRows)
        {
            var row = CreateRenderedQuickSettingsRow(surface, descriptor);
            rendered.RenderedRows.Add(descriptor.Row.RowId, row);
            if (descriptor.IsFeatureHeader)
                rowStack.Children.Insert(0, row.Row.Container);
            else
                detailStack.Children.Add(row.Row.Container);
        }

        rendered.Rows = desiredRows.Select(descriptor => rendered.RenderedRows[descriptor.Row.RowId].Row).ToArray();
        return rendered;
    }

    private RenderedQuickSettingsSection ReconcileQuickSettingsSection(
        QuickSettingsSurface surface,
        RenderedQuickSettingsSection rendered,
        QuickSettingsSection section)
    {
        var plan = OverlayQuickSettingsSectionRendering.PlanRows(
            rendered.RenderedRows.ToDictionary(pair => pair.Key, pair => pair.Value.Descriptor), section);
        if (plan.DesiredRows.Count == 0)
        {
            RemoveQuickSettingsSection(surface, rendered);
            return new RenderedQuickSettingsSection { Section = section, Rows = [] };
        }

        if (rendered.Card is null || rendered.RowStack is null || rendered.DetailStack is null)
            return BuildQuickSettingsSection(surface, section);

        foreach (var rowId in plan.ChangedRowIds)
            if (rendered.RenderedRows.TryGetValue(rowId, out var oldRow))
                RemoveRenderedQuickSettingsRow(surface, rendered, rowId, oldRow);

        var desiredHeader = plan.DesiredRows.FirstOrDefault(row => row.IsFeatureHeader);
        var desiredHeadingLabel = desiredHeader is null && !string.IsNullOrEmpty(section.Label)
            ? section.Label
            : null;
        if (!string.Equals(rendered.HeadingLabel, desiredHeadingLabel, StringComparison.Ordinal))
        {
            if (rendered.Heading is not null)
                rendered.RowStack.Children.Remove(rendered.Heading);
            rendered.Heading = desiredHeadingLabel is null ? null : CreateOverlaySectionHeadingRow(desiredHeadingLabel);
            rendered.HeadingLabel = desiredHeadingLabel;
            if (rendered.Heading is not null)
                rendered.RowStack.Children.Insert(0, rendered.Heading);
        }

        foreach (var descriptor in plan.DesiredRows)
        {
            if (!rendered.RenderedRows.TryGetValue(descriptor.Row.RowId, out var renderedRow))
            {
                renderedRow = CreateRenderedQuickSettingsRow(surface, descriptor);
                rendered.RenderedRows.Add(descriptor.Row.RowId, renderedRow);
                if (descriptor.IsFeatureHeader)
                    rendered.RowStack.Children.Insert(0, renderedRow.Row.Container);
                else
                    rendered.DetailStack.Children.Add(renderedRow.Row.Container);
            }
            else
                renderedRow.Descriptor = descriptor;

            ApplyQuickSettingsRowValue(surface, descriptor.Row);
        }

        for (var index = 0; index < plan.DesiredRows.Count; index++)
        {
            var descriptor = plan.DesiredRows[index];
            var renderedRow = rendered.RenderedRows[descriptor.Row.RowId];
            if (descriptor.IsFeatureHeader)
                PlaceQuickSettingsChild(rendered.RowStack, renderedRow.Row.Container, 0);
            else
                PlaceQuickSettingsChild(rendered.DetailStack, renderedRow.Row.Container,
                    plan.DesiredRows.Take(index).Count(row => !row.IsFeatureHeader));
        }

        var topRowCount = desiredHeader is not null || rendered.Heading is not null ? 1 : 0;
        PlaceQuickSettingsChild(rendered.RowStack, rendered.DetailStack, topRowCount);
        rendered.Section = section;
        rendered.Rows = plan.DesiredRows.Select(descriptor => rendered.RenderedRows[descriptor.Row.RowId].Row).ToArray();
        return rendered;
    }

    private static void RemoveQuickSettingsSection(QuickSettingsSurface surface, RenderedQuickSettingsSection section)
    {
        if (section.Card is not null)
            surface.Content.Children.Remove(section.Card);

        foreach (var rowId in section.RenderedRows.Keys.ToArray())
            RemoveQuickSettingsRowState(surface, rowId);
        section.RenderedRows.Clear();
    }

    private static void RemoveRenderedQuickSettingsRow(
        QuickSettingsSurface surface,
        RenderedQuickSettingsSection section,
        QuickSettingsRowId rowId,
        RenderedQuickSettingsRow row)
    {
        if (row.Descriptor.IsFeatureHeader)
            section.RowStack?.Children.Remove(row.Row.Container);
        else
            section.DetailStack?.Children.Remove(row.Row.Container);
        section.RenderedRows.Remove(rowId);
        RemoveQuickSettingsRowState(surface, rowId);
    }

    private static void RemoveQuickSettingsRowState(QuickSettingsSurface surface, QuickSettingsRowId rowId)
    {
        surface.ToggleRows.Remove(rowId);
        surface.NumericSliderRows.Remove(rowId);
        surface.ValueRows.Remove(rowId);
    }

    private static void PlaceQuickSettingsChild(Panel parent, UIElement child, int targetIndex)
    {
        var currentIndex = parent.Children.IndexOf(child);
        if (currentIndex == targetIndex) return;
        if (currentIndex >= 0)
            parent.Children.RemoveAt(currentIndex);
        parent.Children.Insert(Math.Min(targetIndex, parent.Children.Count), child);
    }

    private RenderedQuickSettingsRow CreateRenderedQuickSettingsRow(
        QuickSettingsSurface surface,
        QuickSettingsRenderedRowDescriptor descriptor)
    {
        if (!TryCreateQuickSettingsRow(surface, descriptor.Row, out var row,
                descriptor.IsFeatureHeader ? descriptor.DisplayLabel : null,
                descriptor.StrongLabel))
            throw new InvalidOperationException("A malformed Quick Settings row reached the Overlay renderer.");
        RegisterRowPointerSelection(row.Container);
        return new RenderedQuickSettingsRow { Descriptor = descriptor, Row = row };
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

    private QuickSettingsRowId? CaptureQuickSettingsSelection(QuickSettingsSurface surface)
    {
        if (_tabState.SelectedTab != surface.TabId ||
            !_pageRows.TryGetValue(surface.TabId, out var oldRows) ||
            _rowSelection.SelectedIndex is not { } selectedIndex || selectedIndex < 0 || selectedIndex >= oldRows.Count)
            return null;

        return oldRows[selectedIndex].QuickSettingsRowId;
    }

    private void RestoreQuickSettingsSelection(
        QuickSettingsSurface surface,
        QuickSettingsRowId? preferredRowId,
        bool bringIntoView)
    {
        if (_tabState.SelectedTab != surface.TabId) return;

        var rowIds = _pageRows.TryGetValue(surface.TabId, out var rows)
            ? rows.Select(row => row.QuickSettingsRowId).ToArray()
            : Array.Empty<QuickSettingsRowId?>();
        var preferredIndex = OverlayQuickSettingsSectionRendering.FindPreferredRowIndex(rowIds, preferredRowId);

        _rowSelection.SetRows(CapabilitiesFor(surface.TabId), preferredIndex);
        ApplyRowSelectionVisual();
        var selectedIndex = _rowSelection.SelectedIndex;
        var selectedRowId = selectedIndex is { } selectedRowIndex && _pageRows.TryGetValue(surface.TabId, out var currentRows) &&
            selectedRowIndex >= 0 && selectedRowIndex < currentRows.Count
                ? currentRows[selectedRowIndex].QuickSettingsRowId
                : null;
        if (bringIntoView || selectedRowId != preferredRowId)
            BringSelectedRowIntoView();
    }

    private static Border CreateOverlaySectionHeadingRow(string title)
    {
        var heading = new TextBlock
        {
            Text = title,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center,
        };
        OverlayQamResources.ApplyTextStyle(heading, "QamBodyStrongTextStyle");
        return OverlayRowChrome.Create(heading);
    }

    private static StackPanel CreateOverlayDetailStack() => new()
    {
        Spacing = OverlayQamResources.Get("QamRowSpacing", 0.0),
        Margin = OverlayQamResources.Get("QamDetailIndent", new Thickness(16, 0, 0, 0)),
    };

    private static void SetOverlaySectionSeparator(Border card, bool visible)
    {
        card.BorderBrush = OverlayQamResources.Brush("QamSeparatorBrush");
        card.BorderThickness = visible
            ? OverlayQamResources.Get("QamSectionSeparatorThickness", new Thickness(0, 1, 0, 0))
            : new Thickness(0);
    }

    private static void ApplyQuickSettingsSectionSeparators(QuickSettingsSurface surface, QuickSettingsPageSnapshot page)
    {
        var hasFeature = false;
        foreach (var section in page.Sections)
        {
            if (!surface.RenderedSections.TryGetValue(section.SectionId, out var rendered) || rendered.Card is null)
                continue;

            if (surface.PageId == QuickSettingsPageId.Profile && section.SectionId == QuickSettingsSectionId.ProfileGeneral)
            {
                SetOverlaySectionSeparator(rendered.Card, visible: false);
                continue;
            }

            SetOverlaySectionSeparator(rendered.Card, hasFeature);
            hasFeature = true;
        }
    }

    private static Border CreateOverlaySectionCard(UIElement child)
    {
        return new Border
        {
            Child = child,
            Margin = OverlayQamResources.Get("QamRowMargin", new Thickness(-16, 0, -16, 0)),
            Padding = OverlayQamResources.Get("QamSectionPadding", new Thickness(16, 12, 16, 12)),
            BorderBrush = OverlayQamResources.Brush("QamSeparatorBrush"),
            BorderThickness = new Thickness(0),
            CornerRadius = OverlayQamResources.Get("QamSectionCornerRadius", new CornerRadius(0)),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Background = OverlayQamResources.Brush("QamSectionBrush"),
        };
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
        RenderQuickSettingsPage(surface, forceRowId: rowId);
    }

    private OverlayRow CreateQuickSettingsValueRow(QuickSettingsSurface surface, QuickSettingsRow row)
    {
        var rowId = row.RowId;
        var spec = row.SliderSpec!;
        if (spec.Kind == QuickSettingsSliderKind.Numeric &&
            row.CommitPolicy is { Mode: QuickSettingsCommitMode.TrailingDebounce, DelayMilliseconds: > 0 })
        {
            var suffix = spec.Suffix ?? string.Empty;
            var sliderRow = new OverlayNumericSliderRow(row.Label,
                value => OverlayValueRow.FormatInteger(value) + suffix,
                desired => ScheduleQuickSettingsSlider(surface, rowId, QuickSettingsValue.Integer((int)Math.Round(desired))));
            ApplyQuickSettingsNumericSliderState(sliderRow, row);
            surface.NumericSliderRows[rowId] = sliderRow;
            return new OverlayRow(sliderRow.Container, sliderRow.Capabilities, rowId);
        }

        if (spec.Kind == QuickSettingsSliderKind.Numeric)
        {
            var suffix = spec.Suffix ?? string.Empty;
            var stepperRow = new OverlayValueRow(row.Label,
                value => OverlayValueRow.FormatInteger(value) + suffix,
                desired => ScheduleQuickSettingsSlider(surface, rowId, QuickSettingsValue.Integer((int)Math.Round(desired))),
                OverlayValueButtonKind.NumericStepper);
            ApplyQuickSettingsValueState(stepperRow, row);
            surface.ValueRows[rowId] = stepperRow;
            return new OverlayRow(stepperRow.Container, stepperRow.Capabilities, rowId);
        }

        var options = spec.Options!;
        var valueRow = new OverlayValueRow(row.Label,
            index => FormatDiscreteLabel(options, index),
            desired =>
            {
                var i = (int)Math.Round(desired);
                if (i < 0 || i >= options.Count) return;
                ScheduleQuickSettingsSlider(surface, rowId, QuickSettingsValue.Integer(options[i].Value));
            },
            OverlayValueButtonKind.DiscreteChoice);

        ApplyQuickSettingsValueState(valueRow, row);
        surface.ValueRows[rowId] = valueRow;
        return new OverlayRow(valueRow.Container, valueRow.Capabilities, rowId);
    }

    private void ScheduleQuickSettingsSlider(QuickSettingsSurface surface, QuickSettingsRowId rowId, QuickSettingsValue desired)
    {
        if (surface.Binding is null) return;
        surface.Binding.ScheduleSlider(rowId, desired, out var changedRowIds);
        var page = surface.Binding.BuildEffectivePage();
        var renderedPage = surface.RenderedPage;
        if (renderedPage is null || !renderedPage.Available || !page.Available ||
            renderedPage.PageId != page.PageId || renderedPage.ProfileTarget != page.ProfileTarget)
        {
            RenderQuickSettingsPage(surface);
            return;
        }

        // Always reapply the edited row so a rejected mutation restores the current effective
        // value. The binding returns only additional group members whose linked value changed.
        changedRowIds = changedRowIds.Append(rowId).ToHashSet();
        ApplyQuickSettingsRowValues(surface, page, changedRowIds);
        surface.RenderedPage = page;
    }

    private static string FormatDiscreteLabel(IReadOnlyList<QuickSettingsDiscreteOption> options, double index)
    {
        var i = (int)Math.Round(index);
        return i >= 0 && i < options.Count ? options[i].Label : "--";
    }
}
