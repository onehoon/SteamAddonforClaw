using System.Xml.Linq;
using SteamInputAddonforClaw.Contracts.Frontend;
using SteamInputAddonforClaw.Views;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class MainUiPowerSourceSettingsTests
{
    [Fact]
    public void Current_source_filter_matches_all_preference_and_source_combinations()
    {
        FrontendTdpPowerSource?[] sources = [FrontendTdpPowerSource.AC, FrontendTdpPowerSource.DC, null];
        foreach (var simplified in new[] { false, true })
        foreach (var currentOnly in new[] { false, true })
        foreach (var source in sources)
        foreach (var rail in new[] { FrontendTdpPowerSource.AC, FrontendTdpPowerSource.DC })
        {
            var expected = !currentOnly || source is null || source == rail;
            Assert.Equal(expected, MainUiPowerSourceVisibility.IsVisible(currentOnly, source, rail));
            Assert.Equal(!simplified, DevicePage.TdpDraftPolicy.ShouldShowPl2(simplified, new(8, 30, 8, 37)));
            Assert.True(DevicePage.TdpDraftPolicy.ShouldShowPl2(simplified, new(8, 32, 8, 40)));
            Assert.True(DevicePage.TdpDraftPolicy.ShouldShowPl2(simplified, null));
        }
    }

    [Fact]
    public void All_three_main_editors_use_the_shared_source_filter_without_changing_feature_state()
    {
        var root = FindRepositoryRoot();
        var device = Read(root, "DevicePage.xaml.cs");
        var steam = Read(root, "ProfilePage.xaml.cs");
        var xbox = Read(root, "XboxPage.xaml.cs");

        Assert.Contains("SetVisibilityIfChanged(TdpAcCard, ac)", device);
        Assert.Contains("SetVisibilityIfChanged(CpuBoostAcCard, ac)", device);
        Assert.Contains("SetVisibilityIfChanged(MaximumProcessorStateAcCard, ac)", device);
        Assert.Contains("SetVisibilityIfChanged(TdpDcCard, dc)", device);
        Assert.Contains("SetVisibilityIfChanged(CpuBoostDcCard, dc)", device);
        Assert.Contains("SetVisibilityIfChanged(MaximumProcessorStateDcCard, dc)", device);
        foreach (var (source, fields) in new[]
        {
            (steam, new[] { "CpuBoost", "Tdp", "PowerMode", "GpuMinimumClock", "FpsLimit" }),
            (xbox, new[] { "CpuBoost", "Tdp", "PowerMode", "GpuMinimumClock", "FpsLimit" }),
        })
        {
            Assert.Contains("MainUiPowerSourceVisibility.IsVisible(_quickSettingsCurrentPowerSourceOnly, _currentPowerSource, FrontendTdpPowerSource.AC)", source);
            Assert.Contains("MainUiPowerSourceVisibility.IsVisible(_quickSettingsCurrentPowerSourceOnly, _currentPowerSource, FrontendTdpPowerSource.DC)", source);
            foreach (var field in fields)
                Assert.Contains($"snapshot?.{field}", source);
        }
        Assert.Contains("bootstrap.CurrentPowerSource", device);
        Assert.Contains("bootstrap.CurrentPowerSource", steam);
        Assert.Contains("bootstrap.CurrentPowerSource", xbox);
    }

    [Theory]
    [InlineData("ProfilePage.xaml", "Steam_TdpCard", "TdpEnabledToggle")]
    [InlineData("XboxPage.xaml", "Xbox_TdpCard", "TdpEnabledToggle")]
    public void Game_profile_tdp_reuses_four_rail_controls_in_compact_card_and_expander(
        string file, string uid, string toggleName)
    {
        var path = Path.Combine(FindRepositoryRoot(), "src", "SteamInputAddonforClaw.UI", "Views", file);
        var document = XDocument.Load(path);
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
        var tdpCard = Assert.Single(document.Descendants(), element =>
            element.Name.LocalName == "SettingsCard" && (string?)element.Attribute(xaml + "Uid") == uid);
        Assert.Equal("Collapsed", (string?)tdpCard.Attribute("Visibility"));
        var expander = Assert.Single(document.Descendants(), element =>
            element.Name.LocalName == "SettingsExpander" && (string?)element.Attribute(xaml + "Name") == "TdpExpander");
        Assert.Equal("Collapsed", (string?)expander.Attribute("Visibility"));
        var toggle = Assert.Single(expander.Descendants(), element =>
            element.Name.LocalName == "ToggleSwitch" && (string?)element.Attribute(xaml + "Name") == toggleName);
        Assert.Equal("Collapsed", (string?)toggle.Attribute("Visibility"));
        var acRail = Assert.Single(document.Descendants(), element =>
            (string?)element.Attribute(xaml + "Name") == "TdpAcCard");
        var dcRail = Assert.Single(document.Descendants(), element =>
            (string?)element.Attribute(xaml + "Name") == "TdpDcCard");
        Assert.Equal("Grid", acRail.Name.LocalName);
        Assert.Equal("Grid", dcRail.Name.LocalName);
        Assert.Equal("AC", (string?)acRail.Descendants().Single(element => (string?)element.Attribute(xaml + "Uid") == uid.Replace("TdpCard", "TdpAcRailLabel", StringComparison.Ordinal)).Attribute("Text"));
        Assert.Equal("DC", (string?)dcRail.Descendants().Single(element => (string?)element.Attribute(xaml + "Uid") == uid.Replace("TdpCard", "TdpDcRailLabel", StringComparison.Ordinal)).Attribute("Text"));
        Assert.Contains(acRail, expander.Descendants());
        Assert.Contains(dcRail, expander.Descendants());
        var sliders = document.Descendants().Where(element => element.Name.LocalName == "Slider"
            && ((string?)element.Attribute(xaml + "Name")) is "AcPl1Slider" or "AcPl2Slider" or "DcPl1Slider" or "DcPl2Slider").ToArray();
        Assert.Equal(4, sliders.Length);
        foreach (var slider in sliders)
        {
            var grid = slider.Ancestors().First(element => element.Name.LocalName == "Grid");
            Assert.Equal("*", (string?)grid.Elements().First(element => element.Name.LocalName == "Grid.ColumnDefinitions")
                .Elements().Last(element => element.Name.LocalName == "ColumnDefinition").Attribute("Width"));
        }

        var code = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "src", "SteamInputAddonforClaw.UI", "Views", file + ".cs"));
        Assert.Contains("SetTdpControlParent(tdpExpanded)", code, StringComparison.Ordinal);
        Assert.Contains("ReferenceEquals(TdpAcExpandedCard.Content, TdpAcCard)", code, StringComparison.Ordinal);
        Assert.Contains("ReferenceEquals(TdpDcExpandedCard.Content, TdpDcCard)", code, StringComparison.Ordinal);
        Assert.Contains("_tdpCompactGrid.Children.Add(TdpAcCard)", code, StringComparison.Ordinal);
        Assert.Contains("_tdpCompactGrid.Children.Add(TdpDcCard)", code, StringComparison.Ordinal);

        foreach (var (rail, labelUid, pl1RowName, pl2RowName, pl1SliderName) in new[]
        {
            (acRail, uid.Replace("TdpCard", "TdpAcRailLabel", StringComparison.Ordinal), "TdpAcPl1Row", "AcPl2Row", "AcPl1Slider"),
            (dcRail, uid.Replace("TdpCard", "TdpDcRailLabel", StringComparison.Ordinal), "TdpDcPl1Row", "DcPl2Row", "DcPl1Slider")
        })
        {
            var columns = Assert.Single(rail.Elements(), element => element.Name.LocalName == "Grid.ColumnDefinitions")
                .Elements().Select(element => (string?)element.Attribute("Width")).ToArray();
            Assert.Equal(new[] { "Auto", "*" }, columns);
            var label = Assert.Single(rail.Descendants(), element => (string?)element.Attribute(xaml + "Uid") == labelUid);
            Assert.Equal("0", (string?)label.Attribute("Grid.Row"));
            Assert.Equal("0", (string?)label.Attribute("Grid.Column"));
            var pl1Row = Assert.Single(rail.Descendants(), element => (string?)element.Attribute(xaml + "Name") == pl1RowName);
            Assert.Equal("0", (string?)pl1Row.Attribute("Grid.Row"));
            Assert.Equal("1", (string?)pl1Row.Attribute("Grid.Column"));
            Assert.Contains(pl1Row.Descendants(), element => (string?)element.Attribute(xaml + "Name") == pl1SliderName);
            var pl2Row = Assert.Single(rail.Descendants(), element => (string?)element.Attribute(xaml + "Name") == pl2RowName);
            Assert.Equal("1", (string?)pl2Row.Attribute("Grid.Row"));
            Assert.Equal("1", (string?)pl2Row.Attribute("Grid.Column"));
        }
    }

    [Theory]
    [InlineData("ProfilePage.xaml", "Steam")]
    [InlineData("XboxPage.xaml", "Xbox")]
    public void Game_profile_cpu_and_power_modes_reuse_combo_boxes_across_compact_and_expanded_hosts(string file, string prefix)
    {
        var root = FindRepositoryRoot();
        var document = XDocument.Load(Path.Combine(root, "src", "SteamInputAddonforClaw.UI", "Views", file));
        var code = Read(root, file + ".cs");
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

        foreach (var feature in new[] { "CpuBoost", "PowerMode" })
        {
            var compact = Assert.Single(document.Descendants(), element => element.Name.LocalName == "SettingsCard"
                && (string?)element.Attribute(xaml + "Name") == feature + "Card");
            Assert.Equal("Collapsed", (string?)compact.Attribute("Visibility"));
            Assert.Single(document.Descendants(), element => element.Name.LocalName == "SettingsExpander"
                && (string?)element.Attribute(xaml + "Name") == feature + "Expander");
            Assert.Contains("ApplyRailComboLayout(" + feature + "Card", code, StringComparison.Ordinal);
            foreach (var rail in new[] { "Ac", "Dc" })
            {
                var comboName = feature + rail + "ComboBox";
                Assert.Single(document.Descendants(), element => element.Name.LocalName == "ComboBox"
                    && (string?)element.Attribute(xaml + "Name") == comboName);
                Assert.Single(document.Descendants(), element => element.Name.LocalName == "SettingsCard"
                    && (string?)element.Attribute(xaml + "Name") == feature + rail + "Card");
            }
        }

        Assert.Contains("ApplyGameProfilePresentationLayout();", code, StringComparison.Ordinal);
        Assert.Contains("var profileReady = _snapshot is not null", code, StringComparison.Ordinal);
        Assert.Contains("MainUiPowerSourceVisibility.IsVisible(_quickSettingsCurrentPowerSourceOnly, _currentPowerSource, FrontendTdpPowerSource.AC)", code, StringComparison.Ordinal);
        Assert.Contains(prefix + "_CpuBoostCard.Header", ReadResources(root, "en-US"), StringComparison.Ordinal);
        Assert.Contains(prefix + "_PowerModeCard.Header", ReadResources(root, "ko-KR"), StringComparison.Ordinal);

        var layout = ExtractMethod(code, "private void ApplyGameProfilePresentationLayout()");
        Assert.True(layout.IndexOf("if (!profileReady)", StringComparison.Ordinal)
            < layout.IndexOf("SetTdpControlParent(tdpExpanded)", StringComparison.Ordinal));
        Assert.Contains("previous?.TdpExpanded != tdpExpanded", layout, StringComparison.Ordinal);
        Assert.Contains("previous is null || previous.Value.ActiveRail != activeRail", layout, StringComparison.Ordinal);
        var beginLoad = ExtractMethod(code, "private void BeginProfileLoad(");
        Assert.Contains("_snapshot = null; ApplyPowerSourceVisibility();", beginLoad, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Steam")]
    [InlineData("Xbox")]
    public void Game_profile_performance_descriptions_are_removed_only_for_the_three_updated_options(string prefix)
    {
        var root = FindRepositoryRoot();
        foreach (var language in new[] { "en-US", "ko-KR" })
        {
            var resources = ReadResources(root, language);
            foreach (var name in new[]
            {
                prefix + "_TdpCard.Description",
                prefix + "_CpuBoostExpander.Description",
                prefix + "_PowerModeExpander.Description"
            })
                Assert.DoesNotContain(name, resources, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Device_tdp_reuses_the_same_rail_controls_in_compact_and_expanded_hosts()
    {
        var root = FindRepositoryRoot();
        var xaml = Read(root, "DevicePage.xaml");
        var code = Read(root, "DevicePage.xaml.cs");
        XNamespace xamlNs = "http://schemas.microsoft.com/winfx/2006/xaml";
        var document = XDocument.Parse(xaml);

        Assert.Contains("x:Name=\"TdpCard\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"TdpExpander\"", xaml, StringComparison.Ordinal);
        Assert.Equal(1, document.Descendants().Count(element => (string?)element.Attribute(xamlNs + "Name") == "TdpAcCard"));
        Assert.Equal(1, document.Descendants().Count(element => (string?)element.Attribute(xamlNs + "Name") == "TdpDcCard"));
        Assert.Contains("ReferenceEquals(TdpAcExpandedCard.Content, TdpAcCard)", code, StringComparison.Ordinal);
        Assert.Contains("ReferenceEquals(TdpDcExpandedCard.Content, TdpDcCard)", code, StringComparison.Ordinal);
        Assert.Contains("_tdpCompactGrid.Children.Add(TdpAcCard)", code, StringComparison.Ordinal);
        Assert.Contains("_tdpCompactGrid.Children.Add(TdpDcCard)", code, StringComparison.Ordinal);
    }

    [Fact]
    public void Device_tdp_rail_label_and_pl1_share_a_row_with_pl2_below()
    {
        var root = FindRepositoryRoot();
        var document = XDocument.Parse(Read(root, "DevicePage.xaml"));
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

        foreach (var (railName, labelUid, pl1Name, pl2Name, pl1SliderName) in new[]
        {
            ("TdpAcCard", "Device_TdpAcRailLabel", "TdpAcPl1Row", "TdpAcPl2Row", "TdpAcPl1Slider"),
            ("TdpDcCard", "Device_TdpDcRailLabel", "TdpDcPl1Row", "TdpDcPl2Row", "TdpDcPl1Slider")
        })
        {
            var rail = Assert.Single(document.Descendants(), element => (string?)element.Attribute(xaml + "Name") == railName);
            Assert.Equal("Grid", rail.Name.LocalName);
            var railColumns = Assert.Single(rail.Elements(), element => element.Name.LocalName == "Grid.ColumnDefinitions")
                .Elements().Select(element => (string?)element.Attribute("Width")).ToArray();
            Assert.Equal(new[] { "Auto", "*" }, railColumns);

            var label = Assert.Single(rail.Descendants(), element => (string?)element.Attribute(xaml + "Uid") == labelUid);
            Assert.Equal("0", (string?)label.Attribute("Grid.Row"));
            Assert.Equal("0", (string?)label.Attribute("Grid.Column"));

            var pl1Row = Assert.Single(rail.Descendants(), element => (string?)element.Attribute(xaml + "Name") == pl1Name);
            Assert.Equal("0", (string?)pl1Row.Attribute("Grid.Row"));
            Assert.Equal("1", (string?)pl1Row.Attribute("Grid.Column"));
            var pl1Slider = Assert.Single(pl1Row.Descendants(), element => (string?)element.Attribute(xaml + "Name") == pl1SliderName);
            var pl1Columns = Assert.Single(pl1Row.Elements(), element => element.Name.LocalName == "Grid.ColumnDefinitions")
                .Elements().Select(element => (string?)element.Attribute("Width")).ToArray();
            Assert.Equal("*", pl1Columns[^1]);
            Assert.Equal("4", (string?)pl1Slider.Attribute("Grid.Column"));

            var pl2Row = Assert.Single(rail.Descendants(), element => (string?)element.Attribute(xaml + "Name") == pl2Name);
            Assert.Equal("1", (string?)pl2Row.Attribute("Grid.Row"));
            Assert.Equal("1", (string?)pl2Row.Attribute("Grid.Column"));
        }
    }

    [Fact]
    public void Device_tdp_containers_stay_hidden_until_the_first_authoritative_snapshot()
    {
        var root = FindRepositoryRoot();
        var document = XDocument.Parse(Read(root, "DevicePage.xaml"));
        var code = Read(root, "DevicePage.xaml.cs");
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

        foreach (var name in new[] { "TdpCard", "TdpExpander" })
        {
            var container = Assert.Single(document.Descendants(), element => (string?)element.Attribute(xaml + "Name") == name);
            Assert.Equal("Collapsed", (string?)container.Attribute("Visibility"));
        }

        var layout = ExtractMethod(code, "private void ApplyDevicePresentationLayout()");
        Assert.Contains("var tdpReady = _hasTdpSnapshot;", layout, StringComparison.Ordinal);
        Assert.Contains("_featureSupport.Tdp && tdpReady && !tdpExpanded", layout, StringComparison.Ordinal);
        Assert.Contains("_featureSupport.Tdp && tdpReady && tdpExpanded", layout, StringComparison.Ordinal);

        var render = ExtractMethod(code, "private void RenderTdp(");
        Assert.True(render.IndexOf("_hasTdpSnapshot = true;", StringComparison.Ordinal)
            < render.IndexOf("ApplyDevicePresentationLayout();", StringComparison.Ordinal));
        Assert.Contains("if (!snapshot.Available)", render, StringComparison.Ordinal);
        Assert.Contains("SetInfoBarOpen(TdpInfoBar, true)", render, StringComparison.Ordinal);
    }

    [Fact]
    public void Main_settings_owns_shared_preferences_and_quick_overlay_keeps_only_its_own_controls()
    {
        var root = FindRepositoryRoot();
        var settingsXaml = Read(root, "SettingsPage.xaml");
        var settingsCode = Read(root, "SettingsPage.xaml.cs");
        var quickOverlayXaml = Read(root, "QuickOverlayPage.xaml");
        var quickOverlayCode = Read(root, "QuickOverlayPage.xaml.cs");

        Assert.Contains("Settings_QuickSettingsPowerSourceOnlyCard", settingsXaml);
        Assert.Contains("Settings_SimplifiedTdpControlsCard", settingsXaml);
        Assert.Contains("SetQuickSettingsCurrentPowerSourceOnlyAsync", settingsCode);
        Assert.Contains("SetSimplifiedTdpControlsAsync", settingsCode);
        Assert.Contains("bootstrap.Settings.QuickSettingsCurrentPowerSourceOnly", settingsCode);
        Assert.Contains("bootstrap.Settings.SimplifiedTdpControls", settingsCode);
        Assert.DoesNotContain("QuickSettingsPowerSourceOnlyToggleSwitch", quickOverlayXaml);
        Assert.DoesNotContain("SimplifiedTdpControlsToggleSwitch", quickOverlayXaml);
        Assert.DoesNotContain("SetQuickSettingsCurrentPowerSourceOnlyAsync", quickOverlayCode);
        Assert.DoesNotContain("SetSimplifiedTdpControlsAsync", quickOverlayCode);
        Assert.Contains("RequestQuickSettingsPreferencesRefresh", settingsCode);
        Assert.Contains("RequestQuickSettingsPreferencesRefresh", File.ReadAllText(Path.Combine(root, "src", "SteamInputAddonforClaw.UI", "MainWindow.xaml.cs")));
        Assert.Contains("SimplifiedTdpControlsRequested", File.ReadAllText(Path.Combine(root, "src", "SteamInputAddonforClaw.Overlay", "OverlayWindow.ClawHud.cs")));
    }

    [Fact]
    public void Main_settings_uses_authoritative_mutation_readback_and_retries_invalidations_after_in_flight_work()
    {
        Assert.True(SettingsPage.ResolvePreference(lastKnownValue: true, refreshedValue: null));
        Assert.False(SettingsPage.ResolvePreference(lastKnownValue: false, refreshedValue: null));
        Assert.True(SettingsPage.ResolvePreference(lastKnownValue: false, refreshedValue: true));
        Assert.False(SettingsPage.ResolvePreference(lastKnownValue: true, refreshedValue: false));

        var root = FindRepositoryRoot();
        var settings = Read(root, "SettingsPage.xaml.cs");
        var requestRefresh = ExtractMethod(settings, "internal void RequestQuickSettingsPreferencesRefresh()");
        var refresh = ExtractMethod(settings, "private async Task RefreshQuickSettingsPreferencesAsync()");
        var drain = ExtractMethod(settings, "private void DrainPendingQuickSettingsPreferencesRefresh()");
        var powerSourceMutation = ExtractMethod(settings, "private async void QuickSettingsPowerSourceOnlyToggleSwitch_Toggled(");
        var simplifiedMutation = ExtractMethod(settings, "private async void SimplifiedTdpControlsToggleSwitch_Toggled(");

        Assert.Contains("_quickSettingsPreferencesRefreshPending = true", requestRefresh, StringComparison.Ordinal);
        Assert.Contains("_powerSourceMutationInProgress || _simplifiedTdpMutationInProgress", requestRefresh, StringComparison.Ordinal);
        Assert.Contains("DrainPendingQuickSettingsPreferencesRefresh()", refresh, StringComparison.Ordinal);
        Assert.Contains("RequestQuickSettingsPreferencesRefresh()", drain, StringComparison.Ordinal);
        Assert.Contains("DrainPendingQuickSettingsPreferencesRefresh()", powerSourceMutation, StringComparison.Ordinal);
        Assert.Contains("DrainPendingQuickSettingsPreferencesRefresh()", simplifiedMutation, StringComparison.Ordinal);
        Assert.Contains("_lastKnownQuickSettingsPowerSourceOnly = settings.QuickSettingsCurrentPowerSourceOnly", settings, StringComparison.Ordinal);
        Assert.Contains("_lastKnownSimplifiedTdpControls = settings.SimplifiedTdpControls", settings, StringComparison.Ordinal);
        Assert.Contains("_applyingQuickSettingsPowerSourceOnly", settings, StringComparison.Ordinal);
        Assert.Contains("_applyingSimplifiedTdpControls", settings, StringComparison.Ordinal);
    }

    [Fact]
    public void Main_page_AC_DC_labels_and_localized_settings_resources_are_present()
    {
        var root = FindRepositoryRoot();
        foreach (var language in new[] { "en-US", "ko-KR" })
        {
            var resources = XDocument.Load(Path.Combine(root, "src", "SteamInputAddonforClaw.UI", "Strings", language, "Resources.resw"));
            var values = resources.Descendants("data").ToDictionary(
                element => (string)element.Attribute("name")!,
                element => element.Element("value")?.Value ?? string.Empty,
                StringComparer.Ordinal);
            Assert.Equal("AC", values["Steam_TdpAcRailLabel.Text"]);
            Assert.Equal("DC", values["Steam_TdpDcRailLabel.Text"]);
            Assert.Equal("AC", values["Xbox_FpsAcCard.Header"]);
            Assert.Equal("DC", values["Xbox_FpsDcCard.Header"]);
            Assert.Contains("Settings_QuickSettingsPowerSourceOnlyCard.Header", values.Keys);
            Assert.Contains("Settings_SimplifiedTdpControlsCard.Header", values.Keys);
            Assert.DoesNotContain(values, item => item.Key.StartsWith("QuickOverlay_PowerSourceCard.", StringComparison.Ordinal));
            Assert.DoesNotContain(values, item => item.Key.StartsWith("QuickOverlay_SimplifiedTdpControlsCard.", StringComparison.Ordinal));
        }

        var presentation = File.ReadAllText(Path.Combine(root, "src", "SteamInputAddonforClaw", "Frontend", "QuickSettingsPresentation.cs"));
        Assert.DoesNotContain("Plugged in", presentation, StringComparison.Ordinal);
        Assert.DoesNotContain("On battery", presentation, StringComparison.Ordinal);
        Assert.Contains("AC · PL1", presentation, StringComparison.Ordinal);
        Assert.Contains("DC · PL1", presentation, StringComparison.Ordinal);
    }

    private static string Read(string root, string file) =>
        File.ReadAllText(Path.Combine(root, "src", "SteamInputAddonforClaw.UI", "Views", file));

    private static string ReadResources(string root, string language) =>
        File.ReadAllText(Path.Combine(root, "src", "SteamInputAddonforClaw.UI", "Strings", language, "Resources.resw"));

    private static string ExtractMethod(string source, string signature)
    {
        var start = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Method not found: {signature}");
        var open = source.IndexOf('{', start);
        Assert.True(open >= 0);
        var depth = 0;
        for (var index = open; index < source.Length; index++)
        {
            if (source[index] == '{') depth++;
            else if (source[index] == '}' && --depth == 0) return source[open..(index + 1)];
        }
        throw new InvalidOperationException($"Method is not closed: {signature}");
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "SteamInputAddonforClaw.slnx"))) return directory.FullName;
        throw new DirectoryNotFoundException("Repository root was not found.");
    }
}
