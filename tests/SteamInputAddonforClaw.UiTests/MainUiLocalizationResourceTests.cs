using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using SteamInputAddonforClaw.Contracts.BackButtons;
using SteamInputAddonforClaw.Contracts.Frontend;
using SteamInputAddonforClaw.UI;
using SteamInputAddonforClaw.Views;
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
            "MainNav_QuickOverlay.Content",
            "MainNav_Shortcut.Content",
            "MainNav_HowToUse.Content",
            "HowToUse_Loading.Text",
            "Settings_AppTheme_Description",
            "Device_AutoStart_Verified",
            "Device_AutoStart_NeedsAttention",
            "Device_AutoStart_Unknown",
            "Device_AutoStart_Repairing",
            "Device_AutoStartRepairButton.Content",
            "Device_AutoStart_VerifiedHint",
            "Device_AutoStart_UnknownHint",
            "Device_AutoStart_RuntimeUnavailable",
            "Device_AutoStart_Issue_ReadFailed",
            "Device_AutoStart_Issue_StableExecutableMissing",
            "Device_AutoStart_Issue_RegistrationFailed",
            "Device_AutoStart_Issue_ReadbackUnverified"
        };

        Assert.Equal(english.Keys.OrderBy(key => key, StringComparer.Ordinal), korean.Keys.OrderBy(key => key, StringComparer.Ordinal));
        foreach (var key in requiredPilotKeys)
        {
            Assert.Contains(key, english.Keys);
        }
        Assert.All(english.Values, value => Assert.False(string.IsNullOrWhiteSpace(value)));
        Assert.All(korean.Values, value => Assert.False(string.IsNullOrWhiteSpace(value)));
        Assert.Equal("Auto start OK", english["Device_AutoStart_Verified"]);
        Assert.Equal("Auto start Error", english["Device_AutoStart_NeedsAttention"]);
        Assert.Equal("Auto start Unavailable", english["Device_AutoStart_Unknown"]);
        Assert.Equal("Auto start Repairing…", english["Device_AutoStart_Repairing"]);
        Assert.Equal("자동 시작 정상", korean["Device_AutoStart_Verified"]);
        Assert.Equal("자동 시작 오류", korean["Device_AutoStart_NeedsAttention"]);
        Assert.Equal("자동 시작 확인 불가", korean["Device_AutoStart_Unknown"]);
        Assert.Equal("자동 시작 복구 중…", korean["Device_AutoStart_Repairing"]);
    }

    [Fact]
    public void Steam_and_xbox_catalogs_localize_unknown_favorite_state_messages()
    {
        var root = FindRepositoryRoot();
        var stringsDirectory = Path.Combine(root, "src", "SteamInputAddonforClaw.UI", "Strings");
        var english = ReadResources(Path.Combine(stringsDirectory, "en-US", "Resources.resw"));
        var korean = ReadResources(Path.Combine(stringsDirectory, "ko-KR", "Resources.resw"));
        var expected = new[]
        {
            "Steam_FavoriteStateUnavailableInfoBar.Message",
            "Xbox_FavoriteStateUnavailableInfoBar.Message"
        };

        var steam = XDocument.Load(Path.Combine(root, "src", "SteamInputAddonforClaw.UI", "Views", "ProfilePage.xaml"));
        var xbox = XDocument.Load(Path.Combine(root, "src", "SteamInputAddonforClaw.UI", "Views", "XboxPage.xaml"));
        foreach (var (document, uid) in new[]
        {
            (steam, "Steam_FavoriteStateUnavailableInfoBar"),
            (xbox, "Xbox_FavoriteStateUnavailableInfoBar")
        })
        {
            var infoBar = Assert.Single(document.Descendants(), element =>
                (string?)element.Attribute(XamlNamespace + "Uid") == uid);
            Assert.False(string.IsNullOrWhiteSpace((string?)infoBar.Attribute("Message")));
            Assert.Contains(uid + ".Message", expected);
        }

        Assert.All(expected, key =>
        {
            Assert.Contains(key, english.Keys);
            Assert.Contains(key, korean.Keys);
            Assert.False(string.IsNullOrWhiteSpace(english[key]));
            Assert.False(string.IsNullOrWhiteSpace(korean[key]));
        });
        Assert.Contains('확', korean[expected[0]]);
        Assert.Contains('확', korean[expected[1]]);
    }

    [Fact]
    public void Tweaks_navigation_and_fixed_page_text_resolve_through_unique_resource_uids()
    {
        var root = FindRepositoryRoot();
        var stringsDirectory = Path.Combine(root, "src", "SteamInputAddonforClaw.UI", "Strings");
        var english = ReadResources(Path.Combine(stringsDirectory, "en-US", "Resources.resw"));
        var korean = ReadResources(Path.Combine(stringsDirectory, "ko-KR", "Resources.resw"));
        var mainWindow = XDocument.Load(Path.Combine(root, "src", "SteamInputAddonforClaw.UI", "MainWindow.xaml"));
        var navigation = Assert.Single(mainWindow.Descendants(), element =>
            element.Name.LocalName == "NavigationViewItem" && (string?)element.Attribute("Tag") == "Tweaks");

        Assert.Equal("MainNav_Tweaks", (string?)navigation.Attribute(XamlNamespace + "Uid"));
        Assert.Equal("Tweaks", (string?)navigation.Attribute("Content"));
        Assert.Equal("Tweaks", (string?)navigation.Attribute("Tag"));
        Assert.Equal("Repair", (string?)navigation.Descendants().Single(element => element.Name.LocalName == "SymbolIcon").Attribute("Symbol"));
        Assert.Equal("Tweaks", english["MainNav_Tweaks.Content"]);
        Assert.False(string.IsNullOrWhiteSpace(korean["MainNav_Tweaks.Content"]));

        var document = XDocument.Load(Path.Combine(root, "src", "SteamInputAddonforClaw.UI", "Views", "WindowsTweaksPage.xaml"));
        var uidElements = document.Descendants()
            .Where(element => element.Attribute(XamlNamespace + "Uid") is not null)
            .ToArray();
        var expectedUids = new[]
        {
            "Tweaks_PageTitle", "Tweaks_EntryPrompt", "Tweaks_GamingPerformanceHeader", "Tweaks_RegistryTweaksHeader",
            "Tweaks_MemoryIntegrityCard", "Tweaks_MemoryIntegrityDescription",
            "Tweaks_VirtualMachinePlatformCard", "Tweaks_VirtualMachinePlatformDescription",
            "Tweaks_InstantContextMenusCard", "Tweaks_InstantContextMenusDescription",
            "Tweaks_ClassicContextMenuCard", "Tweaks_ClassicContextMenuDescription",
            "Tweaks_DisablePasswordOnWakeCard", "Tweaks_DisablePasswordOnWakeDescription",
            "Tweaks_DisableEdgeBackgroundCard", "Tweaks_DisableEdgeBackgroundDescription",
            "Tweaks_DisableStartupAppDelayCard", "Tweaks_DisableStartupAppDelayDescription"
        };
        var actualUids = uidElements.Select(element => (string)element.Attribute(XamlNamespace + "Uid")!).ToArray();
        Assert.Equal(expectedUids.OrderBy(value => value, StringComparer.Ordinal), actualUids.OrderBy(value => value, StringComparer.Ordinal));
        Assert.Equal(actualUids.Length, actualUids.Distinct(StringComparer.Ordinal).Count());

        foreach (var element in uidElements)
        {
            var uid = (string)element.Attribute(XamlNamespace + "Uid")!;
            foreach (var property in new[] { "Text", "Header" })
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

        var dynamicMessages = document.Descendants().Where(element =>
            (string?)element.Attribute(XamlNamespace + "Name") == "OperationMessageText"
            || ((string?)element.Attribute(XamlNamespace + "Name"))?.EndsWith("State", StringComparison.Ordinal) == true);
        Assert.All(dynamicMessages, element => Assert.Null(element.Attribute(XamlNamespace + "Uid")));
    }

    [Fact]
    public void Tweaks_code_resources_cover_owned_messages_typed_states_and_complete_safety_notice()
    {
        var root = FindRepositoryRoot();
        var stringsDirectory = Path.Combine(root, "src", "SteamInputAddonforClaw.UI", "Strings");
        var english = ReadResources(Path.Combine(stringsDirectory, "en-US", "Resources.resw"));
        var korean = ReadResources(Path.Combine(stringsDirectory, "ko-KR", "Resources.resw"));
        var source = File.ReadAllText(Path.Combine(root, "src", "SteamInputAddonforClaw.UI", "Views", "WindowsTweaksPage.xaml.cs"))
            .Replace("\r\n", "\n", StringComparison.Ordinal);

        var localMessages = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Tweaks_Loading"] = "Reading current Windows settings…",
            ["Tweaks_AcceptSafetyPrompt"] = "Review and accept the Windows Tweaks safety notice to view these controls.",
            ["Tweaks_AcknowledgementSaveFailed"] = "The notice was accepted, but its date could not be saved. It may be shown again after the app restarts.",
            ["Tweaks_LoadFailed"] = "Windows Tweaks could not be loaded. Select this tab again to retry.",
            ["Tweaks_Updating"] = "Updating…",
            ["Tweaks_ChangeUnconfirmed"] = "The change could not be confirmed. Reopen the Tweaks tab to refresh Windows state.",
            ["Tweaks_State_ConfiguredNotRunning"] = "Configured — not running",
            ["Tweaks_State_Enabled"] = "Enabled",
            ["Tweaks_State_Disabled"] = "Disabled",
            ["Tweaks_State_RestartRequired"] = "Restart required",
            ["Tweaks_State_ExplorerRestartRequired"] = "Explorer restart required",
            ["Tweaks_State_Unsupported"] = "Unsupported",
            ["Tweaks_State_Managed"] = "Managed",
            ["Tweaks_State_MixedApply"] = "Mixed — turn On to apply",
            ["Tweaks_State_Unavailable"] = "Unavailable",
            ["Tweaks_SafetyNotice_Title"] = "Windows Tweaks safety notice",
            ["Tweaks_SafetyNotice_Accept"] = "I understand",
            ["Tweaks_SafetyNotice_Cancel"] = "Cancel"
        };
        foreach (var (key, fallback) in localMessages)
        {
            Assert.Contains(key, english.Keys);
            Assert.Contains(key, korean.Keys);
            Assert.Equal(fallback, english[key]);
            Assert.False(string.IsNullOrWhiteSpace(korean[key]));
            Assert.Contains($"\"{key}\"", source, StringComparison.Ordinal);
        }

        var safetyBody = string.Join("\n\n", new[]
        {
            "Some changes need a Windows restart or Explorer restart to become fully effective. This app will not restart either automatically.",
            "Memory Integrity strengthens kernel protection. Disabling it reduces protection. Enabling it can make incompatible drivers or apps malfunction and, rarely, prevent normal boot.",
            "Virtual Machine Platform may be required by virtualization-dependent software; disabling it can stop that software from working.",
            "Disabling password/sign-in on wake lets someone with physical access resume directly to the desktop.",
            "These settings are reversible where Windows and policy permit. Windows policy or firmware locks can prevent a requested change. Registry policy controls may be restricted, and removing startup delay can make startup apps launch closer together."
        });
        Assert.Equal(safetyBody, english["Tweaks_SafetyNotice_Body"]);
        Assert.Equal(5, english["Tweaks_SafetyNotice_Body"].Split("\n\n", StringSplitOptions.None).Length);
        Assert.Equal(4, Regex.Matches(english["Tweaks_SafetyNotice_Body"], "\\n\\n").Count);
        Assert.Equal(5, korean["Tweaks_SafetyNotice_Body"].Split("\n\n", StringSplitOptions.None).Length);
        Assert.Equal(4, Regex.Matches(korean["Tweaks_SafetyNotice_Body"], "\\n\\n").Count);
        Assert.Contains("MainUiStrings.Get(\n            \"Tweaks_SafetyNotice_Body\",", source, StringComparison.Ordinal);
        Assert.Contains("Content = new TextBlock { Text = content,", source, StringComparison.Ordinal);
        Assert.Contains("DefaultButton = ContentDialogButton.Primary", source, StringComparison.Ordinal);
        Assert.Contains("if (!await ShowDailyWarningAsync()) return;", source, StringComparison.Ordinal);
        Assert.Contains("AcknowledgeWindowsTweaksWarningAsync(today)", source, StringComparison.Ordinal);
        Assert.Contains("SetOperationMessage(result.Succeeded ? null : result.FailureMessage)", source, StringComparison.Ordinal);

        Assert.Contains("Tweaks_DisableEdgeBackgroundDescription/Text", source, StringComparison.Ordinal);
        Assert.Contains("ToolTipService.SetToolTip(", source, StringComparison.Ordinal);
        Assert.Contains("ToolTipService.ToolTip=\"Disable Edge Startup Boost and background mode; existing processes may continue until closed.\"",
            File.ReadAllText(Path.Combine(root, "src", "SteamInputAddonforClaw.UI", "Views", "WindowsTweaksPage.xaml")), StringComparison.Ordinal);
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
    public void Prerequisite_setup_progress_and_result_dialog_localize_without_changing_result_flow()
    {
        var root = FindRepositoryRoot();
        var stringsDirectory = Path.Combine(root, "src", "SteamInputAddonforClaw.UI", "Strings");
        var english = ReadResources(Path.Combine(stringsDirectory, "en-US", "Resources.resw"));
        var korean = ReadResources(Path.Combine(stringsDirectory, "ko-KR", "Resources.resw"));
        var mainWindow = XDocument.Load(Path.Combine(root, "src", "SteamInputAddonforClaw.UI", "MainWindow.xaml"));
        var source = File.ReadAllText(Path.Combine(root, "src", "SteamInputAddonforClaw.UI", "MainWindow.xaml.cs"));
        var methodStart = source.IndexOf("private async Task HandlePrerequisiteSetupResultAsync", StringComparison.Ordinal);
        var methodEnd = source.IndexOf("private async Task TryShowCenterMDisableConfirmationAsync", methodStart, StringComparison.Ordinal);
        Assert.True(methodStart >= 0 && methodEnd > methodStart);
        var resultHandler = source[methodStart..methodEnd];

        var progressTexts = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["MainWindow_PrerequisiteSetupBusy_ProgressText"] = "Installing required components…",
            ["MainWindow_PrerequisiteSetupBusy_WaitText"] = "Please wait. This may take a moment."
        };
        foreach (var (uid, fallback) in progressTexts)
        {
            var element = Assert.Single(mainWindow.Descendants(), candidate =>
                (string?)candidate.Attribute(XamlNamespace + "Uid") == uid);
            Assert.Equal(fallback, (string?)element.Attribute("Text"));
            Assert.Equal(fallback, english[uid + ".Text"]);
            Assert.False(string.IsNullOrWhiteSpace(korean[uid + ".Text"]));
            Assert.Contains(uid + ".Text", english.Keys);
            Assert.Contains(uid + ".Text", korean.Keys);
        }

        var runtimeMessages = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["MainWindow_PrerequisiteSetup_AlreadyInProgress"] = "Another setup operation is already in progress.",
            ["MainWindow_PrerequisiteSetup_Failed"] = "Setup couldn't be completed. Check Settings > Required Components or the application log for details.",
            ["MainWindow_PrerequisiteSetup_Title"] = "Setup unavailable",
            ["MainWindow_Common_Ok"] = "OK"
        };
        foreach (var (key, fallback) in runtimeMessages)
        {
            Assert.Contains($"\"{key}\"", resultHandler, StringComparison.Ordinal);
            Assert.Contains(fallback, resultHandler, StringComparison.Ordinal);
            Assert.Equal(fallback, english[key]);
            Assert.False(string.IsNullOrWhiteSpace(korean[key]));
        }

        Assert.Contains("MainUiStrings.Get(", resultHandler, StringComparison.Ordinal);
        Assert.Contains("Title = MainUiStrings.Get(", resultHandler, StringComparison.Ordinal);
        Assert.Contains("CloseButtonText = MainUiStrings.Get(", resultHandler, StringComparison.Ordinal);
        Assert.Contains("FrontendPrerequisiteSetupResultKind.Blocked", resultHandler, StringComparison.Ordinal);
        Assert.Contains("FrontendPrerequisiteSetupResultKind.AlreadyInProgress", resultHandler, StringComparison.Ordinal);
        Assert.Contains("FrontendPrerequisiteSetupResultKind.Failed", resultHandler, StringComparison.Ordinal);
        Assert.Contains("TryShowCenterMDisableConfirmationAsync(afterPrerequisiteSetup: true)", resultHandler, StringComparison.Ordinal);
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
    public void Shortcut_and_how_to_use_shell_resources_preserve_action_data_and_localize_all_icon_names()
    {
        var root = FindRepositoryRoot();
        var stringsDirectory = Path.Combine(root, "src", "SteamInputAddonforClaw.UI", "Strings");
        var english = ReadResources(Path.Combine(stringsDirectory, "en-US", "Resources.resw"));
        var korean = ReadResources(Path.Combine(stringsDirectory, "ko-KR", "Resources.resw"));
        var mainWindow = XDocument.Load(Path.Combine(root, "src", "SteamInputAddonforClaw.UI", "MainWindow.xaml"));
        var shortcutDocument = XDocument.Load(Path.Combine(root, "src", "SteamInputAddonforClaw.UI", "Views", "ShortcutPage.xaml"));
        var howToUseDocument = XDocument.Load(Path.Combine(root, "src", "SteamInputAddonforClaw.UI", "Views", "HowToUsePage.xaml"));
        var shortcutCode = File.ReadAllText(Path.Combine(root, "src", "SteamInputAddonforClaw.UI", "Views", "ShortcutPage.xaml.cs"));

        foreach (var expected in new[]
        {
            (Tag: "Shortcut", Uid: "MainNav_Shortcut", Content: "Shortcut"),
            (Tag: "HowToUse", Uid: "MainNav_HowToUse", Content: "How to Use")
        })
        {
            var item = Assert.Single(mainWindow.Descendants(), element =>
                element.Name.LocalName == "NavigationViewItem" && (string?)element.Attribute("Tag") == expected.Tag);
            Assert.Equal(expected.Uid, (string?)item.Attribute(XamlNamespace + "Uid"));
            Assert.Equal(expected.Content, (string?)item.Attribute("Content"));
            Assert.Equal(expected.Tag, (string?)item.Attribute("Tag"));
            Assert.Equal(expected.Content, english[$"{expected.Uid}.Content"]);
            Assert.False(string.IsNullOrWhiteSpace(korean[$"{expected.Uid}.Content"]));
        }

        var expectedShortcutUidProperties = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Shortcut_PageTitle"] = "Text",
            ["Shortcut_PageSubtitle"] = "Text",
            ["Shortcut_AddButton"] = "Content",
            ["Shortcut_EditButton"] = "Content",
            ["Shortcut_DeleteButton"] = "Content",
            ["Shortcut_EmptyTitle"] = "Text",
            ["Shortcut_EmptySubtitle"] = "Text"
        };
        var shortcutUidElements = shortcutDocument.Descendants()
            .Where(element => element.Attribute(XamlNamespace + "Uid") is not null)
            .ToArray();
        Assert.Equal(expectedShortcutUidProperties.Keys.OrderBy(key => key, StringComparer.Ordinal),
            shortcutUidElements.Select(element => (string)element.Attribute(XamlNamespace + "Uid")!)
                .OrderBy(key => key, StringComparer.Ordinal));
        foreach (var element in shortcutUidElements)
        {
            var uid = (string)element.Attribute(XamlNamespace + "Uid")!;
            var property = expectedShortcutUidProperties[uid];
            var key = $"{uid}.{property}";
            Assert.Contains(key, english.Keys);
            Assert.Contains(key, korean.Keys);
            Assert.False(string.IsNullOrWhiteSpace(english[key]));
            Assert.False(string.IsNullOrWhiteSpace(korean[key]));
            Assert.Equal(english[key], (string?)element.Attribute(property));
        }

        var expectedHowToUseUidProperties = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["HowToUse_Failure"] = "Text",
            ["HowToUse_Retry"] = "Content"
        };
        var howToUseUidElements = howToUseDocument.Descendants()
            .Where(element => element.Attribute(XamlNamespace + "Uid") is not null)
            .ToArray();
        Assert.Equal(new[] { "HowToUse_Failure", "HowToUse_Loading", "HowToUse_Retry" },
            howToUseUidElements.Select(element => (string)element.Attribute(XamlNamespace + "Uid")!)
                .OrderBy(value => value, StringComparer.Ordinal));
        foreach (var element in howToUseUidElements.Where(element => expectedHowToUseUidProperties.ContainsKey(
                     (string)element.Attribute(XamlNamespace + "Uid")!)))
        {
            var uid = (string)element.Attribute(XamlNamespace + "Uid")!;
            var key = $"{uid}.{expectedHowToUseUidProperties[uid]}";
            Assert.Contains(key, english.Keys);
            Assert.Contains(key, korean.Keys);
            Assert.Equal(english[key], (string?)element.Attribute(expectedHowToUseUidProperties[uid]));
        }

        var cardTemplate = shortcutDocument.Descendants().Single(element => element.Name.LocalName == "DataTemplate"
            && (string?)element.Attribute(XamlNamespace + "Key") == "ShortcutCardTemplate");
        Assert.Equal("{x:Bind Title}", (string?)cardTemplate.Descendants().Single(element =>
            element.Name.LocalName == "TextBlock" && (string?)element.Attribute("Text") == "{x:Bind Title}").Attribute("Text"));
        Assert.Equal("{x:Bind Action.ValidationMessage}", (string?)cardTemplate.Descendants().Single(element =>
            element.Name.LocalName == "TextBlock" && (string?)element.Attribute("Text") == "{x:Bind Action.ValidationMessage}").Attribute("Text"));
        var targetSummary = Assert.Single(cardTemplate.Descendants(), element => element.Name.LocalName == "TextBlock"
            && ((string?)element.Attribute("Text"))?.Contains("GetDisplayTargetSummary", StringComparison.Ordinal) == true);
        Assert.Equal("{x:Bind views:ShortcutPage.GetDisplayTargetSummary(Action.Kind, TargetSummary)}",
            (string?)targetSummary.Attribute("Text"));

        var catalog = ShortcutIconCatalog.Choices;
        Assert.Equal(32, catalog.Count);
        Assert.Equal(catalog.Count, catalog.Select(choice => choice.Glyph).Distinct(StringComparer.Ordinal).Count());
        foreach (var choice in catalog)
        {
            var key = $"Shortcut_Icon_{Convert.ToInt32(choice.Glyph[0]):X4}";
            Assert.Equal(choice.Name, english[key]);
            Assert.False(string.IsNullOrWhiteSpace(korean[key]));
        }
        Assert.Contains("Convert.ToInt32(choice.Glyph[0]):X4", shortcutCode, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.SetName(choiceButton, localizedName)", shortcutCode, StringComparison.Ordinal);
        Assert.Contains("ToolTipService.SetToolTip(choiceButton, localizedName)", shortcutCode, StringComparison.Ordinal);
        Assert.Contains("MainUiStrings.Format(\"Shortcut_IconPreview_A11y\", \"{0} Shortcut icon\", iconName.Text)", shortcutCode, StringComparison.Ordinal);

        var codeResourceKeys = Regex.Matches(shortcutCode, "MainUiStrings\\.(?:Get|Format)\\(\\\"(?<key>Shortcut_[^\\\"]+)\\\"")
            .Select(match => match.Groups["key"].Value)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        Assert.NotEmpty(codeResourceKeys);
        foreach (var key in codeResourceKeys)
        {
            Assert.Contains(key, english.Keys);
            Assert.Contains(key, korean.Keys);
        }
        var codeResourceFallbacks = Regex.Matches(shortcutCode,
                "MainUiStrings\\.(?:Get|Format)\\(\"(?<key>Shortcut_[^\"]+)\"\\s*,\\s*\"(?<fallback>[^\"]*)\"")
            .Cast<Match>();
        foreach (var call in codeResourceFallbacks)
        {
            Assert.Equal(call.Groups["fallback"].Value, english[call.Groups["key"].Value]);
        }

        Assert.Equal("Steam Big Picture", ShortcutPage.GetDisplayTargetSummary(
            FrontendShortcutEditorActionKind.SteamBigPicture, "runtime value"));
        Assert.Equal("Steam client", ShortcutPage.GetDisplayTargetSummary(
            FrontendShortcutEditorActionKind.SteamClient, "runtime value"));
        Assert.Equal("XBOX app", ShortcutPage.GetDisplayTargetSummary(
            FrontendShortcutEditorActionKind.XboxApp, "runtime value"));
        Assert.Equal("Fullscreen screenshot", ShortcutPage.GetDisplayTargetSummary(
            FrontendShortcutEditorActionKind.ScreenshotFullscreen, "runtime value"));
        Assert.Equal("PowerShell", ShortcutPage.GetDisplayTargetSummary(
            FrontendShortcutEditorActionKind.PowerShell, "runtime value"));
        Assert.Equal("game.exe", ShortcutPage.GetDisplayTargetSummary(
            FrontendShortcutEditorActionKind.Executable, "game.exe"));
        Assert.Equal("example.com", ShortcutPage.GetDisplayTargetSummary(
            FrontendShortcutEditorActionKind.Url, "example.com"));
        Assert.Equal("Unsupported in this version · vendor.action", ShortcutPage.GetDisplayTargetSummary(
            FrontendShortcutEditorActionKind.Unsupported, "Unsupported in this version · vendor.action"));
        Assert.Contains("snapshot.FailureMessage ?? MainUiStrings.Get(\"Shortcut_EditorUnavailable\"", shortcutCode, StringComparison.Ordinal);
        Assert.Contains("result.FailureMessage ?? MainUiStrings.Get(\"Shortcut_SaveFailed\"", shortcutCode, StringComparison.Ordinal);
        Assert.Contains("MainUiStrings.Format(\"Shortcut_Delete_ConfirmationBody\", \"Delete ‘{0}’? This cannot be undone.\", tile.Title)", shortcutCode, StringComparison.Ordinal);
        Assert.Equal("Loading documentation…", english["HowToUse_Loading.Text"]);
        Assert.Contains("RetryButton_Click", File.ReadAllText(Path.Combine(root, "src", "SteamInputAddonforClaw.UI", "Views", "HowToUsePage.xaml")), StringComparison.Ordinal);
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
    public void Settings_general_xaml_uses_unique_resources_and_preserves_excluded_controls()
    {
        var root = FindRepositoryRoot();
        var stringsDirectory = Path.Combine(root, "src", "SteamInputAddonforClaw.UI", "Strings");
        var english = ReadResources(Path.Combine(stringsDirectory, "en-US", "Resources.resw"));
        var korean = ReadResources(Path.Combine(stringsDirectory, "ko-KR", "Resources.resw"));
        var xamlPath = Path.Combine(root, "src", "SteamInputAddonforClaw.UI", "Views", "SettingsPage.xaml");
        var document = XDocument.Load(xamlPath);
        var uidElements = document.Descendants()
            .Where(element => element.Attribute(XamlNamespace + "Uid") is not null)
            .ToArray();
        var actualUids = uidElements.Select(element => (string)element.Attribute(XamlNamespace + "Uid")!).ToArray();
        var expectedUids = new[]
        {
            "Settings_PageTitle", "Settings_UpdateCard", "Settings_UpdateButton", "Settings_AppThemeCard",
            "Settings_AppTheme_System", "Settings_AppTheme_Light", "Settings_AppTheme_Dark", "Settings_GamingHomeExpander",
            "Settings_GamingHome_Off", "Settings_GamingHome_Xbox", "Settings_GamingHome_SteamBigPicture",
            "Settings_GamingHome_Other", "Settings_GamingHomeStartupCard",
            "Settings_LoggingCard", "Settings_OpenLogFolderButton", "Settings_RequiredComponentsExpander"
        };

        Assert.Equal(expectedUids.OrderBy(uid => uid, StringComparer.Ordinal), actualUids.OrderBy(uid => uid, StringComparer.Ordinal));
        Assert.Equal(actualUids.Length, actualUids.Distinct(StringComparer.Ordinal).Count());
        foreach (var element in uidElements)
        {
            var uid = (string)element.Attribute(XamlNamespace + "Uid")!;
            foreach (var property in new[] { "Text", "Header", "Description", "Content" })
            {
                var sourceValue = (string?)element.Attribute(property);
                if (sourceValue is null) continue;

                var key = $"{uid}.{property}";
                if (uid == "Settings_AppThemeCard" && property == "Description")
                {
                    Assert.DoesNotContain(key, english.Keys);
                    Assert.DoesNotContain(key, korean.Keys);
                    continue;
                }

                Assert.Contains(key, english.Keys);
                Assert.Contains(key, korean.Keys);
                Assert.Equal(sourceValue, english[key]);
                Assert.False(string.IsNullOrWhiteSpace(korean[key]));
            }
        }

        Assert.Equal("Settings", english["MainNav_Settings"]);
        Assert.False(string.IsNullOrWhiteSpace(korean["MainNav_Settings"]));
        Assert.DoesNotContain("MainNav_Settings.Content", english.Keys);
        var expectedKorean = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Settings_PageTitle.Text"] = "설정",
            ["Settings_UpdateCard.Header"] = "앱 업데이트",
            ["Settings_UpdateButton.Content"] = "확인",
            ["Settings_AppThemeCard.Header"] = "테마",
            ["Settings_AppTheme_System.Content"] = "시스템",
            ["Settings_AppTheme_Light.Content"] = "라이트",
            ["Settings_AppTheme_Dark.Content"] = "다크",
            ["Settings_GamingHomeExpander.Header"] = "XBOX 모드(Full Screen Experience)",
            ["Settings_GamingHomeExpander.Description"] = "XBOX 모드에서 사용할 앱을 선택하세요.",
            ["Settings_GamingHome_Off.Content"] = "사용안함",
            ["Settings_GamingHome_Xbox.Content"] = "XBOX",
            ["Settings_GamingHome_SteamBigPicture.Content"] = "Steam 빅픽쳐 모드",
            ["Settings_GamingHomeStartupCard.Header"] = "시작 시 XBOX 모드로 전환",
            ["Settings_LoggingCard.Header"] = "로그",
            ["Settings_LoggingCard.Description"] = "앱 로그 레벨을 선택하세요.",
            ["Settings_OpenLogFolderButton.Content"] = "폴더 열기",
            ["Settings_RequiredComponentsExpander.Header"] = "필수 추가 설치 앱",
            ["Settings_Update_InstallAction"] = "업데이트 설치",
            ["Settings_Update_Checking"] = "업데이트 확인 중..."
        };
        foreach (var (key, value) in expectedKorean)
        {
            Assert.Equal(value, korean[key]);
        }

        Assert.Equal("XBOX Mode (Full Screen Experience)", english["Settings_GamingHomeExpander.Header"]);
        Assert.Equal("Choose the home app used by XBOX Mode", english["Settings_GamingHomeExpander.Description"]);
        Assert.Equal("XBOX", english["Settings_GamingHome_Xbox.Content"]);
        var appThemeCard = document.Descendants().Single(element =>
            element.Name.LocalName == "SettingsCard" && (string?)element.Attribute(XamlNamespace + "Name") == "AppThemeCard");
        Assert.Equal("Choose the main app appearance.", (string?)appThemeCard.Attribute("Description"));
        Assert.Equal("Choose the main app appearance.", english["Settings_AppTheme_Description"]);
        Assert.False(string.IsNullOrWhiteSpace(korean["Settings_AppTheme_Description"]));

        var developerCard = document.Descendants().Single(element =>
            element.Name.LocalName == "SettingsCard" && (string?)element.Attribute(XamlNamespace + "Name") == "DeveloperMenuCard");
        Assert.Null(developerCard.Attribute(XamlNamespace + "Uid"));
        Assert.Equal("Developer Menu", (string?)developerCard.Attribute("Header"));
        Assert.Equal("Developer-only diagnostics. General users should not use this menu.", (string?)developerCard.Attribute("Description"));
        Assert.DoesNotContain(english.Keys, key => key.Contains("Developer", StringComparison.OrdinalIgnoreCase));

        var loggingChoices = document.Descendants().Single(element =>
            element.Name.LocalName == "ComboBox" && (string?)element.Attribute(XamlNamespace + "Name") == "LogLevelComboBox")
            .Elements().Where(element => element.Name.LocalName == "ComboBoxItem").ToArray();
        Assert.Equal(new[] { "Off", "Info", "Debug" }, loggingChoices.Select(item => (string?)item.Attribute("Content")));
        Assert.All(loggingChoices, item => Assert.Null(item.Attribute(XamlNamespace + "Uid")));
        foreach (var key in new[] { "Settings_Logging_Off.Content", "Settings_Logging_Info.Content", "Settings_Logging_Debug.Content" })
        {
            Assert.DoesNotContain(key, english.Keys);
            Assert.DoesNotContain(key, korean.Keys);
        }
    }

    [Fact]
    public void Quick_overlay_page_owns_the_moved_power_source_option_and_localizes_its_shell()
    {
        var root = FindRepositoryRoot();
        var stringsDirectory = Path.Combine(root, "src", "SteamInputAddonforClaw.UI", "Strings");
        var english = ReadResources(Path.Combine(stringsDirectory, "en-US", "Resources.resw"));
        var korean = ReadResources(Path.Combine(stringsDirectory, "ko-KR", "Resources.resw"));
        var pagePath = Path.Combine(root, "src", "SteamInputAddonforClaw.UI", "Views", "QuickOverlayPage.xaml");
        var page = XDocument.Load(pagePath);
        Assert.Equal(2, page.Descendants().Count(element => element.Name.LocalName == "SettingsCard"));
        var tabOrderExpander = Assert.Single(page.Descendants(), element => element.Name.LocalName == "SettingsExpander");
        Assert.Single(page.Descendants(), element => element.Name.LocalName == "ToggleSwitch");
        var uidElements = page.Descendants()
            .Where(element => element.Attribute(XamlNamespace + "Uid") is not null)
            .ToArray();
        var actualUids = uidElements.Select(element => (string)element.Attribute(XamlNamespace + "Uid")!).ToArray();
        Assert.Equal(new[]
            {
                "QuickOverlay_PageTitle",
                "QuickOverlay_PowerSourceCard",
                "QuickOverlay_TabOrderExpander",
                "QuickOverlay_TabOrderStatusText",
                "QuickOverlay_YButtonQuickJumpCard",
                "QuickOverlay_YButtonQuickJumpComboBox",
                "QuickOverlay_YButtonQuickJump_Controller",
                "QuickOverlay_YButtonQuickJump_Device",
                "QuickOverlay_YButtonQuickJump_Off",
                "QuickOverlay_YButtonQuickJump_Profile",
                "QuickOverlay_YButtonQuickJump_Setting",
                "QuickOverlay_YButtonQuickJump_Shortcut",
            },
            actualUids.OrderBy(uid => uid, StringComparer.Ordinal));
        Assert.Equal(actualUids.Length, actualUids.Distinct(StringComparer.Ordinal).Count());

        foreach (var element in uidElements)
        {
            var uid = (string)element.Attribute(XamlNamespace + "Uid")!;
            foreach (var property in new[] { "Text", "Header", "Description", "Content", "AutomationProperties.Name" })
            {
                var sourceValue = (string?)element.Attribute(property);
                if (sourceValue is null) continue;

                var key = $"{uid}.{property}";
                Assert.Equal(sourceValue, english[key]);
                Assert.False(string.IsNullOrWhiteSpace(korean[key]));
            }
        }

        Assert.Equal("Quick Overlay", english["MainNav_QuickOverlay.Content"]);
        Assert.Equal("퀵 오버레이", korean["MainNav_QuickOverlay.Content"]);
        Assert.Equal("퀵 오버레이", korean["QuickOverlay_PageTitle.Text"]);
        Assert.Equal("Y Button Quick Jump", english["QuickOverlay_YButtonQuickJumpCard.Header"]);
        Assert.Equal("Y 버튼 빠른 탭 이동", korean["QuickOverlay_YButtonQuickJumpCard.Header"]);
        Assert.Equal("Press Y to jump to the selected tab while Quick Overlay is open.", english["QuickOverlay_YButtonQuickJumpCard.Description"]);
        Assert.Equal("퀵 오버레이를 연 상태에서 Y 버튼을 누르면 선택한 탭으로 바로 이동합니다.", korean["QuickOverlay_YButtonQuickJumpCard.Description"]);
        Assert.Equal(new[] { "Profile", "Shortcut", "Device", "Controller", "Setting", "Off" },
            new[] { "Profile", "Shortcut", "Device", "Controller", "Setting", "Off" }.Select(name => english[$"QuickOverlay_YButtonQuickJump_{name}.Content"]));
        Assert.Equal(new[] { "프로필", "바로 가기", "디바이스", "컨트롤러", "설정", "사용 안 함" },
            new[] { "Profile", "Shortcut", "Device", "Controller", "Setting", "Off" }.Select(name => korean[$"QuickOverlay_YButtonQuickJump_{name}.Content"]));
        Assert.Equal("Y Button Quick Jump destination", english["QuickOverlay_YButtonQuickJumpComboBox_A11y"]);
        Assert.Equal("Y 버튼 빠른 탭 이동 대상", korean["QuickOverlay_YButtonQuickJumpComboBox_A11y"]);

        var quickOverlayPageSource = File.ReadAllText(Path.Combine(
            root, "src", "SteamInputAddonforClaw.UI", "Views", "QuickOverlayPage.xaml.cs"));
        Assert.Contains("AutomationProperties.SetName(", quickOverlayPageSource, StringComparison.Ordinal);
        Assert.Contains("YButtonQuickJumpComboBox,", quickOverlayPageSource, StringComparison.Ordinal);
        Assert.Contains("MainUiStrings.Get(", quickOverlayPageSource, StringComparison.Ordinal);
        Assert.Contains("\"QuickOverlay_YButtonQuickJumpComboBox_A11y\"", quickOverlayPageSource, StringComparison.Ordinal);
        Assert.Equal("Show only current power source", english["QuickOverlay_PowerSourceCard.Header"]);
        Assert.Equal("현재 전원 방식 옵션만 보기", korean["QuickOverlay_PowerSourceCard.Header"]);
        Assert.Equal("퀵 셋팅 오버레이에 현재 연결된 전원 방식 옵션만 표시합니다.",
            korean["QuickOverlay_PowerSourceCard.Description"]);
        Assert.Equal("Quick Overlay Tab Order", english["QuickOverlay_TabOrderExpander.Header"]);
        Assert.Equal("퀵 오버레이 탭 순서", korean["QuickOverlay_TabOrderExpander.Header"]);
        Assert.Equal("Set the tab order and which tab opens first.", english["QuickOverlay_TabOrderExpander.Description"]);
        Assert.Equal("퀵 오버레이 탭 순서와 처음 열리는 탭을 설정합니다.",
            korean["QuickOverlay_TabOrderExpander.Description"]);
        Assert.Equal("Loading tab order…", english["QuickOverlay_TabOrderStatusText.Text"]);
        Assert.Equal("탭 순서를 불러오는 중…", korean["QuickOverlay_TabOrderStatusText.Text"]);
        Assert.Equal("Move {0} up", english["QuickOverlay_TabOrder_MoveUp_A11y"]);
        Assert.Equal("{0} 위로 이동", korean["QuickOverlay_TabOrder_MoveUp_A11y"]);
        Assert.Equal("Move {0} down", english["QuickOverlay_TabOrder_MoveDown_A11y"]);
        Assert.Equal("{0} 아래로 이동", korean["QuickOverlay_TabOrder_MoveDown_A11y"]);
        Assert.Equal("Tab order is currently unavailable.", english["QuickOverlay_TabOrder_Unavailable"]);
        Assert.Equal("현재 탭 순서를 사용할 수 없습니다.", korean["QuickOverlay_TabOrder_Unavailable"]);
        Assert.Equal("False", (string?)tabOrderExpander.Attribute("IsExpanded"));
        Assert.Equal("Quick Overlay Tab Order", (string?)tabOrderExpander.Attribute("Header"));
        Assert.Contains(tabOrderExpander.Elements(), element =>
            element.Name.LocalName == "SettingsExpander.HeaderIcon" &&
            element.Elements().Any(icon => icon.Name.LocalName == "SymbolIcon" &&
                                           (string?)icon.Attribute("Symbol") == "List"));
        Assert.DoesNotContain("Settings_QuickSettingsPowerSourceCard.Header", english.Keys);
        Assert.DoesNotContain("Settings_QuickSettingsPowerSourceCard.Description", english.Keys);
        Assert.DoesNotContain("Settings_QuickSettingsPowerSourceCard.Header", korean.Keys);
        Assert.DoesNotContain("Settings_QuickSettingsPowerSourceCard.Description", korean.Keys);

        var navigation = XDocument.Load(Path.Combine(root, "src", "SteamInputAddonforClaw.UI", "MainWindow.xaml"));
        var navItem = Assert.Single(navigation.Descendants(), element =>
            element.Name.LocalName == "NavigationViewItem" && (string?)element.Attribute("Tag") == "QuickOverlay");
        Assert.Equal("MainNav_QuickOverlay", (string?)navItem.Attribute(XamlNamespace + "Uid"));
        Assert.Equal("Quick Overlay", (string?)navItem.Attribute("Content"));

        var englishReadme = File.ReadAllText(Path.Combine(root, "README.md"));
        var koreanReadme = File.ReadAllText(Path.Combine(root, "docs", "howtouse", "README_KO.md"));
        foreach (var readme in new[] { englishReadme, koreanReadme })
        {
            var clawHud = readme.IndexOf("### ClawHUD", StringComparison.Ordinal);
            var quickOverlay = readme.IndexOf("### Quick Overlay", StringComparison.Ordinal);
            var shortcut = readme.IndexOf("### Shortcut", StringComparison.Ordinal);
            Assert.True(clawHud >= 0 && quickOverlay > clawHud && shortcut > quickOverlay);
            var quickOverlaySection = readme[quickOverlay..shortcut];
            Assert.Contains("current power source", quickOverlaySection, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Y Button Quick Jump", quickOverlaySection, StringComparison.Ordinal);
        }
        Assert.Contains("controls which tab opens first on the next Overlay Show", englishReadme, StringComparison.Ordinal);
        Assert.Contains("의 첫 번째 탭은 다음에 Overlay를 열 때 처음 표시", koreanReadme, StringComparison.Ordinal);

        var englishSettingsStart = englishReadme.IndexOf("### Settings", StringComparison.Ordinal);
        var englishSettingsEnd = englishReadme.IndexOf("## Game profiles", englishSettingsStart, StringComparison.Ordinal);
        var koreanSettingsStart = koreanReadme.IndexOf("### Settings", StringComparison.Ordinal);
        var koreanSettingsEnd = koreanReadme.IndexOf("## 게임 프로필", koreanSettingsStart, StringComparison.Ordinal);
        Assert.True(englishSettingsStart >= 0 && englishSettingsEnd > englishSettingsStart);
        Assert.True(koreanSettingsStart >= 0 && koreanSettingsEnd > koreanSettingsStart);
        Assert.DoesNotContain("Show only current power source", englishReadme[englishSettingsStart..englishSettingsEnd], StringComparison.Ordinal);
        Assert.DoesNotContain("현재 전원 방식 옵션만 보기", koreanReadme[koreanSettingsStart..koreanSettingsEnd], StringComparison.Ordinal);
        Assert.Contains("**Setting** — rearrange the Overlay tabs and choose whether to show only the current power source.", englishReadme, StringComparison.Ordinal);
        Assert.Contains("**Setting** — Overlay 탭 순서와 현재 전원 소스만 표시하는 옵션을 설정합니다.", koreanReadme, StringComparison.Ordinal);
    }

    [Fact]
    public void Settings_dynamic_ui_messages_are_resourced_and_preserve_producer_data()
    {
        var root = FindRepositoryRoot();
        var stringsDirectory = Path.Combine(root, "src", "SteamInputAddonforClaw.UI", "Strings");
        var english = ReadResources(Path.Combine(stringsDirectory, "en-US", "Resources.resw"));
        var korean = ReadResources(Path.Combine(stringsDirectory, "ko-KR", "Resources.resw"));
        var source = File.ReadAllText(Path.Combine(root, "src", "SteamInputAddonforClaw.UI", "Views", "SettingsPage.xaml.cs"))
            .Replace("\r\n", "\n", StringComparison.Ordinal);
        var localMessages = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Settings_Update_InstallAction"] = "Install update",
            ["Settings_Update_Checking"] = "Checking for updates…",
            ["Settings_Update_OperationFailed"] = "The update operation failed. Try again.",
            ["Settings_GamingHome_InitiallyUnavailable"] = "Windows Gaming Full Screen Experience is unavailable.",
            ["Settings_GamingHome_VerifyFailed"] = "Windows Gaming Full Screen Experience could not be verified.",
            ["Settings_GamingHome_OtherDescription"] = "The current Windows app is outside Addon control. Choose Off, Xbox, or Steam Big Picture to replace it.",
            ["Settings_GamingHome_UnavailableDescription"] = "The current Windows setting could not be verified.",
            ["Settings_GamingHome_OtherStartupDescription"] = "The current value is shown but startup behavior for this app is not managed by the Addon.",
            ["Settings_GamingHome_SelectionChangeFailed"] = "The Gaming Home selection could not be changed.",
            ["Settings_GamingHome_StartupChangeFailed"] = "The Gaming Home startup setting could not be changed."
        };

        foreach (var (key, fallback) in localMessages)
        {
            Assert.Contains(key, english.Keys);
            Assert.Contains(key, korean.Keys);
            Assert.Equal(fallback, english[key]);
            Assert.False(string.IsNullOrWhiteSpace(korean[key]));
            Assert.Matches($"MainUiStrings\\.Get\\(\\s*\"{Regex.Escape(key)}\"", source);
        }

        Assert.Contains("MainUiStrings.Get(\"Settings_UpdateButton/Content\", \"Check\")", source, StringComparison.Ordinal);
        Assert.Contains("UpdateButton.Content = snapshot.CanInstall\n            ? MainUiStrings.Get(\"Settings_Update_InstallAction\", \"Install update\")\n            : MainUiStrings.Get(\"Settings_UpdateButton/Content\", \"Check\");", source, StringComparison.Ordinal);
        Assert.Matches("MainUiStrings\\.Get\\(\\s*\"Settings_GamingHomeExpander/Description\"", source);
        Assert.Matches("MainUiStrings\\.Get\\(\\s*\"Settings_GamingHomeExpander/Description\",\\s*\"Choose the home app used by XBOX Mode\"\\)", source);
        Assert.Matches("MainUiStrings\\.Get\\(\\s*\"Settings_GamingHomeStartupCard/Description\"", source);
        Assert.DoesNotContain("MainUiStrings.Get(\"Settings_GamingHomeExpander.Description\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("MainUiStrings.Get(\"Settings_GamingHomeStartupCard.Description\"", source, StringComparison.Ordinal);
        Assert.Contains("snapshot.UnavailableReason ?? MainUiStrings.Get(", source, StringComparison.Ordinal);
        Assert.Contains("result.FailureMessage ?? fallbackFailureMessage", source, StringComparison.Ordinal);
        Assert.Contains("UpdateCard.Description = snapshot.Message", source, StringComparison.Ordinal);
        Assert.Contains("UpdateCard.Description = result.FailureMessage", source, StringComparison.Ordinal);

        const string readyCountKey = "Settings_RequiredComponents_ReadyCount";
        const string readyCountFormat = "{0} of {1} ready";
        Assert.Equal(readyCountFormat, english[readyCountKey]);
        Assert.False(string.IsNullOrWhiteSpace(korean[readyCountKey]));
        Assert.Equal(new[] { 0, 1 }, PlaceholderIndexes(english[readyCountKey]));
        Assert.Equal(PlaceholderIndexes(english[readyCountKey]), PlaceholderIndexes(korean[readyCountKey]));
        Assert.Equal("2 of 3 ready", MainUiStrings.Format(readyCountKey, readyCountFormat, 2, 3));
        Assert.Contains("MainUiStrings.Format(\n                \"Settings_RequiredComponents_ReadyCount\",", source, StringComparison.Ordinal);
        Assert.Contains("components.Count(item => string.Equals(item.Status, \"Ready\", StringComparison.OrdinalIgnoreCase))", source, StringComparison.Ordinal);
        Assert.Contains("(\"HidHide\", snapshot.Prerequisites.HidHideStatus.ToString(), snapshot.Prerequisites.HidHideReason)", source, StringComparison.Ordinal);
        Assert.Contains("(\"usbip-win2\", snapshot.Prerequisites.UsbIpStatus.ToString(), snapshot.Prerequisites.UsbIpReason)", source, StringComparison.Ordinal);
        Assert.Contains("(\"VIIPER\", snapshot.Prerequisites.ViiperStatus.ToString(), snapshot.Prerequisites.ViiperReason)", source, StringComparison.Ordinal);
        Assert.Contains("RequiredComponentsExpander.Items.Clear();", source, StringComparison.Ordinal);
        Assert.Contains("Header = name,", source, StringComparison.Ordinal);
        Assert.Contains("Description = reason,", source, StringComparison.Ordinal);
        Assert.Contains("Text = status,", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Device_xaml_text_uses_unique_resource_uids_and_localizes_only_the_repair_button_in_summary()
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
            "Device_AutoStartRepairButton",
            "Device_TdpExpander",
            "Device_TdpAcCard",
            "Device_TdpDcCard",
            "Device_CpuBoostExpander",
            "Device_CpuBoostAcCard",
            "Device_CpuBoostDcCard",
            "Device_MaximumProcessorStateExpander",
            "Device_MaximumProcessorStateAcCard",
            "Device_MaximumProcessorStateDcCard",
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
        Assert.Equal(new[] { "Device_AutoStartRepairButton" }, summaryCard.DescendantsAndSelf()
            .Where(element => element.Attribute(XamlNamespace + "Uid") is not null)
            .Select(element => (string)element.Attribute(XamlNamespace + "Uid")!));
        Assert.Equal(
            new[]
            {
                "Border(Name=DeviceSummaryCard,Padding=18,16,Background={ThemeResource CardBackgroundFillColorDefaultBrush},BorderBrush={ThemeResource CardStrokeColorDefaultBrush},BorderThickness=1,CornerRadius=8)",
                "Grid(ColumnSpacing=8,RowSpacing=4)",
                "Grid.RowDefinitions()",
                "RowDefinition(Height=Auto)",
                "RowDefinition(Height=Auto)",
                "Grid.ColumnDefinitions()",
                "ColumnDefinition(Width=Auto)",
                "ColumnDefinition(Width=*)",
                "ColumnDefinition(Width=Auto)",
                "TextBlock(Name=DeviceManufacturerText,FontSize=18,FontWeight=SemiBold,VerticalAlignment=Center)",
                "TextBlock(Name=DeviceModelText,Grid.Column=1,FontSize=18,FontWeight=SemiBold,TextWrapping=Wrap,VerticalAlignment=Center)",
                "TextBlock(Name=DeviceBoardGpuText,Grid.Row=1,Grid.ColumnSpan=2,Opacity=0.7,TextWrapping=Wrap)",
                "StackPanel(Grid.RowSpan=2,Grid.Column=2,Spacing=6,HorizontalAlignment=Right,VerticalAlignment=Center)",
                "StackPanel(Orientation=Horizontal,Spacing=6,HorizontalAlignment=Right,VerticalAlignment=Center)",
                "Ellipse(Name=DeviceSupportIndicator,Width=8,Height=8,Style={StaticResource DeviceSummaryIndicatorNeutralStyle},VerticalAlignment=Center)",
                "TextBlock(Name=DeviceSupportText,FontSize=13,FontWeight=SemiBold,TextAlignment=Right,TextWrapping=Wrap,VerticalAlignment=Center)",
                "StackPanel(Orientation=Horizontal,Spacing=6,HorizontalAlignment=Right,VerticalAlignment=Center)",
                "Ellipse(Name=AutoStartStatusIndicator,Width=8,Height=8,Style={StaticResource DeviceSummaryIndicatorNeutralStyle},VerticalAlignment=Center)",
                "TextBlock(Name=AutoStartStatusText,FontSize=12,TextAlignment=Right,TextWrapping=Wrap,VerticalAlignment=Center)",
                "Button(Name=AutoStartRepairButton,Uid=Device_AutoStartRepairButton,Content=Repair,Padding=8,2,MinHeight=24,FontSize=12,Visibility=Collapsed,Click=AutoStartRepairButton_Click)",
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
            var message = (string?)element.Attribute("Message");
            if (message is null) continue;

            Assert.Equal(message, english[$"{uid}.Message"]);
            Assert.False(string.IsNullOrWhiteSpace(korean[$"{uid}.Message"]));
        }

        var localizedProperties = new[] { "Text", "Content", "Header", "Description", "Message" };
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
        foreach (var (key, englishValue, koreanValue) in new[]
        {
            ("Controller_SteamVirtualControllerExpander.Header", "Controller for Steam Game", "Steam 게임용 컨트롤러"),
            ("Controller_SteamVirtualControllerActivity.Text", "Selection is disabled while a Steam game or Big Picture Mode is active.", "Steam 게임 또는 빅픽쳐 모드 중에는 변경 할 수 없습니다."),
            ("Controller_MappingUnavailable.Text", "Unavailable", "사용할 수 없음"),
            ("Controller_FrontButtonPathValidationInfoBar.Message", "Enter a fully qualified path to an .exe file.", "실행 파일을 선택하려면 전체 경로가 포함된 .exe 파일을 입력하세요."),
            ("Controller_ButtonMappingHeader.Text", "Button Mapping", "버튼 맵핑"),
            ("Controller_GamebarExpander.Header", "XBOX Gamebar Button", "XBOX 게임바 버튼"),
            ("Controller_GamebarSteamCard.Header", "Steam Game / Big Picture", "Steam 게임 / Steam 빅픽쳐 모드"),
            ("Controller_CenterMExpander.Header", "Center M Button", "Center M 버튼"),
            ("Controller_CenterMSteamCard.Header", "Steam Game / Big Picture", "Steam 게임 / Steam 빅픽쳐 모드"),
            ("Controller_BackButtonMappingExpander.Header", "M1 / M2 Button", "M1 / M2 버튼"),
            ("Controller_BackButtonMappingExpander.Description", "XBOX 360 mode only. Steam Game / Big Picture keeps M1 as R4 and M2 as L4.", "XBOX 360 mode only. Steam Game / Big Picture keeps M1 as R4 and M2 as L4."),
            ("Controller_M1Card.Header", "M1 Button", "M1 버튼"),
            ("Controller_M2Card.Header", "M2 Button", "M2 버튼"),
            ("Controller_SettingsHeader.Text", "Controller Settings", "컨트롤러 설정"),
            ("Controller_LedExpander.Header", "Joystick LED", "조이스틱 LED"),
            ("Controller_LedBrightnessCard.Header", "Brightness", "밝기"),
            ("Controller_LedColorCard.Header", "Color", "색상"),
            ("Controller_VibrationStrengthExpander.Header", "Vibration Strength", "진동 세기"),
            ("Controller_LeftMotorCard.Header", "Left Motor", "왼쪽 모터"),
            ("Controller_RightMotorCard.Header", "Right Motor", "오른쪽 모터"),
            ("Controller_VibrationRecoveryCard.Header", "Vibration Recovery", "진동 복구"),
            ("Controller_RestoreVibrationButton.Content", "Restore", "복구"),
            ("Controller_Action_Xbox", "XBOX", "XBOX"),
            ("Controller_BackTarget_XboxGuide", "XBOX Guide", "XBOX Guide")
        })
        {
            Assert.Equal(englishValue, english[key]);
            Assert.Equal(koreanValue, korean[key]);
        }

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
            ("XboxApp", "Controller_Action_Xbox", "XBOX")
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
        Assert.Contains("? \"XBOX Guide\"", source, StringComparison.Ordinal);
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
        Assert.Contains("MainUiStrings.Get(\"Controller_RestoreVibrationButton/Content\", \"Restore\")", source, StringComparison.Ordinal);
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
            "Steam_CatalogTitle", "Steam_RefreshGamesButton", "Steam_GameSearchBox", "Steam_FavoriteStateUnavailableInfoBar", "Steam_BackButton",
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
            foreach (var property in new[] { "Text", "Content", "Header", "Description", "PlaceholderText", "Message" })
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
        var propertyPattern = Regex.Match(verifier, @"if \(\$key -match '(?<pattern>[^']+)'\)");
        Assert.True(propertyPattern.Success, "The published PRI verifier must expose its XAML property-key pattern.");
        var messageKeyMatch = Regex.Match(
            "Controller_FrontButtonPathValidationInfoBar.Message",
            propertyPattern.Groups["pattern"].Value);
        Assert.True(messageKeyMatch.Success, "The published PRI verifier must treat Message as a XAML property resource.");
        Assert.Equal(
            "/Resources/Controller_FrontButtonPathValidationInfoBar/Message",
            $"/Resources/{messageKeyMatch.Groups["uid"].Value}/{messageKeyMatch.Groups["property"].Value}");
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
            "Xbox_CatalogTitle", "Xbox_RefreshGamesButton", "Xbox_GameSearchBox", "Xbox_FavoriteStateUnavailableInfoBar", "Xbox_BackButton",
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
            foreach (var property in new[] { "Text", "Content", "Header", "Description", "PlaceholderText", "Message" })
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

        var expectedClawHudResources = new Dictionary<string, (string English, string Korean)>(StringComparer.Ordinal)
        {
            ["ClawHud_PageTitle.Text"] = ("ClawHUD", "ClawHUD"),
            ["ClawHud_PageSubtitle.Text"] = ("Configure the performance overlay and display behavior.", "ClawHUD 오버레이 설정을 변경할 수 있습니다."),
            ["ClawHud_RetryButton.Content"] = ("Retry", "재시도"),
            ["ClawHud_EnableCard.Header"] = ("ClawHUD", "ClawHUD"),
            ["ClawHud_DisplayModeCard.Header"] = ("Display mode", "디스플레이 모드"),
            ["ClawHud_DisplayModeCard.Description"] = ("Choose when the HUD is visible.", "ClawHUD의 표시 방식을 선택합니다."),
            ["ClawHud_SizeCard.Description"] = ("Adjust the overall HUD scale.", "ClawHUD의 크기를 조절합니다."),
            ["ClawHud_Size_Option_Default.Content"] = ("Default", "기본"),
            ["ClawHud_FontCard.Description"] = ("Font used by the performance HUD.", "ClawHUD 폰트를 선택합니다."),
            ["ClawHud_AlignmentCard.Header"] = ("Alignment", "정렬방식"),
            ["ClawHud_AlignmentCard.Description"] = ("Horizontal alignment of HUD content.", "ClawHUD의 가로 정렬방식을 선택합니다."),
            ["ClawHud_BackgroundWidthCard.Header"] = ("Background width", "ClawHUD 배경 방식"),
            ["ClawHud_BackgroundWidthCard.Description"] = ("Choose how far the HUD background extends.", "ClawHUD의 배경 표시 방식을 선택합니다."),
            ["ClawHud_BackgroundWidth_Option_FullWidth.Content"] = ("Full width", "전체 폭"),
            ["ClawHud_BackgroundWidth_Option_ContentWidth.Content"] = ("Content width", "콘텐츠 폭"),
            ["ClawHud_OpacityCard.Header"] = ("ClawHUD opacity", "ClawHUD 투명도"),
            ["ClawHud_LocalStatus_Off"] = ("Off", "끔"),
            ["ClawHud_LocalStatus_Starting"] = ("Starting…", "시작 중..."),
            ["ClawHud_VrrStatus_AlreadyCorrect"] = ("Fixed", "수정됨")
        };
        foreach (var (key, expected) in expectedClawHudResources)
        {
            Assert.Equal(expected.English, english[key]);
            Assert.Equal(expected.Korean, korean[key]);
        }

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
            var expectedEnglish = status == nameof(FrontendClawHudIntelVrrStatus.AlreadyCorrect) ? "Fixed" : status;
            Assert.Equal(expectedEnglish, english[key]);
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
