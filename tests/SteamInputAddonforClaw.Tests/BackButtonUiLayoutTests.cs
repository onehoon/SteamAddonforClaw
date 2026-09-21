using Xunit;

namespace SteamInputAddonforClaw.Tests;

/// <summary>PR4 source-level coverage for the Main UI M1/M2 Xbox360 mapping surface.</summary>
public sealed class BackButtonUiLayoutTests
{
    private static string Read(string relative)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "SteamInputAddonforClaw.slnx"))) dir = dir.Parent;
        return File.ReadAllText(Path.Combine(dir!.FullName, relative));
    }

    [Fact]
    public void Controller_page_places_one_m1_m2_editor_after_front_buttons()
    {
        var xaml = Read("src/SteamInputAddonforClaw.UI/Views/ControllerPage.xaml");

        var gamebar = xaml.IndexOf("Header=\"Gamebar Button\"", StringComparison.Ordinal);
        var centerM = xaml.IndexOf("Header=\"Center M Button\"", StringComparison.Ordinal);
        var back = xaml.IndexOf("Header=\"M1 / M2\"", StringComparison.Ordinal);

        Assert.True(gamebar >= 0 && centerM >= 0 && back >= 0);
        Assert.True(gamebar < centerM && centerM < back);
        Assert.Equal(1, Count(xaml, "x:Name=\"M1TargetComboBox\""));
        Assert.Equal(1, Count(xaml, "x:Name=\"M2TargetComboBox\""));
        Assert.DoesNotContain("M1Normal", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("M2Steam", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void Controller_page_explains_xbox360_only_and_steamdeck_rear_behavior()
    {
        var xaml = Read("src/SteamInputAddonforClaw.UI/Views/ControllerPage.xaml");

        Assert.Contains("Xbox 360 mode only", xaml, StringComparison.Ordinal);
        Assert.Contains("Steam Game / Big Picture", xaml, StringComparison.Ordinal);
        Assert.Contains("M1 as R4", xaml, StringComparison.Ordinal);
        Assert.Contains("M2 as L4", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void Controller_page_uses_back_availability_and_preserves_front_independence()
    {
        var codeBehind = Read("src/SteamInputAddonforClaw.UI/Views/ControllerPage.xaml.cs");

        Assert.Contains("bootstrap.BackButtonMappingAvailable", codeBehind, StringComparison.Ordinal);
        Assert.Contains("BackButtonMappingExpander.Visibility = _backButtonAvailable", codeBehind, StringComparison.Ordinal);
        Assert.Contains("FrontButtonMappingContent.Visibility = _available", codeBehind, StringComparison.Ordinal);
        Assert.Contains("MappingContent.Visibility = _available || _backButtonAvailable", codeBehind, StringComparison.Ordinal);
    }

    [Fact]
    public void Controller_page_offers_every_defined_target_with_readable_labels()
    {
        var codeBehind = Read("src/SteamInputAddonforClaw.UI/Views/ControllerPage.xaml.cs");

        Assert.Contains("Enum.GetValues<Xbox360BackButtonTarget>()", codeBehind, StringComparison.Ordinal);
        Assert.Contains("DescribeBackButtonTarget(target)", codeBehind, StringComparison.Ordinal);
        foreach (var label in new[]
        {
            "Disabled", "D-Pad Up", "D-Pad Right", "D-Pad Down", "D-Pad Left",
            "Left Bumper (LB)", "Right Bumper (RB)", "Left Trigger (LT)", "Right Trigger (RT)",
            "Left Stick Click (L3)", "Right Stick Click (R3)", "Xbox Guide"
        })
        {
            Assert.Contains($"=> \"{label}\"", codeBehind, StringComparison.Ordinal);
        }

        Assert.DoesNotContain("=> \"DPadUp\"", codeBehind, StringComparison.Ordinal);
        Assert.DoesNotContain("=> \"LeftStickClick\"", codeBehind, StringComparison.Ordinal);
    }

    [Fact]
    public void Rear_edits_emit_one_whole_record_and_allow_duplicate_targets()
    {
        var codeBehind = Read("src/SteamInputAddonforClaw.UI/Views/ControllerPage.xaml.cs");
        var handlerStart = codeBehind.IndexOf("private void BackButtonTargetComboBox_SelectionChanged", StringComparison.Ordinal);
        var handlerEnd = codeBehind.IndexOf("private static void SelectBackButtonTarget", handlerStart, StringComparison.Ordinal);
        Assert.True(handlerStart >= 0 && handlerEnd > handlerStart);
        var handler = codeBehind[handlerStart..handlerEnd];

        Assert.Contains("new BackButtonMappingSettings(target.Value, _backButtonMapping.M2)", handler, StringComparison.Ordinal);
        Assert.Contains("new BackButtonMappingSettings(_backButtonMapping.M1, target.Value)", handler, StringComparison.Ordinal);
        Assert.Contains("BackButtonMappingEditRequested?.Invoke(this, _backButtonMapping)", handler, StringComparison.Ordinal);
        Assert.DoesNotContain("DisablePartnerAction", handler, StringComparison.Ordinal);
        Assert.DoesNotContain("RefreshPartnerAvailability", handler, StringComparison.Ordinal);
    }

    [Fact]
    public void MainWindow_owns_the_ordered_back_mapping_save_and_rollback_path()
    {
        var mainWindow = Read("src/SteamInputAddonforClaw.UI/MainWindow.xaml.cs");

        Assert.Contains("_backButtonSaveChain", mainWindow, StringComparison.Ordinal);
        Assert.Contains("QueueBackButtonMutation", mainWindow, StringComparison.Ordinal);
        Assert.Contains("SetBackButtonMappingAsync(next)", mainWindow, StringComparison.Ordinal);
        Assert.Contains("ControllerContent.ApplyBackButtonMapping(result.BackButtonMapping)", mainWindow, StringComparison.Ordinal);
        Assert.Contains("ControllerContent.ApplyBackButtonMapping(_backButtonPersistedMapping)", mainWindow, StringComparison.Ordinal);
        Assert.Contains("if (version != _backButtonEditVersion) return;", mainWindow, StringComparison.Ordinal);
        Assert.DoesNotContain("SetBackButtonMappingAsync", Read("src/SteamInputAddonforClaw.UI/Views/ControllerPage.xaml.cs"), StringComparison.Ordinal);
    }

    private static int Count(string text, string value)
    {
        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }
        return count;
    }
}
