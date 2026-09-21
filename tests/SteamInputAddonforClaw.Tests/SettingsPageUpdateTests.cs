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
    public void Steam_fse_card_uses_one_silent_guarded_toggle_and_authoritative_refresh()
    {
        var xaml = ReadSource("src", "SteamInputAddonforClaw.UI", "Views", "SettingsPage.xaml");
        var source = ReadSource("src", "SteamInputAddonforClaw.UI", "Views", "SettingsPage.xaml.cs");
        var toggleMethodStart = source.IndexOf("private async void SteamFseToggleSwitch_Toggled", StringComparison.Ordinal);
        var toggleMethodEnd = source.IndexOf("\n    }", toggleMethodStart, StringComparison.Ordinal);
        var toggleMethod = source[toggleMethodStart..toggleMethodEnd];

        Assert.Equal(1, xaml.Split("x:Name=\"SteamFseToggleSwitch\"", StringSplitOptions.None).Length - 1);
        Assert.Contains("Header=\"Steam Big Picture Full Screen Experience\"", xaml, StringComparison.Ordinal);
        Assert.Contains("ToggleSwitch x:Name=\"SteamFseToggleSwitch\"", xaml, StringComparison.Ordinal);
        Assert.Contains("SetSteamFseEnabledAsync", source, StringComparison.Ordinal);
        Assert.Contains("_applyingSteamFseState", source, StringComparison.Ordinal);
        Assert.Contains("_steamFseMutationInProgress", source, StringComparison.Ordinal);
        Assert.Contains("RenderSteamFse(result.Snapshot)", source, StringComparison.Ordinal);
        Assert.Contains("SteamFseToggleSwitch.IsEnabled = false", source, StringComparison.Ordinal);
        Assert.True(toggleMethodStart >= 0 && toggleMethodEnd > toggleMethodStart);
        Assert.DoesNotContain("ContentDialog", toggleMethod, StringComparison.Ordinal);
        Assert.DoesNotContain("SetGamingFullScreenExperience", toggleMethod, StringComparison.Ordinal);
        Assert.DoesNotContain("Restart", toggleMethod, StringComparison.OrdinalIgnoreCase);
    }

    private static string ReadSource(params string[] parts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "SteamInputAddonforClaw.slnx")))
            directory = directory.Parent;
        return File.ReadAllText(Path.Combine([directory!.FullName, .. parts]));
    }
}
