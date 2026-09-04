using SteamInputAddonforClaw.Contracts.Oem1;
using SteamInputAddonforClaw.Install;
using SteamInputAddonforClaw.Settings;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

/// <summary>
/// The OEM1 mapping rides on the Addon's existing settings file -- there is no second store. These
/// cover the persistence guarantees the work order names: locked first-install defaults, survival
/// across a restart, and "remapping off never erases the bindings".
/// </summary>
public sealed class Oem1MappingSettingsPersistenceTests : IDisposable
{
    private readonly string _testDirectory = Path.Combine(Path.GetTempPath(), $"SteamInputAddonforClaw.Tests.{Guid.NewGuid():N}");

    private string SettingsPath => Path.Combine(_testDirectory, "settings.json");

    [Fact]
    public void No_persisted_value_yields_the_locked_defaults()
    {
        var mapping = new SettingsStore(SettingsPath).Load().Oem1Mapping;

        Assert.True(mapping.RemappingEnabled);
        Assert.Equal(Oem1Action.SteamBigPicture, mapping.NormalSingle.Action);
        Assert.Equal(Oem1Action.None, mapping.NormalDouble.Action);
        Assert.Equal(Oem1Action.SteamQuickAccess, mapping.RoutingSingle.Action);
        Assert.Equal(Oem1Action.None, mapping.RoutingDouble.Action);
    }

    [Fact]
    public void A_settings_file_predating_the_feature_yields_the_locked_defaults()
    {
        Directory.CreateDirectory(_testDirectory);
        File.WriteAllText(SettingsPath, "{\"LaunchAtWindowsStartup\":false}");

        Assert.Equal(Oem1MappingSettings.Default, new SettingsStore(SettingsPath).Load().Oem1Mapping);
    }

    [Fact]
    public void A_malformed_mapping_with_no_readable_switch_fails_open_rather_than_defaulting_to_enabled()
    {
        // Review fix (MAJOR): a corrupt Oem1Mapping object with no readable RemappingEnabled must not
        // fall back to Oem1MappingSettings.Default (RemappingEnabled == true) -- that would silently
        // turn OEM1 suppression back on for a settings file the app cannot actually make sense of.
        // Bindings are not safety-relevant the same way, so they may still fall back to the defaults.
        Directory.CreateDirectory(_testDirectory);
        File.WriteAllText(SettingsPath, "{\"LaunchAtWindowsStartup\":false,\"Oem1Mapping\":{\"NormalSingle\":{\"Action\":\"NotARealAction\"}}}");

        var settings = new SettingsStore(SettingsPath).Load();

        Assert.False(settings.Oem1Mapping.RemappingEnabled);
        Assert.Equal(Oem1MappingSettings.Default.NormalSingle, settings.Oem1Mapping.NormalSingle);
        Assert.Equal(Oem1MappingSettings.Default.NormalDouble, settings.Oem1Mapping.NormalDouble);
        Assert.Equal(Oem1MappingSettings.Default.RoutingSingle, settings.Oem1Mapping.RoutingSingle);
        Assert.Equal(Oem1MappingSettings.Default.RoutingDouble, settings.Oem1Mapping.RoutingDouble);
    }

    [Fact]
    public void A_malformed_mapping_with_an_explicit_persisted_off_switch_preserves_that_off_value()
    {
        // The exact scenario the fix is for: the mapping object itself is unreadable, but its
        // top-level RemappingEnabled is intact and says the user explicitly turned the feature off.
        // Falling back to the ON default here would reactivate suppression behind the user's back.
        Directory.CreateDirectory(_testDirectory);
        File.WriteAllText(SettingsPath, "{\"Oem1Mapping\":{\"RemappingEnabled\":false,\"NormalSingle\":{\"Action\":\"NotARealAction\"}}}");

        var settings = new SettingsStore(SettingsPath).Load();

        Assert.False(settings.Oem1Mapping.RemappingEnabled);
    }

    [Fact]
    public void A_mapping_with_a_null_nested_slot_binding_falls_back_to_default_bindings_without_crashing()
    {
        // An explicit JSON null for a slot binding deserializes successfully (Oem1MappingSettings
        // parses) but leaves that record-typed property null, which would NullReferenceException the
        // first time the dispatcher resolves that slot. Must be caught here, not at dispatch time.
        // The top-level RemappingEnabled is still independently readable here, so it is honored
        // exactly as an explicit value -- only the bindings fall back to defaults.
        Directory.CreateDirectory(_testDirectory);
        File.WriteAllText(SettingsPath, "{\"Oem1Mapping\":{\"RemappingEnabled\":true,\"NormalSingle\":null}}");

        var settings = new SettingsStore(SettingsPath).Load();

        Assert.True(settings.Oem1Mapping.RemappingEnabled);
        Assert.NotNull(settings.Oem1Mapping.NormalSingle);
    }

