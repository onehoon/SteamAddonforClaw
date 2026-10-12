using SteamInputAddonforClaw.Contracts.Frontend;
using System.Xml.Linq;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class MainWindowTests
{
    [Fact]
    public void FormatWindowTitle_DefaultPreservesTheOriginalTitle()
    {
        Assert.Equal("Steam Addon for Claw v0.1.20", MainWindow.FormatWindowTitle("0.1.20"));
    }

    [Theory]
    [InlineData(false, "Steam Addon for Claw v0.1.20")]
    [InlineData(true, "Steam Addon for Claw v0.1.20 — Update Available")]
    public void FormatWindowTitle_PreservesTheVersionPrefixAndOptionallyShowsUpdateReady(bool updateReady, string expected)
    {
        Assert.Equal(expected, MainWindow.FormatWindowTitle("0.1.20", updateReady));
    }

    [Fact]
    public void Update_ready_indicator_is_driven_only_by_the_ready_to_install_snapshot()
    {
        foreach (var state in Enum.GetValues<FrontendUpdateState>())
        {
            var snapshot = new FrontendUpdateSnapshot(state, "status");
            Assert.Equal(state == FrontendUpdateState.ReadyToInstall, snapshot.CanInstall);
        }

        var source = File.ReadAllText(Source("src", "SteamInputAddonforClaw.UI", "MainWindow.xaml.cs"));
        var handlerStart = source.IndexOf("private void OnAppUpdateSnapshotRendered", StringComparison.Ordinal);
        var handlerEnd = source.IndexOf("private void ApplyUpdateReadySettingsBadge", handlerStart, StringComparison.Ordinal);
        Assert.True(handlerStart >= 0 && handlerEnd > handlerStart);
        Assert.Contains("_isUpdateReady = snapshot.CanInstall;", source[handlerStart..handlerEnd], StringComparison.Ordinal);
    }

    [Fact]
    public void Update_indicators_use_the_existing_settings_snapshot_and_built_in_navigation_item()
    {
        var code = File.ReadAllText(Source("src", "SteamInputAddonforClaw.UI", "MainWindow.xaml.cs"));
        var xamlPath = Source("src", "SteamInputAddonforClaw.UI", "MainWindow.xaml");
        var xamlText = File.ReadAllText(xamlPath);
        var xaml = XDocument.Load(xamlPath);

        var subscribe = code.IndexOf("SettingsContent.AppUpdateSnapshotRendered += OnAppUpdateSnapshotRendered", StringComparison.Ordinal);
        var initialize = code.IndexOf("SettingsContent.Initialize(_bootstrap, _frontend, _mainUiThemePreference)", StringComparison.Ordinal);
        Assert.True(subscribe >= 0 && initialize > subscribe);

        var loadedStart = code.IndexOf("private void MainNavigationView_Loaded", StringComparison.Ordinal);
        var loadedEnd = code.IndexOf("private void SetEnglishSettingsItemContent", loadedStart, StringComparison.Ordinal);
        Assert.True(loadedStart >= 0 && loadedEnd > loadedStart);
        Assert.Contains("ApplyUpdateReadySettingsBadge();", code[loadedStart..loadedEnd], StringComparison.Ordinal);
        Assert.Contains("Loaded=\"MainNavigationView_Loaded\"", xamlText, StringComparison.Ordinal);

        var badgeStart = code.IndexOf("private void ApplyUpdateReadySettingsBadge", StringComparison.Ordinal);
        var badgeEnd = code.IndexOf("private void SetEnglishSettingsItemContent", badgeStart, StringComparison.Ordinal);
        Assert.True(badgeStart >= 0 && badgeEnd > badgeStart);
        var badgeMethod = code[badgeStart..badgeEnd];
        Assert.Contains("MainNavigationView.SettingsItem is NavigationViewItem settingsItem", badgeMethod, StringComparison.Ordinal);
        Assert.Contains("settingsItem.InfoBadge = _isUpdateReady ? _updateReadyDotBadge : null", badgeMethod, StringComparison.Ordinal);

        var menuItems = xaml.Descendants().Single(element => element.Name.LocalName == "NavigationView.MenuItems");
        Assert.DoesNotContain(menuItems.Elements().Where(element => element.Name.LocalName == "NavigationViewItem"), item =>
            string.Equals((string?)item.Attribute("Content"), "Settings", StringComparison.Ordinal));
        Assert.DoesNotContain("CaptureAppUpdateAsync", code, StringComparison.Ordinal);
        Assert.Contains("SettingsContent.RequestAppUpdateRefresh();", code, StringComparison.Ordinal);
    }

    private static string Source(params string[] parts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "SteamInputAddonforClaw.slnx")))
            directory = directory.Parent;

        Assert.NotNull(directory);
        return Path.Combine([directory!.FullName, .. parts]);
    }
}
