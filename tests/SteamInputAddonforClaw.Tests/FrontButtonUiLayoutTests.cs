using SteamInputAddonforClaw.Contracts.FrontButtons;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

/// <summary>Controller mapping UI preserves front-button domains, supported actions, and mutation semantics.</summary>
public sealed class FrontButtonUiLayoutTests
{
    private static string Read(string relative)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "SteamInputAddonforClaw.slnx"))) dir = dir.Parent;
        return File.ReadAllText(Path.Combine(dir!.FullName, relative));
    }

    [Fact]
    public void Controller_page_exposes_both_front_button_domains_by_identity()
    {
        var xaml = Read("src/SteamInputAddonforClaw.UI/Views/ControllerPage.xaml");
        var code = Read("src/SteamInputAddonforClaw.UI/Views/ControllerPage.xaml.cs");
        Assert.Contains("x:Uid=\"Controller_GamebarExpander\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Uid=\"Controller_CenterMExpander\"", xaml, StringComparison.Ordinal);
        foreach (var control in new[] { "GamebarNormalActionComboBox", "GamebarSteamActionComboBox",
                     "CenterMNormalActionComboBox", "CenterMSteamActionComboBox" })
            Assert.Contains($"x:Name=\"{control}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("FrontButtonActionCapabilities.ActionsFor(domain)", code, StringComparison.Ordinal);
        foreach (var domain in Enum.GetValues<FrontButtonDomain>())
        foreach (var action in FrontButtonActionCapabilities.ActionsFor(domain))
            Assert.True(FrontButtonActionCapabilities.Supports(action, domain));
    }
    [Fact]
    public void Front_button_action_catalog_excludes_unsupported_no_op_actions()
    {
        foreach (var domain in Enum.GetValues<FrontButtonDomain>())
        {
            Assert.DoesNotContain((FrontButtonAction)99, FrontButtonActionCapabilities.ActionsFor(domain));
            Assert.False(FrontButtonActionCapabilities.Supports((FrontButtonAction)99, domain));
        }
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
    public void Xbox_action_uses_the_shared_domain_capability_catalog()
    {
        var code = Read("src/SteamInputAddonforClaw.UI/Views/ControllerPage.xaml.cs");
        Assert.Contains("FrontButtonActionCapabilities.ActionsFor(domain)", code, StringComparison.Ordinal);
        Assert.Contains("actionComboBox.Items.Add(new ComboBoxItem { Content = DescribeAction(action), Tag = action })", code, StringComparison.Ordinal);
        Assert.Contains(FrontButtonAction.XboxApp, FrontButtonActionCapabilities.ActionsFor(FrontButtonDomain.Normal));
        Assert.DoesNotContain(FrontButtonAction.XboxApp, FrontButtonActionCapabilities.ActionsFor(FrontButtonDomain.Steam));
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

    [Fact]
    public void Executable_path_editor_keeps_typing_as_a_draft_until_focus_commit()
    {
        var codeBehind = Read("src/SteamInputAddonforClaw.UI/Views/ControllerPage.xaml.cs");
        var xaml = Read("src/SteamInputAddonforClaw.UI/Views/ControllerPage.xaml");

        Assert.Contains("_path.LostFocus += (_, _) => page.CommitExecutablePath(this)", codeBehind, StringComparison.Ordinal);
        Assert.DoesNotContain("_path.TextChanged +=", codeBehind, StringComparison.Ordinal);
        Assert.Contains("_committedExecutablePath", codeBehind, StringComparison.Ordinal);
        Assert.Contains("_committedExecutablePath,", codeBehind, StringComparison.Ordinal);
        Assert.Contains("FrontButtonMappingInfoBar.IsOpen = true", codeBehind, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"FrontButtonMappingInfoBar\"", xaml, StringComparison.Ordinal);
    }
}
