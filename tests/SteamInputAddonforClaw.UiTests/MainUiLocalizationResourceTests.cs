using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using SteamInputAddonforClaw.Contracts.BackButtons;
using SteamInputAddonforClaw.Contracts.Frontend;
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
            "MainNav_Steam.Content",
            "MainNav_Xbox.Content",
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
    public void Steam_navigation_localizes_only_content_and_keeps_Profile_identity()
    {
        var root = FindRepositoryRoot();
        var document = XDocument.Load(Path.Combine(root, "src", "SteamInputAddonforClaw.UI", "MainWindow.xaml"));
        var steam = Assert.Single(document.Descendants(), element =>
            element.Name.LocalName == "NavigationViewItem" && (string?)element.Attribute("Tag") == "Profile");

        Assert.Equal("MainNav_Steam", (string?)steam.Attribute(XamlNamespace + "Uid"));
        Assert.Equal("Steam", (string?)steam.Attribute("Content"));
        Assert.Equal("Profile", (string?)steam.Attribute("Tag"));
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
    public void Controller_navigation_and_fixed_xaml_prose_resolve_through_unique_resource_uids()
    {
        var root = FindRepositoryRoot();
        var mainWindow = XDocument.Load(Path.Combine(root, "src", "SteamInputAddonforClaw.UI", "MainWindow.xaml"));
        var controllerNavigation = Assert.Single(mainWindow.Descendants(), element =>
            element.Name.LocalName == "NavigationViewItem" && (string?)element.Attribute("Tag") == "Controller");
        Assert.Equal("MainNav_Controller", (string?)controllerNavigation.Attribute(XamlNamespace + "Uid"));
        Assert.Equal("Controller", (string?)controllerNavigation.Attribute("Content"));

        var document = XDocument.Load(Path.Combine(root, "src", "SteamInputAddonforClaw.UI", "Views", "ControllerPage.xaml"));
        var english = ReadResources(Path.Combine(root, "src", "SteamInputAddonforClaw.UI", "Strings", "en-US", "Resources.resw"));
        var korean = ReadResources(Path.Combine(root, "src", "SteamInputAddonforClaw.UI", "Strings", "ko-KR", "Resources.resw"));
        Assert.Contains("MainNav_Controller.Content", english.Keys);
        Assert.Contains("MainNav_Controller.Content", korean.Keys);

        var uidElements = document.Descendants()
            .Where(element => element.Attribute(XamlNamespace + "Uid") is not null)
            .ToArray();
        var actualUids = uidElements.Select(element => (string)element.Attribute(XamlNamespace + "Uid")!).ToArray();
        Assert.NotEmpty(actualUids);
        Assert.Equal(actualUids.Length, actualUids.Distinct(StringComparer.Ordinal).Count());
        foreach (var element in uidElements)
        {
            var uid = (string)element.Attribute(XamlNamespace + "Uid")!;
            Assert.StartsWith("Controller_", uid, StringComparison.Ordinal);
        }

        var localizedProperties = new[] { "Text", "Content", "Header", "Description" };
        foreach (var element in document.Descendants())
        {
            var visibleProperties = localizedProperties
                .Where(property => !string.IsNullOrWhiteSpace((string?)element.Attribute(property))
                    && !string.Equals((string?)element.Attribute(property), "—", StringComparison.Ordinal))
                .ToArray();
            if (visibleProperties.Length == 0) continue;

            var uid = (string?)element.Attribute(XamlNamespace + "Uid");
            Assert.False(string.IsNullOrWhiteSpace(uid), $"Visible Controller text has no x:Uid: {element}");
            foreach (var property in visibleProperties)
            {
                var key = $"{uid}.{property}";
                Assert.Contains(key, english.Keys);
                Assert.Contains(key, korean.Keys);
            }
        }
    }

    [Fact]
    public void Controller_code_display_resources_cover_actions_dynamic_back_targets_and_fallbacks()
    {
        var root = FindRepositoryRoot();
        var source = File.ReadAllText(Path.Combine(root, "src", "SteamInputAddonforClaw.UI", "Views", "ControllerPage.xaml.cs"))
            .Replace("\r\n", "\n", StringComparison.Ordinal);
        var english = ReadResources(Path.Combine(root, "src", "SteamInputAddonforClaw.UI", "Strings", "en-US", "Resources.resw"));
        var korean = ReadResources(Path.Combine(root, "src", "SteamInputAddonforClaw.UI", "Strings", "ko-KR", "Resources.resw"));
        var literalKeys = Regex.Matches(source, "MainUiStrings\\.Get\\(\\\"(?<key>Controller_[^\\\"]+)\\\"")
            .Select(match => match.Groups["key"].Value)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        Assert.NotEmpty(literalKeys);
        foreach (var key in literalKeys)
        {
            var resourceKey = key.Replace('/', '.');
            Assert.Contains(resourceKey, english.Keys);
            Assert.Contains(resourceKey, korean.Keys);
        }

        foreach (var (action, key, fallback) in new[]
        {
            ("QuickSettingsOverlay", "Controller_Action_QuickSettingsOverlay", "Quick Settings Overlay"),
            ("SteamBigPicture", "Controller_Action_SteamBigPicture", "Steam Big Picture"),
            ("SteamButton", "Controller_Action_SteamButton", "Steam Button"),
            ("SteamQuickAccess", "Controller_Action_SteamQuickAccess", "Steam Quick Access"),
            ("KeyboardHotkey", "Controller_Action_KeyboardHotkey", "Keyboard / Hotkey"),
            ("LaunchApplication", "Controller_Action_LaunchApplication", "Launch Application"),
            ("XboxApp", "Controller_Action_Xbox", "Xbox")
        })
        {
            Assert.Contains($"FrontButtonAction.{action} => MainUiStrings.Get(\"{key}\", \"{fallback}\")", source, StringComparison.Ordinal);
            Assert.Contains(key, english.Keys);
            Assert.Contains(key, korean.Keys);
        }
        Assert.Contains("new ComboBoxItem { Content = DescribeAction(action), Tag = action }", source, StringComparison.Ordinal);
        Assert.Contains("FrontButtonActionCapabilities.ActionsFor(domain)", source, StringComparison.Ordinal);

        Assert.Contains("$\"Controller_BackTarget_{target}\"", source, StringComparison.Ordinal);
        Assert.Contains("BackButtonMappingLabels.GetDisplayName(target)", source, StringComparison.Ordinal);
        foreach (var target in Enum.GetValues<Xbox360BackButtonTarget>())
        {
            var key = $"Controller_BackTarget_{target}";
            Assert.Contains(key, english.Keys);
            Assert.Contains(key, korean.Keys);
        }

        var populateStart = source.IndexOf("private void PopulateBackButtonTargets()", StringComparison.Ordinal);
        var populateEnd = source.IndexOf("private void BackButtonTargetComboBox_SelectionChanged", populateStart, StringComparison.Ordinal);
        Assert.True(populateStart >= 0 && populateEnd > populateStart);
        var populate = source[populateStart..populateEnd];
        Assert.True(populate.IndexOf("AddTargets(M1TargetComboBox)", StringComparison.Ordinal)
            < populate.IndexOf("LocalizeControllerBackTargets(M1TargetComboBox)", StringComparison.Ordinal));
        Assert.True(populate.IndexOf("AddTargets(M2TargetComboBox)", StringComparison.Ordinal)
            < populate.IndexOf("LocalizeControllerBackTargets(M2TargetComboBox)", StringComparison.Ordinal));
        Assert.DoesNotContain("LocalizeControllerBackTargets", File.ReadAllText(Path.Combine(root, "src", "SteamInputAddonforClaw.UI", "Views", "XboxPage.xaml.cs")), StringComparison.Ordinal);

        Assert.Contains("private readonly CheckBox _control = new() { Content = \"Ctrl\" }", source, StringComparison.Ordinal);
        Assert.Contains("new ComboBoxItem { Content = vkey.ToString(), Tag = vkey }", source, StringComparison.Ordinal);
        Assert.Contains("_runAsAdministrator.IsChecked == true", source, StringComparison.Ordinal);
        Assert.Contains("MainUiStrings.Get(\"Controller_RestoreVibrationButton/Content\", \"Restore Vibration\")", source, StringComparison.Ordinal);
        Assert.Contains("Controller_RestoreVibrationButton.Content", english.Keys);
        Assert.Contains("Controller_RestoreVibrationButton.Content", korean.Keys);
        Assert.Equal("safe fallback", MainUiStrings.Get("Missing_Controller_Test_Key", "safe fallback"));
    }

    [Fact]
    public void Steam_xaml_text_uses_unique_resource_uids_and_preserves_dynamic_identity()
    {
        var root = FindRepositoryRoot();
        var document = XDocument.Load(Path.Combine(root, "src", "SteamInputAddonforClaw.UI", "Views", "ProfilePage.xaml"));
        var english = ReadResources(Path.Combine(root, "src", "SteamInputAddonforClaw.UI", "Strings", "en-US", "Resources.resw"));
        var korean = ReadResources(Path.Combine(root, "src", "SteamInputAddonforClaw.UI", "Strings", "ko-KR", "Resources.resw"));
        var expectedUids = new[]
        {
            "Steam_CatalogTitle", "Steam_RefreshGamesButton", "Steam_GameSearchBox", "Steam_BackButton",
            "Steam_VirtualControllerCard", "Steam_VirtualController_DefaultOption",
            "Steam_VirtualController_SteamDeckOption", "Steam_VirtualController_DualSenseEdgeOption",
            "Steam_TdpExpander", "Steam_TdpAcCard", "Steam_TdpDcCard",
            "Steam_CpuBoostExpander", "Steam_CpuBoostAcCard", "Steam_CpuBoostDcCard",
            "Steam_PowerModeExpander", "Steam_PowerModeAcCard", "Steam_PowerModeDcCard",
            "Steam_GpuMinimumClockExpander", "Steam_GpuMinimumClockAcCard", "Steam_GpuMinimumClockDcCard",
            "Steam_IntelFpsExpander", "Steam_FpsAcCard", "Steam_FpsDcCard", "Steam_ResolutionCard"
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
            foreach (var property in new[] { "Text", "Content", "Header", "Description", "PlaceholderText" })
            {
                var sourceValue = (string?)element.Attribute(property);
                if (sourceValue is null) continue;

                var key = $"{uid}.{property}";
                Assert.Contains(key, english.Keys);
                Assert.Contains(key, korean.Keys);
                Assert.Equal(sourceValue, english[key]);
            }
        }

        var selector = Assert.Single(document.Descendants(), element =>
            element.Name.LocalName == "ComboBox" && (string?)element.Attribute(XamlNamespace + "Name") == "SteamVirtualControllerComboBox");
        Assert.Equal(new[] { "Default", "SteamDeck", "DualSenseEdge" }, selector.Elements()
            .Where(element => element.Name.LocalName == "ComboBoxItem")
            .Select(element => (string)element.Attribute("Tag")!));
        var selectedGameName = Assert.Single(document.Descendants(), element =>
            (string?)element.Attribute(XamlNamespace + "Name") == "SelectedGameNameText");
        Assert.Null(selectedGameName.Attribute(XamlNamespace + "Uid"));
    }

    [Fact]
    public void Steam_code_resources_cover_modes_errors_and_runtime_message_precedence()
    {
        var root = FindRepositoryRoot();
        var source = File.ReadAllText(Path.Combine(root, "src", "SteamInputAddonforClaw.UI", "Views", "ProfilePage.xaml.cs"))
            .Replace("\r\n", "\n", StringComparison.Ordinal);
        var english = ReadResources(Path.Combine(root, "src", "SteamInputAddonforClaw.UI", "Strings", "en-US", "Resources.resw"));
        var korean = ReadResources(Path.Combine(root, "src", "SteamInputAddonforClaw.UI", "Strings", "ko-KR", "Resources.resw"));
        var literalKeys = Regex.Matches(source, "MainUiStrings\\.Get\\(\\\"(?<key>Steam_[^\\\"]+)\\\"")
            .Select(match => match.Groups["key"].Value)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        Assert.NotEmpty(literalKeys);
        foreach (var key in literalKeys)
        {
            var resourceKey = key.Replace('/', '.');
            Assert.Contains(resourceKey, english.Keys);
            Assert.Contains(resourceKey, korean.Keys);
        }

        var cpuModes = Regex.Matches(source, "new\\(CpuBoostMode\\.(?<mode>\\w+),\\s*MainUiStrings\\.Get\\(\\\"(?<key>Steam_CpuBoost_Mode_[^\\\"]+)\\\"")
            .Select(match => (Mode: match.Groups["mode"].Value, Key: match.Groups["key"].Value))
            .ToArray();
        Assert.Equal(
            new[]
            {
                ("Disabled", "Steam_CpuBoost_Mode_Disabled"),
                ("Enabled", "Steam_CpuBoost_Mode_Enabled"),
                ("Aggressive", "Steam_CpuBoost_Mode_Aggressive"),
                ("EfficientEnabled", "Steam_CpuBoost_Mode_EfficientEnabled"),
                ("EfficientAggressive", "Steam_CpuBoost_Mode_EfficientAggressive"),
                ("AggressiveAtGuaranteed", "Steam_CpuBoost_Mode_AggressiveAtGuaranteed"),
                ("EfficientAggressiveAtGuaranteed", "Steam_CpuBoost_Mode_EfficientAggressiveAtGuaranteed")
            },
            cpuModes);

        var powerModes = Regex.Matches(source, "new\\(WindowsPowerMode\\.(?<mode>\\w+),\\s*MainUiStrings\\.Get\\(\\\"(?<key>Steam_PowerMode_Mode_[^\\\"]+)\\\"")
            .Select(match => (Mode: match.Groups["mode"].Value, Key: match.Groups["key"].Value))
            .ToArray();
        Assert.Equal(
            new[]
            {
                ("BestPowerEfficiency", "Steam_PowerMode_Mode_BestPowerEfficiency"),
                ("Balanced", "Steam_PowerMode_Mode_Balanced"),
                ("BestPerformance", "Steam_PowerMode_Mode_BestPerformance")
            },
            powerModes);
        Assert.Contains("new(null, null, MainUiStrings.Get(\"Steam_Resolution_DoNotChange\", \"Do not change\")), new(1920, 1200, \"1920 × 1200\"), new(1920, 1080, \"1920 × 1080\"), new(1680, 1050, \"1680 × 1050\"), new(1440, 900, \"1440 × 900\")", source, StringComparison.Ordinal);
        Assert.Contains("x => x.Width == snapshot.Resolution?.Width && x.Height == snapshot.Resolution?.Height", source, StringComparison.Ordinal);

        var localErrors = new[]
        {
            ("Steam_Catalog_NoGames", "No installed Steam games were found."),
            ("Steam_Catalog_RefreshFailed", "Game catalog could not be refreshed."),
            ("Steam_Favorite_SaveFailed", "Favorite could not be saved."),
            ("Steam_Profile_LoadFailed", "Profile settings could not be loaded."),
            ("Steam_Profile_SettingsUnwritable", "Profile settings could not be loaded, so changes are disabled to avoid overwriting the existing profile."),
            ("Steam_Tdp_Unavailable", "TDP Control is unavailable on this device."),
            ("Steam_VirtualController_UpdateFailed", "Virtual Controller could not be updated."),
            ("Steam_IntelFps_Unavailable", "Intel FPS Limit is unavailable."),
            ("Steam_GpuMinimumClock_UpdateFailed", "Minimum GPU Clock could not be updated."),
            ("Steam_IntelFps_UpdateFailed", "Intel FPS Limit could not be updated."),
            ("Steam_CpuBoost_UpdateFailed", "CPU Boost could not be updated."),
            ("Steam_TdpControl_ToggleUpdateFailed", "TDP Control could not be updated."),
            ("Steam_PowerMode_ToggleUpdateFailed", "Windows Power Mode could not be updated."),
            ("Steam_Tdp_UpdateFailed", "TDP could not be updated."),
            ("Steam_PowerMode_UpdateFailed", "Power Mode could not be updated."),
            ("Steam_Profile_UpdateFailed", "Profile could not be updated."),
            ("Steam_Profile_RuntimeInterrupted", "Profile could not be updated because the Runtime connection was interrupted."),
            ("Steam_Resolution_UpdateFailed", "Display resolution could not be updated.")
        };
        foreach (var (key, fallback) in localErrors)
        {
            Assert.Contains(key, english.Keys);
            Assert.Contains(key, korean.Keys);
            Assert.Equal(fallback, english[key]);
            Assert.Contains($"MainUiStrings.Get(\"{key}\", \"{fallback}\")", source, StringComparison.Ordinal);
        }

        Assert.Equal("Uses Intel's official API. Some games may not support FPS limiting.", english["Steam_IntelFpsExpander.Description"]);
        Assert.Equal("Overrides the Device minimum GPU clock for this game. Actual clock may still fall lower when required by power or thermal limits.", english["Steam_GpuMinimumClockExpander.Description"]);
        Assert.Contains("snapshot.FpsLimit?.UnavailableReason ?? MainUiStrings.Get(\"Steam_IntelFps_Unavailable\"", source, StringComparison.Ordinal);
        Assert.Contains("gpu?.UnavailableReason ?? string.Empty", source, StringComparison.Ordinal);
        Assert.Contains("gpu?.UnavailableReason is { Length: > 0 } warning", source, StringComparison.Ordinal);
        Assert.Contains("result.FailureMessage ?? failureMessage", source, StringComparison.Ordinal);
        Assert.Contains("RestoreSelectedAfterMutationFailureAsync(appId, failureMessage, exception)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("${feature} could not be updated.", source, StringComparison.Ordinal);

        var verifier = File.ReadAllText(Path.Combine(root, "scripts", "verify-ui-localization-pri.ps1"));
        Assert.Contains("Text|Content|Header|Description|PlaceholderText", verifier, StringComparison.Ordinal);
    }

    [Fact]
    public void Xbox_navigation_and_fixed_xaml_text_use_unique_resource_uids()
    {
        var root = FindRepositoryRoot();
        var mainWindow = XDocument.Load(Path.Combine(root, "src", "SteamInputAddonforClaw.UI", "MainWindow.xaml"));
        var xboxNavigation = Assert.Single(mainWindow.Descendants(), element =>
            element.Name.LocalName == "NavigationViewItem" && (string?)element.Attribute("Tag") == "Xbox");
        Assert.Equal("MainNav_Xbox", (string?)xboxNavigation.Attribute(XamlNamespace + "Uid"));
        Assert.Equal("XBOX", (string?)xboxNavigation.Attribute("Content"));
        Assert.Equal("Xbox", (string?)xboxNavigation.Attribute("Tag"));

        var document = XDocument.Load(Path.Combine(root, "src", "SteamInputAddonforClaw.UI", "Views", "XboxPage.xaml"));
        var english = ReadResources(Path.Combine(root, "src", "SteamInputAddonforClaw.UI", "Strings", "en-US", "Resources.resw"));
        var korean = ReadResources(Path.Combine(root, "src", "SteamInputAddonforClaw.UI", "Strings", "ko-KR", "Resources.resw"));
        Assert.Contains("MainNav_Xbox.Content", english.Keys);
        Assert.Contains("MainNav_Xbox.Content", korean.Keys);
        Assert.Equal("XBOX", english["MainNav_Xbox.Content"]);
        var expectedUids = new[]
        {
            "Xbox_CatalogTitle", "Xbox_RefreshGamesButton", "Xbox_GameSearchBox", "Xbox_BackButton",
            "Xbox_ControllerMappingExpander", "Xbox_M1Card", "Xbox_M2Card",
            "Xbox_TdpExpander", "Xbox_TdpAcCard", "Xbox_TdpDcCard",
            "Xbox_CpuBoostExpander", "Xbox_CpuBoostAcCard", "Xbox_CpuBoostDcCard",
            "Xbox_PowerModeExpander", "Xbox_PowerModeAcCard", "Xbox_PowerModeDcCard",
            "Xbox_GpuMinimumClockExpander", "Xbox_GpuMinimumClockAcCard", "Xbox_GpuMinimumClockDcCard",
            "Xbox_IntelFpsExpander", "Xbox_FpsAcCard", "Xbox_FpsDcCard", "Xbox_ResolutionCard"
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
            Assert.StartsWith("Xbox_", uid, StringComparison.Ordinal);
            foreach (var property in new[] { "Text", "Content", "Header", "Description", "PlaceholderText" })
            {
                var sourceValue = (string?)element.Attribute(property);
                if (string.IsNullOrWhiteSpace(sourceValue)) continue;

                var key = $"{uid}.{property}";
                Assert.Contains(key, english.Keys);
                Assert.Contains(key, korean.Keys);
                Assert.Equal(sourceValue, english[key]);
            }
        }
    }

    [Fact]
    public void Xbox_code_resources_cover_modes_targets_errors_and_runtime_message_precedence()
    {
        var root = FindRepositoryRoot();
        var source = File.ReadAllText(Path.Combine(root, "src", "SteamInputAddonforClaw.UI", "Views", "XboxPage.xaml.cs"))
            .Replace("\r\n", "\n", StringComparison.Ordinal);
        var english = ReadResources(Path.Combine(root, "src", "SteamInputAddonforClaw.UI", "Strings", "en-US", "Resources.resw"));
        var korean = ReadResources(Path.Combine(root, "src", "SteamInputAddonforClaw.UI", "Strings", "ko-KR", "Resources.resw"));
        var literalKeys = Regex.Matches(source, "MainUiStrings\\.Get\\(\\\"(?<key>Xbox_[^\\\"]+)\\\"")
            .Select(match => match.Groups["key"].Value)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        Assert.NotEmpty(literalKeys);
        foreach (var key in literalKeys)
        {
            var resourceKey = key.Replace('/', '.');
            Assert.Contains(resourceKey, english.Keys);
            Assert.Contains(resourceKey, korean.Keys);
        }

        var cpuModes = Regex.Matches(source, "new\\(CpuBoostMode\\.(?<mode>\\w+),\\s*MainUiStrings\\.Get\\(\\\"(?<key>Xbox_CpuBoost_Mode_[^\\\"]+)\\\"")
            .Select(match => (Mode: match.Groups["mode"].Value, Key: match.Groups["key"].Value))
            .ToArray();
        Assert.Equal(
            new[]
            {
                ("Disabled", "Xbox_CpuBoost_Mode_Disabled"),
                ("Enabled", "Xbox_CpuBoost_Mode_Enabled"),
                ("Aggressive", "Xbox_CpuBoost_Mode_Aggressive"),
                ("EfficientEnabled", "Xbox_CpuBoost_Mode_EfficientEnabled"),
                ("EfficientAggressive", "Xbox_CpuBoost_Mode_EfficientAggressive"),
                ("AggressiveAtGuaranteed", "Xbox_CpuBoost_Mode_AggressiveAtGuaranteed"),
                ("EfficientAggressiveAtGuaranteed", "Xbox_CpuBoost_Mode_EfficientAggressiveAtGuaranteed")
            },
            cpuModes);

        var powerModes = Regex.Matches(source, "new\\(WindowsPowerMode\\.(?<mode>\\w+),\\s*MainUiStrings\\.Get\\(\\\"(?<key>Xbox_PowerMode_Mode_[^\\\"]+)\\\"")
            .Select(match => (Mode: match.Groups["mode"].Value, Key: match.Groups["key"].Value))
            .ToArray();
        Assert.Equal(
            new[]
            {
                ("BestPowerEfficiency", "Xbox_PowerMode_Mode_BestPowerEfficiency"),
                ("Balanced", "Xbox_PowerMode_Mode_Balanced"),
                ("BestPerformance", "Xbox_PowerMode_Mode_BestPerformance")
            },
            powerModes);
        Assert.Contains("new(null, null, MainUiStrings.Get(\"Xbox_Resolution_DoNotChange\", \"Do not change\")), new(1920, 1200, \"1920 × 1200\"), new(1920, 1080, \"1920 × 1080\"), new(1680, 1050, \"1680 × 1050\"), new(1440, 900, \"1440 × 900\")", source, StringComparison.Ordinal);

        var mapping = File.ReadAllText(Path.Combine(root, "src", "SteamInputAddonforClaw.UI", "Views", "BackButtonMappingUiOptions.cs"));
        Assert.Contains("comboBox.Items.Add(new ComboBoxItem { Content = Describe(target), Tag = target })", mapping, StringComparison.Ordinal);
        Assert.Contains("item.Tag is not Xbox360BackButtonTarget target", source, StringComparison.Ordinal);
        Assert.Contains("item.Content = MainUiStrings.Get(", source, StringComparison.Ordinal);
        Assert.Contains("\"Xbox_BackTarget_\" + target", source, StringComparison.Ordinal);
        Assert.Contains("BackButtonMappingLabels.GetDisplayName(target)", source, StringComparison.Ordinal);
        var addM1 = source.IndexOf("BackButtonMappingUiOptions.AddTargets(M1BackButtonTargetComboBox)", StringComparison.Ordinal);
        var localizeM1 = source.IndexOf("LocalizeXboxBackTargets(M1BackButtonTargetComboBox)", addM1, StringComparison.Ordinal);
        var addM2 = source.IndexOf("BackButtonMappingUiOptions.AddTargets(M2BackButtonTargetComboBox)", StringComparison.Ordinal);
        var localizeM2 = source.IndexOf("LocalizeXboxBackTargets(M2BackButtonTargetComboBox)", addM2, StringComparison.Ordinal);
        Assert.True(addM1 >= 0 && localizeM1 > addM1);
        Assert.True(addM2 >= 0 && localizeM2 > addM2);
        Assert.DoesNotContain("LocalizeControllerBackTargets", source, StringComparison.Ordinal);
        Assert.Contains("new BackButtonMappingSettings(m1, m2)", source, StringComparison.Ordinal);
        Assert.Contains("var mapping = perGameEnabled ? ReadSelectedBackButtonMapping() : null;", source, StringComparison.Ordinal);
        var targets = Enum.GetValues<Xbox360BackButtonTarget>();
        Assert.Equal(18, targets.Length);
        foreach (var target in targets)
        {
            var key = $"Xbox_BackTarget_{target}";
            var fallback = BackButtonMappingLabels.GetDisplayName(target);
            Assert.Contains(key, english.Keys);
            Assert.Contains(key, korean.Keys);
            Assert.Equal(fallback, english[key]);
        }

        var localErrors = new[]
        {
            ("Xbox_Catalog_LoadFailed", "XBOX game catalog could not be loaded."),
            ("Xbox_Catalog_NoGames", "No installed XBOX games were found."),
            ("Xbox_Catalog_RefreshFailed", "XBOX game catalog could not be refreshed."),
            ("Xbox_Favorite_SaveFailed", "Favorite could not be saved."),
            ("Xbox_Profile_LoadFailed", "XBOX profile settings could not be loaded."),
            ("Xbox_Profile_SettingsUnwritable", "Profile settings could not be loaded, so changes are disabled to avoid overwriting the existing profile."),
            ("Xbox_Tdp_Unavailable", "TDP Control is unavailable on this device."),
            ("Xbox_IntelFps_Unavailable", "Intel FPS Limit is unavailable."),
            ("Xbox_GpuMinimumClock_UpdateFailed", "Minimum GPU Clock could not be updated."),
            ("Xbox_IntelFps_UpdateFailed", "Intel FPS Limit could not be updated."),
            ("Xbox_CpuBoost_UpdateFailed", "CPU Boost could not be updated."),
            ("Xbox_TdpControl_ToggleUpdateFailed", "TDP Control could not be updated."),
            ("Xbox_PowerMode_ToggleUpdateFailed", "Windows Power Mode could not be updated."),
            ("Xbox_Profile_UpdateFailed", "Profile could not be updated."),
            ("Xbox_Profile_RuntimeInterrupted", "Profile could not be updated because the Runtime connection was interrupted."),
            ("Xbox_BackMapping_InvalidTargets", "Choose valid M1 and M2 targets before saving the per-game mapping."),
            ("Xbox_BackMapping_UpdateFailed", "M1 / M2 mapping could not be updated."),
            ("Xbox_PowerMode_UpdateFailed", "Power Mode could not be updated."),
            ("Xbox_Resolution_UpdateFailed", "Display resolution could not be updated."),
            ("Xbox_Tdp_UpdateFailed", "TDP could not be updated.")
        };
        foreach (var (key, fallback) in localErrors)
        {
            Assert.Contains(key, english.Keys);
            Assert.Contains(key, korean.Keys);
            Assert.Equal(fallback, english[key]);
            Assert.Contains($"MainUiStrings.Get(\"{key}\", \"{fallback}\")", source, StringComparison.Ordinal);
        }

        Assert.Contains("snapshot.FailureMessage ?? MainUiStrings.Get(\"Xbox_Catalog_LoadFailed\", \"XBOX game catalog could not be loaded.\")", source, StringComparison.Ordinal);
        Assert.Contains("snapshot.FpsLimit?.UnavailableReason ?? MainUiStrings.Get(\"Xbox_IntelFps_Unavailable\", \"Intel FPS Limit is unavailable.\")", source, StringComparison.Ordinal);
        Assert.Contains("gpu?.UnavailableReason ?? string.Empty", source, StringComparison.Ordinal);
        Assert.Contains("gpu?.UnavailableReason is { Length: > 0 } warning", source, StringComparison.Ordinal);
        Assert.Contains("MainUiStrings.Get(\"Xbox_IntelFpsExpander/Description\"", source, StringComparison.Ordinal);
        Assert.Contains("MainUiStrings.Get(\"Xbox_GpuMinimumClockExpander/Description\"", source, StringComparison.Ordinal);
        Assert.Contains("result.FailureMessage ?? failureMessage", source, StringComparison.Ordinal);
        Assert.Contains("RestoreSelectedAfterMutationFailureAsync(key, failureMessage, exception)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("MainUiStrings.Get(\"Xbox_CpuBoost_UpdateFailed\", MainUiStrings.Get(", source, StringComparison.Ordinal);
        Assert.Contains("SelectedGameNameText.Text = game.DisplayName", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Clawhud_navigation_and_fixed_xaml_text_use_unique_resource_uids()
    {
        var root = FindRepositoryRoot();
        var mainWindow = XDocument.Load(Path.Combine(root, "src", "SteamInputAddonforClaw.UI", "MainWindow.xaml"));
        var navigation = Assert.Single(mainWindow.Descendants(), element =>
            element.Name.LocalName == "NavigationViewItem" && (string?)element.Attribute("Tag") == "Overlay");
        Assert.Equal("MainNav_ClawHud", (string?)navigation.Attribute(XamlNamespace + "Uid"));
        Assert.Equal("ClawHUD", (string?)navigation.Attribute("Content"));
        Assert.Equal("Overlay", (string?)navigation.Attribute("Tag"));

        var english = ReadResources(Path.Combine(root, "src", "SteamInputAddonforClaw.UI", "Strings", "en-US", "Resources.resw"));
        var korean = ReadResources(Path.Combine(root, "src", "SteamInputAddonforClaw.UI", "Strings", "ko-KR", "Resources.resw"));
        Assert.Equal("ClawHUD", english["MainNav_ClawHud.Content"]);
        Assert.Contains("MainNav_ClawHud.Content", korean.Keys);
        Assert.False(string.IsNullOrWhiteSpace(korean["MainNav_ClawHud.Content"]));

        var document = XDocument.Load(Path.Combine(root, "src", "SteamInputAddonforClaw.UI", "Views", "OverlayPage.xaml"));
        var expectedUids = new[]
        {
            "ClawHud_PageTitle", "ClawHud_PageSubtitle", "ClawHud_RetryButton", "ClawHud_EnableCard",
            "ClawHud_DisplayModeCard", "ClawHud_DisplayMode_Option_InGameOnly", "ClawHud_DisplayMode_Option_Always",
            "ClawHud_SizeCard", "ClawHud_Size_Option_Default", "ClawHud_FontCard",
            "ClawHud_Font_Option_Unispace", "ClawHud_Font_Option_SegoeUiVariable", "ClawHud_AlignmentCard",
            "ClawHud_Alignment_Option_Left", "ClawHud_Alignment_Option_Center", "ClawHud_Alignment_Option_Right",
            "ClawHud_BackgroundWidthCard", "ClawHud_BackgroundWidth_Option_FullWidth",
            "ClawHud_BackgroundWidth_Option_ContentWidth", "ClawHud_OpacityCard", "ClawHud_IntelVrrCard"
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
            Assert.StartsWith("ClawHud_", uid, StringComparison.Ordinal);
            foreach (var property in new[] { "Text", "Content", "Header", "Description", "PlaceholderText" })
            {
                var sourceValue = (string?)element.Attribute(property);
                if (sourceValue is null) continue;

                var key = $"{uid}.{property}";
                Assert.Contains(key, english.Keys);
                Assert.Contains(key, korean.Keys);
                Assert.Equal(sourceValue, english[key]);
                Assert.False(string.IsNullOrWhiteSpace(korean[key]));
            }
        }

        string[] Options(string name)
        {
            var comboBox = Assert.Single(document.Descendants(), element =>
                element.Name.LocalName == "ComboBox" && (string?)element.Attribute(XamlNamespace + "Name") == name);
            return comboBox.Elements()
                .Where(element => element.Name.LocalName == "ComboBoxItem")
                .Select(element => (string?)element.Attribute("Content") ?? string.Empty)
                .ToArray();
        }

        Assert.Equal(new[] { "In game only", "Always" }, Options("ClawHudDisplayModeComboBox"));
        Assert.Equal(new[] { "-2", "-1", "Default", "+1", "+2" }, Options("ClawHudSizeComboBox"));
        Assert.Equal(new[] { "Unispace", "Segoe UI Variable" }, Options("ClawHudFontComboBox"));
        Assert.Equal(new[] { "Left", "Center", "Right" }, Options("ClawHudAlignmentComboBox"));
        Assert.Equal(new[] { "Full width", "Content width" }, Options("ClawHudBackgroundComboBox"));

        var source = File.ReadAllText(Path.Combine(root, "src", "SteamInputAddonforClaw.UI", "Views", "OverlayPage.xaml.cs"))
            .Replace("\r\n", "\n", StringComparison.Ordinal);
        Assert.Contains("ClawHudDisplayModeComboBox.SelectedIndex == 0", source, StringComparison.Ordinal);
        Assert.Contains("ClawHudSizeComboBox.SelectedIndex - 2", source, StringComparison.Ordinal);
        Assert.Contains("ClawHudFontComboBox.SelectedIndex == 0", source, StringComparison.Ordinal);
        Assert.Contains("(FrontendClawHudAlignment)ClawHudAlignmentComboBox.SelectedIndex", source, StringComparison.Ordinal);
        Assert.Contains("ClawHudBackgroundComboBox.SelectedIndex == 0", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Clawhud_code_resources_cover_local_messages_and_vrr_status_without_translating_runtime_data()
    {
        var root = FindRepositoryRoot();
        var source = File.ReadAllText(Path.Combine(root, "src", "SteamInputAddonforClaw.UI", "Views", "OverlayPage.xaml.cs"))
            .Replace("\r\n", "\n", StringComparison.Ordinal);
        var english = ReadResources(Path.Combine(root, "src", "SteamInputAddonforClaw.UI", "Strings", "en-US", "Resources.resw"));
        var korean = ReadResources(Path.Combine(root, "src", "SteamInputAddonforClaw.UI", "Strings", "ko-KR", "Resources.resw"));
        var localMessages = new[]
        {
            ("ClawHud_LocalStatus_Off", "Off"),
            ("ClawHud_LocalStatus_Starting", "Starting…"),
            ("ClawHud_Enable_ChangeFailed", "ClawHUD could not be changed."),
            ("ClawHud_Setting_VerificationFailed", "HUD setting could not be verified.")
        };
        foreach (var (key, fallback) in localMessages)
        {
            Assert.Contains(key, english.Keys);
            Assert.Contains(key, korean.Keys);
            Assert.Equal(fallback, english[key]);
            Assert.False(string.IsNullOrWhiteSpace(korean[key]));
            Assert.Contains($"MainUiStrings.Get(\"{key}\", \"{fallback}\")", source, StringComparison.Ordinal);
        }

        Assert.Contains("ClawHud_LocalStatus_Off", source, StringComparison.Ordinal);
        Assert.Contains("StatusMessage = desired\n                ? MainUiStrings.Get(\"ClawHud_LocalStatus_Starting\", \"Starting…\")", source, StringComparison.Ordinal);

        foreach (var status in Enum.GetNames<FrontendClawHudIntelVrrStatus>())
        {
            var key = $"ClawHud_VrrStatus_{status}";
            Assert.Contains(key, english.Keys);
            Assert.Contains(key, korean.Keys);
            Assert.Equal(status, english[key]);
            Assert.False(string.IsNullOrWhiteSpace(korean[key]));
        }
        Assert.Equal(9, Enum.GetNames<FrontendClawHudIntelVrrStatus>().Length);
        Assert.Contains("MainUiStrings.Get(\"ClawHud_VrrStatus_\" + result.Status, result.Status.ToString())", source, StringComparison.Ordinal);
        Assert.Contains("ClawHudStatusText.Text = snapshot.StatusMessage;", source, StringComparison.Ordinal);
        Assert.Contains("ClawHudInfoBar.Message = snapshot.StatusMessage;", source, StringComparison.Ordinal);
        Assert.Contains("result.Succeeded ? result.Snapshot.StatusMessage : result.FailureMessage ?? result.Snapshot.StatusMessage", source, StringComparison.Ordinal);
        Assert.Contains("result.RangeBefore} → {result.RangeAfter", source, StringComparison.Ordinal);
        Assert.Contains("result.PanelName", source, StringComparison.Ordinal);
        Assert.Contains("result.Message", source, StringComparison.Ordinal);
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
