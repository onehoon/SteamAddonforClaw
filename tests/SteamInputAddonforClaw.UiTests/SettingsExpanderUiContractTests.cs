using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;

namespace SteamInputAddonforClaw.UiTests;

public sealed class SettingsExpanderUiContractTests
{
    [Fact]
    public void Only_the_Steam_controller_expander_starts_open_and_child_cards_have_no_icons()
    {
        var viewsDirectory = Path.Combine(FindRepositoryRoot(), "src", "SteamInputAddonforClaw.UI", "Views");
        var expanders = Directory.EnumerateFiles(viewsDirectory, "*.xaml", SearchOption.AllDirectories)
            .SelectMany(path => XDocument.Load(path)
                .Descendants()
                .Where(element => element.Name.LocalName == "SettingsExpander")
                .Select(element => (Path: path, Element: element)))
            .ToArray();

        Assert.NotEmpty(expanders);
        foreach (var (path, expander) in expanders)
        {
            var name = (string?)expander.Attribute(XName.Get("Name", "http://schemas.microsoft.com/winfx/2006/xaml"));
            var shouldStartExpanded = string.Equals(name, "SteamVirtualControllerExpander", StringComparison.Ordinal);
            Assert.True(
                string.Equals(shouldStartExpanded ? "True" : "False", (string?)expander.Attribute("IsExpanded"), StringComparison.OrdinalIgnoreCase),
                $"{Path.GetFileName(path)}: {name ?? "unnamed expander"} has an unexpected initial expansion state.");

            Assert.Single(expander.Elements(), element => element.Name.LocalName == "SettingsExpander.HeaderIcon");
            foreach (var card in expander.Descendants().Where(element => element.Name.LocalName == "SettingsCard"))
            {
                Assert.DoesNotContain(card.Elements(), element => element.Name.LocalName == "SettingsCard.HeaderIcon");
            }
        }
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

    [Fact]
    public void Generated_required_component_cards_do_not_add_inner_icons()
    {
        var path = Path.Combine(FindRepositoryRoot(), "src", "SteamInputAddonforClaw.UI", "Views", "SettingsPage.xaml.cs");
        var source = File.ReadAllText(path);
        var start = source.IndexOf("internal void RenderRequiredComponents(", StringComparison.Ordinal);
        var end = source.IndexOf("internal static Visibility GetDeveloperMenuCardVisibility(", start, StringComparison.Ordinal);

        Assert.True(start >= 0);
        Assert.True(end > start);
        Assert.DoesNotContain("HeaderIcon", source[start..end], StringComparison.Ordinal);
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
