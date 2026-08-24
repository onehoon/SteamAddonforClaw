using SteamInputAddonforClaw.Install;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class UninstallBootstrapTests
{
    [Fact]
    public void Data_root_is_the_full_reset_root()
    {
        var root = AddonDataPaths.ResolveDataRoot("C:\\Users\\Test\\App");
        Assert.EndsWith("SteamInputAddonforClaw-Data", root, StringComparison.OrdinalIgnoreCase);
    }
}
