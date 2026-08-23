using SteamInputAddonforClaw.Install;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class UninstallBootstrapTests
{
    [Fact]
    public void Elevated_cleanup_argument_is_distinct_from_prerequisite_setup()
    {
        Assert.Equal("--elevated-uninstall-cleanup", UninstallBootstrap.ElevatedArgument);
        Assert.NotEqual("--elevated-prerequisite-setup", UninstallBootstrap.ElevatedArgument);
    }

    [Fact]
    public void Data_root_is_the_full_reset_root()
    {
        var root = AddonDataPaths.ResolveDataRoot("C:\\Users\\Test\\App");
        Assert.EndsWith("SteamInputAddonforClaw-Data", root, StringComparison.OrdinalIgnoreCase);
    }
}
