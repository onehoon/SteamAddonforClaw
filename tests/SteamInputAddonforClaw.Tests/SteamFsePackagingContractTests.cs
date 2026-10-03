using System.Security.Cryptography;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class SteamFsePackagingContractTests
{
    [Fact]
    public void Fse_home_owns_a_bounded_native_steam_handoff_without_controller_dependencies()
    {
        var program = ReadSource("src", "SteamInputAddonforClaw.FseHome", "Program.cs");
        var native = ReadSource("src", "SteamInputAddonforClaw.FseHome", "FseHomeNative.cs");
        var project = ReadSource("src", "SteamInputAddonforClaw.FseHome", "SteamInputAddonforClaw.FseHome.csproj");

        Assert.Contains("HandoffWindow.Create()", program, StringComparison.Ordinal);
        Assert.Contains("ReadinessTimeout = TimeSpan.FromSeconds(60)", program, StringComparison.Ordinal);
        Assert.Contains("ProbeIntervalMilliseconds = 500", program, StringComparison.Ordinal);
        Assert.Contains("Stopwatch.GetTimestamp()", program, StringComparison.Ordinal);
        Assert.Contains("SteamLauncher.RequestBigPicture()", program, StringComparison.Ordinal);
        Assert.Contains("handoffWindow?.Dispose()", program, StringComparison.Ordinal);
        Assert.Contains("0x80000000", native, StringComparison.Ordinal); // WS_POPUP
        Assert.Contains("0x00000008", native, StringComparison.Ordinal); // WS_EX_TOPMOST
        Assert.Contains("SetPerMonitorV2DpiAwareness()", native, StringComparison.Ordinal);
        Assert.Contains("EnumWindows", native, StringComparison.Ordinal);
        Assert.Contains("SetForegroundWindow(candidate.Handle)", program, StringComparison.Ordinal);
        Assert.Equal(1, CountOccurrences(program, "NativeMethods.SetForegroundWindow("));
        Assert.Contains("ExpectedProcessName = \"steamwebhelper\"", native, StringComparison.Ordinal);
        Assert.Contains("ExpectedWindowClass = \"SDL_app\"", native, StringComparison.Ordinal);
        Assert.Contains("ExpectedTitlePrefix = \"Steam Big Picture\"", native, StringComparison.Ordinal);
        Assert.Contains("intentionally matches SteamBigPictureWindowProbe", native, StringComparison.Ordinal);
        Assert.Contains("steam://open/bigpicture", program, StringComparison.Ordinal);
        Assert.Contains("SteamExe", program, StringComparison.Ordinal);
        Assert.Contains("SteamPath", program, StringComparison.Ordinal);
        Assert.Contains("finally", program, StringComparison.Ordinal);
        foreach (var forbidden in new[] { "VIIPER", "HidHide", "PID1902", "AttachThreadInput", "keybd_event", "SendInput" })
        {
            Assert.DoesNotContain(forbidden, program, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(forbidden, native, StringComparison.OrdinalIgnoreCase);
        }
        Assert.DoesNotContain("SteamInputAddonforClaw.csproj", project, StringComparison.Ordinal);
    }

    [Fact]
    public void Fse_package_keeps_a_fixed_identity_version_and_gaming_home_capability()
    {
        var manifest = ReadSource("src", "SteamInputAddonforClaw.FseHome", "Packaging", "AppxManifest.xml");
        var sccd = ReadSource("src", "SteamInputAddonforClaw.FseHome", "Packaging", "CustomCapability.SCCD");
        var project = ReadSource("src", "SteamInputAddonforClaw.FseHome", "SteamInputAddonforClaw.FseHome.csproj");

        Assert.Contains("Name=\"SteamInputAddonforClaw.FseHome\"", manifest, StringComparison.Ordinal);
        Assert.Contains("Publisher=\"CN=SteamInputAddonforClaw\"", manifest, StringComparison.Ordinal);
        Assert.Contains("Version=\"1.0.1.0\"", manifest, StringComparison.Ordinal);
        Assert.Contains("Name=\"windows.gamingApp\"", manifest, StringComparison.Ordinal);
        Assert.Contains("Name=\"Microsoft.appCategory.gamingHome_8wekyb3d8bbwe\"", manifest, StringComparison.Ordinal);
        Assert.Contains("Logo>Assets\\AppIcon.png", manifest, StringComparison.Ordinal);
        Assert.Contains("Square150x150Logo=\"Assets\\AppIcon.png\"", manifest, StringComparison.Ordinal);
        Assert.Contains("Square44x44Logo=\"Assets\\AppIcon.png\"", manifest, StringComparison.Ordinal);
        Assert.Contains("<Application Id=\"App\"", manifest, StringComparison.Ordinal);
        Assert.Contains("Microsoft.appCategory.gamingHome_8wekyb3d8bbwe", sccd, StringComparison.Ordinal);
        Assert.DoesNotContain("AllowExternalContent", manifest, StringComparison.Ordinal);
        Assert.DoesNotContain("uap:SplashScreen", manifest, StringComparison.Ordinal);
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
        var sdkTool = ReadSource("scripts", "invoke-sdk-tool.ps1");
        var configuration = ReadSource("src", "SteamInputAddonforClaw", "WindowsGaming", "SteamFseConfiguration.cs");
        var gitignore = ReadSource(".gitignore");
        var signingDocumentation = ReadSource("docs", "STEAM_FSE_PACKAGE_SIGNING.md");
        var ci = ReadSource(".github", "workflows", "ci.yml");
        var release = ReadSource(".github", "workflows", "release.yml");

        Assert.DoesNotContain("FseCertificatePath", pack, StringComparison.Ordinal);
        Assert.DoesNotContain("FseCertificatePassword", pack, StringComparison.Ordinal);
        Assert.Contains("Packaging\\Distribution", layout, StringComparison.Ordinal);
        Assert.Contains("SteamInputAddonforClaw.FseHome.msix", layout, StringComparison.Ordinal);
        Assert.Contains("SteamInputAddonforClaw.FseHome.cer", layout, StringComparison.Ordinal);
        Assert.DoesNotContain("$fseProject", layout, StringComparison.Ordinal);
        Assert.DoesNotContain("FSE Home publish failed", layout, StringComparison.Ordinal);
        Assert.Contains("ZipFile]::OpenRead", verifier, StringComparison.Ordinal);
        Assert.Contains("1.0.1.0", configuration, StringComparison.Ordinal);
        Assert.Contains("1.0.1.0", verifier, StringComparison.Ordinal);
        Assert.Contains("1.0.1.0", package, StringComparison.Ordinal);
        Assert.Contains(".private/", gitignore, StringComparison.Ordinal);
        Assert.Contains("*.pfx", gitignore, StringComparison.Ordinal);
        Assert.Contains("*.p12", gitignore, StringComparison.Ordinal);
        Assert.Contains(".private/signing/SteamInputAddonforClaw.FseHome.pfx", signingDocumentation, StringComparison.Ordinal);
        Assert.Contains("Reuse this same PFX", signingDocumentation, StringComparison.Ordinal);
        Assert.Contains("uninstall that exact package before registering 1.0.1.0", signingDocumentation, StringComparison.Ordinal);
        Assert.DoesNotContain("FSE_SIGNING_CERTIFICATE", signingDocumentation, StringComparison.Ordinal);
        var distribution = Path.Combine(
            FindRepositoryRoot(), "src", "SteamInputAddonforClaw.FseHome", "Packaging", "Distribution");
        var packagePath = Path.Combine(distribution, "SteamInputAddonforClaw.FseHome.msix");
        var certificatePath = Path.Combine(distribution, "SteamInputAddonforClaw.FseHome.cer");
        Assert.Contains(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(packagePath))), verifier, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(certificatePath))), verifier, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("makeappx", verifier, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("signtool", verifier, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("CertificatePath", package, StringComparison.Ordinal);
        Assert.Contains("-RedactArguments", package, StringComparison.Ordinal);
        Assert.Contains("[arguments redacted]", sdkTool, StringComparison.Ordinal);
        Assert.Contains("FseVersion", package, StringComparison.Ordinal);
        Assert.DoesNotContain("New-SelfSignedCertificate", ci, StringComparison.Ordinal);
        Assert.DoesNotContain("FSE_SIGNING_CERTIFICATE", release, StringComparison.Ordinal);
        Assert.DoesNotContain("FseCertificate", release, StringComparison.Ordinal);
    }

    private static string ReadSource(params string[] parts)
    {
        return File.ReadAllText(Path.Combine([FindRepositoryRoot(), .. parts]));
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "SteamInputAddonforClaw.slnx")))
            directory = directory.Parent;
        return directory!.FullName;
    }

    private static int CountOccurrences(string source, string value)
    {
        var count = 0;
        var index = 0;
        while ((index = source.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }
        return count;
    }
}
