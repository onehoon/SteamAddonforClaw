using SteamInputAddonforClaw.Contracts.BackButtons;
using SteamInputAddonforClaw.Install;
using SteamInputAddonforClaw.Settings;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class BackButtonMappingPersistenceTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"SteamInputAddonforClaw.BackButtons.{Guid.NewGuid():N}");
    private string SettingsPath => Path.Combine(_directory, "settings.json");

    [Fact]
    public void Defaults_are_disabled_and_duplicate_targets_are_valid()
    {
        Assert.Equal(Xbox360BackButtonTarget.Disabled, BackButtonMappingSettings.Default.M1);
        Assert.Equal(Xbox360BackButtonTarget.Disabled, BackButtonMappingSettings.Default.M2);
        Assert.Null(BackButtonMappingValidation.Validate(new(Xbox360BackButtonTarget.A, Xbox360BackButtonTarget.A)));
    }

    [Fact]
    public void Validation_rejects_missing_and_undefined_targets()
    {
        Assert.Equal("MappingMissing", BackButtonMappingValidation.Validate(null));
        Assert.Equal("InvalidM1Target", BackButtonMappingValidation.Validate(new((Xbox360BackButtonTarget)999, Xbox360BackButtonTarget.Disabled)));
        Assert.Equal("InvalidM2Target", BackButtonMappingValidation.Validate(new(Xbox360BackButtonTarget.Disabled, (Xbox360BackButtonTarget)999)));
    }

    [Fact]
    public void Missing_mapping_defaults_only_this_feature()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(SettingsPath, "{\"LogLevel\":\"Debug\"}");

        var settings = new SettingsStore(SettingsPath).Load();

        Assert.Equal(BackButtonMappingSettings.Default, settings.BackButtonMapping);
        Assert.Equal(AppLogPreference.Debug, settings.LogLevel);
    }

    [Fact]
    public void Save_and_load_round_trip_enum_names()
    {
        var mapping = new BackButtonMappingSettings(Xbox360BackButtonTarget.A, Xbox360BackButtonTarget.RightBumper);
        new SettingsStore(SettingsPath).Save(new AppSettings { BackButtonMapping = mapping });

        var json = File.ReadAllText(SettingsPath);
        Assert.Contains("\"BackButtonMapping\"", json, StringComparison.Ordinal);
        Assert.Contains("\"M1\": \"A\"", json, StringComparison.Ordinal);
        Assert.Contains("\"M2\": \"RightBumper\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"M1\": 1", json, StringComparison.Ordinal);
        Assert.Equal(mapping, new SettingsStore(SettingsPath).Load().BackButtonMapping);
    }

    [Theory]
    [InlineData("\"BackButtonMapping\":\"bad\"")]
    [InlineData("\"BackButtonMapping\":{\"M1\":\"A\"}")]
    [InlineData("\"BackButtonMapping\":{\"M2\":\"A\"}")]
    [InlineData("\"BackButtonMapping\":{\"M1\":\"Unknown\",\"M2\":\"A\"}")]
    [InlineData("\"BackButtonMapping\":{\"M1\":\"A\",\"M2\":\"Unknown\"}")]
    [InlineData("\"BackButtonMapping\":{\"M1\":1,\"M2\":\"A\"}")]
    [InlineData("\"BackButtonMapping\":{\"M1\":\"A\",\"M2\":2}")]
    public void Malformed_mapping_defaults_only_this_feature(string fragment)
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(SettingsPath, "{\"LogLevel\":\"Debug\"," + fragment + "}");

        var settings = new SettingsStore(SettingsPath).Load();

        Assert.Equal(BackButtonMappingSettings.Default, settings.BackButtonMapping);
        Assert.Equal(AppLogPreference.Debug, settings.LogLevel);
    }

    [Fact]
    public void Coordinator_saves_before_publishing_and_rejects_invalid_candidates()
    {
        var store = new SettingsStore(SettingsPath);
        var coordinator = new StartupSettingsCoordinator(new AppSettings(), store, new NoOpStartupManager());
        var mapping = new BackButtonMappingSettings(Xbox360BackButtonTarget.A, Xbox360BackButtonTarget.RightBumper);

        Assert.True(coordinator.ChangeBackButtonMapping(mapping));
        Assert.Equal(mapping, coordinator.BackButtonMapping);
        Assert.Equal(mapping, store.Load().BackButtonMapping);
        Assert.True(coordinator.ChangeBackButtonMapping(mapping));

        var invalid = new BackButtonMappingSettings((Xbox360BackButtonTarget)999, Xbox360BackButtonTarget.Disabled);
        Assert.False(coordinator.ChangeBackButtonMapping(invalid));
        Assert.Equal(mapping, coordinator.BackButtonMapping);
        Assert.Equal(mapping, store.Load().BackButtonMapping);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }

    private sealed class NoOpStartupManager : IWindowsStartupManager
    {
        public StartupRegistrationResult Synchronize(bool enabled) => StartupRegistrationResult.Enabled();
    }
}
