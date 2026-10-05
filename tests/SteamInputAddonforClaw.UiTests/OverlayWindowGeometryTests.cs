using SteamInputAddonforClaw.Overlay;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class OverlayWindowGeometryTests
{
    [Fact]
    public void UsesFullMonitorBoundsForTheReferenceDisplay()
    {
        var result = OverlayWindowGeometry.Calculate(
            0, 0, 1920, 1200,
            144);

        Assert.Equal(new OverlayRect(1266, 6, 648, 1188), result);
    }

    [Theory]
    [InlineData(96, 4, 1484, 432, 1192)]
    [InlineData(120, 5, 1375, 540, 1190)]
    [InlineData(144, 6, 1266, 648, 1188)]
    [InlineData(168, 7, 1157, 756, 1186)]
    [InlineData(192, 8, 1048, 864, 1184)]
    public void ScalesFloatingGapAndMaximumWidthWithDpi(uint dpi, int expectedGap, int expectedX, int expectedWidth, int expectedHeight)
    {
        var result = OverlayWindowGeometry.Calculate(
            0, 0, 1920, 1200,
            dpi);

        Assert.Equal(new OverlayRect(expectedX, expectedGap, expectedWidth, expectedHeight), result);
    }

    [Fact]
    public void PreservesNonZeroMonitorOrigin()
    {
        var result = OverlayWindowGeometry.Calculate(
            100, 40, 2020, 1240,
            144);

        Assert.Equal(new OverlayRect(1366, 46, 648, 1188), result);
    }

    [Fact]
    public void AdjacentPointsFallOutsideTheCappedWindowRectangle()
    {
        var result = OverlayWindowGeometry.Calculate(
            0, 0, 1920, 1200,
            144);

        Assert.Equal(648, result.Width);
        Assert.False(Contains(result, result.X - 1, result.Y + result.Height / 2));
        Assert.False(Contains(result, result.X + result.Width, result.Y + result.Height / 2));
        Assert.True(Contains(result, result.X, result.Y + result.Height / 2));
        Assert.True(Contains(result, result.X + result.Width - 1, result.Y + result.Height / 2));
    }

    [Fact]
    public void UsesFloatingGapWhenDpiIsZero()
    {
        var result = OverlayWindowGeometry.Calculate(
            0, 0, 1920, 1200,
            0);

        Assert.Equal(new OverlayRect(1484, 4, 432, 1192), result);
    }

    [Fact]
    public void ClampsDimensionsForAnUnusuallySmallMonitor()
    {
        var result = OverlayWindowGeometry.Calculate(
            0, 0, 10, 10,
            192);

        Assert.Equal(new OverlayRect(8, 8, 0, 0), result);
        Assert.True(result.X >= 0 && result.X <= result.X + result.Width && result.X + result.Width <= 10);
        Assert.True(result.Y >= 0 && result.Y <= result.Y + result.Height && result.Y + result.Height <= 10);
    }

    [Fact]
    public void PreservesOriginForAZeroSizedMonitor()
    {
        var result = OverlayWindowGeometry.Calculate(
            100, -50, 100, -50,
            96);

        Assert.Equal(new OverlayRect(100, -50, 0, 0), result);
    }

    private static bool Contains(OverlayRect rect, int x, int y) =>
        x >= rect.X && x < rect.X + rect.Width && y >= rect.Y && y < rect.Y + rect.Height;
}
