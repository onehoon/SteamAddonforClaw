using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class XboxCatalogDiagnosticUiTests
{
    [Fact]
    public void Developer_menu_opens_a_dedicated_read_only_diagnostic_child()
    {
        var developerXaml = Read("src/SteamInputAddonforClaw.UI/Views/DeveloperPage.xaml");
        var mainWindowXaml = Read("src/SteamInputAddonforClaw.UI/MainWindow.xaml");
        var diagnosticXaml = Read("src/SteamInputAddonforClaw.UI/Views/XboxCatalogDiagnosticPage.xaml");
        var mainWindowCode = Read("src/SteamInputAddonforClaw.UI/MainWindow.xaml.cs");

        Assert.Contains("Header=\"XBOX Catalog Diagnostic\"", developerXaml, StringComparison.Ordinal);
        Assert.Contains("Click=\"OpenXboxCatalogDiagnosticButton_Click\"", developerXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"XboxCatalogDiagnosticContent\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Read-only. No package or profile changes are performed.", diagnosticXaml, StringComparison.Ordinal);
        Assert.Contains("Back to Developer Menu", diagnosticXaml, StringComparison.Ordinal);
        Assert.Contains("XboxCatalogDiagnosticContent.Initialize(_frontend)", mainWindowCode, StringComparison.Ordinal);
    }

    [Fact]
    public void Scan_is_explicit_cancellation_aware_and_keeps_package_enumeration_out_of_the_ui()
    {
        var diagnosticXaml = Read("src/SteamInputAddonforClaw.UI/Views/XboxCatalogDiagnosticPage.xaml");
        var diagnosticCode = Read("src/SteamInputAddonforClaw.UI/Views/XboxCatalogDiagnosticPage.xaml.cs");
        var mainWindowXaml = Read("src/SteamInputAddonforClaw.UI/MainWindow.xaml");

        Assert.Contains("Content=\"Scan Installed XBOX Games\"", diagnosticXaml, StringComparison.Ordinal);
        Assert.Contains("RunXboxCatalogDiagnosticAsync(scanCancellation.Token)", diagnosticCode, StringComparison.Ordinal);
        Assert.Contains("_scanCancellation?.Cancel()", diagnosticCode, StringComparison.Ordinal);
        Assert.Contains("ScanButton.IsEnabled = false", diagnosticCode, StringComparison.Ordinal);
        Assert.DoesNotContain("Windows.Management.Deployment", diagnosticCode, StringComparison.Ordinal);
        Assert.DoesNotContain("FindPackagesForUser", diagnosticCode, StringComparison.Ordinal);
        Assert.DoesNotContain("NavigationViewItem Content=\"XBOX\"", mainWindowXaml, StringComparison.Ordinal);
    }

    private static string Read(string relativePath)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, relativePath.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(candidate))
                return File.ReadAllText(candidate);
        }

        throw new DirectoryNotFoundException($"Could not find repository source '{relativePath}'.");
    }
}