    [Fact]
    public void Every_slot_and_its_action_specific_configuration_survives_a_restart()
    {
        var store = new SettingsStore(SettingsPath);
        var mapping = new Oem1MappingSettings
        {
            RemappingEnabled = true,
            NormalSingle = Oem1SlotBinding.Of(Oem1Action.KeyboardHotkey) with
            {
                Hotkey = new(Oem1HotkeyModifiers.Control | Oem1HotkeyModifiers.Shift, Oem1HotkeyKey.S)
            },
            NormalDouble = Oem1SlotBinding.Of(Oem1Action.LaunchApplication) with
            {
                Launch = new(@"C:\fake\app.exe", "--windowed")
            },
            RoutingSingle = Oem1SlotBinding.Of(Oem1Action.SteamQuickAccess),
            RoutingDouble = Oem1SlotBinding.Of(Oem1Action.None)
        };

        store.Save(new AppSettings() with { Oem1Mapping = mapping });

        // A brand-new store instance over the same path is exactly what an app restart does.
        Assert.Equal(mapping, new SettingsStore(SettingsPath).Load().Oem1Mapping);
    }

    [Fact]
    public void Turning_remapping_off_persists_the_switch_without_touching_the_bindings()
    {
        var store = new SettingsStore(SettingsPath);
        var coordinator = new StartupSettingsCoordinator(store.Load(), store, new NoOpStartupManager());
        var configured = Oem1MappingSettings.Default with
        {
            NormalDouble = Oem1SlotBinding.Of(Oem1Action.KeyboardHotkey) with { Hotkey = new(Key: Oem1HotkeyKey.F12) }
        };
        coordinator.ChangeOem1Mapping(configured);

        coordinator.ChangeOem1Mapping(coordinator.Oem1Mapping with { RemappingEnabled = false });

        var persisted = new SettingsStore(SettingsPath).Load().Oem1Mapping;
        Assert.False(persisted.RemappingEnabled);
        Assert.Equal(configured.NormalSingle, persisted.NormalSingle);
        Assert.Equal(configured.NormalDouble, persisted.NormalDouble);
        Assert.Equal(configured.RoutingSingle, persisted.RoutingSingle);
        Assert.Equal(configured.RoutingDouble, persisted.RoutingDouble);
    }

    [Fact]
    public void Turning_remapping_back_on_restores_the_previously_saved_mappings()
    {
        var store = new SettingsStore(SettingsPath);
        var coordinator = new StartupSettingsCoordinator(store.Load(), store, new NoOpStartupManager());
        var configured = Oem1MappingSettings.Default with
        {
            RoutingDouble = Oem1SlotBinding.Of(Oem1Action.LaunchApplication) with { Launch = new(@"C:\fake\app.exe") }
        };
        coordinator.ChangeOem1Mapping(configured);

        coordinator.ChangeOem1Mapping(coordinator.Oem1Mapping with { RemappingEnabled = false });
        coordinator.ChangeOem1Mapping(coordinator.Oem1Mapping with { RemappingEnabled = true });

        Assert.Equal(configured, new SettingsStore(SettingsPath).Load().Oem1Mapping);
    }

    [Fact]
    public void Changing_the_mapping_raises_exactly_one_change_notification_and_no_op_writes_raise_none()
    {
        var store = new SettingsStore(SettingsPath);
        var coordinator = new StartupSettingsCoordinator(store.Load(), store, new NoOpStartupManager());
        var changes = 0;
        coordinator.Oem1MappingChanged += (_, _) => changes++;

        coordinator.ChangeOem1Mapping(coordinator.Oem1Mapping with { RemappingEnabled = false });
        Assert.Equal(1, changes);

        // Value equality, not reference equality: re-saving an identical mapping must not churn the
        // runtime's suppression lifecycle.
        coordinator.ChangeOem1Mapping(Oem1MappingSettings.Default with { RemappingEnabled = false });
        Assert.Equal(1, changes);
    }

    [Fact]
    public void Changing_the_remapping_switch_persists_without_disturbing_other_settings()
    {
        var store = new SettingsStore(SettingsPath);
        var coordinator = new StartupSettingsCoordinator(store.Load(), store, new NoOpStartupManager());

        coordinator.ChangeLogLevel(AppLogPreference.Debug);
        coordinator.ChangeOem1Mapping(coordinator.Oem1Mapping with { RemappingEnabled = false });

        Assert.False(new SettingsStore(SettingsPath).Load().Oem1Mapping.RemappingEnabled);
        Assert.Equal(AppLogPreference.Debug, new SettingsStore(SettingsPath).Load().LogLevel);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDirectory)) Directory.Delete(_testDirectory, recursive: true);
    }

    private sealed class NoOpStartupManager : IWindowsStartupManager
    {
        public StartupRegistrationResult Synchronize(bool enabled) => new(true, "No-op");
    }
}
