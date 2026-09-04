using SteamInputAddonforClaw.Contracts.FrontButtons;
using SteamInputAddonforClaw.Install;
using SteamInputAddonforClaw.Settings;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

/// <summary>App UI PR-C section 22.4 / 22.5: the atomic front-button mapping persists through the one
/// settings file; a malformed/obsolete value resolves only this feature to the frozen defaults and
/// never resets unrelated settings; the settings coordinator validates before it writes.</summary>
public sealed class FrontButtonMappingPersistenceTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"SteamInputAddonforClaw.FrontButtons.{Guid.NewGuid():N}");
    private string PathName => Path.Combine(_directory, "settings.json");

    private StartupSettingsCoordinator NewCoordinator()
    {
        var store = new SettingsStore(PathName);
        return new StartupSettingsCoordinator(store.Load(), store, new NoOpStartupManager());
    }

    [Fact]
    public void Missing_mapping_uses_the_frozen_defaults()
        => Assert.Equal(FrontButtonMappingSettings.Default, new SettingsStore(PathName).Load().FrontButtonMapping);

    [Fact]
    public void Valid_mapping_round_trips_and_publishes_one_event()
    {
        var coordinator = NewCoordinator();
        var changes = 0;
        coordinator.FrontButtonMappingChanged += (_, _) => changes++;

        var mapping = FrontButtonMappingSettings.Default
            .With(FrontButtonKind.Gamebar, FrontButtonDomain.Normal, FrontButtonBinding.Of(FrontButtonAction.KeyboardHotkey) with
            {
                Hotkey = new FrontButtonHotkeyBinding(FrontButtonHotkeyModifiers.Control, FrontButtonHotkeyKey.R)
            });

        Assert.True(coordinator.ChangeFrontButtonMapping(mapping));
        Assert.Equal(1, changes);
        Assert.Equal(mapping, new SettingsStore(PathName).Load().FrontButtonMapping);
    }

    [Fact]
    public void Invalid_duplicate_candidate_is_rejected_with_no_write_and_no_event()
    {
        var coordinator = NewCoordinator();
        var changes = 0;
        coordinator.FrontButtonMappingChanged += (_, _) => changes++;

        var duplicate = FrontButtonMappingSettings.Default.With(
            FrontButtonKind.CenterM, FrontButtonDomain.Normal, FrontButtonBinding.Of(FrontButtonAction.QuickSettingsOverlay));

        Assert.False(coordinator.ChangeFrontButtonMapping(duplicate));
        Assert.Equal(0, changes);
        Assert.Equal(FrontButtonMappingSettings.Default, coordinator.FrontButtonMapping);
        Assert.False(File.Exists(PathName));
    }

    [Fact]
    public void Domain_invalid_candidate_is_rejected()
    {
        var coordinator = NewCoordinator();
        var invalid = FrontButtonMappingSettings.Default.With(
            FrontButtonKind.CenterM, FrontButtonDomain.Normal, FrontButtonBinding.Of(FrontButtonAction.SteamButton));

        Assert.False(coordinator.ChangeFrontButtonMapping(invalid));
        Assert.Equal(FrontButtonMappingSettings.Default, coordinator.FrontButtonMapping);
    }

    [Fact]
    public void A_later_log_level_change_does_not_erase_the_mapping()
    {
        var coordinator = NewCoordinator();
        var mapping = FrontButtonMappingSettings.Default.With(
            FrontButtonKind.Gamebar, FrontButtonDomain.Steam, FrontButtonBinding.Of(FrontButtonAction.QuickSettingsOverlay));
        coordinator.ChangeFrontButtonMapping(mapping);
        coordinator.ChangeLogLevel(AppLogPreference.Debug);

        Assert.Equal(mapping, new SettingsStore(PathName).Load().FrontButtonMapping);
    }

    [Theory]
    [InlineData("{\"FrontButtonMapping\":{\"Normal\":{\"Gamebar\":{\"Action\":\"SteamButton\"},\"CenterM\":{\"Action\":\"SteamBigPicture\"}},\"Steam\":{\"Gamebar\":{\"Action\":\"SteamButton\"},\"CenterM\":{\"Action\":\"SteamQuickAccess\"}}},\"LogLevel\":\"Debug\"}")] // Normal.Gamebar domain-invalid
    [InlineData("{\"FrontButtonMapping\":{\"Normal\":{\"Gamebar\":{\"Action\":\"Nonsense\"},\"CenterM\":{\"Action\":\"SteamBigPicture\"}},\"Steam\":{\"Gamebar\":{\"Action\":\"SteamButton\"},\"CenterM\":{\"Action\":\"SteamQuickAccess\"}}},\"LogLevel\":\"Debug\"}")] // unknown action
    [InlineData("{\"FrontButtonMapping\":{\"Normal\":{\"Gamebar\":{\"Action\":\"QuickSettingsOverlay\"},\"CenterM\":{\"Action\":\"QuickSettingsOverlay\"}},\"Steam\":{\"Gamebar\":{\"Action\":\"SteamButton\"},\"CenterM\":{\"Action\":\"SteamQuickAccess\"}}},\"LogLevel\":\"Debug\"}")] // duplicate Normal pair
    [InlineData("{\"FrontButtonMapping\":\"garbage\",\"LogLevel\":\"Debug\"}")] // not even an object
    public void A_malformed_mapping_resolves_only_this_feature_to_defaults(string json)
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(PathName, json);

        var loaded = new SettingsStore(PathName).Load();

        Assert.Equal(FrontButtonMappingSettings.Default, loaded.FrontButtonMapping);
        Assert.Equal(AppLogPreference.Debug, loaded.LogLevel); // unrelated setting preserved
    }

    [Fact]
    public void Old_split_oem1_wing_json_is_ignored_and_dropped_on_the_next_save()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(PathName,
            "{\"Oem1Mapping\":{\"RemappingEnabled\":true,\"NormalSingle\":{\"Action\":\"SteamBigPicture\"}},\"WingMapping\":{\"Single\":{\"Action\":\"SteamButton\"}},\"LogLevel\":\"Info\"}");

        var coordinator = NewCoordinator();
        Assert.Equal(FrontButtonMappingSettings.Default, coordinator.FrontButtonMapping);
        Assert.Equal(AppLogPreference.Info, coordinator.Settings.LogLevel);

        coordinator.ChangeLogLevel(AppLogPreference.Debug);
        var text = File.ReadAllText(PathName);
        Assert.DoesNotContain("Oem1Mapping", text);
        Assert.DoesNotContain("WingMapping", text);
        Assert.Contains("FrontButtonMapping", text);
    }

    public void Dispose() { if (Directory.Exists(_directory)) Directory.Delete(_directory, true); }

    private sealed class NoOpStartupManager : IWindowsStartupManager
    {
        public StartupRegistrationResult Synchronize(bool enabled) => new(true, "No-op");
    }
}
