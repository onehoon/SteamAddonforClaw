using SteamInputAddonforClaw.Windowing;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class DpiAwareWindowSizeTests
{
    [Fact]
    public void Calculate_At150PercentDpi_ReturnsPhysicalPixelSize()
    {
        var size = DpiAwareWindowSize.Calculate(144, 1920, 1152);

        Assert.Equal(new WindowSize(1800, 1080), size);
    }

    [Fact]
    public void Calculate_WhenDisplayWorkAreaIsSmaller_ClampsToWorkArea()
    {
        var size = DpiAwareWindowSize.Calculate(96, 1000, 600);

        Assert.Equal(new WindowSize(1000, 600), size);
    }

    [Fact]
    public void Calculate_WhenDpiIsUnavailable_Uses96DpiFallback()
    {
        var size = DpiAwareWindowSize.Calculate(0, 1920, 1080);

        Assert.Equal(new WindowSize(1200, 720), size);
    }
}
