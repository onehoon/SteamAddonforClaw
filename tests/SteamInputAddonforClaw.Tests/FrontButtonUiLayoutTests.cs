using Xunit;

namespace SteamInputAddonforClaw.Tests;

/// <summary>App UI PR-C section 22.9: the Controller mapping surface shows Gamebar Button before
/// Center M Button, exposes both presentation domains, and never surfaces the retired
/// WING / None / Remapping Enabled / Steam Input Routing vocabulary as user labels.</summary>
public sealed class FrontButtonUiLayoutTests
{
    private static string Read(string relative)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "SteamInputAddonforClaw.slnx"))) dir = dir.Parent;
        return File.ReadAllText(Path.Combine(dir!.FullName, relative));
    }

    [Fact]
    public void Controller_page_lists_gamebar_before_center_m_with_both_domains()
    {
        var xaml = Read("src/SteamInputAddonforClaw.UI/Views/ControllerPage.xaml");

        Assert.Contains("Button Mapping", xaml, StringComparison.Ordinal);
        var gamebar = xaml.IndexOf("Header=\"Gamebar Button\"", StringComparison.Ordinal);
        var centerM = xaml.IndexOf("Header=\"Center M Button\"", StringComparison.Ordinal);
        Assert.True(gamebar >= 0 && centerM >= 0);
        Assert.True(gamebar < centerM);
        Assert.Contains("Header=\"Normal\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Header=\"Steam Game / Big Picture\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void Controller_mapping_surface_does_not_use_the_retired_vocabulary()
    {
        var xaml = Read("src/SteamInputAddonforClaw.UI/Views/ControllerPage.xaml");
        var codeBehind = Read("src/SteamInputAddonforClaw.UI/Views/ControllerPage.xaml.cs");

        foreach (var forbidden in new[] { "WING", "Remapping Enabled", "RemappingEnabled", "Steam Input Routing", "SteamInputRouting" })
        {
            Assert.DoesNotContain(forbidden, xaml, StringComparison.Ordinal);
            Assert.DoesNotContain(forbidden, codeBehind, StringComparison.Ordinal);
        }

        // No "None" action label offered anywhere in the mapping editor.
        Assert.DoesNotContain("\"None\"", codeBehind, StringComparison.Ordinal);
        Assert.DoesNotContain("FrontButtonAction.None", codeBehind, StringComparison.Ordinal);
    }

    [Fact]
    public void The_editor_cannot_leave_a_gamebar_win_g_hotkey_selected()
    {
        var codeBehind = Read("src/SteamInputAddonforClaw.UI/Views/ControllerPage.xaml.cs");

        // A Gamebar editor disables the G key while Win is checked and clears a G selection, so the
        // Win+G combination can never be persisted from the UI (it is also rejected by validation).
        Assert.Contains("RefreshHotkeyAvailability", codeBehind, StringComparison.Ordinal);
        Assert.Contains("Kind == FrontButtonKind.Gamebar && _windows.IsChecked == true", codeBehind, StringComparison.Ordinal);
        Assert.Contains("_key.SelectedItem = null", codeBehind, StringComparison.Ordinal);
    }

    [Fact]
    public void The_center_m_button_detail_page_is_gone()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "SteamInputAddonforClaw.slnx"))) dir = dir.Parent;
        Assert.False(File.Exists(Path.Combine(dir!.FullName, "src/SteamInputAddonforClaw.UI/Views/CenterMButtonPage.xaml")));
        Assert.False(File.Exists(Path.Combine(dir!.FullName, "src/SteamInputAddonforClaw.UI/Views/CenterMButtonPage.xaml.cs")));
    }

    [Fact]
    public void MainWindow_owns_one_whole_mapping_save_chain()
    {
        var mainWindow = Read("src/SteamInputAddonforClaw.UI/MainWindow.xaml.cs");

        Assert.Contains("QueueFrontButtonMutation", mainWindow, StringComparison.Ordinal);
        Assert.Contains("_frontButtonSaveChain", mainWindow, StringComparison.Ordinal);
        Assert.DoesNotContain("_oem1SaveChain", mainWindow, StringComparison.Ordinal);
        Assert.DoesNotContain("QueueOem1Mutation", mainWindow, StringComparison.Ordinal);
    }
}
