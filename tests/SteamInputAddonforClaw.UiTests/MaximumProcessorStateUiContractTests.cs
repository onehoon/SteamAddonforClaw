using System.Xml.Linq;
using Xunit;

namespace SteamInputAddonforClaw.UiTests;

public sealed class MaximumProcessorStateUiContractTests
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
    public void Maximum_processor_state_uses_independent_unmanaged_ac_dc_choices()
    {
        var root = FindRepositoryRoot();
        var xaml = File.ReadAllText(Path.Combine(root, "src/SteamInputAddonforClaw.UI/Views/DevicePage.xaml"));
        var code = File.ReadAllText(Path.Combine(root, "src/SteamInputAddonforClaw.UI/Views/DevicePage.xaml.cs"));
        Assert.DoesNotContain("MaximumProcessorStateEnabledToggleSwitch", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("MaximumProcessorStateEnabledToggleSwitch_Toggled", code, StringComparison.Ordinal);
        Assert.Contains("MaximumProcessorStateAcComboBox_SelectionChanged", xaml, StringComparison.Ordinal);
        Assert.Contains("MaximumProcessorStateDcComboBox_SelectionChanged", xaml, StringComparison.Ordinal);
        var start = code.IndexOf("private static readonly MaximumProcessorStateOptionItem[] MaximumProcessorStateOptions", StringComparison.Ordinal);
        var end = code.IndexOf("public DevicePage()", start, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start);
        Assert.Contains("new(null, MainUiStrings.Get(\"Device_MaximumProcessorState_DoNotChange\", \"Do not change\"))", code[start..end], StringComparison.Ordinal);
        var values = System.Text.RegularExpressions.Regex.Matches(code[start..end], @"new\((\d+),")
            .Select(match => int.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture)).Order();
        Assert.Equal(new[] { 99, 100 }, values);
        Assert.Contains("UpdateMaximumProcessorStateEditability();", code, StringComparison.Ordinal);
        Assert.Contains("MaximumProcessorStateAcComboBox.IsEnabled = editable", code, StringComparison.Ordinal);
        Assert.Contains("MaximumProcessorStateDcComboBox.IsEnabled = editable", code, StringComparison.Ordinal);
        Assert.Contains("snapshot.Ac.DesiredPercent", code, StringComparison.Ordinal);
        Assert.Contains("snapshot.Dc.DesiredPercent", code, StringComparison.Ordinal);
        Assert.Contains("SetDeviceMaximumProcessorStateAcAsync(item.Percent)", code, StringComparison.Ordinal);
        Assert.Contains("SetDeviceMaximumProcessorStateDcAsync(item.Percent)", code, StringComparison.Ordinal);
        Assert.Contains("SetDeviceMaximumProcessorStateAcAsync(int? percent", File.ReadAllText(Path.Combine(root, "src/SteamInputAddonforClaw.Contracts/Frontend/FrontendContracts.cs")), StringComparison.Ordinal);
    }

    [Fact]
    public void Device_performance_layout_keeps_existing_controls_in_compact_cards_or_expanders()
    {
        var root = FindRepositoryRoot();
        var xaml = File.ReadAllText(Path.Combine(root, "src/SteamInputAddonforClaw.UI/Views/DevicePage.xaml"));
        var code = File.ReadAllText(Path.Combine(root, "src/SteamInputAddonforClaw.UI/Views/DevicePage.xaml.cs"));

        foreach (var name in new[] { "TdpCard", "TdpExpander", "CpuBoostCard", "CpuBoostExpander", "MaximumProcessorStateCard", "MaximumProcessorStateExpander", "PowerModeCard", "PowerModeExpander" })
            Assert.Contains($"x:Name=\"{name}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("ApplyDevicePresentationLayout();", code, StringComparison.Ordinal);
        Assert.Contains("previous?.TdpExpanded != layout.TdpExpanded", code, StringComparison.Ordinal);
        Assert.Contains("previous is null || previous.Value.ActiveRail != layout.ActiveRail", code, StringComparison.Ordinal);
        Assert.Contains("SetVisibilityIfChanged", code, StringComparison.Ordinal);
        Assert.DoesNotContain("IsExpanded =", code, StringComparison.Ordinal);
    }
    [Fact]
    public void Maximum_processor_state_remains_device_only()
    {
        var root = FindRepositoryRoot();
        var presentation = File.ReadAllText(Path.Combine(root, "src/SteamInputAddonforClaw/Frontend/QuickSettingsPresentation.cs"));
        var overlay = File.ReadAllText(Path.Combine(root, "src/SteamInputAddonforClaw.UI/Views/OverlayPage.xaml"));
        var gameProfile = File.ReadAllText(Path.Combine(root, "src/SteamInputAddonforClaw/Profiles/GameProfile.cs"));
        var xboxGameProfile = File.ReadAllText(Path.Combine(root, "src/SteamInputAddonforClaw/Profiles/XboxGameProfile.cs"));
        Assert.DoesNotContain("MaximumProcessorState", presentation, StringComparison.Ordinal);
        Assert.DoesNotContain("MaximumProcessorState", overlay, StringComparison.Ordinal);
        Assert.DoesNotContain("MaximumProcessorState", gameProfile, StringComparison.Ordinal);
        Assert.DoesNotContain("MaximumProcessorState", xboxGameProfile, StringComparison.Ordinal);
    }

    [Fact]
    public void Device_removes_descriptive_copy_and_localizes_new_card_headers_and_unmanaged_choice()
    {
        var root = FindRepositoryRoot();
        var xamlPath = Path.Combine(root, "src/SteamInputAddonforClaw.UI/Views/DevicePage.xaml");
        var xaml = XDocument.Load(xamlPath);
        Assert.DoesNotContain(xaml.Descendants().Where(element => element.Name.LocalName is "SettingsCard" or "SettingsExpander"),
            element => element.Attribute("Description") is not null);

        foreach (var language in new[] { "en-US", "ko-KR" })
        {
            var resourcesPath = Path.Combine(root, "src/SteamInputAddonforClaw.UI/Strings", language, "Resources.resw");
            var resources = XDocument.Load(resourcesPath).Descendants("data")
                .Select(element => (string)element.Attribute("name")!).ToHashSet(StringComparer.Ordinal);
            Assert.Contains("Device_CpuBoostCard.Header", resources);
            Assert.Contains("Device_MaximumProcessorStateCard.Header", resources);
            Assert.Contains("Device_PowerModeCard.Header", resources);
            Assert.Contains("Device_TdpExpander.Header", resources);
            Assert.Contains("Device_MaximumProcessorState_DoNotChange", resources);
            Assert.DoesNotContain("Device_TdpCard.Description", resources);
            Assert.DoesNotContain("Device_CpuBoostExpander.Description", resources);
            Assert.DoesNotContain("Device_MaximumProcessorStateExpander.Description", resources);
            Assert.DoesNotContain("Device_PowerModeExpander.Description", resources);
            Assert.DoesNotContain("Device_BatteryChargeLimitCard.Description", resources);
        }
    }
}
