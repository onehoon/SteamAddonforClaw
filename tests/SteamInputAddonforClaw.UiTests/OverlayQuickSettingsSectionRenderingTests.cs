using SteamInputAddonforClaw.Contracts.Frontend;
using SteamInputAddonforClaw.Overlay;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class OverlayQuickSettingsSectionRenderingTests
{
    [Fact]
    public void Labeled_section_with_a_first_visible_toggle_uses_that_toggle_as_the_header()
    {
        var toggle = ToggleRow(QuickSettingsRowId.DeviceTdpEnabled);
        var section = new QuickSettingsSection(QuickSettingsSectionId.DeviceTdp, "TDP", [toggle]);

        Assert.True(OverlayQuickSettingsSectionRendering.TryGetFeatureHeaderToggle(section, out var header));
        Assert.Same(toggle, header);
    }

    [Fact]
    public void Hidden_rows_do_not_prevent_the_first_visible_toggle_from_being_the_header()
    {
        var hidden = ToggleRow(QuickSettingsRowId.DeviceTdpEnabled) with { Visible = false };
        var visible = ToggleRow(QuickSettingsRowId.DeviceCpuBoostEnabled);
        var section = new QuickSettingsSection(QuickSettingsSectionId.DeviceTdp, "TDP", [hidden, visible]);

        Assert.True(OverlayQuickSettingsSectionRendering.TryGetFeatureHeaderToggle(section, out var header));
        Assert.Same(visible, header);
    }

    [Fact]
    public void Unlabeled_or_non_toggle_first_visible_rows_keep_the_normal_rendering_path()
    {
        var toggle = ToggleRow(QuickSettingsRowId.DeviceTdpEnabled);
        var labeledWithoutToggle = new QuickSettingsSection(QuickSettingsSectionId.DeviceTdp, null, [toggle]);
        var value = ValueRow(QuickSettingsRowId.DeviceTdpAcPl1);
        var labeledWithValueFirst = new QuickSettingsSection(QuickSettingsSectionId.DeviceTdp, "TDP", [value, toggle]);

        Assert.False(OverlayQuickSettingsSectionRendering.TryGetFeatureHeaderToggle(labeledWithoutToggle, out _));
        Assert.False(OverlayQuickSettingsSectionRendering.TryGetFeatureHeaderToggle(labeledWithValueFirst, out _));
    }

    [Fact]
    public void Section_shape_ignores_authoritative_values_but_detects_renderer_metadata_changes()
    {
        var toggle = ToggleRow(QuickSettingsRowId.DeviceTdpEnabled);
        var before = new QuickSettingsSection(QuickSettingsSectionId.DeviceTdp, "TDP", [toggle], "Status");
        var valueOnly = before with
        {
            Rows = [toggle with { Available = false, Writable = false, Value = QuickSettingsValue.Boolean(true) }],
        };

        Assert.True(OverlayQuickSettingsSectionRendering.HasSameShape(before, valueOnly));
        Assert.False(OverlayQuickSettingsSectionRendering.HasSameShape(before, before with { Label = "TDP Control" }));
        Assert.True(OverlayQuickSettingsSectionRendering.HasSameShape(before, before with { Message = "Changed status" }));
        Assert.False(OverlayQuickSettingsSectionRendering.HasSameShape(before,
            before with { Rows = [toggle with { Visible = false }] }));
        Assert.False(OverlayQuickSettingsSectionRendering.HasSameShape(before,
            before with { Rows = [ValueRow(QuickSettingsRowId.DeviceTdpEnabled)] }));
    }

    [Fact]
    public void Section_shape_detects_discrete_options_captured_by_the_row_renderer()
    {
        var firstOptions = new[] { new QuickSettingsDiscreteOption(0, "Off"), new QuickSettingsDiscreteOption(1, "On") };
        var changedOptions = new[] { new QuickSettingsDiscreteOption(0, "Off"), new QuickSettingsDiscreteOption(1, "Enabled") };
        var row = ValueRow(QuickSettingsRowId.DeviceCpuBoostAc) with
        {
            SliderSpec = new QuickSettingsSliderSpec(QuickSettingsSliderKind.Discrete, Options: firstOptions),
        };
        var before = new QuickSettingsSection(QuickSettingsSectionId.DeviceCpuBoost, "CPU Boost", [row]);
        var after = before with
        {
            Rows = [row with { SliderSpec = new QuickSettingsSliderSpec(QuickSettingsSliderKind.Discrete, Options: changedOptions) }],
        };

        Assert.False(OverlayQuickSettingsSectionRendering.HasSameShape(before, after));
    }

    [Fact]
    public void Toggle_on_and_off_reconcile_only_child_rows_and_preserve_control_identities()
    {
        var header = ToggleRow(QuickSettingsRowId.DeviceTdpEnabled);
        var stableChild = NumericRow(QuickSettingsRowId.DeviceTdpAcPl1, 20);
        var conditionalChild = NumericRow(QuickSettingsRowId.DeviceTdpAcPl2, 25) with { Visible = false };
        var off = new QuickSettingsSection(QuickSettingsSectionId.DeviceTdp, "TDP", [header, stableChild, conditionalChild]);

        var offPlan = OverlayQuickSettingsSectionRendering.PlanRows(
            new Dictionary<QuickSettingsRowId, QuickSettingsRenderedRowDescriptor>(), off);
        var offDescriptors = offPlan.DesiredRows.ToDictionary(row => row.Row.RowId);
        var headerControl = new object();
        var stableChildControl = new object();
        var offControls = new Dictionary<QuickSettingsRowId, object>
        {
            [header.RowId] = headerControl,
            [stableChild.RowId] = stableChildControl,
        };

        var on = off with
        {
            Rows =
            [
                header with { Label = "Internal toggle label changed", Value = QuickSettingsValue.Boolean(true) },
                stableChild,
                conditionalChild with { Visible = true },
            ],
        };
        var onPlan = OverlayQuickSettingsSectionRendering.PlanRows(offDescriptors, on);
        var onControls = ApplyRowPlan(offDescriptors, offControls, onPlan);

        Assert.Equal([conditionalChild.RowId], onPlan.ChangedRowIds);
        Assert.Same(headerControl, onControls[header.RowId]);
        Assert.Same(stableChildControl, onControls[stableChild.RowId]);
        Assert.NotSame(stableChildControl, onControls[conditionalChild.RowId]);

        var onDescriptors = onPlan.DesiredRows.ToDictionary(row => row.Row.RowId);
        var offAgain = on with
        {
            Rows = [header with { Value = QuickSettingsValue.Boolean(false) }, stableChild, conditionalChild with { Visible = false }],
        };
        var offAgainPlan = OverlayQuickSettingsSectionRendering.PlanRows(onDescriptors, offAgain);
        var offAgainControls = ApplyRowPlan(onDescriptors, onControls, offAgainPlan);

        Assert.Equal([conditionalChild.RowId], offAgainPlan.ChangedRowIds);
        Assert.DoesNotContain(conditionalChild.RowId, offAgainControls.Keys);
        Assert.Same(headerControl, offAgainControls[header.RowId]);
        Assert.Same(stableChildControl, offAgainControls[stableChild.RowId]);
    }

    [Fact]
    public void Row_plan_replaces_only_rows_with_incompatible_rendering_metadata()
    {
        var header = ToggleRow(QuickSettingsRowId.DeviceTdpEnabled);
        var child = NumericRow(QuickSettingsRowId.DeviceTdpAcPl1, 20);
        var before = new QuickSettingsSection(QuickSettingsSectionId.DeviceTdp, "TDP", [header, child]);
        var existing = OverlayQuickSettingsSectionRendering.PlanRows(
            new Dictionary<QuickSettingsRowId, QuickSettingsRenderedRowDescriptor>(), before);
        var currentDescriptors = existing.DesiredRows.ToDictionary(row => row.Row.RowId);
        var changed = before with
        {
            Rows =
            [
                header,
                child with { Label = "PL1 value" },
            ],
        };

        var plan = OverlayQuickSettingsSectionRendering.PlanRows(currentDescriptors, changed);

        Assert.Equal([child.RowId], plan.ChangedRowIds);

        var differentRenderer = changed with
        {
            Rows = [header, child with { CommitPolicy = QuickSettingsCommitPolicy.Immediate }],
        };
        plan = OverlayQuickSettingsSectionRendering.PlanRows(currentDescriptors, differentRenderer);
        Assert.Equal([child.RowId], plan.ChangedRowIds);
    }

    [Fact]
    public void Selection_tracks_row_id_across_insertions_and_falls_back_when_selected_row_disappears()
    {
        var selection = new OverlayRowSelection();
        var enabled = QuickSettingsRowId.DeviceTdpEnabled;
        var firstChild = QuickSettingsRowId.DeviceTdpAcPl1;
        var conditionalChild = QuickSettingsRowId.DeviceTdpAcPl2;
        var capabilities = new OverlayRowCapabilities(() => true);
        selection.SetRows([capabilities, capabilities, capabilities], preferredIndex: 2);

        var afterInsertion = new QuickSettingsRowId?[] { enabled, conditionalChild, firstChild };
        var preferredIndex = OverlayQuickSettingsSectionRendering.FindPreferredRowIndex(afterInsertion, conditionalChild);
        selection.SetRows([capabilities, capabilities, capabilities], preferredIndex);

        Assert.Equal(1, selection.SelectedIndex);
        Assert.Equal(conditionalChild, afterInsertion[selection.SelectedIndex!.Value]);

        var afterRemoval = new QuickSettingsRowId?[] { enabled, firstChild };
        preferredIndex = OverlayQuickSettingsSectionRendering.FindPreferredRowIndex(afterRemoval, conditionalChild);
        selection.SetRows([capabilities, capabilities], preferredIndex);

        Assert.Null(preferredIndex);
        Assert.Equal(0, selection.SelectedIndex);
        Assert.Equal(enabled, afterRemoval[selection.SelectedIndex!.Value]);
    }

    private static Dictionary<QuickSettingsRowId, object> ApplyRowPlan(
        IReadOnlyDictionary<QuickSettingsRowId, QuickSettingsRenderedRowDescriptor> previousRows,
        IReadOnlyDictionary<QuickSettingsRowId, object> previousControls,
        QuickSettingsSectionRowPlan plan)
    {
        var controls = new Dictionary<QuickSettingsRowId, object>();
        foreach (var row in plan.DesiredRows)
        {
            if (!plan.ChangedRowIds.Contains(row.Row.RowId) &&
                previousRows.ContainsKey(row.Row.RowId) &&
                previousControls.TryGetValue(row.Row.RowId, out var control))
                controls.Add(row.Row.RowId, control);
            else
                controls.Add(row.Row.RowId, new object());
        }

        return controls;
    }

    [Fact]
    public void Profile_fps_value_only_update_identifies_only_the_changed_power_source_row()
    {
        var before = ProfileFpsPage(ac: 60, dc: 45);
        var after = ProfileFpsPage(ac: 75, dc: 45);

        Assert.True(QuickSettingsRowRendering.IsValueOnlyPageUpdate(before, after));
        Assert.Equal(
            [QuickSettingsRowId.ProfileFpsLimitAc],
            QuickSettingsRowRendering.FindChangedValueRowIds(before, after));
        Assert.Empty(QuickSettingsRowRendering.FindChangedValueRowIds(before, before));
    }

    [Fact]
    public void Linked_tdp_preview_identifies_only_rows_whose_effective_values_changed()
    {
        var before = new QuickSettingsPageSnapshot(
            QuickSettingsPageId.Device,
            null,
            true,
            null,
            [new QuickSettingsSection(QuickSettingsSectionId.DeviceTdp, "TDP",
            [
                ToggleRow(QuickSettingsRowId.DeviceTdpEnabled),
                NumericRow(QuickSettingsRowId.DeviceTdpAcPl1, 20),
                NumericRow(QuickSettingsRowId.DeviceTdpAcPl2, 25),
                NumericRow(QuickSettingsRowId.DeviceTdpDcPl1, 15),
                NumericRow(QuickSettingsRowId.DeviceTdpDcPl2, 20),
            ])],
            [new QuickSettingsLinkedSliderConstraint(
                QuickSettingsRowId.DeviceTdpAcPl1,
                QuickSettingsRowId.DeviceTdpAcPl2,
                MinimumGap: 1)]);
        var after = before with
        {
            Sections =
            [
                before.Sections[0] with
                {
                    Rows =
                    [
                        before.Sections[0].Rows[0],
                        before.Sections[0].Rows[1] with { Value = QuickSettingsValue.Integer(26) },
                        before.Sections[0].Rows[2] with { Value = QuickSettingsValue.Integer(27) },
                        before.Sections[0].Rows[3],
                        before.Sections[0].Rows[4],
                    ],
                },
            ],
        };

        Assert.True(QuickSettingsRowRendering.IsValueOnlyPageUpdate(before, after));
        Assert.Equal(
            [QuickSettingsRowId.DeviceTdpAcPl1, QuickSettingsRowId.DeviceTdpAcPl2],
            QuickSettingsRowRendering.FindChangedValueRowIds(before, after));
    }

    [Fact]
    public void Value_only_page_update_rejects_context_shape_availability_and_range_changes()
    {
        var before = ProfileFpsPage(ac: 60, dc: 45);
        var changedValue = ProfileFpsPage(ac: 75, dc: 45);
        var fpsSection = before.Sections[0];

        Assert.True(QuickSettingsRowRendering.IsValueOnlyPageUpdate(before, changedValue));
        Assert.False(QuickSettingsRowRendering.IsValueOnlyPageUpdate(
            before, changedValue with { ProfileTarget = QuickSettingsProfileTarget.ForSteam(481) }));
        Assert.False(QuickSettingsRowRendering.IsValueOnlyPageUpdate(
            before, changedValue with { Available = false }));
        Assert.False(QuickSettingsRowRendering.IsValueOnlyPageUpdate(before with
        {
            Sections = [fpsSection with { Rows = [fpsSection.Rows[0] with { Available = false }, .. fpsSection.Rows.Skip(1)] }],
        }, changedValue));
        Assert.False(QuickSettingsRowRendering.IsValueOnlyPageUpdate(before with
        {
            Sections = [fpsSection with
            {
                Rows = [fpsSection.Rows[0], fpsSection.Rows[1] with
                {
                    SliderSpec = fpsSection.Rows[1].SliderSpec! with { Maximum = 144 },
                }, fpsSection.Rows[2]],
            }],
        }, changedValue));
        Assert.False(QuickSettingsRowRendering.IsValueOnlyPageUpdate(before with
        {
            Sections = [fpsSection with { Label = "FPS Limit" }],
        }, changedValue));
    }

    private static QuickSettingsPageSnapshot ProfileFpsPage(int ac, int dc)
    {
        var target = QuickSettingsProfileTarget.ForSteam(480);
        var enabled = ToggleRow(QuickSettingsRowId.ProfileFpsLimitEnabled);
        var acRow = NumericRow(QuickSettingsRowId.ProfileFpsLimitAc, ac) with
        {
            SliderSpec = new QuickSettingsSliderSpec(QuickSettingsSliderKind.Numeric, Minimum: 30, Maximum: 120, Step: 5),
        };
        var dcRow = NumericRow(QuickSettingsRowId.ProfileFpsLimitDc, dc) with
        {
            SliderSpec = new QuickSettingsSliderSpec(QuickSettingsSliderKind.Numeric, Minimum: 30, Maximum: 120, Step: 5),
        };
        return new QuickSettingsPageSnapshot(
            QuickSettingsPageId.Profile,
            target,
            true,
            null,
            [new QuickSettingsSection(QuickSettingsSectionId.ProfileFpsLimit, "Intel FPS Limit", [enabled, acRow, dcRow])],
            []);
    }

    [Fact]
    public void Profile_controller_uses_the_generic_toggle_and_discrete_row_renderers()
    {
        var options = new[]
        {
            new QuickSettingsDiscreteOption(0, "Disabled"),
            new QuickSettingsDiscreteOption(9, "Left Bumper (LB)"),
            new QuickSettingsDiscreteOption(10, "Right Bumper (RB)"),
        };
        var useGlobal = ToggleRow(QuickSettingsRowId.ProfileBackButtonUseGlobal) with { Value = QuickSettingsValue.Boolean(true) };
        var m1 = ValueRow(QuickSettingsRowId.ProfileBackButtonM1) with
        {
            Writable = false,
            Value = QuickSettingsValue.Integer(9),
            SliderSpec = new QuickSettingsSliderSpec(QuickSettingsSliderKind.Discrete, Options: options),
            CommitGroupId = QuickSettingsCommitGroupId.ProfileBackButtonMapping,
        };
        var m2 = m1 with { RowId = QuickSettingsRowId.ProfileBackButtonM2, Label = "M2", Value = QuickSettingsValue.Integer(10) };
        var section = new QuickSettingsSection(QuickSettingsSectionId.ProfileController, "Controller", [useGlobal, m1, m2]);

        Assert.True(OverlayQuickSettingsSectionRendering.TryGetFeatureHeaderToggle(section, out var header));
        Assert.Same(useGlobal, header);
        Assert.True(QuickSettingsRowRendering.IsWellFormed(m1));
        Assert.True(QuickSettingsRowRendering.IsWellFormed(m2));
        Assert.False(OverlayQuickSettingsPageBinding.CanMutate(m1));
        Assert.False(OverlayQuickSettingsPageBinding.CanMutate(m2));

        var explicitM1 = m1 with { Writable = true };
        var explicitM2 = m2 with { Writable = true };
        Assert.True(OverlayQuickSettingsPageBinding.CanMutate(explicitM1));
        Assert.True(OverlayQuickSettingsPageBinding.CanMutate(explicitM2));
        Assert.Equal(QuickSettingsSliderKind.Discrete, explicitM1.SliderSpec!.Kind);
    }

    private static QuickSettingsRow ToggleRow(QuickSettingsRowId rowId) => new(
        rowId,
        "Toggle",
        QuickSettingsControlKind.Toggle,
        Available: true,
        Writable: true,
        QuickSettingsValue.Boolean(false),
        SliderSpec: null,
        QuickSettingsCommitPolicy.Immediate);

    private static QuickSettingsRow ValueRow(QuickSettingsRowId rowId) => new(
        rowId,
        "Value",
        QuickSettingsControlKind.Slider,
        Available: true,
        Writable: true,
        QuickSettingsValue.Integer(10),
        new QuickSettingsSliderSpec(QuickSettingsSliderKind.Numeric, Minimum: 0, Maximum: 20),
        QuickSettingsCommitPolicy.TrailingDebounce300);

    private static QuickSettingsRow NumericRow(QuickSettingsRowId rowId, int value) => new(
        rowId,
        rowId.ToString(),
        QuickSettingsControlKind.Slider,
        Available: true,
        Writable: true,
        QuickSettingsValue.Integer(value),
        new QuickSettingsSliderSpec(QuickSettingsSliderKind.Numeric, Minimum: 8, Maximum: 37),
        QuickSettingsCommitPolicy.TrailingDebounce300);
}
