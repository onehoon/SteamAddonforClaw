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
    public void Maximum_processor_state_keeps_supported_values_and_editability_gates()
    {
        var root = FindRepositoryRoot();
        var xaml = File.ReadAllText(Path.Combine(root, "src/SteamInputAddonforClaw.UI/Views/DevicePage.xaml"));
        var code = File.ReadAllText(Path.Combine(root, "src/SteamInputAddonforClaw.UI/Views/DevicePage.xaml.cs"));
        Assert.Contains("MaximumProcessorStateEnabledToggleSwitch_Toggled", xaml, StringComparison.Ordinal);
        Assert.Contains("MaximumProcessorStateAcComboBox_SelectionChanged", xaml, StringComparison.Ordinal);
        Assert.Contains("MaximumProcessorStateDcComboBox_SelectionChanged", xaml, StringComparison.Ordinal);
        var start = code.IndexOf("private static readonly MaximumProcessorStatePercentItem[] MaximumProcessorStatePercentages", StringComparison.Ordinal);
        var end = code.IndexOf("public DevicePage()", start, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start);
        var values = System.Text.RegularExpressions.Regex.Matches(code[start..end], @"new\((\d+),")
            .Select(match => int.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture)).Order();
        Assert.Equal(new[] { 99, 100 }, values);
        Assert.Contains("UpdateMaximumProcessorStateEditability();", code, StringComparison.Ordinal);
        Assert.Contains("MaximumProcessorStateAcComboBox.IsEnabled = editable && _maximumProcessorStateSnapshot.Enabled", code, StringComparison.Ordinal);
        Assert.Contains("MaximumProcessorStateDcComboBox.IsEnabled = editable && _maximumProcessorStateSnapshot.Enabled", code, StringComparison.Ordinal);
        Assert.Contains("snapshot.Ac.DesiredPercent", code, StringComparison.Ordinal);
        Assert.Contains("snapshot.Dc.DesiredPercent", code, StringComparison.Ordinal);
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
}
