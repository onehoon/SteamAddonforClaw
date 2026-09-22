using SteamInputAddonforClaw.Overlay;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class OverlayWindowGeometryTests
{
    [Fact]
    public void UsesTaskbarReferenceAndGapForTheReferenceDisplay()
    {
        var result = OverlayWindowGeometry.Calculate(
            0, 0, 1920, 1200,
            0, 0, 1920, 1128,
            144,
            out var metrics);

        Assert.Equal(new OverlayRect(240, 90, 1440, 1020), result);
        Assert.Equal(72, metrics.ReservedEdgePx);
        Assert.Equal(72, metrics.ReferenceTaskbarPx);
        Assert.Equal(18, metrics.ExtraGapPx);
        Assert.Equal(90, metrics.OuterMarginPx);
    }

    [Theory]
    [InlineData(96, 60, 480, 960)]
    [InlineData(120, 75, 360, 1200)]
    [InlineData(144, 90, 240, 1440)]
    [InlineData(168, 105, 120, 1680)]
    [InlineData(192, 120, 120, 1680)]
    public void ScalesReferenceMarginAndSurfaceWidthWithDpiWhenWorkAreaHasNoReservedEdge(uint dpi, int expectedMargin, int expectedX, int expectedWidth)
    {
        var result = OverlayWindowGeometry.Calculate(
            0, 0, 1920, 1200,
            0, 0, 1920, 1200,
            dpi);

        Assert.Equal(new OverlayRect(expectedX, expectedMargin, expectedWidth, 1200 - 2 * expectedMargin), result);
    }

    [Fact]
    public void PreservesNonZeroMonitorOrigin()
    {
        var result = OverlayWindowGeometry.Calculate(
            100, 40, 2020, 1240,
            100, 40, 2020, 1168,
            144);

        Assert.Equal(340, result.X);
        Assert.Equal(130, result.Y);
        Assert.Equal(1440, result.Width);
        Assert.Equal(1020, result.Height);
    }

    [Fact]
    public void LargerReservedEdgeWinsBeforeTheAdditionalGap()
    {
        var result = OverlayWindowGeometry.Calculate(
            0, 0, 1920, 1200,
            80, 60, 1840, 1120,
            96);

        Assert.Equal(new OverlayRect(480, 92, 960, 1016), result);
    }

    [Fact]
    public void UsesReferenceMarginWhenWorkAreaHasNoReservedEdge()
    {
        var result = OverlayWindowGeometry.Calculate(
            0, 0, 1920, 1200,
            0, 0, 1920, 1200,
            0);

        Assert.Equal(new OverlayRect(480, 60, 960, 1080), result);
    }

    [Fact]
    public void AdjacentPointsFallOutsideTheCappedWindowRectangle()
    {
        var result = OverlayWindowGeometry.Calculate(
            0, 0, 1920, 1200,
            0, 0, 1920, 1128,
            144);

        Assert.Equal(1440, result.Width);
        Assert.False(Contains(result, result.X - 1, result.Y + result.Height / 2));
        Assert.False(Contains(result, result.X + result.Width, result.Y + result.Height / 2));
        Assert.True(Contains(result, result.X, result.Y + result.Height / 2));
        Assert.True(Contains(result, result.X + result.Width - 1, result.Y + result.Height / 2));
    }

    [Fact]
    public void ClampsMarginAndDimensionsForAnUnusuallySmallMonitor()
    {
        var result = OverlayWindowGeometry.Calculate(
            0, 0, 10, 10,
            0, 0, 10, 10,
            192);

        Assert.True(result.Width >= 0);
        Assert.True(result.Height >= 0);
        Assert.True(result.X >= 0 && result.X <= result.X + result.Width && result.X + result.Width <= 10);
        Assert.True(result.Y >= 0 && result.Y <= result.Y + result.Height && result.Y + result.Height <= 10);
    }

    [Fact]
    public void NeverProducesNegativeDimensionsForAZeroSizedMonitor()
    {
        var result = OverlayWindowGeometry.Calculate(
            0, 0, 0, 0,
            0, 0, 0, 0,
            96);

        Assert.Equal(0, result.Width);
        Assert.Equal(0, result.Height);
    }

    private static bool Contains(OverlayRect rect, int x, int y) =>
        x >= rect.X && x < rect.X + rect.Width && y >= rect.Y && y < rect.Y + rect.Height;
}
