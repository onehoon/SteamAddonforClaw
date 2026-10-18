using System.Xml.Linq;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class MsiLiveUpdateCatalogUiTests
{
    [Fact]
    public void Catalog_expander_is_inserted_between_summary_and_existing_center_m_card()
    {
        var xaml = ReadSource("src", "SteamInputAddonforClaw.UI", "Views", "DevicePage.xaml");

        var summary = xaml.IndexOf("x:Name=\"DeviceSummaryCard\"", StringComparison.Ordinal);
        var expander = xaml.IndexOf("x:Name=\"MsiUpdatesExpander\"", StringComparison.Ordinal);
        var centerM = xaml.IndexOf("x:Name=\"CenterMStartupInfoBar\"", StringComparison.Ordinal);

        Assert.True(summary >= 0 && summary < expander && expander < centerM);
        Assert.Contains("x:Uid=\"Device_MsiUpdatesExpander\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Header=\"MSI Updates\" IsExpanded=\"False\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void Catalog_query_is_manual_and_tabs_are_fixed()
    {
        var xaml = ReadSource("src", "SteamInputAddonforClaw.UI", "Views", "DevicePage.xaml");
        var code = ReadSource("src", "SteamInputAddonforClaw.UI", "Views", "DevicePage.xaml.cs");
        var activate = ExtractMethod(code, "internal void Activate()");
        var invalidated = ExtractMethod(code, "private void OnStateInvalidated(");
        var click = ExtractMethod(code, "private async void MsiUpdatesCheckButton_Click(");

        Assert.Contains("Click=\"MsiUpdatesCheckButton_Click\"", xaml, StringComparison.Ordinal);
        Assert.Contains("IsAddTabButtonVisible=\"False\" CanReorderTabs=\"False\" CanDragTabs=\"False\"", xaml, StringComparison.Ordinal);
        Assert.Equal(4, xaml.Split("<TabViewItem ", StringSplitOptions.None).Length - 1);
        Assert.All(new[] { "Bios", "Firmware", "Drivers", "Utilities" }, name =>
            Assert.Contains($"Device_MsiUpdates{name}Tab", xaml, StringComparison.Ordinal));
        Assert.DoesNotContain("RequestMsiLiveUpdateCatalogAsync", activate, StringComparison.Ordinal);
        Assert.DoesNotContain("RequestMsiLiveUpdateCatalogAsync", invalidated, StringComparison.Ordinal);
        Assert.Contains("RequestMsiLiveUpdateCatalogAsync", click, StringComparison.Ordinal);
        Assert.DoesNotContain("Content=\"Install\"", xaml, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("MsiUpdatesInstall", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void Msi_catalog_strings_exist_in_both_supported_locales()
    {
        var english = ReadResources("en-US");
        var korean = ReadResources("ko-KR");
        var required = new[]
        {
            "Device_MsiUpdatesExpander.Header", "Device_MsiUpdatesExpander.Description",
            "Device_MsiUpdatesCatalogOnly.Text", "Device_MsiUpdatesCheckButton.Content",
            "Device_MsiUpdatesBiosTab.Header", "Device_MsiUpdatesFirmwareTab.Header",
            "Device_MsiUpdatesDriversTab.Header", "Device_MsiUpdatesUtilitiesTab.Header",
            "Device_MsiUpdates_Checking", "Device_MsiUpdates_NotChecked", "Device_MsiUpdates_Completed",
            "Device_MsiUpdates_PartialFailure", "Device_MsiUpdates_Unavailable", "Device_MsiUpdates_Failed",
            "Device_MsiUpdates_LastChecked", "Device_MsiUpdates_CategoryFailed", "Device_MsiUpdates_NoEntries",
            "Device_MsiUpdates_NotCheckedCategory", "Device_MsiUpdates_UnavailableCategory", "Device_MsiUpdates_NoData",
            "Device_MsiUpdates_LatestVersion", "Device_MsiUpdates_ReleaseDate", "Device_MsiUpdates_VersionNotProvided"
        };

        Assert.All(required, key =>
        {
            Assert.Contains(key, english);
            Assert.Contains(key, korean);
        });
    }

    [Fact]
    public void Catalog_description_decodes_entities_strips_markup_and_preserves_breaks()
    {
        var formatted = SteamInputAddonforClaw.Views.DevicePage.FormatMsiCatalogDescription(
            "Steps: &lt;b&gt;Install&lt;/b&gt; ISH_SW&gt;OS Components.<br> 1. Run SetupISS.exe.<br/> 2. Restart.");

        Assert.Equal(
            string.Join(Environment.NewLine,
                "Steps: Install ISH_SW>OS Components.",
                "1. Run SetupISS.exe.",
                "2. Restart."),
            formatted);
    }

    [Fact]
    public void Catalog_entries_use_the_app_theme_card_style()
    {
        var xaml = ReadSource("src", "SteamInputAddonforClaw.UI", "Views", "DevicePage.xaml");
        var code = ReadSource("src", "SteamInputAddonforClaw.UI", "Views", "DevicePage.xaml.cs");

        Assert.Contains("x:Key=\"MsiCatalogEntryCardStyle\"", xaml, StringComparison.Ordinal);
        Assert.Contains("{ThemeResource CardBackgroundFillColorDefaultBrush}", xaml, StringComparison.Ordinal);
        Assert.Contains("{ThemeResource CardStrokeColorDefaultBrush}", xaml, StringComparison.Ordinal);
        Assert.Contains("Style = (Style)Resources[\"MsiCatalogEntryCardStyle\"]", code, StringComparison.Ordinal);
        Assert.Contains("Text = FormatMsiCatalogDescription(entry.Description)", code, StringComparison.Ordinal);
    }

    private static string[] ReadResources(string culture)
    {
        var document = XDocument.Load(Path.Combine(FindRepositoryRoot(), "src", "SteamInputAddonforClaw.UI", "Strings", culture, "Resources.resw"));
        return document.Root!.Elements("data").Select(element => (string)element.Attribute("name")!).ToArray();
    }

    private static string ExtractMethod(string source, string signature)
    {
        var start = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Method signature not found: {signature}");
        var openBrace = source.IndexOf('{', start);
        var depth = 0;
        for (var index = openBrace; index < source.Length; index++)
        {
            if (source[index] == '{') depth++;
            else if (source[index] == '}' && --depth == 0) return source[start..(index + 1)];
        }
        throw new InvalidOperationException($"Method body was not closed: {signature}");
    }

    private static string ReadSource(params string[] pathParts) => File.ReadAllText(Path.Combine(FindRepositoryRoot(), Path.Combine(pathParts)));

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "SteamInputAddonforClaw.slnx"))) return directory.FullName;
        }
        throw new DirectoryNotFoundException("Repository root was not found.");
    }
}
