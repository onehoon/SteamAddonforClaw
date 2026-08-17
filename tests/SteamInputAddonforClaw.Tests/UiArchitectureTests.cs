using System.Xml.Linq;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class UiArchitectureTests
{
    [Fact]
    public void Project_references_preserve_the_headless_dependency_direction()
    {
        var ui = References("src/SteamInputAddonforClaw.UI/SteamInputAddonforClaw.UI.csproj");
        var runtime = References("src/SteamInputAddonforClaw/SteamInputAddonforClaw.csproj");
        var transport = References("src/SteamInputAddonforClaw.FrontendTransport/SteamInputAddonforClaw.FrontendTransport.csproj");

        Assert.Contains("SteamInputAddonforClaw.Contracts.csproj", ui);
        Assert.Contains("SteamInputAddonforClaw.FrontendTransport.csproj", ui);
        Assert.Contains("SteamInputAddonforClaw.FrontendTransport.csproj", runtime);
        Assert.Contains("SteamInputAddonforClaw.Contracts.csproj", transport);
        Assert.DoesNotContain("SteamInputAddonforClaw.csproj", ui);
        Assert.DoesNotContain("SteamInputAddonforClaw.UI.csproj", runtime);
        Assert.DoesNotContain("SteamInputAddonforClaw.csproj", transport);
    }

    [Fact]
    public void Frontend_sources_have_one_physical_owner()
    {
        var root = FindRepositoryRoot();
        Assert.True(File.Exists(Path.Combine(root, "src/SteamInputAddonforClaw.UI/MainWindow.xaml")));
        Assert.True(File.Exists(Path.Combine(root, "src/SteamInputAddonforClaw.UI/MainWindow.xaml.cs")));
        Assert.False(File.Exists(Path.Combine(root, "src/SteamInputAddonforClaw/MainWindow.xaml")));
        Assert.False(File.Exists(Path.Combine(root, "src/SteamInputAddonforClaw/MainWindow.xaml.cs")));
        Assert.False(File.Exists(Path.Combine(root, "src/SteamInputAddonforClaw/Views/ClawSensorProbePage.xaml")));
        Assert.False(File.Exists(Path.Combine(root, "src/SteamInputAddonforClaw.UI/Views/ClawSensorProbePage.xaml")));
        Assert.True(Directory.Exists(Path.Combine(root, "src/SteamInputAddonforClaw/Diagnostics/ClawSensorProbe")));
    }

    [Fact]
    public void Runtime_is_true_headless_and_ui_keeps_winui_ownership()
    {
        var root = FindRepositoryRoot();
        var runtimeProject = File.ReadAllText(Path.Combine(root, "src/SteamInputAddonforClaw/SteamInputAddonforClaw.csproj"));
        var uiProject = File.ReadAllText(Path.Combine(root, "src/SteamInputAddonforClaw.UI/SteamInputAddonforClaw.UI.csproj"));
        var runtimeSourceRoot = Path.Combine(root, "src/SteamInputAddonforClaw");

        Assert.DoesNotContain("<UseWinUI>true</UseWinUI>", runtimeProject, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Microsoft.WindowsAppSDK", runtimeProject, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("CommunityToolkit.WinUI", runtimeProject, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("<UseWinUI>true</UseWinUI>", uiProject, StringComparison.OrdinalIgnoreCase);
        Assert.True(!Directory.EnumerateFiles(runtimeSourceRoot, "*.xaml", SearchOption.AllDirectories)
            .Where(path => !path.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                        && !path.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            .Any());

        var sourceText = string.Join(Environment.NewLine,
            Directory.EnumerateFiles(runtimeSourceRoot, "*.cs", SearchOption.AllDirectories)
                .Where(path => !path.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                            && !path.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                .Select(File.ReadAllText));
        Assert.DoesNotContain("Microsoft.UI", sourceText, StringComparison.Ordinal);
        Assert.DoesNotContain("WinRT", sourceText, StringComparison.Ordinal);
        Assert.DoesNotContain("Application.Start", sourceText, StringComparison.Ordinal);
    }

    private static IReadOnlyList<string> References(string relativeProjectPath)
    {
        var projectPath = Path.Combine(FindRepositoryRoot(), relativeProjectPath.Replace('/', Path.DirectorySeparatorChar));
        return XDocument.Load(projectPath)
            .Descendants("ProjectReference")
            .Select(element => Path.GetFileName((string?)element.Attribute("Include") ?? string.Empty))
            .ToArray();
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "SteamInputAddonforClaw.slnx")))
                return directory.FullName;
        }
        throw new DirectoryNotFoundException("Repository root was not found.");
    }
}
