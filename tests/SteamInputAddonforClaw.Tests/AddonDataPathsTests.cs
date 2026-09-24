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
    public void ResolveClawHudRuntimePaths_UseCanonicalDataRootOutsideInstallRoot()
    {
        var runtimeRoot = AddonDataPaths.ResolveClawHudRuntimeRoot(InstallRoot);
        var versionDirectory = AddonDataPaths.ResolveClawHudRuntimeVersionDirectory(InstallRoot, "1.0.1");

        Assert.Equal(@"C:\Users\Test\AppData\Local\SteamInputAddonforClaw-Data\Runtime\ClawHUD", runtimeRoot);
        Assert.Equal(@"C:\Users\Test\AppData\Local\SteamInputAddonforClaw-Data\Runtime\ClawHUD\1.0.1", versionDirectory);
        Assert.False(versionDirectory.StartsWith(Path.GetFullPath(InstallRoot) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Paths_AreInsideDataRootAndOutsideInstallRoot()
    {
        var settingsPath = AddonDataPaths.ResolveSettingsPath(InstallRoot);
        var profilesPath = AddonDataPaths.ResolveProfilesPath(InstallRoot);
        var shortcutsPath = AddonDataPaths.ResolveShortcutsPath(InstallRoot);

        Assert.Equal(@"C:\Users\Test\AppData\Local\SteamInputAddonforClaw-Data\settings.json", settingsPath);
        Assert.Equal(@"C:\Users\Test\AppData\Local\SteamInputAddonforClaw-Data\profiles.json", profilesPath);
        Assert.Equal(@"C:\Users\Test\AppData\Local\SteamInputAddonforClaw-Data\shortcuts.json", shortcutsPath);
        Assert.False(settingsPath.StartsWith(Path.GetFullPath(InstallRoot) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));
        Assert.False(profilesPath.StartsWith(Path.GetFullPath(InstallRoot) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));
        Assert.False(shortcutsPath.StartsWith(Path.GetFullPath(InstallRoot) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ResolveDataRoot_RejectsFilesystemRoot()
    {
        var root = Path.GetPathRoot(Path.GetFullPath(Environment.CurrentDirectory))!;

        Assert.Throws<ArgumentException>(() => AddonDataPaths.ResolveDataRoot(root));
    }
}
