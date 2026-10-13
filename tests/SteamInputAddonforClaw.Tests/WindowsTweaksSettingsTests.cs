using SteamInputAddonforClaw.Settings;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class WindowsTweaksSettingsTests
{
    [Fact]
    public void Warning_acknowledgement_date_round_trips_through_existing_settings_store()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"WindowsTweaksSettings-{Guid.NewGuid():N}");
        var path = Path.Combine(directory, "settings.json");
        var date = new DateOnly(2026, 10, 13);
        var store = new SettingsStore(path);

        store.Save(new AppSettings { WindowsTweaksWarningAcknowledgedLocalDate = date });

        Assert.Equal(date, store.Load().WindowsTweaksWarningAcknowledgedLocalDate);
        Directory.Delete(directory, recursive: true);
    }

    [Theory]
    [InlineData("not-a-date")]
    [InlineData("2026-13-40")]
    public void Malformed_warning_date_is_ignored_without_losing_other_settings(string date)
    {
        var directory = Path.Combine(Path.GetTempPath(), $"WindowsTweaksSettings-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "settings.json");
        File.WriteAllText(path, $"{{\"LogLevel\":\"Debug\",\"WindowsTweaksWarningAcknowledgedLocalDate\":\"{date}\"}}");

        var loaded = new SettingsStore(path).Load();

        Assert.Null(loaded.WindowsTweaksWarningAcknowledgedLocalDate);
        Assert.Equal(AppLogPreference.Debug, loaded.LogLevel);
        Directory.Delete(directory, recursive: true);
    }
}
