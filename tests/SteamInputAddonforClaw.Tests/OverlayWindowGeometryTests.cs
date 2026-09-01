using SteamInputAddonforClaw.Overlay;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class OverlayWindowGeometryTests
{
    [Theory]
    [InlineData(96, 400)]
    [InlineData(120, 500)]
    [InlineData(144, 600)]
    [InlineData(168, 700)]
    [InlineData(192, 800)]
    public void ConvertsDipWidthUsingDpi(uint dpi, int expectedWidth)
    {
        var result = OverlayWindowGeometry.Calculate(0, 0, 1920, 1120, dpi);

        Assert.Equal(new OverlayRect(0, 0, expectedWidth, 1120), result);
    }

    [Fact]
    public void PreservesNonZeroWorkAreaOrigin()
    {
        var result = OverlayWindowGeometry.Calculate(100, 40, 2020, 1160, 144);

        Assert.Equal(100, result.X);
        Assert.Equal(40, result.Y);
        Assert.Equal(600, result.Width);
        Assert.Equal(1120, result.Height);
    }

    [Fact]
    public void ClampsWidthToWorkArea()
    {
        var result = OverlayWindowGeometry.Calculate(0, 0, 500, 800, 192);

        Assert.Equal(500, result.Width);
    }

    [Fact]
    public void Uses96DpiWhenDpiIsZero()
    {
        var result = OverlayWindowGeometry.Calculate(0, 0, 1920, 1120, 0);

        Assert.Equal(400, result.Width);
    }
}
