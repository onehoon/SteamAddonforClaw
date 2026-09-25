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
        Assert.False(OverlayQuickSettingsSectionRendering.HasSameShape(before, before with { Message = "Changed status" }));
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
        QuickSettingsCommitPolicy.TrailingDebounce2000);
}
