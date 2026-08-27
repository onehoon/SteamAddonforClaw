using System.Xml.Linq;
using Microsoft.UI.Xaml;
using SteamInputAddonforClaw.Views;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class UiArchitectureTests
{
    [Fact]
    public void Project_references_preserve_the_headless_dependency_direction()
    {
        var ui = References("src/SteamInputAddonforClaw.UI/SteamInputAddonforClaw.UI.csproj");
        var runtime = References("src/SteamInputAddonforClaw/SteamInputAddonforClaw.csproj");
        var transport = References("src/SteamInputAddonforClaw.FrontendTransport/SteamInputAddonforClaw.FrontendTransport.csproj");

        Assert.Contains("SteamInputAddonforClaw.Contracts.csproj", ui);
        Assert.Contains("SteamInputAddonforClaw.FrontendTransport.csproj", ui);
        Assert.Contains("SteamInputAddonforClaw.FrontendTransport.csproj", runtime);
        Assert.Contains("SteamInputAddonforClaw.Contracts.csproj", transport);
        Assert.DoesNotContain("SteamInputAddonforClaw.csproj", ui);
        Assert.DoesNotContain("SteamInputAddonforClaw.UI.csproj", runtime);
        Assert.DoesNotContain("SteamInputAddonforClaw.csproj", transport);
    }

    [Fact]
    public void Frontend_sources_have_one_physical_owner()
    {
        var root = FindRepositoryRoot();
        Assert.True(File.Exists(Path.Combine(root, "src/SteamInputAddonforClaw.UI/MainWindow.xaml")));
        Assert.True(File.Exists(Path.Combine(root, "src/SteamInputAddonforClaw.UI/MainWindow.xaml.cs")));
        Assert.False(File.Exists(Path.Combine(root, "src/SteamInputAddonforClaw/MainWindow.xaml")));
        Assert.False(File.Exists(Path.Combine(root, "src/SteamInputAddonforClaw/MainWindow.xaml.cs")));
        // The Claw Sensor Probe UI was restored (work order "restore-claw-sensor-probe-diagnostic")
        // as a proper frontend-boundary page: the WinUI page lives ONLY in the UI project, and the
        // Runtime-owned coordinator it talks to over IAddonFrontendControl lives ONLY in Runtime --
        // never the old pre-PR213 shape where the page held the coordinator directly in-process.
        Assert.False(File.Exists(Path.Combine(root, "src/SteamInputAddonforClaw/Views/ClawSensorProbePage.xaml")));
        Assert.False(File.Exists(Path.Combine(root, "src/SteamInputAddonforClaw/Views/ClawSensorProbePage.xaml.cs")));
        Assert.True(File.Exists(Path.Combine(root, "src/SteamInputAddonforClaw.UI/Views/ClawSensorProbePage.xaml")));
        Assert.True(File.Exists(Path.Combine(root, "src/SteamInputAddonforClaw.UI/Views/ClawSensorProbePage.xaml.cs")));
        Assert.True(Directory.Exists(Path.Combine(root, "src/SteamInputAddonforClaw/Diagnostics/ClawSensorProbe")));
    }

    [Fact]
    public void Runtime_is_true_headless_and_ui_keeps_winui_ownership()
    {
        var root = FindRepositoryRoot();
        var runtimeProject = File.ReadAllText(Path.Combine(root, "src/SteamInputAddonforClaw/SteamInputAddonforClaw.csproj"));
        var uiProject = File.ReadAllText(Path.Combine(root, "src/SteamInputAddonforClaw.UI/SteamInputAddonforClaw.UI.csproj"));
        Assert.DoesNotContain("<UseWinUI>true</UseWinUI>", runtimeProject, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Microsoft.WindowsAppSDK", runtimeProject, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("CommunityToolkit.WinUI", runtimeProject, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("<UseWinUI>true</UseWinUI>", uiProject, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<Page Include=\"MainWindow.xaml\"", runtimeProject, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<PackageReference Include=\"Microsoft.WindowsAppSDK\"", runtimeProject, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void External_ui_app_registers_required_winui_control_resources()
    {
        var root = FindRepositoryRoot();
        var appXaml = File.ReadAllText(Path.Combine(root, "src/SteamInputAddonforClaw.UI/App.xaml"));

        Assert.Contains("<Application.Resources>", appXaml, StringComparison.Ordinal);
        Assert.Contains("XamlControlsResources", appXaml, StringComparison.Ordinal);
    }

    [Fact]
    public void Vibration_test_state_invalidations_are_marshaled_to_the_ui_queue()
    {
        var root = FindRepositoryRoot();
        var page = File.ReadAllText(Path.Combine(root, "src/SteamInputAddonforClaw.UI/Views/VibrationTestPage.xaml.cs"));

        Assert.Contains("DispatcherQueue.TryEnqueue", page, StringComparison.Ordinal);
        Assert.Contains("if (_active)", page, StringComparison.Ordinal);
    }

    [Fact]
    public void Tdp_toggle_disables_editors_while_the_first_enable_is_in_flight()
    {
        var root = FindRepositoryRoot();
        var page = File.ReadAllText(Path.Combine(root, "src/SteamInputAddonforClaw.UI/Views/DevicePage.xaml.cs"));

        Assert.Contains("SetTdpMutationBusy(true)", page, StringComparison.Ordinal);
        Assert.Contains("finally { SetTdpMutationBusy(false); }", page, StringComparison.Ordinal);
        Assert.Contains("if (_tdpSnapshot.Configuration?.Enabled == true) ScheduleTdpEdit(isAc);", page, StringComparison.Ordinal);
    }

    [Fact]
    public void Power_mode_ui_failures_clear_stale_state()
    {
        var root = FindRepositoryRoot();
        var devicePage = File.ReadAllText(Path.Combine(root, "src/SteamInputAddonforClaw.UI/Views/DevicePage.xaml.cs"));
        var profilePage = File.ReadAllText(Path.Combine(root, "src/SteamInputAddonforClaw.UI/Views/ProfilePage.xaml.cs"));

        Assert.Contains("RenderPowerMode(FrontendPowerModeSnapshot.Unavailable)", devicePage, StringComparison.Ordinal);
        Assert.Contains("PowerModeInfoBar.Message = \"Power Mode settings could not be loaded.\"", devicePage, StringComparison.Ordinal);
        Assert.Contains("PowerModeAcComboBox.SelectedItem = null", profilePage, StringComparison.Ordinal);
        Assert.Contains("PowerModeDcComboBox.SelectedItem = null", profilePage, StringComparison.Ordinal);
    }

    [Fact]
    public void Device_feature_expanders_follow_authoritative_enabled_snapshots()
    {
        var root = FindRepositoryRoot();
        var xaml = File.ReadAllText(Path.Combine(root, "src/SteamInputAddonforClaw.UI/Views/DevicePage.xaml"));
        var codeBehind = File.ReadAllText(Path.Combine(root, "src/SteamInputAddonforClaw.UI/Views/DevicePage.xaml.cs"));

        Assert.DoesNotContain("IsExpanded=\"True\"", xaml, StringComparison.Ordinal);
        Assert.Contains("CpuBoostExpander.IsExpanded = snapshot.Enabled", codeBehind, StringComparison.Ordinal);
        Assert.Contains("PowerModeExpander.IsExpanded = snapshot.Enabled", codeBehind, StringComparison.Ordinal);
        Assert.Contains("TdpExpander.IsExpanded = snapshot.Configuration?.Enabled == true", codeBehind, StringComparison.Ordinal);
    }

    [Fact]
    public void Profile_feature_order_expander_state_and_resolution_contract_are_explicit()
    {
        var root = FindRepositoryRoot();
        var xaml = File.ReadAllText(Path.Combine(root, "src/SteamInputAddonforClaw.UI/Views/ProfilePage.xaml"));
        var codeBehind = File.ReadAllText(Path.Combine(root, "src/SteamInputAddonforClaw.UI/Views/ProfilePage.xaml.cs"));

        Assert.True(xaml.IndexOf("Header=\"TDP Control\"", StringComparison.Ordinal) < xaml.IndexOf("Header=\"Intel FPS Limit\"", StringComparison.Ordinal));
        Assert.True(xaml.IndexOf("Header=\"Intel FPS Limit\"", StringComparison.Ordinal) < xaml.IndexOf("Header=\"CPU Boost\"", StringComparison.Ordinal));
        Assert.True(xaml.IndexOf("Header=\"CPU Boost\"", StringComparison.Ordinal) < xaml.IndexOf("Header=\"Windows Power Mode\"", StringComparison.Ordinal));
        Assert.True(xaml.IndexOf("Header=\"Windows Power Mode\"", StringComparison.Ordinal) < xaml.IndexOf("Header=\"Resolution\"", StringComparison.Ordinal));
        Assert.DoesNotContain("x:Name=\"DisplayExpander\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("IsExpanded=\"True\"", xaml, StringComparison.Ordinal);
        Assert.Contains("<ctcontrols:SettingsCard Header=\"Resolution\">", xaml, StringComparison.Ordinal);
        Assert.Contains("IntelFpsExpander.IsExpanded = snapshot.FpsLimit?.Enabled == true", codeBehind, StringComparison.Ordinal);
        Assert.Contains("PowerModeExpander.IsExpanded = snapshot.PowerMode?.Enabled == true", codeBehind, StringComparison.Ordinal);
        Assert.Contains("CpuBoostExpander.IsExpanded = snapshot.CpuBoost.Enabled", codeBehind, StringComparison.Ordinal);
        Assert.Contains("TdpExpander.IsExpanded = snapshot.Tdp.Enabled", codeBehind, StringComparison.Ordinal);
        Assert.Contains("new(null, null, \"Do not change\"), new(1920, 1200, \"1920 × 1200\"), new(1920, 1080, \"1920 × 1080\"), new(1680, 1050, \"1680 × 1050\"), new(1440, 900, \"1440 × 900\")", codeBehind, StringComparison.Ordinal);
        Assert.Contains("HeaderIcon", xaml, StringComparison.Ordinal);
        Assert.Contains("Glyph=\"&#xEC4A;\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Glyph=\"&#xE83F;\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Glyph=\"&#xE7F4;\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("<TextBlock Text=\"Profile\" VerticalAlignment=\"Center\"/>", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ProfileEnabledToggle\" Grid.Column=\"1\" HorizontalAlignment=\"Right\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"DetailPanel\" Visibility=\"Collapsed\" HorizontalContentAlignment=\"Stretch\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("OnContent=\"On\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("OffContent=\"Off\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void Profile_power_mode_enable_mutation_preserves_apply_failure_contract()
    {
        var root = FindRepositoryRoot();
        var control = File.ReadAllText(Path.Combine(root, "src/SteamInputAddonforClaw/Frontend/InProcessAddonFrontendControl.cs"));
        var start = control.IndexOf("SetGameProfilePowerModeEnabledAsync", StringComparison.Ordinal);
        var end = control.IndexOf("SetGameProfileCpuBoostAcAsync", start, StringComparison.Ordinal);
        var method = control[start..end];

        Assert.Contains("ReconcileWithResult", method, StringComparison.Ordinal);
        Assert.Contains("FrontendGameProfileMutationOutcome.ApplyFailed", method, StringComparison.Ordinal);
        Assert.Contains("Power Mode apply failed.", method, StringComparison.Ordinal);
    }

    [Fact]
    public void Developer_cards_have_unique_gyro_icon_and_requested_order()
    {
        var root = FindRepositoryRoot();
        var page = File.ReadAllText(Path.Combine(root, "src/SteamInputAddonforClaw.UI/Views/DeveloperPage.xaml"));

        Assert.Equal(1, page.Split("Symbol=\"Rotate\"", StringSplitOptions.None).Length - 1);
        Assert.True(page.IndexOf("Header=\"Test Mode\"", StringComparison.Ordinal) < page.IndexOf("Text=\"Environment Discovery\"", StringComparison.Ordinal));
        Assert.True(page.IndexOf("Text=\"Environment Discovery\"", StringComparison.Ordinal) < page.IndexOf("Header=\"Vibration Test\"", StringComparison.Ordinal));
        Assert.True(page.IndexOf("Header=\"Vibration Test\"", StringComparison.Ordinal) < page.IndexOf("Header=\"Gyro / Sensor Test\"", StringComparison.Ordinal));
        Assert.True(page.IndexOf("Header=\"Gyro / Sensor Test\"", StringComparison.Ordinal) < page.IndexOf("Header=\"Logging\"", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(false, Visibility.Collapsed)]
    [InlineData(true, Visibility.Visible)]
    public void Settings_developer_menu_card_visibility_follows_bootstrap_flag(bool enabled, Visibility expected)
    {
        Assert.Equal(expected, SettingsPage.GetDeveloperMenuCardVisibility(enabled));
    }

    private static IReadOnlyList<string> References(string relativeProjectPath)
    {
        var projectPath = Path.Combine(FindRepositoryRoot(), relativeProjectPath.Replace('/', Path.DirectorySeparatorChar));
        return XDocument.Load(projectPath)
            .Descendants("ProjectReference")
            .Select(element => Path.GetFileName((string?)element.Attribute("Include") ?? string.Empty))
            .ToArray();
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "SteamInputAddonforClaw.slnx")))
                return directory.FullName;
        }
        throw new DirectoryNotFoundException("Repository root was not found.");
    }
}
