using SteamInputAddonforClaw.Contracts.Frontend;
using SteamInputAddonforClaw.Overlay;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class OverlayWindowGeometryTests
{
    [Fact]
    public void Preserves_the_left_edge_and_fills_the_reference_monitor_edges()
    {
        var result = OverlayWindowGeometry.Calculate(0, 0, 1920, 1200, 144);

        Assert.Equal(new OverlayRect(1194, 0, 726, 1200), result);
    }

    [Theory]
    [InlineData(false, FrontendClawHudFont.Unispace, -2, 0)]
    [InlineData(false, FrontendClawHudFont.Unispace, -1, 0)]
    [InlineData(false, FrontendClawHudFont.Unispace, 0, 0)]
    [InlineData(false, FrontendClawHudFont.Unispace, 1, 0)]
    [InlineData(false, FrontendClawHudFont.Unispace, 2, 0)]
    [InlineData(false, FrontendClawHudFont.SegoeUiVariable, -2, 0)]
    [InlineData(false, FrontendClawHudFont.SegoeUiVariable, -1, 0)]
    [InlineData(false, FrontendClawHudFont.SegoeUiVariable, 0, 0)]
    [InlineData(false, FrontendClawHudFont.SegoeUiVariable, 1, 0)]
    [InlineData(false, FrontendClawHudFont.SegoeUiVariable, 2, 0)]
    [InlineData(true, FrontendClawHudFont.Unispace, -2, 21)]
    [InlineData(true, FrontendClawHudFont.Unispace, -1, 22)]
    [InlineData(true, FrontendClawHudFont.Unispace, 0, 23)]
    [InlineData(true, FrontendClawHudFont.Unispace, 1, 24)]
    [InlineData(true, FrontendClawHudFont.Unispace, 2, 25)]
    [InlineData(true, FrontendClawHudFont.SegoeUiVariable, -2, 23)]
    [InlineData(true, FrontendClawHudFont.SegoeUiVariable, -1, 24)]
    [InlineData(true, FrontendClawHudFont.SegoeUiVariable, 0, 25)]
    [InlineData(true, FrontendClawHudFont.SegoeUiVariable, 1, 26)]
    [InlineData(true, FrontendClawHudFont.SegoeUiVariable, 2, 27)]
    public void ClawHud_top_inset_uses_the_measured_font_and_size_heights(
        bool desiredEnabled,
        FrontendClawHudFont font,
        int hudSizeOffset,
        int expectedInsetPx)
    {
        Assert.Equal(expectedInsetPx, OverlayWindowGeometry.ClawHudTopInsetPx(desiredEnabled, font, hudSizeOffset));
    }

    [Theory]
    [InlineData(FrontendClawHudFont.Unispace, -2, 21)]
    [InlineData(FrontendClawHudFont.Unispace, 0, 23)]
    [InlineData(FrontendClawHudFont.Unispace, 2, 25)]
    [InlineData(FrontendClawHudFont.SegoeUiVariable, -2, 23)]
    [InlineData(FrontendClawHudFont.SegoeUiVariable, 0, 25)]
    [InlineData(FrontendClawHudFont.SegoeUiVariable, 2, 27)]
    public void Measured_font_and_size_examples_keep_horizontal_anchor_and_monitor_bottom(
        FrontendClawHudFont font,
        int hudSizeOffset,
        int expectedInsetPx)
    {
        var insetPx = OverlayWindowGeometry.ClawHudTopInsetPx(true, font, hudSizeOffset);
        var result = OverlayWindowGeometry.Calculate(0, 0, 1920, 1200, 144, insetPx);

        Assert.Equal(expectedInsetPx, insetPx);
        Assert.Equal(new OverlayRect(1194, insetPx, 726, 1200 - insetPx), result);
        Assert.Equal(1920, result.X + result.Width);
        Assert.Equal(1200, result.Y + result.Height);
    }

    [Fact]
    public void Top_inset_is_relative_to_negative_monitor_origin_and_clamped_to_monitor_height()
    {
        var insetResult = OverlayWindowGeometry.Calculate(-1920, -120, 0, 1080, 144, 25);
        var clampedResult = OverlayWindowGeometry.Calculate(0, 0, 10, 10, 192, int.MaxValue);
        var negativeInsetResult = OverlayWindowGeometry.Calculate(0, 0, 1920, 1200, 192, -1);

        Assert.Equal(new OverlayRect(-726, -95, 726, 1175), insetResult);
        Assert.Equal(new OverlayRect(0, 10, 10, 0), clampedResult);
        Assert.Equal(new OverlayRect(968, 0, 952, 1200), negativeInsetResult);
    }

    [Theory]
    [InlineData(96)]
    [InlineData(144)]
    [InlineData(192)]
    public void ClawHud_inset_remains_in_physical_pixels_across_dpi(uint dpi)
    {
        var result = OverlayWindowGeometry.Calculate(0, 0, 1920, 1200, dpi, topInsetPx: 21);

        Assert.Equal(21, result.Y);
        Assert.Equal(1179, result.Height);
    }

    [Theory]
    [InlineData(96, 1420, 500)]
    [InlineData(120, 1307, 613)]
    [InlineData(144, 1194, 726)]
    [InlineData(168, 1081, 839)]
    [InlineData(192, 968, 952)]
    public void Keeps_the_old_left_anchor_and_touches_the_other_monitor_edges_at_each_dpi(
        uint dpi,
        int expectedX,
        int expectedWidth)
    {
        var result = OverlayWindowGeometry.Calculate(0, 0, 1920, 1200, dpi);

        Assert.Equal(new OverlayRect(expectedX, 0, expectedWidth, 1200), result);
        Assert.Equal(1920, result.X + result.Width);
        Assert.Equal(0, result.Y);
        Assert.Equal(1200, result.Y + result.Height);
    }

    [Fact]
    public void Preserves_nonzero_monitor_origin_while_filling_the_monitor_edges()
    {
        var result = OverlayWindowGeometry.Calculate(100, 40, 2020, 1240, 144);

        Assert.Equal(new OverlayRect(1294, 40, 726, 1200), result);
    }

    [Fact]
    public void Preserves_negative_monitor_origin_and_fills_the_monitor_edges()
    {
        var result = OverlayWindowGeometry.Calculate(-1920, -120, 0, 1080, 144);

        Assert.Equal(new OverlayRect(-726, -120, 726, 1200), result);
        Assert.Equal(0, result.X + result.Width);
        Assert.Equal(-120, result.Y);
        Assert.Equal(1080, result.Y + result.Height);
    }

    [Fact]
    public void Adjacent_points_fall_outside_the_window_rectangle()
    {
        var result = OverlayWindowGeometry.Calculate(0, 0, 1920, 1200, 144);

        Assert.Equal(726, result.Width);
        Assert.False(Contains(result, result.X - 1, result.Y + result.Height / 2));
        Assert.False(Contains(result, result.X + result.Width, result.Y + result.Height / 2));
        Assert.True(Contains(result, result.X, result.Y + result.Height / 2));
        Assert.True(Contains(result, result.X + result.Width - 1, result.Y + result.Height / 2));
    }

    [Fact]
    public void Uses_default_dpi_for_the_preserved_left_edge_when_dpi_is_zero()
    {
        var result = OverlayWindowGeometry.Calculate(0, 0, 1920, 1200, 0);

        Assert.Equal(new OverlayRect(1420, 0, 500, 1200), result);
    }

    [Fact]
    public void Clamps_dimensions_for_an_unusually_small_monitor_without_insets()
    {
        var result = OverlayWindowGeometry.Calculate(0, 0, 10, 10, 192);

        Assert.Equal(new OverlayRect(0, 0, 10, 10), result);
        Assert.True(result.Width >= 0 && result.Height >= 0);
        Assert.Equal(10, result.X + result.Width);
        Assert.Equal(10, result.Y + result.Height);
    }

    [Fact]
    public void Preserves_origin_for_a_zero_sized_monitor()
    {
        var result = OverlayWindowGeometry.Calculate(100, -50, 100, -50, 96);

        Assert.Equal(new OverlayRect(100, -50, 0, 0), result);
    }

    private static bool Contains(OverlayRect rect, int x, int y) =>
        x >= rect.X && x < rect.X + rect.Width && y >= rect.Y && y < rect.Y + rect.Height;
}
