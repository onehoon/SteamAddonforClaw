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
