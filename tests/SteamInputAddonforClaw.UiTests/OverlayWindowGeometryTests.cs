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

        Assert.Equal(new OverlayRect(1194, 48, 678, 1104), result);
    }

    [Theory]
    [InlineData(96, 1420, 452)]
    [InlineData(120, 1307, 565)]
    [InlineData(144, 1194, 678)]
    [InlineData(168, 1081, 791)]
    [InlineData(192, 968, 904)]
    public void KeepsPhysicalOuterInsetWhileMaximumWidthScalesWithDpi(
        uint dpi,
        int expectedX,
        int expectedWidth)
    {
        var result = OverlayWindowGeometry.Calculate(
            0, 0, 1920, 1200,
            dpi);

        Assert.Equal(new OverlayRect(expectedX, 48, expectedWidth, 1104), result);
        Assert.Equal(48, 1920 - (result.X + result.Width));
    }

    [Fact]
    public void PreservesNonZeroMonitorOrigin()
    {
        var result = OverlayWindowGeometry.Calculate(
            100, 40, 2020, 1240,
            144);

        Assert.Equal(new OverlayRect(1294, 88, 678, 1104), result);
    }

    [Fact]
    public void AdjacentPointsFallOutsideTheCappedWindowRectangle()
    {
        var result = OverlayWindowGeometry.Calculate(
            0, 0, 1920, 1200,
            144);

        Assert.Equal(678, result.Width);
        Assert.False(Contains(result, result.X - 1, result.Y + result.Height / 2));
        Assert.False(Contains(result, result.X + result.Width, result.Y + result.Height / 2));
        Assert.True(Contains(result, result.X, result.Y + result.Height / 2));
        Assert.True(Contains(result, result.X + result.Width - 1, result.Y + result.Height / 2));
    }

    [Fact]
    public void UsesDefaultDpiForMaximumWidthWhenDpiIsZero()
    {
        var result = OverlayWindowGeometry.Calculate(
            0, 0, 1920, 1200,
            0);

        Assert.Equal(new OverlayRect(1420, 48, 452, 1104), result);
    }

    [Fact]
    public void ClampsDimensionsForAnUnusuallySmallMonitor()
    {
        var result = OverlayWindowGeometry.Calculate(
            0, 0, 10, 10,
            192);

        Assert.Equal(new OverlayRect(0, 5, 0, 0), result);
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
