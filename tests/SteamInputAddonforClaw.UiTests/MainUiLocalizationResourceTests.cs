using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using SteamInputAddonforClaw.UI;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class MainUiLocalizationResourceTests
{
    private static readonly XNamespace XamlNamespace = "http://schemas.microsoft.com/winfx/2006/xaml";

    [Fact]
    public void Main_ui_resw_files_share_keys_and_include_nonempty_pilot_resources()
    {
        var stringsDirectory = Path.Combine(FindRepositoryRoot(), "src", "SteamInputAddonforClaw.UI", "Strings");
        var languages = Directory.EnumerateDirectories(stringsDirectory)
            .Select(Path.GetFileName)
            .OrderBy(language => language, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(new[] { "en-US", "ko-KR" }, languages);

        var english = ReadResources(Path.Combine(stringsDirectory, "en-US", "Resources.resw"));
        var korean = ReadResources(Path.Combine(stringsDirectory, "ko-KR", "Resources.resw"));
        var requiredPilotKeys = new[]
        {
            "MainNav_Device.Content",
            "HowToUse_Loading.Text",
            "Settings_AppTheme_Description"
        };

        Assert.Equal(english.Keys.OrderBy(key => key, StringComparer.Ordinal), korean.Keys.OrderBy(key => key, StringComparer.Ordinal));
        foreach (var key in requiredPilotKeys)
        {
            Assert.Contains(key, english.Keys);
        }
        Assert.All(english.Values, value => Assert.False(string.IsNullOrWhiteSpace(value)));
        Assert.All(korean.Values, value => Assert.False(string.IsNullOrWhiteSpace(value)));
    }

    [Fact]
    public void Device_navigation_localizes_only_content_and_keeps_its_stable_identity()
    {
        var root = FindRepositoryRoot();
        var document = XDocument.Load(Path.Combine(root, "src", "SteamInputAddonforClaw.UI", "MainWindow.xaml"));
        var device = Assert.Single(document.Descendants(), element =>
            (string?)element.Attribute(XamlNamespace + "Name") == "DeviceNavigationItem");

        Assert.Equal("MainNav_Device", (string?)device.Attribute(XamlNamespace + "Uid"));
        Assert.Equal("Device", (string?)device.Attribute("Content"));
        Assert.Equal("Device", (string?)device.Attribute("Tag"));
    }

    [Fact]
    public void How_to_use_loading_localizes_only_the_existing_fallback_text()
    {
        var root = FindRepositoryRoot();
        var document = XDocument.Load(Path.Combine(root, "src", "SteamInputAddonforClaw.UI", "Views", "HowToUsePage.xaml"));
        var loading = Assert.Single(document.Descendants(), element =>
            element.Name.LocalName == "TextBlock" && (string?)element.Attribute("Text") == "Loading documentation…");

        Assert.Equal("HowToUse_Loading", (string?)loading.Attribute(XamlNamespace + "Uid"));
        Assert.Equal("Loading documentation…", (string?)loading.Attribute("Text"));
    }

    [Fact]
    public void App_theme_description_lookup_keeps_the_existing_failure_priority_and_English_fallback()
    {
        var source = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "src", "SteamInputAddonforClaw.UI", "Views", "SettingsPage.xaml.cs"));

        Assert.Contains("private const string DefaultAppThemeDescription = \"Choose the main app appearance.\";", source, StringComparison.Ordinal);
        Assert.Contains("AppThemeCard.Description = failureDescription ?? GetDefaultAppThemeDescription();", source, StringComparison.Ordinal);
        Assert.Contains("return MainUiStrings.Get(\"Settings_AppTheme_Description\", DefaultAppThemeDescription);", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Device_xaml_text_uses_unique_resource_uids_and_excludes_the_summary_card()
    {
        var root = FindRepositoryRoot();
        var document = XDocument.Load(Path.Combine(root, "src", "SteamInputAddonforClaw.UI", "Views", "DevicePage.xaml"));
        var english = ReadResources(Path.Combine(root, "src", "SteamInputAddonforClaw.UI", "Strings", "en-US", "Resources.resw"));
        var korean = ReadResources(Path.Combine(root, "src", "SteamInputAddonforClaw.UI", "Strings", "ko-KR", "Resources.resw"));
        var expectedUids = new[]
        {
            "Device_PageTitle",
            "Device_CenterMStartupCard",
            "Device_CenterMStartupStatusText",
            "Device_CenterMStartupEnableButton",
            "Device_CenterMStartupDisableButton",
            "Device_TdpExpander",
            "Device_TdpAcCard",
            "Device_TdpDcCard",
            "Device_CpuBoostExpander",
            "Device_CpuBoostAcCard",
            "Device_CpuBoostDcCard",
            "Device_PowerModeExpander",
            "Device_PowerModeAcCard",
            "Device_PowerModeDcCard",
            "Device_BatteryChargeLimitCard"
        };
        var uidElements = document.Descendants()
            .Where(element => element.Attribute(XamlNamespace + "Uid") is not null)
            .ToArray();
        var actualUids = uidElements.Select(element => (string)element.Attribute(XamlNamespace + "Uid")!).ToArray();

        Assert.Equal(expectedUids.OrderBy(value => value, StringComparer.Ordinal), actualUids.OrderBy(value => value, StringComparer.Ordinal));
        Assert.Equal(actualUids.Length, actualUids.Distinct(StringComparer.Ordinal).Count());
        foreach (var element in uidElements)
        {
            var uid = (string)element.Attribute(XamlNamespace + "Uid")!;
            foreach (var property in new[] { "Text", "Content", "Header", "Description" })
            {
                if (element.Attribute(property) is null) continue;
                var key = $"{uid}.{property}";
                Assert.Contains(key, english.Keys);
                Assert.Contains(key, korean.Keys);
            }
        }

        var summaryCard = Assert.Single(document.Descendants(), element =>
            (string?)element.Attribute(XamlNamespace + "Name") == "DeviceSummaryCard");
        Assert.DoesNotContain(summaryCard.DescendantsAndSelf(), element => element.Attribute(XamlNamespace + "Uid") is not null);
        Assert.Equal(
            new[]
            {
                "Border(Name=DeviceSummaryCard,Padding=18,16,Background={ThemeResource CardBackgroundFillColorDefaultBrush},BorderBrush={ThemeResource CardStrokeColorDefaultBrush},BorderThickness=1,CornerRadius=8)",
                "StackPanel(Spacing=4)",
                "Grid(ColumnSpacing=8)",
                "Grid.ColumnDefinitions()",
                "ColumnDefinition(Width=Auto)",
                "ColumnDefinition(Width=*)",
                "ColumnDefinition(Width=Auto)",
                "TextBlock(Name=DeviceManufacturerText,FontSize=18,FontWeight=SemiBold,VerticalAlignment=Center)",
                "TextBlock(Name=DeviceModelText,Grid.Column=1,FontSize=18,FontWeight=SemiBold,TextWrapping=Wrap,VerticalAlignment=Center)",
                "Border(Grid.Column=2,Padding=8,3,Background={ThemeResource SubtleFillColorSecondaryBrush},CornerRadius=12,HorizontalAlignment=Right,VerticalAlignment=Center)",
                "TextBlock(Name=DeviceSupportText,FontSize=13,FontWeight=SemiBold,TextAlignment=Center,TextWrapping=Wrap,VerticalAlignment=Center)",
                "TextBlock(Name=DeviceBoardGpuText,Opacity=0.7,TextWrapping=Wrap)"
            },
            summaryCard.DescendantsAndSelf().Select(element =>
                $"{element.Name.LocalName}({string.Join(",", element.Attributes().Select(attribute => $"{attribute.Name.LocalName}={attribute.Value}"))})"));
    }

    [Fact]
    public void Device_code_string_keys_exist_and_selection_modes_keep_their_identity_and_order()
    {
        var root = FindRepositoryRoot();
        var source = File.ReadAllText(Path.Combine(root, "src", "SteamInputAddonforClaw.UI", "Views", "DevicePage.xaml.cs"))
            .Replace("\r\n", "\n", StringComparison.Ordinal);
        var english = ReadResources(Path.Combine(root, "src", "SteamInputAddonforClaw.UI", "Strings", "en-US", "Resources.resw"));
        var korean = ReadResources(Path.Combine(root, "src", "SteamInputAddonforClaw.UI", "Strings", "ko-KR", "Resources.resw"));
        var resourceKeys = Regex.Matches(source, "\\\"(Device_[^\\\"]+)\\\"")
            .Select(match => match.Groups[1].Value)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        Assert.NotEmpty(resourceKeys);
        foreach (var key in resourceKeys)
        {
            Assert.Contains(key, english.Keys);
            Assert.Contains(key, korean.Keys);
        }

        var cpuModes = Regex.Matches(source, "new\\(CpuBoostMode\\.(?<mode>\\w+),\\s*MainUiStrings\\.Get\\(\\\"(?<key>Device_CpuBoost_Mode_[^\\\"]+)\\\"")
            .Select(match => (Mode: match.Groups["mode"].Value, Key: match.Groups["key"].Value))
            .ToArray();
        Assert.Equal(
            new[]
            {
                ("Disabled", "Device_CpuBoost_Mode_Disabled"),
                ("Enabled", "Device_CpuBoost_Mode_Enabled"),
                ("Aggressive", "Device_CpuBoost_Mode_Aggressive"),
                ("EfficientEnabled", "Device_CpuBoost_Mode_EfficientEnabled"),
                ("EfficientAggressive", "Device_CpuBoost_Mode_EfficientAggressive"),
                ("AggressiveAtGuaranteed", "Device_CpuBoost_Mode_AggressiveAtGuaranteed"),
                ("EfficientAggressiveAtGuaranteed", "Device_CpuBoost_Mode_EfficientAggressiveAtGuaranteed")
            },
            cpuModes);

        var powerModes = Regex.Matches(source, "new\\(WindowsPowerMode\\.(?<mode>\\w+),\\s*MainUiStrings\\.Get\\(\\\"(?<key>Device_PowerMode_Mode_[^\\\"]+)\\\"")
            .Select(match => (Mode: match.Groups["mode"].Value, Key: match.Groups["key"].Value))
            .ToArray();
        Assert.Equal(
            new[]
            {
                ("BestPowerEfficiency", "Device_PowerMode_Mode_BestPowerEfficiency"),
                ("Balanced", "Device_PowerMode_Mode_Balanced"),
                ("BestPerformance", "Device_PowerMode_Mode_BestPerformance")
            },
            powerModes);
    }

    [Fact]
    public void Device_format_resources_have_compatible_placeholders_and_format_safely()
    {
        var root = FindRepositoryRoot();
        var english = ReadResources(Path.Combine(root, "src", "SteamInputAddonforClaw.UI", "Strings", "en-US", "Resources.resw"));
        var korean = ReadResources(Path.Combine(root, "src", "SteamInputAddonforClaw.UI", "Strings", "ko-KR", "Resources.resw"));
        var formatKeys = new[]
        {
            "Device_Battery_CurrentKnown",
            "Device_Battery_DesiredKnown",
            "Device_CpuBoost_LastChangeFailed"
        };

        foreach (var key in formatKeys)
        {
            var englishIndexes = PlaceholderIndexes(english[key]);
            var koreanIndexes = PlaceholderIndexes(korean[key]);
            Assert.Equal(englishIndexes, koreanIndexes);
            Assert.NotEmpty(englishIndexes);
            foreach (var value in new[] { english[key], korean[key] })
            {
                var args = Enumerable.Range(0, englishIndexes.Max() + 1).Cast<object>().ToArray();
                Assert.NotNull(string.Format(CultureInfo.InvariantCulture, value, args));
            }
        }

        foreach (var key in new[] { "Device_CenterM_EnableConfirmation", "Device_CenterM_DisableConfirmation" })
        {
            Assert.Single(Regex.Matches(english[key], "\\n\\n").Cast<Match>());
            Assert.Single(Regex.Matches(korean[key], "\\n\\n").Cast<Match>());
        }

        Assert.Equal("safe label", MainUiStrings.Format("Missing_Device_Test_Key", "safe {0}", "label"));
        var helper = File.ReadAllText(Path.Combine(root, "src", "SteamInputAddonforClaw.UI", "MainUiStrings.cs"));
        Assert.Contains("new ResourceManager(priPath)", helper, StringComparison.Ordinal);
        Assert.Contains("Lazy<ResourceManager?> Resource", helper, StringComparison.Ordinal);
        Assert.Single(Regex.Matches(helper, "new ResourceManager\\(").Cast<Match>());
        Assert.Contains("catch (Exception)", helper, StringComparison.Ordinal);
        Assert.Contains("catch (FormatException)", helper, StringComparison.Ordinal);
        Assert.Contains("englishFallback, args", helper, StringComparison.Ordinal);
        Assert.Contains("return englishFallback;", helper, StringComparison.Ordinal);
    }

    [Fact]
    public void Center_m_localization_preserves_confirmation_order_and_authoritative_failure_messages()
    {
        var root = FindRepositoryRoot();
        var source = File.ReadAllText(Path.Combine(root, "src", "SteamInputAddonforClaw.UI", "Views", "DevicePage.xaml.cs"))
            .Replace("\r\n", "\n", StringComparison.Ordinal);
        var dialog = source[source.IndexOf("private async Task RequestCenterMTransitionAsync", StringComparison.Ordinal)..];
        var showIndex = dialog.IndexOf("dialog.ShowAsync()", StringComparison.Ordinal);
        var requestIndex = dialog.IndexOf("_frontend.RequestCenterMAuthorityTransitionAsync(centerMEnabled)", StringComparison.Ordinal);

        Assert.True(showIndex >= 0 && requestIndex > showIndex);
        Assert.Contains("if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;", dialog, StringComparison.Ordinal);
        Assert.Contains("DefaultButton = ContentDialogButton.Close", dialog, StringComparison.Ordinal);
        Assert.Contains("result.FailureMessage\n                    ??", dialog, StringComparison.Ordinal);
        Assert.Contains("snapshot.FailureMessage ?? MainUiStrings.Get(\"Device_CenterM_Unavailable\"", source, StringComparison.Ordinal);
        Assert.Contains("snapshot.LastFailure ?? (editable", source, StringComparison.Ordinal);
        Assert.Contains("result.FailureMessage ?? MainUiStrings.Get(\"Device_Tdp_UpdateFailed\"", source, StringComparison.Ordinal);
        Assert.Contains("result.FailureMessage ?? MainUiStrings.Get(\"Device_CpuBoost_ChangeFailed\"", source, StringComparison.Ordinal);
        Assert.Contains("result.FailureMessage ?? MainUiStrings.Get(\"Device_PowerMode_UpdateFailed\"", source, StringComparison.Ordinal);
        Assert.Contains("result.FailureMessage ?? MainUiStrings.Get(\"Device_Battery_UpdateFailed\"", source, StringComparison.Ordinal);
        Assert.Contains("!centerMEnabled && result.Snapshot.State == FrontendCenterMStartupState.Enabled", dialog, StringComparison.Ordinal);
    }

    private static int[] PlaceholderIndexes(string value) => Regex.Matches(value, @"\{(?<index>\d+)\}")
        .Select(match => int.Parse(match.Groups["index"].Value, CultureInfo.InvariantCulture))
        .Distinct()
        .OrderBy(index => index)
        .ToArray();

    private static Dictionary<string, string> ReadResources(string path)
    {
        var document = XDocument.Load(path);
        return document.Root!
            .Elements("data")
            .ToDictionary(
                element => (string)element.Attribute("name")!,
                element => element.Element("value")!.Value,
                StringComparer.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "SteamInputAddonforClaw.slnx")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root was not found.");
    }
}
