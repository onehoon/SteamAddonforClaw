using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class XboxGameSessionRetirementUiTests
{
    [Fact]
    public void Developer_session_diagnostic_page_and_navigation_are_retired()
    {
        var root = RepositoryRoot();
        var developerPage = Read(root, "src/SteamInputAddonforClaw.UI/Views/DeveloperPage.xaml");
        var developerCode = Read(root, "src/SteamInputAddonforClaw.UI/Views/DeveloperPage.xaml.cs");
        var mainWindowXaml = Read(root, "src/SteamInputAddonforClaw.UI/MainWindow.xaml");
        var mainWindowCode = Read(root, "src/SteamInputAddonforClaw.UI/MainWindow.xaml.cs");
        var navigation = Read(root, "src/SteamInputAddonforClaw.UI/MainNavigationState.cs");
        var app = Read(root, "src/SteamInputAddonforClaw.UI/App.xaml.cs");

        foreach (var source in new[] { developerPage, developerCode, mainWindowXaml, mainWindowCode, navigation, app })
            Assert.DoesNotContain("XboxSessionDiagnostic", source, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(root, "src/SteamInputAddonforClaw.UI/Views/XboxSessionDiagnosticPage.xaml")));
        Assert.False(File.Exists(Path.Combine(root, "src/SteamInputAddonforClaw.UI/Views/XboxSessionDiagnosticPage.xaml.cs")));
    }

    private static string Read(string root, string relativePath) =>
        File.ReadAllText(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));

    private static string RepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "SteamInputAddonforClaw.slnx")))
                return directory.FullName;
        throw new DirectoryNotFoundException("Repository root was not found.");
    }
}
