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

        Assert.Equal(new OverlayRect(90, 90, 1740, 1020), result);
        Assert.Equal(72, metrics.ReservedEdgePx);
        Assert.Equal(72, metrics.ReferenceTaskbarPx);
        Assert.Equal(18, metrics.ExtraGapPx);
        Assert.Equal(90, metrics.OuterMarginPx);
    }

    [Theory]
    [InlineData(96, 60)]
    [InlineData(120, 75)]
    [InlineData(144, 90)]
    [InlineData(168, 105)]
    [InlineData(192, 120)]
    public void ScalesReferenceMarginWithDpiWhenWorkAreaHasNoReservedEdge(uint dpi, int expectedMargin)
    {
        var result = OverlayWindowGeometry.Calculate(
            0, 0, 1920, 1200,
            0, 0, 1920, 1200,
            dpi);

        Assert.Equal(new OverlayRect(expectedMargin, expectedMargin, 1920 - 2 * expectedMargin, 1200 - 2 * expectedMargin), result);
    }

    [Fact]
    public void PreservesNonZeroMonitorOrigin()
    {
        var result = OverlayWindowGeometry.Calculate(
            100, 40, 2020, 1240,
            100, 40, 2020, 1168,
            144);

        Assert.Equal(190, result.X);
        Assert.Equal(130, result.Y);
        Assert.Equal(1740, result.Width);
        Assert.Equal(1020, result.Height);
    }

    [Fact]
    public void LargerReservedEdgeWinsBeforeTheAdditionalGap()
    {
        var result = OverlayWindowGeometry.Calculate(
            0, 0, 1920, 1200,
            80, 60, 1840, 1120,
            96);

        Assert.Equal(new OverlayRect(92, 92, 1736, 1016), result);
    }

    [Fact]
    public void UsesReferenceMarginWhenWorkAreaHasNoReservedEdge()
    {
        var result = OverlayWindowGeometry.Calculate(
            0, 0, 1920, 1200,
            0, 0, 1920, 1200,
            0);

        Assert.Equal(new OverlayRect(60, 60, 1800, 1080), result);
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
}
