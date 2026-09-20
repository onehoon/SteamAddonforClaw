using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class SteamFsePackagingContractTests
{
    [Fact]
    public void Fse_home_is_a_tiny_uri_launcher_without_controller_dependencies()
    {
        var source = ReadSource("src", "SteamInputAddonforClaw.FseHome", "Program.cs");

        Assert.Contains("steam://open/bigpicture", source, StringComparison.Ordinal);
        Assert.Contains("UseShellExecute = true", source, StringComparison.Ordinal);
        foreach (var forbidden in new[] { "VIIPER", "HidHide", "PID1902", "QuickAccess", "while (", "Task.Delay" })
            Assert.DoesNotContain(forbidden, source, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Fse_package_keeps_a_stable_identity_and_gaming_home_capability()
    {
        var manifest = ReadSource("src", "SteamInputAddonforClaw.FseHome", "Packaging", "AppxManifest.xml");
        var sccd = ReadSource("src", "SteamInputAddonforClaw.FseHome", "Packaging", "CustomCapability.SCCD");
        var project = ReadSource("src", "SteamInputAddonforClaw.FseHome", "SteamInputAddonforClaw.FseHome.csproj");

        Assert.Contains("Name=\"SteamInputAddonforClaw.FseHome\"", manifest, StringComparison.Ordinal);
        Assert.Contains("Name=\"windows.gamingApp\"", manifest, StringComparison.Ordinal);
        Assert.Contains("Name=\"Microsoft.appCategory.gamingHome_8wekyb3d8bbwe\"", manifest, StringComparison.Ordinal);
        Assert.Contains("Version=\"__PACKAGE_VERSION__\"", manifest, StringComparison.Ordinal);
        Assert.Contains("<Application Id=\"App\"", manifest, StringComparison.Ordinal);
        Assert.Contains("Microsoft.appCategory.gamingHome_8wekyb3d8bbwe", sccd, StringComparison.Ordinal);
        Assert.DoesNotContain("SteamInputAddonforClaw.csproj", project, StringComparison.Ordinal);
    }

    [Fact]
    public void Registration_script_reports_the_actual_registered_family_name_and_aumid()
    {
        var script = ReadSource("scripts", "register-fse-home.ps1");

        Assert.Contains("Get-AppxPackage -Name 'SteamInputAddonforClaw.FseHome'", script, StringComparison.Ordinal);
        Assert.Contains("PackageFamilyName", script, StringComparison.Ordinal);
        Assert.Contains("$($package[0].PackageFamilyName)!App", script, StringComparison.Ordinal);
        Assert.DoesNotContain("GamingHomeApp", script, StringComparison.Ordinal);
    }

    private static string ReadSource(params string[] parts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "SteamInputAddonforClaw.slnx")))
            directory = directory.Parent;
        return File.ReadAllText(Path.Combine([directory!.FullName, .. parts]));
    }
}
