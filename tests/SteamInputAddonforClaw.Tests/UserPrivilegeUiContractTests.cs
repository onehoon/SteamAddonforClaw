using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class UserPrivilegeUiContractTests
{
    [Fact]
    public void Shortcut_admin_checkboxes_are_confined_to_exe_and_powershell_and_round_trip_through_inputs()
    {
        var source = Read("src/SteamInputAddonforClaw.UI/Views/ShortcutPage.xaml.cs");
        var executablePanel = Slice(source, "var executableRunAsAdministrator", "var script = new TextBox");
        var powerShellPanel = Slice(source, "var powerShellRunAsAdministrator", "var url = new TextBox");
        var otherPanels = source[source.IndexOf("var url = new TextBox", StringComparison.Ordinal)..];

        Assert.Contains("Content = \"Run as administrator\"", executablePanel, StringComparison.Ordinal);
        Assert.Contains("Content = \"Run as administrator\"", powerShellPanel, StringComparison.Ordinal);
        Assert.Contains("existing.Action.RunAsAdministrator", executablePanel, StringComparison.Ordinal);
        Assert.Contains("existing.Action.RunAsAdministrator", powerShellPanel, StringComparison.Ordinal);
        Assert.Contains("RunAsAdministrator: executableRunAsAdministrator.IsChecked == true", source, StringComparison.Ordinal);
        Assert.Contains("RunAsAdministrator: powerShellRunAsAdministrator.IsChecked == true", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Run as administrator", otherPanels, StringComparison.Ordinal);
        Assert.Equal(2, source.Split("Content = \"Run as administrator\"", StringSplitOptions.None).Length - 1);
    }

    [Fact]
    public void Front_button_admin_checkbox_belongs_only_to_launch_application_editor_and_uses_existing_capture_flow()
    {
        var source = Read("src/SteamInputAddonforClaw.UI/Views/ControllerPage.xaml.cs");
        var launchPanel = Slice(source, "_launchPanel = new StackPanel", "configPanel.Children.Add(_hotkeyPanel)");

        Assert.Contains("_runAsAdministrator", launchPanel, StringComparison.Ordinal);
        Assert.Contains("binding.Launch.RunAsAdministrator", source, StringComparison.Ordinal);
        Assert.Contains("_runAsAdministrator.IsChecked == true", source, StringComparison.Ordinal);
        Assert.Contains("_runAsAdministrator.Checked +=", source, StringComparison.Ordinal);
        Assert.Contains("_runAsAdministrator.Unchecked +=", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Run as administrator", Slice(source, "_hotkeyPanel = new StackPanel", "var browse = new Button"), StringComparison.Ordinal);
    }

    private static string Read(string relative)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "SteamInputAddonforClaw.slnx")))
            directory = directory.Parent;
        return File.ReadAllText(Path.Combine(directory!.FullName, relative));
    }

    private static string Slice(string source, string start, string end)
    {
        var startIndex = source.IndexOf(start, StringComparison.Ordinal);
        var endIndex = source.IndexOf(end, startIndex + start.Length, StringComparison.Ordinal);
        Assert.True(startIndex >= 0 && endIndex > startIndex, $"Could not locate source span {start} .. {end}.");
        return source[startIndex..endIndex];
    }
}
