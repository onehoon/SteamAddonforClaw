using SteamInputAddonforClaw.Overlay;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class OverlayWindowGeometryTests
{
    // OQ5 UI Polish A: every DPI still converts the 400-DIP width and the small edge inset using
    // the same DPI-scale rule; expected values below already fold in that inset.
    [Theory]
    [InlineData(96, 400, 4)]
    [InlineData(120, 500, 5)]
    [InlineData(144, 600, 6)]
    [InlineData(168, 700, 7)]
    [InlineData(192, 800, 8)]
    public void ConvertsDipWidthAndInsetUsingDpi(uint dpi, int expectedWidth, int expectedInset)
    {
        var result = OverlayWindowGeometry.Calculate(0, 0, 1920, 1120, dpi);

        Assert.Equal(new OverlayRect(expectedInset, expectedInset, expectedWidth, 1120 - 2 * expectedInset), result);
    }

    [Fact]
    public void PreservesNonZeroWorkAreaOrigin()
    {
        var result = OverlayWindowGeometry.Calculate(100, 40, 2020, 1160, 144);

        Assert.Equal(106, result.X);
        Assert.Equal(46, result.Y);
        Assert.Equal(600, result.Width);
        Assert.Equal(1108, result.Height);
    }

    [Fact]
    public void ClampsWidthToWorkAreaAfterTheEdgeInset()
    {
        var result = OverlayWindowGeometry.Calculate(0, 0, 500, 800, 192);

        // insetPx = 8 at 192 DPI; available width is 500 - 8, not the full 500.
        Assert.Equal(492, result.Width);
        Assert.Equal(8, result.X);
    }

    [Fact]
    public void Uses96DpiWhenDpiIsZero()
    {
        var result = OverlayWindowGeometry.Calculate(0, 0, 1920, 1120, 0);

        Assert.Equal(400, result.Width);
        Assert.Equal(4, result.X);
        Assert.Equal(4, result.Y);
    }

    [Fact]
    public void ClampsTheInsetAndDimensionsForAnUnusuallySmallWorkArea()
    {
        var result = OverlayWindowGeometry.Calculate(0, 0, 10, 10, 96);

        Assert.True(result.Width >= 0);
        Assert.True(result.Height >= 0);
        Assert.True(result.X >= 0 && result.X <= 10);
        Assert.True(result.Y >= 0 && result.Y <= 10);
    }

    [Fact]
    public void NeverProducesNegativeDimensionsForAZeroSizedWorkArea()
    {
        var result = OverlayWindowGeometry.Calculate(0, 0, 0, 0, 96);

        Assert.Equal(0, result.Width);
        Assert.Equal(0, result.Height);
    }
}
