using System.Xml.Linq;
using Xunit;

namespace SteamInputAddonforClaw.UiTests;

public sealed class MaximumProcessorStateUiContractTests
{
    [Fact]
    public void Device_expander_order_icon_toggle_and_discrete_choices_match_the_work_order()
    {
        var root = FindRepositoryRoot();
        var xaml = File.ReadAllText(Path.Combine(root, "src/SteamInputAddonforClaw.UI/Views/DevicePage.xaml"));
        var code = File.ReadAllText(Path.Combine(root, "src/SteamInputAddonforClaw.UI/Views/DevicePage.xaml.cs"));

        Assert.True(xaml.IndexOf("x:Name=\"CpuBoostExpander\"", StringComparison.Ordinal)
            < xaml.IndexOf("x:Name=\"MaximumProcessorStateExpander\"", StringComparison.Ordinal));
        Assert.True(xaml.IndexOf("x:Name=\"MaximumProcessorStateExpander\"", StringComparison.Ordinal)
            < xaml.IndexOf("x:Name=\"PowerModeExpander\"", StringComparison.Ordinal));
        Assert.Contains("<FontIcon Glyph=\"&#xF42F;\" />", xaml, StringComparison.Ordinal);
        Assert.Contains("MaximumProcessorStateEnabledToggleSwitch_Toggled", xaml, StringComparison.Ordinal);
        Assert.Contains("MaximumProcessorStateAcComboBox_SelectionChanged", xaml, StringComparison.Ordinal);
        Assert.Contains("MaximumProcessorStateDcComboBox_SelectionChanged", xaml, StringComparison.Ordinal);
        Assert.Contains("UpdateMaximumProcessorStateEditability();", code, StringComparison.Ordinal);
        Assert.Contains("new(100, \"100%\")", code, StringComparison.Ordinal);
        Assert.Contains("new(99, \"99%\")", code, StringComparison.Ordinal);
        Assert.True(code.IndexOf("new(100, \"100%\")", StringComparison.Ordinal)
            < code.IndexOf("new(99, \"99%\")", StringComparison.Ordinal));
        Assert.Contains("MaximumProcessorStateAcComboBox.IsEnabled = editable && _maximumProcessorStateSnapshot.Enabled", code, StringComparison.Ordinal);
        Assert.Contains("MaximumProcessorStateDcComboBox.IsEnabled = editable && _maximumProcessorStateSnapshot.Enabled", code, StringComparison.Ordinal);
        Assert.Contains("snapshot.Ac.DesiredPercent", code, StringComparison.Ordinal);
        Assert.Contains("snapshot.Dc.DesiredPercent", code, StringComparison.Ordinal);
    }

    [Fact]
    public void Required_headers_are_localized_and_feature_is_not_projected_to_overlay_or_game_profiles()
    {
        var root = FindRepositoryRoot();
        var english = ReadResources(Path.Combine(root, "src/SteamInputAddonforClaw.UI/Strings/en-US/Resources.resw"));
        var korean = ReadResources(Path.Combine(root, "src/SteamInputAddonforClaw.UI/Strings/ko-KR/Resources.resw"));
        var expected = new Dictionary<string, (string English, string Korean)>
        {
            ["Device_MaximumProcessorStateExpander.Header"] = ("Maximum Processor State", "최대 프로세서 상태"),
            ["Device_MaximumProcessorStateAcCard.Header"] = ("AC", "AC"),
            ["Device_MaximumProcessorStateDcCard.Header"] = ("DC", "DC"),
            ["Device_MaximumProcessorStateExpander.Description"] =
                ("Set the Windows maximum processor performance state to 99% or 100%. This is not a CPU utilization limit. Turning this off stops applying the setting but does not restore the previous value.",
                 "Windows 최대 프로세서 성능 상태를 99% 또는 100%로 설정합니다. CPU 사용률 제한이 아닙니다. 끄면 설정 적용만 중지하며 이전 값으로 복원하지 않습니다."),
        };

        foreach (var (key, values) in expected)
        {
            Assert.Equal(values.English, english[key]);
            Assert.Equal(values.Korean, korean[key]);
        }
        var featureKeys = english.Keys.Where(key => key.StartsWith("Device_MaximumProcessorState", StringComparison.Ordinal)).OrderBy(key => key, StringComparer.Ordinal);
        Assert.Equal(featureKeys, korean.Keys.Where(key => key.StartsWith("Device_MaximumProcessorState", StringComparison.Ordinal)).OrderBy(key => key, StringComparer.Ordinal));

        var quickSettingsPresentation = File.ReadAllText(Path.Combine(root, "src/SteamInputAddonforClaw/Frontend/QuickSettingsPresentation.cs"));
        var overlayPage = File.ReadAllText(Path.Combine(root, "src/SteamInputAddonforClaw.UI/Views/OverlayPage.xaml"));
        var gameProfile = File.ReadAllText(Path.Combine(root, "src/SteamInputAddonforClaw/Profiles/GameProfile.cs"));
        var xboxGameProfile = File.ReadAllText(Path.Combine(root, "src/SteamInputAddonforClaw/Profiles/XboxGameProfile.cs"));
        Assert.DoesNotContain("MaximumProcessorState", quickSettingsPresentation, StringComparison.Ordinal);
        Assert.DoesNotContain("MaximumProcessorState", overlayPage, StringComparison.Ordinal);
        Assert.DoesNotContain("MaximumProcessorState", gameProfile, StringComparison.Ordinal);
        Assert.DoesNotContain("MaximumProcessorState", xboxGameProfile, StringComparison.Ordinal);
    }

    private static Dictionary<string, string> ReadResources(string path)
        => XDocument.Load(path).Root!.Elements("data").ToDictionary(
            element => (string)element.Attribute("name")!,
            element => element.Element("value")?.Value ?? "",
            StringComparer.Ordinal);

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "SteamInputAddonforClaw.slnx"))) return directory.FullName;
        }
        throw new DirectoryNotFoundException("Repository root was not found.");
    }
}
