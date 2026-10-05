using SteamInputAddonforClaw.Windowing;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class XboxSessionDiagnosticUiTests
{
    [Fact]
    public void Developer_menu_opens_a_dedicated_session_diagnostic_child_and_mouse_back_returns()
    {
        var navigation = new MainNavigationState();
        navigation.OpenDeveloperMenu();

        Assert.Equal(MainNavigationPage.XboxSessionDiagnostic, navigation.OpenXboxSessionDiagnostic());
        Assert.Equal(MainNavigationPage.DeveloperMenu, navigation.GetMouseBackDestination());
        Assert.Equal(MainNavigationPage.DeveloperMenu, navigation.ReturnToDeveloperMenu());
    }

    [Fact]
    public void Developer_page_exposes_start_stop_report_and_page_leave_stops_runtime_observer()
    {
        var developerXaml = Read("src/SteamInputAddonforClaw.UI/Views/DeveloperPage.xaml");
        var mainWindowXaml = Read("src/SteamInputAddonforClaw.UI/MainWindow.xaml");
        var diagnosticXaml = Read("src/SteamInputAddonforClaw.UI/Views/XboxSessionDiagnosticPage.xaml");
        var diagnosticCode = Read("src/SteamInputAddonforClaw.UI/Views/XboxSessionDiagnosticPage.xaml.cs");
        var mainWindowCode = Read("src/SteamInputAddonforClaw.UI/MainWindow.xaml.cs");

        Assert.Contains("Header=\"XBOX Active Game Diagnostic\"", developerXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"XboxSessionDiagnosticContent\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Start", diagnosticXaml, StringComparison.Ordinal);
        Assert.Contains("Stop", diagnosticXaml, StringComparison.Ordinal);
        Assert.Contains("Capture Report", diagnosticXaml, StringComparison.Ordinal);
        Assert.Contains("!_requestPending && !_observerRunning", diagnosticCode, StringComparison.Ordinal);
        Assert.Contains("_stopOnLeaveTask ??= StopAfterLeavingPageAsync()", diagnosticCode, StringComparison.Ordinal);
        Assert.Contains("StopAndRenderAsync(forceStop: true)", diagnosticCode, StringComparison.Ordinal);
        Assert.Contains("else if (wasXboxSessionDiagnostic) XboxSessionDiagnosticContent.Deactivate()", mainWindowCode, StringComparison.Ordinal);
    }

    [Fact]
    public void UI_does_not_query_process_package_or_config_identity()
    {
        var sources = string.Join("\n", [
            Read("src/SteamInputAddonforClaw.UI/MainWindow.xaml.cs"),
            Read("src/SteamInputAddonforClaw.UI/Views/XboxSessionDiagnosticPage.xaml.cs"),
        ]);

        Assert.DoesNotContain("SetWinEventHook", sources, StringComparison.Ordinal);
        Assert.DoesNotContain("QueryFullProcessImageName", sources, StringComparison.Ordinal);
        Assert.DoesNotContain("GetPackageFullName", sources, StringComparison.Ordinal);
        Assert.DoesNotContain("GetPackageFamilyName", sources, StringComparison.Ordinal);
        Assert.DoesNotContain("MicrosoftGameConfigReader", sources, StringComparison.Ordinal);
        Assert.DoesNotContain("XDocument", sources, StringComparison.Ordinal);
    }

    [Fact]
    public void Overlay_source_and_wire_protocol_have_no_session_diagnostic_surface()
    {
        var overlayRoot = Path.Combine(RepositoryRoot(), "src", "SteamInputAddonforClaw.Overlay");
        var overlaySources = string.Join("\n", Directory.GetFiles(overlayRoot, "*.cs", SearchOption.AllDirectories)
            .Concat(Directory.GetFiles(overlayRoot, "*.xaml", SearchOption.AllDirectories))
            .Select(File.ReadAllText));
        var overlayWire = Read("src/SteamInputAddonforClaw.FrontendTransport/OverlayWire.cs");

        Assert.DoesNotContain("XboxSessionDiagnostic", overlaySources, StringComparison.Ordinal);
        Assert.DoesNotContain("XboxSessionDiagnostic", overlayWire, StringComparison.Ordinal);
    }

    private static string Read(string relativePath)
    {
        var path = Path.Combine(RepositoryRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar));
        return File.ReadAllText(path);
    }

    private static string RepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "SteamInputAddonforClaw.slnx")))
                return directory.FullName;
        throw new DirectoryNotFoundException("Repository root was not found.");
    }
}
