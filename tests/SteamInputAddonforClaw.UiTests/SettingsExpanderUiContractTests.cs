using Xunit;

namespace SteamInputAddonforClaw.UiTests;

public sealed class SettingsExpanderUiContractTests
{
    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "SteamInputAddonforClaw.slnx")))
            directory = directory.Parent;

        Assert.NotNull(directory);
        return directory!.FullName;
    }

    [Fact]
    public void View_code_does_not_reopen_expanders_when_rendering_snapshots()
    {
        var viewsDirectory = Path.Combine(FindRepositoryRoot(), "src", "SteamInputAddonforClaw.UI", "Views");
        foreach (var path in Directory.EnumerateFiles(viewsDirectory, "*.xaml.cs", SearchOption.AllDirectories))
        {
            Assert.DoesNotMatch(@"\.\s*IsExpanded\s*=", File.ReadAllText(path));
        }
    }


}
