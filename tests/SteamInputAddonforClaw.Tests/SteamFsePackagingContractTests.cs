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
        Assert.Contains("Logo>Assets\\AppIcon.png", manifest, StringComparison.Ordinal);
        Assert.Contains("Square150x150Logo=\"Assets\\AppIcon.png\"", manifest, StringComparison.Ordinal);
        Assert.Contains("Square44x44Logo=\"Assets\\AppIcon.png\"", manifest, StringComparison.Ordinal);
        Assert.Contains("<Application Id=\"App\"", manifest, StringComparison.Ordinal);
        Assert.Contains("Microsoft.appCategory.gamingHome_8wekyb3d8bbwe", sccd, StringComparison.Ordinal);
        Assert.DoesNotContain("AllowExternalContent", manifest, StringComparison.Ordinal);
        Assert.DoesNotContain("SteamInputAddonforClaw.csproj", project, StringComparison.Ordinal);
    }

    [Fact]
    public void Release_packaging_requires_a_signed_final_msix_and_keeps_identity_versioned_from_release()
    {
        var pack = ReadSource("scripts", "pack.ps1");
        var package = ReadSource("scripts", "package-fse-home.ps1");
        var verifier = ReadSource("scripts", "verify-publish-assets.ps1");
        var sdkTool = ReadSource("scripts", "invoke-sdk-tool.ps1");

        Assert.Contains("package-fse-home.ps1", pack, StringComparison.Ordinal);
        Assert.Contains("-RequireFsePackage", pack, StringComparison.Ordinal);
        Assert.Contains("SteamInputAddonforClaw.FseHome.msix", pack, StringComparison.Ordinal);
        Assert.Contains("CertificatePath", package, StringComparison.Ordinal);
        Assert.Contains("CertificatePassword", package, StringComparison.Ordinal);
        Assert.Contains("signtool", verifier, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("CustomCapability.SCCD", verifier, StringComparison.Ordinal);
        Assert.Contains("WindowsSdkDir", package, StringComparison.Ordinal);
        Assert.Contains("WindowsSdkDir", verifier, StringComparison.Ordinal);
        Assert.Contains("x64", package, StringComparison.Ordinal);
        Assert.Contains("x64", verifier, StringComparison.Ordinal);
        Assert.DoesNotContain("-Path $roots -Recurse", package, StringComparison.Ordinal);
        Assert.DoesNotContain("-Path $roots -Recurse", verifier, StringComparison.Ordinal);
        Assert.Contains("ProcessStartInfo", sdkTool, StringComparison.Ordinal);
        Assert.Contains("UseShellExecute = $false", sdkTool, StringComparison.Ordinal);
        Assert.Contains("WaitForExit", sdkTool, StringComparison.Ordinal);
        Assert.Contains("ReadToEndAsync", sdkTool, StringComparison.Ordinal);
        Assert.Contains("RedirectStandardOutput", sdkTool, StringComparison.Ordinal);
        Assert.Contains("RedirectStandardError", sdkTool, StringComparison.Ordinal);
        Assert.Contains(".Kill()", sdkTool, StringComparison.Ordinal);
        Assert.Contains("Invoke-SdkTool", package, StringComparison.Ordinal);
        Assert.Contains("Invoke-SdkTool", verifier, StringComparison.Ordinal);
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
