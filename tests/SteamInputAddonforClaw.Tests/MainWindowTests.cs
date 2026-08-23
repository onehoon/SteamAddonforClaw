using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class MainWindowTests
{
    [Fact]
    public void FormatWindowTitle_IncludesTheVersionPrefix()
    {
        Assert.Equal("Steam Addon for Claw v0.1.20", MainWindow.FormatWindowTitle("0.1.20"));
    }
}
