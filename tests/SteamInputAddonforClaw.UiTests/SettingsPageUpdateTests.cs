using Xunit;
using SteamInputAddonforClaw.Views;

namespace SteamInputAddonforClaw.Tests;

public sealed class SettingsPageUpdateTests
{
    [Fact]
    public void Quick_settings_toggle_failure_restores_last_known_value_when_refresh_also_fails()
    {
        var lastKnownValue = false;
        bool? refreshedValue = null;

        var restoredValue = SettingsPage.ResolveQuickSettingsPowerSourcePreference(lastKnownValue, refreshedValue);

        Assert.False(restoredValue);
    }

    [Fact]
    public void Quick_settings_toggle_failure_uses_successful_bootstrap_refresh_over_last_known_value()
    {
        var lastKnownValue = false;
        bool? refreshedValue = true;

        var restoredValue = SettingsPage.ResolveQuickSettingsPowerSourcePreference(lastKnownValue, refreshedValue);

        Assert.True(restoredValue);
    }

    [Fact]
    public void Update_action_refreshes_after_releasing_the_operation_guard()
    {
        var source = ReadSource("src", "SteamInputAddonforClaw.UI", "Views", "SettingsPage.xaml.cs");
        var methodStart = source.IndexOf("private async void UpdateButton_Click", StringComparison.Ordinal);
        var methodEnd = source.IndexOf("\n    }", methodStart, StringComparison.Ordinal);
        var method = source[methodStart..methodEnd];
        var release = method.IndexOf("Volatile.Write(ref _updateOperationInProgress, 0)", StringComparison.Ordinal);
        var refresh = method.IndexOf("await RefreshAppUpdateAsync().ConfigureAwait(true)", StringComparison.Ordinal);

        Assert.True(methodStart >= 0);
        Assert.True(release >= 0 && refresh > release);
    }

    [Fact]
    public void Gaming_home_expander_uses_separate_guarded_selection_and_startup_mutations()
    {
        var xaml = ReadSource("src", "SteamInputAddonforClaw.UI", "Views", "SettingsPage.xaml");
        var source = ReadSource("src", "SteamInputAddonforClaw.UI", "Views", "SettingsPage.xaml.cs");
        var selectionHandlerStart = source.IndexOf("private async void GamingHomeComboBox_SelectionChanged", StringComparison.Ordinal);
        var startupHandlerStart = source.IndexOf("private async void GamingHomeStartupToggleSwitch_Toggled", StringComparison.Ordinal);
        var mutationHelperStart = source.IndexOf("private async Task RunGamingHomeMutationAsync", StringComparison.Ordinal);
        var openLogFolderStart = source.IndexOf("private void OpenLogFolderButton_Click", StringComparison.Ordinal);
        var openLogFolderEnd = source.IndexOf("\n    }", openLogFolderStart, StringComparison.Ordinal);
        Assert.Contains("<ctcontrols:SettingsExpander", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"GamingHomeExpander\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Header=\"Windows Gaming Full Screen Experience\"", xaml, StringComparison.Ordinal);
        Assert.Contains("IsExpanded=\"False\"", xaml, StringComparison.Ordinal);
        Assert.Contains("ComboBox x:Name=\"GamingHomeComboBox\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Tag=\"None\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Tag=\"Xbox\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Tag=\"SteamBigPicture\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Tag=\"Other\"", xaml, StringComparison.Ordinal);
        Assert.Contains("GamingHomeOtherSelectionItem.IsEnabled = _gamingHomeSnapshot.Selection == FrontendGamingHomeSelection.Other", source, StringComparison.Ordinal);
        Assert.Contains("ToggleSwitch x:Name=\"GamingHomeStartupToggleSwitch\"", xaml, StringComparison.Ordinal);
        Assert.Contains("SetGamingHomeSelectionAsync", source, StringComparison.Ordinal);
        Assert.Contains("SetGamingHomeStartupAsync", source, StringComparison.Ordinal);
        Assert.Contains("_applyingGamingHomeState", source, StringComparison.Ordinal);
        Assert.Contains("_gamingHomeMutationInProgress", source, StringComparison.Ordinal);
        Assert.Contains("RenderGamingHome(result.Snapshot)", source, StringComparison.Ordinal);
        Assert.Contains("GamingHomeComboBox.SelectedItem = snapshot.Available", source, StringComparison.Ordinal);
        Assert.Contains("GamingHomeStartupToggleSwitch.IsOn = snapshot.StartupEnabled", source, StringComparison.Ordinal);
        Assert.Contains("GamingHomeStartupToggleSwitch.IsEnabled = IsStartupWritable", source, StringComparison.Ordinal);
        Assert.Contains("Selection is FrontendGamingHomeSelection.Xbox or FrontendGamingHomeSelection.SteamBigPicture", source, StringComparison.Ordinal);
        Assert.Contains("snapshot.Selection == FrontendGamingHomeSelection.Other", source, StringComparison.Ordinal);
        Assert.True(selectionHandlerStart >= 0 && selectionHandlerStart < startupHandlerStart);
        Assert.True(startupHandlerStart >= 0 && startupHandlerStart < mutationHelperStart);
        Assert.Contains("_applyingGamingHomeState", source[selectionHandlerStart..startupHandlerStart], StringComparison.Ordinal);
        Assert.Contains("_applyingGamingHomeState", source[startupHandlerStart..mutationHelperStart], StringComparison.Ordinal);
        Assert.Contains("Interlocked.Exchange(ref _gamingHomeMutationInProgress, 1)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("SteamFseToggleSwitch", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("ContentDialog", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ms-settings:", source, StringComparison.OrdinalIgnoreCase);
        Assert.True(openLogFolderStart >= 0 && openLogFolderEnd > openLogFolderStart);
        var openLogFolderHandler = source[openLogFolderStart..openLogFolderEnd];
        Assert.Contains("Process.Start", openLogFolderHandler, StringComparison.Ordinal);
        Assert.DoesNotContain("Process.Start", source.Remove(openLogFolderStart, openLogFolderHandler.Length), StringComparison.Ordinal);
        Assert.DoesNotContain("Restart", source, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Settings_no_longer_exposes_the_app_owned_enter_bios_action()
    {
        var xaml = ReadSource("src", "SteamInputAddonforClaw.UI", "Views", "SettingsPage.xaml");
        var code = ReadSource("src", "SteamInputAddonforClaw.UI", "Views", "SettingsPage.xaml.cs");

        Assert.DoesNotContain("EnterBios", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Enter BIOS", xaml, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("RequestEnterBios", code, StringComparison.Ordinal);
    }

    private static string ReadSource(params string[] parts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "SteamInputAddonforClaw.slnx")))
            directory = directory.Parent;
        return File.ReadAllText(Path.Combine([directory!.FullName, .. parts]));
    }
}
