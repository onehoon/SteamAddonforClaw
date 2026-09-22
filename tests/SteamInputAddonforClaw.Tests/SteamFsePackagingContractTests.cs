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
    public void Fse_package_keeps_a_fixed_identity_version_and_gaming_home_capability()
    {
        var manifest = ReadSource("src", "SteamInputAddonforClaw.FseHome", "Packaging", "AppxManifest.xml");
        var sccd = ReadSource("src", "SteamInputAddonforClaw.FseHome", "Packaging", "CustomCapability.SCCD");
        var project = ReadSource("src", "SteamInputAddonforClaw.FseHome", "SteamInputAddonforClaw.FseHome.csproj");

        Assert.Contains("Name=\"SteamInputAddonforClaw.FseHome\"", manifest, StringComparison.Ordinal);
        Assert.Contains("Publisher=\"CN=SteamInputAddonforClaw\"", manifest, StringComparison.Ordinal);
        Assert.Contains("Version=\"1.0.0.0\"", manifest, StringComparison.Ordinal);
        Assert.Contains("Name=\"windows.gamingApp\"", manifest, StringComparison.Ordinal);
        Assert.Contains("Name=\"Microsoft.appCategory.gamingHome_8wekyb3d8bbwe\"", manifest, StringComparison.Ordinal);
        Assert.Contains("Logo>Assets\\AppIcon.png", manifest, StringComparison.Ordinal);
        Assert.Contains("Square150x150Logo=\"Assets\\AppIcon.png\"", manifest, StringComparison.Ordinal);
        Assert.Contains("Square44x44Logo=\"Assets\\AppIcon.png\"", manifest, StringComparison.Ordinal);
        Assert.Contains("<Application Id=\"App\"", manifest, StringComparison.Ordinal);
        Assert.Contains("Microsoft.appCategory.gamingHome_8wekyb3d8bbwe", sccd, StringComparison.Ordinal);
        Assert.DoesNotContain("AllowExternalContent", manifest, StringComparison.Ordinal);
        Assert.DoesNotContain("SteamInputAddonforClaw.csproj", project, StringComparison.Ordinal);
    }

    [Fact]
    public void Runtime_does_not_provision_fse_and_first_enable_uses_the_fixed_elevated_entrypoint()
    {
        var host = ReadSource("src", "SteamInputAddonforClaw", "Hosting", "AddonProcessHost.cs");
        var program = ReadSource("src", "SteamInputAddonforClaw", "Program.cs");
        var configuration = ReadSource("src", "SteamInputAddonforClaw", "WindowsGaming", "SteamFseConfiguration.cs");
        var registration = ReadSource("src", "SteamInputAddonforClaw", "WindowsGaming", "SteamFseRegistration.cs");

        Assert.DoesNotContain("EnsureProvisioned", host, StringComparison.Ordinal);
        Assert.Contains("SteamFseElevatedRegistration.Argument", program, StringComparison.Ordinal);
        Assert.Contains("--register-fse-home", registration, StringComparison.Ordinal);
        Assert.Contains("PackageRelativePath", registration, StringComparison.Ordinal);
        Assert.Contains("SteamInputAddonforClaw.FseHome.msix", configuration, StringComparison.Ordinal);
        Assert.Contains("TrustedPeople", registration, StringComparison.Ordinal);
        Assert.Contains("AllowDevelopmentWithoutDevLicense", registration, StringComparison.Ordinal);
        Assert.Contains("SetEnabledAsync", configuration, StringComparison.Ordinal);
        Assert.Contains("cancellationToken.ThrowIfCancellationRequested();", registration, StringComparison.Ordinal);
        Assert.Contains("WaitForExitAsync(CancellationToken.None)", registration, StringComparison.Ordinal);
        Assert.DoesNotContain("TryTerminate", registration, StringComparison.Ordinal);
        Assert.DoesNotContain("Kill(entireProcessTree", registration, StringComparison.Ordinal);
    }

    [Fact]
    public void Release_and_pr_ci_use_fixed_public_artifacts_without_fse_signing_material()
    {
        var pack = ReadSource("scripts", "pack.ps1");
        var layout = ReadSource("scripts", "publish-layout.ps1");
        var verifier = ReadSource("scripts", "verify-publish-assets.ps1");
        var package = ReadSource("scripts", "package-fse-home.ps1");
        var ci = ReadSource(".github", "workflows", "ci.yml");
        var release = ReadSource(".github", "workflows", "release.yml");

        Assert.DoesNotContain("FseCertificatePath", pack, StringComparison.Ordinal);
        Assert.DoesNotContain("FseCertificatePassword", pack, StringComparison.Ordinal);
        Assert.Contains("Packaging\\Distribution", layout, StringComparison.Ordinal);
        Assert.Contains("SteamInputAddonforClaw.FseHome.cer", layout, StringComparison.Ordinal);
        Assert.Contains("ZipFile]::OpenRead", verifier, StringComparison.Ordinal);
        Assert.Contains("9D4C46ABCC1324803AE5AB031B11EC8EF39057D77C9C04FCB243D80BC122F86B", verifier, StringComparison.Ordinal);
        Assert.Contains("663053482DA50F9017CC902CA5DF6E9BBFD5A6F06624B8608266318F54687390", verifier, StringComparison.Ordinal);
        Assert.DoesNotContain("makeappx", verifier, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("signtool", verifier, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("CertificatePath", package, StringComparison.Ordinal);
        Assert.Contains("FseVersion", package, StringComparison.Ordinal);
        Assert.DoesNotContain("New-SelfSignedCertificate", ci, StringComparison.Ordinal);
        Assert.DoesNotContain("FSE_SIGNING_CERTIFICATE", release, StringComparison.Ordinal);
        Assert.DoesNotContain("FseCertificate", release, StringComparison.Ordinal);
    }

    private static string ReadSource(params string[] parts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "SteamInputAddonforClaw.slnx")))
            directory = directory.Parent;
        return File.ReadAllText(Path.Combine([directory!.FullName, .. parts]));
    }
}
