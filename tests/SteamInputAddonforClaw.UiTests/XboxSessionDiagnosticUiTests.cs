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
    public void Force_stop_does_not_render_or_mutate_controls_after_page_retirement()
    {
        var diagnosticCode = Read("src/SteamInputAddonforClaw.UI/Views/XboxSessionDiagnosticPage.xaml.cs");
        var start = diagnosticCode.IndexOf("private async Task StopAndRenderAsync(bool forceStop = false)", StringComparison.Ordinal);
        var end = diagnosticCode.IndexOf("private TaskCompletionSource BeginRequest()", start, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start);
        var method = diagnosticCode[start..end];
        var forceStart = method.IndexOf("if (forceStop)", StringComparison.Ordinal);
        var requestStart = method.IndexOf("var request = BeginRequest();", forceStart, StringComparison.Ordinal);
        Assert.True(forceStart >= 0 && requestStart > forceStart);

        var forceStopPath = method[forceStart..requestStart];
        Assert.Contains("_frontend.StopXboxSessionDiagnosticAsync()", forceStopPath, StringComparison.Ordinal);
        Assert.Contains("_observerRunning = snapshot.State == FrontendXboxSessionDiagnosticState.Running;", forceStopPath, StringComparison.Ordinal);
        foreach (var forbidden in new[] { "Render(", "BeginRequest(", "EndRequest(", "UpdateButtons(" })
            Assert.DoesNotContain(forbidden, forceStopPath, StringComparison.Ordinal);

        var activePath = method[requestStart..];
        Assert.Contains("Render(await _frontend.StopXboxSessionDiagnosticAsync()", activePath, StringComparison.Ordinal);
        Assert.Contains("EndRequest(request);", activePath, StringComparison.Ordinal);
    }

    [Fact]
    public void Refresh_does_not_render_a_capture_that_completes_after_deactivation()
    {
        var diagnosticCode = Read("src/SteamInputAddonforClaw.UI/Views/XboxSessionDiagnosticPage.xaml.cs");
        var start = diagnosticCode.IndexOf("private async Task RefreshAsync()", StringComparison.Ordinal);
        var end = diagnosticCode.IndexOf("private async Task StopAfterLeavingPageAsync()", start, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start);
        var method = diagnosticCode[start..end];

        var capture = method.IndexOf("await frontend.CaptureXboxSessionDiagnosticAsync().ConfigureAwait(true)", StringComparison.Ordinal);
        Assert.True(capture >= 0);
        var activeCheck = method.IndexOf("if (!_isActive)", capture, StringComparison.Ordinal);
        Assert.True(activeCheck > capture);
        var render = method.IndexOf("Render(snapshot);", activeCheck, StringComparison.Ordinal);
        Assert.True(render > activeCheck);
        Assert.Contains("var frontend = _frontend;", method, StringComparison.Ordinal);
        Assert.Contains("if (frontend is null || !_isActive || _requestPending)", method, StringComparison.Ordinal);
        Assert.Contains("return;", method[activeCheck..render], StringComparison.Ordinal);
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
