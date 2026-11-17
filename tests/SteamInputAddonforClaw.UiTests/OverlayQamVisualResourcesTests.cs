using System.Xml.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace SteamInputAddonforClaw.UiTests;

public sealed class OverlayQamVisualResourcesTests
{
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    [Fact]
    public void Qam_resources_are_merged_after_winui_controls_and_define_referenced_keys_once()
    {
        var root = RepoRoot();
        var app = XDocument.Load(Path.Combine(root, "src", "SteamInputAddonforClaw.Overlay", "App.xaml"));
        var overlayDirectory = Path.Combine(root, "src", "SteamInputAddonforClaw.Overlay");
        var resources = XDocument.Load(Path.Combine(overlayDirectory, "Themes", "QamOverlayResources.xaml"));
        var merged = app.Descendants().Single(element => element.Name.LocalName.EndsWith(".MergedDictionaries", StringComparison.Ordinal)).Elements().ToArray();
        var controlsIndex = Array.FindIndex(merged, element => element.Name.LocalName == "XamlControlsResources");
        var qamIndex = Array.FindIndex(merged, element => (string?)element.Attribute("Source") == "Themes/QamOverlayResources.xaml");

        Assert.True(controlsIndex >= 0);
        Assert.True(qamIndex > controlsIndex);

        var keys = resources.Descendants().Attributes(Xaml + "Key").Select(attribute => attribute.Value).ToArray();
        Assert.Equal(keys.Length, keys.Distinct(StringComparer.Ordinal).Count());

        var referencedKeys = Directory.EnumerateFiles(overlayDirectory, "*.cs", SearchOption.TopDirectoryOnly)
            .Concat(Directory.EnumerateFiles(overlayDirectory, "*.xaml", SearchOption.AllDirectories))
            .SelectMany(path => Regex.Matches(File.ReadAllText(path),
                    "(?:OverlayQamResources\\.(?:Get|Brush|Style|ApplyTextStyle)\\([^)]*?|\\{StaticResource\\s+)(Qam[A-Za-z0-9]+)")
                .Select(match => match.Groups[1].Value))
            .Distinct(StringComparer.Ordinal);

        // OverlayQamResources.Brush supplies a transparent fallback when this optional key is absent.
        var optionalResourcesWithFallback = new HashSet<string>(StringComparer.Ordinal)
        {
            "QamSubtleStrokeBrush",
        };
        foreach (var key in referencedKeys.Except(optionalResourcesWithFallback, StringComparer.Ordinal))
            Assert.True(keys.Count(existing => existing == key) == 1, $"Referenced resource key '{key}' must be defined exactly once.");
        Assert.NotEmpty(referencedKeys);
    }











    [Fact]
    public void Qam_toggle_and_value_controls_opt_out_of_native_focus()
    {
        var resources = XDocument.Load(Path.Combine(
            RepoRoot(), "src", "SteamInputAddonforClaw.Overlay", "Themes", "QamOverlayResources.xaml"));
        var focusProperties = new[]
        {
            "IsTabStop",
            "IsFocusEngagementEnabled",
            "AllowFocusOnInteraction",
            "UseSystemFocusVisuals",
        };

        foreach (var styleKey in new[] { "QamToggleStyle", "QamValueButtonStyle" })
        {
            var style = resources.Descendants().Single(element =>
                element.Name.LocalName == "Style" && (string?)element.Attribute(Xaml + "Key") == styleKey);
            foreach (var property in focusProperties)
            {
                var setter = style.Elements().Single(element =>
                    element.Name.LocalName == "Setter" && (string?)element.Attribute("Property") == property);
                Assert.Equal("False", (string?)setter.Attribute("Value"));
            }
        }
    }
    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "README.md")))
            directory = directory.Parent;

        Assert.NotNull(directory);
        return directory!.FullName;
    }

}
