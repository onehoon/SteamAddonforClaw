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
    public void ResolveLogDirectory_UsesCanonicalDataRoot()
    {
        Assert.Equal(
            @"C:\Users\Test\AppData\Local\SteamInputAddonforClaw-Data\logs",
            AddonDataPaths.ResolveLogDirectory(InstallRoot));
    }

    [Fact]
    public void Paths_AreInsideDataRootAndOutsideInstallRoot()
    {
        var settingsPath = AddonDataPaths.ResolveSettingsPath(InstallRoot);
        var recoveryPath = AddonDataPaths.ResolveRecoveryJournalPath(InstallRoot);

        Assert.Equal(@"C:\Users\Test\AppData\Local\SteamInputAddonforClaw-Data\settings.json", settingsPath);
        Assert.Equal(@"C:\Users\Test\AppData\Local\SteamInputAddonforClaw-Data\recovery.json", recoveryPath);
        Assert.False(settingsPath.StartsWith(Path.GetFullPath(InstallRoot) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));
        Assert.False(recoveryPath.StartsWith(Path.GetFullPath(InstallRoot) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ResolveDataRoot_RejectsFilesystemRoot()
    {
        var root = Path.GetPathRoot(Path.GetFullPath(Environment.CurrentDirectory))!;

        Assert.Throws<ArgumentException>(() => AddonDataPaths.ResolveDataRoot(root));
    }
}
