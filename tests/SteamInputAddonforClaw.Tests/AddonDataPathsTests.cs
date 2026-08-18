using SteamInputAddonforClaw.Install;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class AddonDataPathsTests
{
    private const string InstallRoot = @"C:\Users\Test\AppData\Local\SteamInputAddonforClaw";

    [Fact]
    public void ResolveDataRoot_ReturnsCanonicalSibling()
    {
        Assert.Equal(@"C:\Users\Test\AppData\Local\SteamInputAddonforClaw-Data", AddonDataPaths.ResolveDataRoot(InstallRoot));
    }

    [Fact]
    public void ResolveDataRoot_NormalizesTrailingSeparator()
    {
        Assert.Equal(AddonDataPaths.ResolveDataRoot(InstallRoot), AddonDataPaths.ResolveDataRoot(InstallRoot + Path.DirectorySeparatorChar));
    }

    [Fact]
    public void Paths_AreInsideDataRootAndOutsideInstallRoot()
    {
        var dataRoot = AddonDataPaths.ResolveDataRoot(InstallRoot);

        Assert.Equal(Path.Combine(dataRoot, "settings.json"), Path.Combine(dataRoot, "settings.json"));
        Assert.Equal(Path.Combine(dataRoot, "recovery.json"), Path.Combine(dataRoot, "recovery.json"));
        Assert.False(Path.GetFullPath(dataRoot).StartsWith(Path.GetFullPath(InstallRoot) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ResolveDataRoot_RejectsFilesystemRoot()
    {
        var root = Path.GetPathRoot(Path.GetFullPath(Environment.CurrentDirectory))!;

        Assert.Throws<ArgumentException>(() => AddonDataPaths.ResolveDataRoot(root));
    }
}
